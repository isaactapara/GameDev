using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Spawning Configuration")]
    public GameObject frogPrefab;
    public float baseSpawnInterval = 1.35f; // Steady, readable pace
    public float minX = -6.2f;
    public float maxX = 6.2f;
    public float spawnY = 5.5f;

    [Header("Game Rules")]
    public float gameDuration = 45f;
    public int startingLives = 5;

    // Runtime UI References
    private TextMeshProUGUI scoreText;
    private TextMeshProUGUI streakText;
    private TextMeshProUGUI timerText;
    private TextMeshProUGUI livesText;
    private GameObject startPanel;
    private GameObject pausePanel;
    private GameObject gameOverPanel;
    private TextMeshProUGUI gameOverScoreText;

    // Audio System
    private AudioSource sfxSource;
    private AudioSource ambientSource;
    private AudioClip[] pentatonicScaleClips;
    private AudioClip heavyHitClip;
    private AudioClip missSoundClip;
    private AudioClip hazardHitClip;
    private AudioClip gameOverClip;

    // Gameplay Tracking
    private int score = 0;
    private int lives;
    private int streak = 0;
    private int comboMultiplier = 1;
    private float timeRemaining;
    private float spawnTimer = 0f;
    private float currentSpawnInterval;
    private bool isGameStarted = false;
    private bool isGameOver = false;
    private bool isPaused = false;
    private int lastHitFrame = -1;

    // Camera Reference for Subtle Screen Shake
    private Camera mainCamera;
    private Vector3 originalCameraPos;
    private Coroutine screenShakeCoroutine;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        Time.timeScale = 1f;
        spawnY = 5.5f;
        currentSpawnInterval = baseSpawnInterval;

        mainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        if (mainCamera != null) originalCameraPos = mainCamera.transform.position;

        // Clean up any lingering ambient dots/particles from previous runs
        GameObject oldPollen = GameObject.Find("AmbientPollenSystem");
        if (oldPollen != null) Destroy(oldPollen);

        // Automated Runtime Environment Construction
        SetupCamera();
        SetupBackground();
        SetupCompleteUI();

        // Audio System
        SetupAudioSystems();
    }

    void Start()
    {
        lives = startingLives;
        timeRemaining = gameDuration;
        streak = 0;
        comboMultiplier = 1;

        UpdateUI();

        if (startPanel != null) startPanel.SetActive(true);
        if (pausePanel != null) pausePanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    void Update()
    {
        if (!isGameStarted || isGameOver || isPaused) return;

        // 1. Countdown timer
        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            TriggerGameOver("Time Expired");
            return;
        }

        // 2. Controlled pacing adjustment (never frantic)
        currentSpawnInterval = Mathf.Clamp(baseSpawnInterval - (score * 0.006f), 0.85f, baseSpawnInterval);

        // 3. Spawning Loop
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= currentSpawnInterval)
        {
            SpawnArchetypeFrog();
            spawnTimer = 0f;
        }

        // 4. Click Detection
        DetectWrongClick();

        UpdateUI();
    }

    void DetectWrongClick()
    {
        if (Time.frameCount == lastHitFrame) return;

        bool wasPressed = false;
        Vector2 screenPos = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
        {
            wasPressed = true;
            screenPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            wasPressed = true;
            screenPos = Input.mousePosition;
        }
#endif

        if (wasPressed)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (mainCamera != null)
            {
                Vector2 mousePos = mainCamera.ScreenToWorldPoint(screenPos);
                RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);
                if (hit.collider == null)
                {
                    WrongClick();
                }
            }
        }
    }

    void SpawnArchetypeFrog()
    {
        if (frogPrefab == null)
        {
            frogPrefab = Resources.Load<GameObject>("frog");
            if (frogPrefab == null) return;
        }

        Vector2 spawnPos = new Vector2(Random.Range(minX, maxX), spawnY);
        GameObject frogGo = Instantiate(frogPrefab, spawnPos, Quaternion.identity);

        Frog frogComponent = frogGo.GetComponent<Frog>();
        if (frogComponent != null)
        {
            float roll = Random.Range(0f, 1f);
            if (roll < 0.65f)
            {
                frogComponent.frogType = FrogType.Standard;
            }
            else if (roll < 0.80f)
            {
                frogComponent.frogType = FrogType.Golden;
            }
            else if (roll < 0.92f)
            {
                frogComponent.frogType = FrogType.Heavy;
            }
            else
            {
                frogComponent.frogType = FrogType.Hazard;
            }
        }
    }

    // --- Kinetic Feedback & Event Handlers ---

    public void RegisterFrogHit(FrogType type, int basePoints, Vector3 hitPosition)
    {
        lastHitFrame = Time.frameCount;

        streak++;
        UpdateMultiplier();

        score += basePoints * comboMultiplier;

        TriggerScreenShake(0.06f, 0.04f);
        PlayHarmonicStreakTone();

        // Particles ONLY spawn when a frog is clicked
        Color burstColor = type == FrogType.Golden ? new Color(1f, 0.88f, 0.2f) : new Color(0.4f, 0.95f, 0.5f);
        SpawnImpactBurst(hitPosition, burstColor);

        UpdateUI();
    }

    public void RegisterFrogDamaged()
    {
        lastHitFrame = Time.frameCount;
        if (sfxSource != null && heavyHitClip != null) sfxSource.PlayOneShot(heavyHitClip);
        TriggerScreenShake(0.04f, 0.03f);
    }

    public void HitHazardFrog(Vector3 hitPosition)
    {
        lastHitFrame = Time.frameCount;

        lives = Mathf.Max(0, lives - 2);
        ResetStreak();

        TriggerScreenShake(0.18f, 0.10f);
        if (sfxSource != null && hazardHitClip != null) sfxSource.PlayOneShot(hazardHitClip);

        // Particles ONLY spawn when clicked
        SpawnImpactBurst(hitPosition, new Color(0.95f, 0.25f, 0.25f));

        UpdateUI();

        if (lives <= 0)
        {
            TriggerGameOver("Toxic Hazard Contact");
        }
    }

    public void MissedFrog(Vector3 waterPosition)
    {
        if (isGameOver) return;

        lives--;
        ResetStreak();

        TriggerScreenShake(0.08f, 0.04f);
        if (sfxSource != null && missSoundClip != null) sfxSource.PlayOneShot(missSoundClip);

        UpdateUI();

        if (lives <= 0)
        {
            TriggerGameOver("Out of Lives");
        }
    }

    public void NotifyHazardAvoided(Vector3 waterPosition)
    {
        // Safe disposal - no penalty
    }

    public void WrongClick()
    {
        if (isGameOver) return;

        lives--;
        ResetStreak();

        TriggerScreenShake(0.08f, 0.04f);
        if (sfxSource != null && missSoundClip != null) sfxSource.PlayOneShot(missSoundClip);

        UpdateUI();

        if (lives <= 0)
        {
            TriggerGameOver("Out of Lives");
        }
    }

    void UpdateMultiplier()
    {
        if (streak >= 10) comboMultiplier = 4;
        else if (streak >= 6) comboMultiplier = 3;
        else if (streak >= 3) comboMultiplier = 2;
        else comboMultiplier = 1;
    }

    void ResetStreak()
    {
        streak = 0;
        comboMultiplier = 1;
    }

    void TriggerGameOver(string reason)
    {
        isGameOver = true;
        if (sfxSource != null && gameOverClip != null) sfxSource.PlayOneShot(gameOverClip);

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
            if (gameOverScoreText != null)
            {
                gameOverScoreText.text = "Reason: " + reason + "\nFinal Score: " + score + "\nHighest Multiplier: " + comboMultiplier + "X";
            }
        }
    }

    void UpdateUI()
    {
        if (scoreText != null) scoreText.text = "SCORE: " + score;
        if (streakText != null) streakText.text = "MULTIPLIER: " + comboMultiplier + "X (" + streak + " STREAK)";
        if (timerText != null) timerText.text = "TIME: " + Mathf.CeilToInt(timeRemaining) + "s";
        if (livesText != null) livesText.text = "LIVES: " + lives;
    }

    // --- State Controls ---

    public void StartGame()
    {
        isGameStarted = true;
        isGameOver = false;
        isPaused = false;
        score = 0;
        lives = startingLives;
        streak = 0;
        comboMultiplier = 1;
        timeRemaining = gameDuration;
        spawnTimer = 0f;
        Time.timeScale = 1f;

        if (startPanel != null) startPanel.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        if (ambientSource != null && !ambientSource.isPlaying) ambientSource.Play();

        UpdateUI();
    }

    public void PauseGame()
    {
        if (!isGameStarted || isGameOver) return;
        isPaused = true;
        Time.timeScale = 0f;
        if (pausePanel != null) pausePanel.SetActive(true);
    }

    public void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = 1f;
        if (pausePanel != null) pausePanel.SetActive(false);
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public bool IsGameOver()
    {
        return isGameOver;
    }

    // --- Subtle Screen Shake ---

    public void TriggerScreenShake(float duration, float magnitude)
    {
        if (mainCamera == null) return;
        if (screenShakeCoroutine != null) StopCoroutine(screenShakeCoroutine);
        screenShakeCoroutine = StartCoroutine(DoScreenShake(duration, magnitude));
    }

    private IEnumerator DoScreenShake(float duration, float magnitude)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float xOffset = Random.Range(-1f, 1f) * magnitude;
            float yOffset = Random.Range(-1f, 1f) * magnitude;
            mainCamera.transform.position = new Vector3(originalCameraPos.x + xOffset, originalCameraPos.y + yOffset, originalCameraPos.z);

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        mainCamera.transform.position = originalCameraPos;
    }

    // --- Localized Click Particle Burst (Only Appears on Click) ---

    void SpawnImpactBurst(Vector3 pos, Color color)
    {
        GameObject burstGo = new GameObject("ClickParticleBurst");
        burstGo.transform.position = pos;

        ParticleSystem ps = burstGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = 0.22f;
        main.startSpeed = 2.8f;
        main.startSize = 0.12f;
        main.startColor = color;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 10) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;

        var renderer = burstGo.GetComponent<ParticleSystemRenderer>();
        if (renderer != null) renderer.sortingOrder = 8;

        ps.Play();

        Destroy(burstGo, 0.4f);
    }

    // --- Audio Architecture ---

    void SetupAudioSystems()
    {
        AudioSource[] sources = GetComponents<AudioSource>();
        sfxSource = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();
        ambientSource = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();

        ambientSource.loop = true;
        ambientSource.volume = 0.18f;

        // Calming ambient chime (Zero low-frequency engine drone)
        ambientSource.clip = CreateSereneAmbience();

        missSoundClip = CreateTone(220f, 0.15f, false);
        heavyHitClip = CreateTone(380f, 0.1f, false);
        hazardHitClip = CreateTone(140f, 0.25f, false);
        gameOverClip = CreateTone(160f, 0.4f, false);

        float[] frequencies = { 261.63f, 293.66f, 329.63f, 392.00f, 440.00f, 523.25f, 587.33f, 659.25f };
        pentatonicScaleClips = new AudioClip[frequencies.Length];
        for (int i = 0; i < frequencies.Length; i++)
        {
            pentatonicScaleClips[i] = CreateTone(frequencies[i], 0.12f, true);
        }
    }

    void PlayHarmonicStreakTone()
    {
        if (sfxSource == null || pentatonicScaleClips == null || pentatonicScaleClips.Length == 0) return;
        int noteIndex = Mathf.Clamp(streak - 1, 0, pentatonicScaleClips.Length - 1);
        sfxSource.PlayOneShot(pentatonicScaleClips[noteIndex]);
    }

    AudioClip CreateSereneAmbience()
    {
        int sampleRate = 44100;
        float duration = 4.0f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        // Pleasant high-register chime sequence - pure serene tone, no bass rumble
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float chime1 = Mathf.Sin(2f * Mathf.PI * 523.25f * t) * Mathf.Exp(-2.5f * (t % 2f)) * 0.035f;
            float chime2 = Mathf.Sin(2f * Mathf.PI * 659.25f * t) * Mathf.Exp(-2.5f * ((t + 1f) % 2f)) * 0.025f;
            samples[i] = chime1 + chime2;
        }

        AudioClip clip = AudioClip.Create("SerenePondAmbience", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    AudioClip CreateTone(float frequency, float duration, bool bellDecay)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = bellDecay ? Mathf.Exp(-6f * (t / duration)) : (1f - (t / duration));
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope;
        }

        AudioClip clip = AudioClip.Create("Tone", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // --- Runtime Environment Construction ---

    void SetupCamera()
    {
        if (mainCamera != null)
        {
            mainCamera.orthographic = true;
            mainCamera.orthographicSize = 5f;
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.08f, 0.14f, 0.20f);
            if (mainCamera.GetComponent<Physics2DRaycaster>() == null)
            {
                mainCamera.gameObject.AddComponent<Physics2DRaycaster>();
            }
        }
    }

    void SetupBackground()
    {
        GameObject bg = GameObject.Find("Background");
        if (bg == null) bg = new GameObject("Background");

        SpriteRenderer sr = bg.GetComponent<SpriteRenderer>();
        if (sr == null) sr = bg.AddComponent<SpriteRenderer>();

        Sprite bgSprite = Resources.Load<Sprite>("background");
        if (bgSprite != null) sr.sprite = bgSprite;

        sr.sortingOrder = -10;
        bg.transform.position = new Vector3(0f, 0f, 5f);
        bg.transform.localScale = new Vector3(0.95f, 0.95f, 1f);
    }

    void SetupCompleteUI()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            esGo.AddComponent<StandaloneInputModule>();
