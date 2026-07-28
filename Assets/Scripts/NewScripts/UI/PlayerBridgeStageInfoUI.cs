using TMPro;
using UnityEngine;

public class PlayerBridgeStageInfoUI : MonoBehaviour
{
    [SerializeField] private PlayerInputNew playerInput;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;

    public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        EnsureReferences();
        Hide();
    }

    private void OnEnable()
    {
        EnsureReferences();
        BridgeStageInfoManager.MessageRequested += Show;
        if (playerInput != null)
        {
            playerInput.OnDismissInfoOverlay += PlayerInput_OnDismissInfoOverlay;
        }
    }

    private void OnDisable()
    {
        BridgeStageInfoManager.MessageRequested -= Show;
        if (playerInput != null)
        {
            playerInput.OnDismissInfoOverlay -= PlayerInput_OnDismissInfoOverlay;
        }
    }

    public void Show(string title, string message)
    {
        EnsureReferences();
        if (titleText != null)
        {
            titleText.text = title ?? string.Empty;
        }

        if (messageText != null)
        {
            messageText.text = message ?? string.Empty;
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }
    }

    public void Hide()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    private void PlayerInput_OnDismissInfoOverlay(object sender, System.EventArgs e)
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private void EnsureReferences()
    {
        if (playerInput == null)
        {
            playerInput = GetComponentInParent<PlayerInputNew>();
        }
    }
}
