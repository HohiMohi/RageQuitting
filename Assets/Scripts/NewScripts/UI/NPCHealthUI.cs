using System;
using UnityEngine;
using UnityEngine.UI;

public class NPCHealthUI : MonoBehaviour
{
    [SerializeField] private NPCHealth npcHealth;
    [SerializeField] private Canvas worldSpaceCanvas;
    [SerializeField] private Image healthFillImage;
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 1.35f, 0f);
    [SerializeField] private Vector2 size = new Vector2(96f, 10f);
    [SerializeField] private float canvasScale = 0.01f;
    [SerializeField] private float visibleAfterDamageDuration = 2f;
    [SerializeField] private Color backgroundColor = new Color(0.12f, 0.02f, 0.02f, 0.78f);
    [SerializeField] private Color fillColor = new Color(0.86f, 0.08f, 0.08f, 0.95f);

    private RectTransform fillRectTransform;
    private float previousHealth;
    private float hideTime;

    private void Awake()
    {
        EnsureReferences();
        previousHealth = npcHealth != null ? npcHealth.CurrentHealth : 0f;
        Hide();
        UpdateVisual();
    }

    private void OnEnable()
    {
        EnsureReferences();
        if (npcHealth != null)
        {
            npcHealth.OnHealthChanged += NPCHealth_OnHealthChanged;
            npcHealth.OnDeath += NPCHealth_OnDeath;
            previousHealth = npcHealth.CurrentHealth;
        }

        Hide();
        UpdateVisual();
    }

    private void OnDisable()
    {
        if (npcHealth != null)
        {
            npcHealth.OnHealthChanged -= NPCHealth_OnHealthChanged;
            npcHealth.OnDeath -= NPCHealth_OnDeath;
        }
    }

    private void LateUpdate()
    {
        FaceCamera();

        if (worldSpaceCanvas != null && worldSpaceCanvas.gameObject.activeSelf && Time.time >= hideTime)
        {
            Hide();
        }
    }

    private void NPCHealth_OnHealthChanged(object sender, EventArgs e)
    {
        if (npcHealth == null)
        {
            return;
        }

        float currentHealth = npcHealth.CurrentHealth;
        bool receivedDamage = currentHealth < previousHealth;
        previousHealth = currentHealth;

        UpdateVisual();

        if (receivedDamage && !npcHealth.IsDead)
        {
            ShowTemporarily();
        }
    }

    private void NPCHealth_OnDeath(object sender, EventArgs e)
    {
        Hide();
    }

    private void ShowTemporarily()
    {
        EnsureReferences();
        if (worldSpaceCanvas == null)
        {
            return;
        }

        hideTime = Time.time + Mathf.Max(0.1f, visibleAfterDamageDuration);
        worldSpaceCanvas.gameObject.SetActive(true);
    }

    private void Hide()
    {
        if (worldSpaceCanvas != null)
        {
            worldSpaceCanvas.gameObject.SetActive(false);
        }
    }

    private void UpdateVisual()
    {
        EnsureReferences();
        if (npcHealth == null || healthFillImage == null || fillRectTransform == null)
        {
            return;
        }

        float normalizedHealth = npcHealth.GetHealthNormalized();
        healthFillImage.fillAmount = normalizedHealth;
        fillRectTransform.anchorMax = new Vector2(normalizedHealth, 1f);
        fillRectTransform.offsetMax = Vector2.zero;
    }

    private void EnsureReferences()
    {
        if (npcHealth == null)
        {
            npcHealth = GetComponent<NPCHealth>();
            npcHealth ??= GetComponentInParent<NPCHealth>();
        }

        if (worldSpaceCanvas == null)
        {
            worldSpaceCanvas = GetComponentInChildren<Canvas>(true);
            if (worldSpaceCanvas == null || worldSpaceCanvas.renderMode != RenderMode.WorldSpace)
            {
                CreateDefaultWorldSpaceCanvas();
            }
        }

        if (healthFillImage != null && fillRectTransform == null)
        {
            fillRectTransform = healthFillImage.rectTransform;
        }
    }

    private void CreateDefaultWorldSpaceCanvas()
    {
        GameObject canvasGameObject = new GameObject("NPCHealthUIWorldSpace", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        RectTransform canvasRectTransform = canvasGameObject.GetComponent<RectTransform>();
        canvasRectTransform.SetParent(transform, false);
        canvasRectTransform.localPosition = localOffset;
        canvasRectTransform.localRotation = Quaternion.identity;
        canvasRectTransform.localScale = Vector3.one * canvasScale;
        canvasRectTransform.sizeDelta = size;

        worldSpaceCanvas = canvasGameObject.GetComponent<Canvas>();
        worldSpaceCanvas.renderMode = RenderMode.WorldSpace;
        worldSpaceCanvas.worldCamera = Camera.main;

        CanvasScaler canvasScaler = canvasGameObject.GetComponent<CanvasScaler>();
        canvasScaler.dynamicPixelsPerUnit = 10f;

        GraphicRaycaster raycaster = canvasGameObject.GetComponent<GraphicRaycaster>();
        raycaster.enabled = false;

        GameObject backgroundGameObject = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform backgroundRectTransform = backgroundGameObject.GetComponent<RectTransform>();
        backgroundRectTransform.SetParent(canvasRectTransform, false);
        backgroundRectTransform.anchorMin = Vector2.zero;
        backgroundRectTransform.anchorMax = Vector2.one;
        backgroundRectTransform.offsetMin = Vector2.zero;
        backgroundRectTransform.offsetMax = Vector2.zero;

        Image backgroundImage = backgroundGameObject.GetComponent<Image>();
        backgroundImage.color = backgroundColor;
        backgroundImage.raycastTarget = false;

        GameObject fillGameObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillRectTransform = fillGameObject.GetComponent<RectTransform>();
        fillRectTransform.SetParent(backgroundRectTransform, false);
        fillRectTransform.anchorMin = Vector2.zero;
        fillRectTransform.anchorMax = Vector2.one;
        fillRectTransform.offsetMin = Vector2.zero;
        fillRectTransform.offsetMax = Vector2.zero;

        healthFillImage = fillGameObject.GetComponent<Image>();
        healthFillImage.color = fillColor;
        healthFillImage.raycastTarget = false;
    }

    private void FaceCamera()
    {
        if (worldSpaceCanvas == null || Camera.main == null)
        {
            return;
        }

        Transform canvasTransform = worldSpaceCanvas.transform;
        canvasTransform.position = transform.TransformPoint(localOffset);
        canvasTransform.forward = Camera.main.transform.forward;
    }
}