#endif
        }

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGo = new GameObject("Canvas");
            canvas = canvasGo.AddComponent<Canvas>();
        }
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();

        string[] oldNames = { "ScoreText", "TimerText", "LivesText", "StartButton", "RestartButton", "TopHUD", "StartPanel", "PausePanel", "GameOverPanel" };
        foreach (string n in oldNames)
        {
            Transform t = canvas.transform.Find(n);
            if (t != null) Destroy(t.gameObject);
        }

        // Top HUD Header
        GameObject topHud = new GameObject("TopHUD");
        topHud.transform.SetParent(canvas.transform, false);
        RectTransform hudRt = topHud.AddComponent<RectTransform>();
        hudRt.anchorMin = new Vector2(0f, 1f);
        hudRt.anchorMax = new Vector2(1f, 1f);
        hudRt.pivot = new Vector2(0.5f, 1f);
        hudRt.anchoredPosition = new Vector2(0f, -20f);
        hudRt.sizeDelta = new Vector2(-60f, 80f);

        Image hudBg = topHud.AddComponent<Image>();
        hudBg.color = new Color(0.04f, 0.08f, 0.14f, 0.92f);

        // Score (Left)
        scoreText = CreateText(topHud.transform, "ScoreText", "SCORE: 0", 26, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(25f, 0f), new Vector2(220f, 50f), TextAlignmentOptions.MidlineLeft, new Color(0.35f, 0.95f, 0.55f));

        // Combo Multiplier (Center-Left)
        streakText = CreateText(topHud.transform, "StreakText", "MULTIPLIER: 1X (0 STREAK)", 22, new Vector2(0.32f, 0.5f), new Vector2(0.32f, 0.5f), Vector2.zero, new Vector2(300f, 50f), TextAlignmentOptions.Center, new Color(0.95f, 0.85f, 0.35f));

        // Timer (Center)
        timerText = CreateText(topHud.transform, "TimerText", "TIME: 45s", 26, new Vector2(0.60f, 0.5f), new Vector2(0.60f, 0.5f), Vector2.zero, new Vector2(180f, 50f), TextAlignmentOptions.Center, new Color(0.4f, 0.8f, 1f));

        // Lives (Right)
        livesText = CreateText(topHud.transform, "LivesText", "LIVES: 5", 26, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-200f, 0f), new Vector2(160f, 50f), TextAlignmentOptions.MidlineRight, new Color(1f, 0.45f, 0.5f));

        // Pause/Stop Button (Far Right)
        CreateButton(topHud.transform, "PauseButton", "PAUSE", new Vector2(1f, 0.5f), new Vector2(-25f, 0f), new Vector2(120f, 48f), new Color(0.2f, 0.35f, 0.5f), PauseGame);

        // Start Panel (Modal)
        startPanel = CreateModal(canvas.transform, "StartPanel", new Vector2(640f, 480f));
        CreateText(startPanel.transform, "Title", "KILL THE FROG", 44, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(500f, 60f), TextAlignmentOptions.Center, new Color(0.3f, 0.95f, 0.55f));
        CreateText(startPanel.transform, "Instructions", "Tap the frogs as they fall into the pond.\nStandard (Green): 1 pt | Golden (Fast): 3 pts\nHeavy Bullfrog: 2 clicks | Red Toad: HAZARD - DO NOT TAP\nConsecutive hits increase your score multiplier.", 20, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(560f, 150f), TextAlignmentOptions.Center, new Color(0.85f, 0.9f, 0.95f));
        CreateButton(startPanel.transform, "StartButton", "START GAME", new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(280f, 65f), new Color(0.12f, 0.72f, 0.4f), StartGame);

        // Pause Panel (Modal)
        pausePanel = CreateModal(canvas.transform, "PausePanel", new Vector2(500f, 360f));
        CreateText(pausePanel.transform, "Title", "GAME PAUSED", 40, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(400f, 60f), TextAlignmentOptions.Center, new Color(1f, 0.85f, 0.3f));
        CreateButton(pausePanel.transform, "ResumeButton", "RESUME", new Vector2(0.5f, 0.5f), new Vector2(0f, 15f), new Vector2(260f, 60f), new Color(0.12f, 0.72f, 0.4f), ResumeGame);
        CreateButton(pausePanel.transform, "RestartButton", "RESTART", new Vector2(0.5f, 0.5f), new Vector2(0f, -65f), new Vector2(260f, 60f), new Color(0.3f, 0.45f, 0.6f), RestartGame);
        pausePanel.SetActive(false);

        // Game Over Panel (Modal)
        gameOverPanel = CreateModal(canvas.transform, "GameOverPanel", new Vector2(520f, 400f));
        CreateText(gameOverPanel.transform, "Title", "GAME OVER", 42, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(400f, 60f), TextAlignmentOptions.Center, new Color(0.95f, 0.35f, 0.35f));
        gameOverScoreText = CreateText(gameOverPanel.transform, "ScoreInfo", "Final Score: 0", 24, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 25f), new Vector2(460f, 80f), TextAlignmentOptions.Center, Color.white);
        CreateButton(gameOverPanel.transform, "PlayAgainButton", "PLAY AGAIN", new Vector2(0.5f, 0f), new Vector2(0f, 65f), new Vector2(260f, 65f), new Color(0.12f, 0.72f, 0.4f), RestartGame);
        gameOverPanel.SetActive(false);
    }

    private GameObject CreateModal(Transform parent, string name, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.04f, 0.08f, 0.14f, 0.96f);

        return go;
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, string content, float fontSize, Vector2 anchor, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, TextAlignmentOptions alignment, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = alignment;
        tmp.color = color;

        RectTransform rt = tmp.rectTransform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        return tmp;
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 anchor, Vector2 anchoredPos, Vector2 sizeDelta, Color color, UnityEngine.Events.UnityAction onClickAction)
    {
        GameObject btnGo = new GameObject(name);
        btnGo.transform.SetParent(parent, false);

        RectTransform rt = btnGo.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        Image img = btnGo.AddComponent<Image>();
        img.color = color;

        Button btn = btnGo.AddComponent<Button>();
        btn.onClick.AddListener(onClickAction);

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(btnGo.transform, false);

        TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 24;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        RectTransform textRt = tmp.rectTransform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;
        textRt.anchoredPosition = Vector2.zero;

        return btn;
    }
}

