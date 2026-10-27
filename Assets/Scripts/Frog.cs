using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public enum FrogType
{
    Standard,
    Golden,
    Heavy,
    Hazard
}

public class Frog : MonoBehaviour, IPointerDownHandler
{
    [Header("Frog Configuration")]
    public FrogType frogType = FrogType.Standard;
    public int points = 1;
    public int health = 1;

    private bool isKilled = false;
    private SpriteRenderer spriteRenderer;

    void Start()
    {
        // 1. Assign real frog sprite
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            if (spriteRenderer.sprite == null || spriteRenderer.sprite.name == "Square")
            {
                Sprite frogSprite = Resources.Load<Sprite>("frog");
                if (frogSprite != null) spriteRenderer.sprite = frogSprite;
            }
            spriteRenderer.sortingOrder = 5;
        }

        // 2. Configure Archetype properties (Calm, predictable, and steady)
        ConfigureFrogArchetype();
    }

    void ConfigureFrogArchetype()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();

        switch (frogType)
        {
            case FrogType.Golden:
                points = 3;
                health = 1;
                transform.localScale = new Vector3(1.15f, 1.15f, 1f);
                if (spriteRenderer != null) spriteRenderer.color = new Color(1f, 0.88f, 0.25f); // Gold
                if (rb != null)
                {
                    rb.gravityScale = 0.48f; // Gentle acceleration, slightly faster than standard
                }
                break;

            case FrogType.Heavy:
                points = 2;
                health = 2; // Requires two clicks
                transform.localScale = new Vector3(1.6f, 1.6f, 1f);
                if (spriteRenderer != null) spriteRenderer.color = new Color(0.15f, 0.58f, 0.25f); // Deep Forest Green
                if (rb != null)
                {
                    rb.gravityScale = 0.26f; // Steady, calm descent
                }
                break;

            case FrogType.Hazard:
                points = 0;
                health = 1;
                transform.localScale = new Vector3(1.3f, 1.3f, 1f);
                if (spriteRenderer != null) spriteRenderer.color = new Color(0.95f, 0.28f, 0.28f); // Warning Red
                if (rb != null)
                {
                    rb.gravityScale = 0.36f;
                }
                break;

            case FrogType.Standard:
            default:
                points = 1;
                health = 1;
                transform.localScale = new Vector3(1.3f, 1.3f, 1f);
                if (spriteRenderer != null) spriteRenderer.color = Color.white;
                if (rb != null)
                {
                    rb.gravityScale = 0.36f; // Standard slow, smooth fall
                }
                break;
        }

        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
            box.size = new Vector2(1.2f, 1.2f);
        }
    }

    void Update()
    {
        if (isKilled) return;

        // Despawn when frog falls below the bottom screen edge into the water
        if (transform.position.y < -5.3f)
        {
            isKilled = true;

            if (GameManager.Instance != null && !GameManager.Instance.IsGameOver())
            {
                if (frogType == FrogType.Hazard)
                {
                    GameManager.Instance.NotifyHazardAvoided(transform.position);
                }
                else
                {
                    GameManager.Instance.MissedFrog(transform.position);
                }
            }

            Destroy(gameObject);
        }
    }

    void OnMouseDown()
    {
        HandleClick();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        HandleClick();
    }

    void HandleClick()
    {
        if (isKilled) return;
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver()) return;

        if (frogType == FrogType.Hazard)
        {
            isKilled = true;
            if (GameManager.Instance != null)
            {
                GameManager.Instance.HitHazardFrog(transform.position);
            }
            StartCoroutine(KillSequence(true));
            return;
        }

        health--;
        if (health > 0)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.RegisterFrogDamaged();
            }
            StartCoroutine(DamageFeedbackSequence());
            return;
        }

        isKilled = true;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterFrogHit(frogType, points, transform.position);
        }

        StartCoroutine(KillSequence(false));
    }

    private IEnumerator DamageFeedbackSequence()
    {
        Vector3 origScale = transform.localScale;
        if (spriteRenderer != null) spriteRenderer.color = new Color(0.95f, 0.65f, 0.15f); // Orange damage flash

        transform.localScale = origScale * 1.2f;
        yield return new WaitForSeconds(0.08f);
        transform.localScale = origScale;
    }

    private IEnumerator KillSequence(bool isHazard)
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false;

        Vector3 startScale = transform.localScale;
        float elapsed = 0f;
        float duration = 0.12f;

        Color targetColor = isHazard ? Color.black : Color.white;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            transform.localScale = Vector3.Lerp(startScale, startScale * 1.3f, t);
            if (spriteRenderer != null)
            {
                spriteRenderer.color = Color.Lerp(spriteRenderer.color, targetColor, t);
            }
            yield return null;
        }

        Destroy(gameObject);
    }
}
