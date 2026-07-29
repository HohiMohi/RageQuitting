using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class PlayerRespawnPromptUI : MonoBehaviour
{
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private string respawnAvailableText = "Press Interact to respawn";
    [SerializeField] private string respawnCountdownFormat = "Respawn available in {0:0}s";
    [SerializeField] private string carriedByEnemyText = "Carried by enemy";
    [SerializeField] private Vector2 anchoredPosition = new Vector2(0f, 140f);
    [SerializeField] private Vector2 size = new Vector2(420f, 54f);

    private GameObject promptRoot;
    private TextMeshProUGUI promptText;

    private void Awake()
    {
        EnsureReferences();
        CreateDefaultPrompt();
    }

    private void OnEnable()
    {
        EnsureReferences();
        if (playerHealth != null)
        {
            playerHealth.OnDownedStateChanged += PlayerHealth_OnStateChanged;
            playerHealth.OnHealthChanged += PlayerHealth_OnStateChanged;
        }

        UpdateVisual();
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDownedStateChanged -= PlayerHealth_OnStateChanged;
            playerHealth.OnHealthChanged -= PlayerHealth_OnStateChanged;
        }
    }

    private void Update()
    {
        UpdateVisual();
    }

    private void PlayerHealth_OnStateChanged(object sender, System.EventArgs e)
    {
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        EnsureReferences();
        if (promptRoot == null || promptText == null || playerHealth == null)
        {
            return;
        }

        bool shouldShow = IsLocalPlayerHealth() && playerHealth.IsDowned;
        promptRoot.SetActive(shouldShow);
        if (!shouldShow)
        {
            return;
        }

        if (playerHealth.IsCarriedByNPC)
        {
            promptText.text = carriedByEnemyText;
            return;
        }

        float timeRemaining = playerHealth.GetRespawnTimeRemaining();
        promptText.text = playerHealth.CanRespawn
            ? respawnAvailableText
            : string.Format(respawnCountdownFormat, timeRemaining);
    }

    private bool IsLocalPlayerHealth()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && playerHealth.IsSpawned)
        {
            return playerHealth.IsOwner;
        }

        return true;
    }

    private void EnsureReferences()
    {
        if (playerHealth == null)
        {
            playerHealth = GetComponentInParent<PlayerHealth>();
        }

        if (playerHealth == null && transform.root != null)
        {
            playerHealth = transform.root.GetComponentInChildren<PlayerHealth>(true);
        }
    }

    private void CreateDefaultPrompt()
    {
        if (promptRoot != null)
        {
            return;
        }

        RectTransform parentRectTransform = transform as RectTransform;
        if (parentRectTransform == null)
        {
            return;
        }

        promptRoot = new GameObject("PlayerRespawnPrompt", typeof(RectTransform), typeof(Image));
        RectTransform rootRectTransform = promptRoot.GetComponent<RectTransform>();
        rootRectTransform.SetParent(parentRectTransform, false);
        rootRectTransform.anchorMin = new Vector2(0.5f, 0f);
        rootRectTransform.anchorMax = new Vector2(0.5f, 0f);
        rootRectTransform.pivot = new Vector2(0.5f, 0.5f);
        rootRectTransform.anchoredPosition = anchoredPosition;
        rootRectTransform.sizeDelta = size;

        Image backgroundImage = promptRoot.GetComponent<Image>();
        backgroundImage.color = new Color(0.03f, 0.03f, 0.035f, 0.82f);
        backgroundImage.raycastTarget = false;

        GameObject textGameObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform textRectTransform = textGameObject.GetComponent<RectTransform>();
        textRectTransform.SetParent(rootRectTransform, false);
        textRectTransform.anchorMin = Vector2.zero;
        textRectTransform.anchorMax = Vector2.one;
        textRectTransform.offsetMin = new Vector2(16f, 6f);
        textRectTransform.offsetMax = new Vector2(-16f, -6f);

        promptText = textGameObject.GetComponent<TextMeshProUGUI>();
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.enableAutoSizing = true;
        promptText.fontSizeMin = 14f;
        promptText.fontSizeMax = 24f;
        promptText.color = Color.white;
        promptText.raycastTarget = false;
        promptRoot.SetActive(false);
    }
}
