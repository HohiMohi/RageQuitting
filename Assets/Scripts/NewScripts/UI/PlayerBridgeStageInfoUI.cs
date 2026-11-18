using System;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PlayerBridgeStageInfoUI : MonoBehaviour
{
    [SerializeField] private PlayerInputNew playerInput;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;

    private BridgeStageInfoManager guidanceScene;
    private GameplayManager guidanceGameplay;
    private BridgeGuidanceSource guidanceSource;
    private BridgeRequirementsUI requirements;
    private NetworkObject playerNetworkObject;
    private GameObject guidanceHud;
    private TextMeshProUGUI guidanceText;
    private RectTransform guidanceRect;
    private BridgeGuidanceSnapshot snapshot;
    private float nextConfigurationRead;
    private string previousCompact = string.Empty;
    private string previousDetails = string.Empty;

    public bool IsVisible => panelRoot != null && panelRoot.activeSelf;
    public bool IsTutorialMode => guidanceScene != null && guidanceScene.UsesBridgeGuidance;
    public bool IsTutorialHudVisible => guidanceHud != null && guidanceHud.activeSelf;
    public BridgeGuidanceSnapshot TutorialSnapshot => snapshot;

    private bool IsLocalPlayer => playerInput != null &&
        (playerNetworkObject != null && playerNetworkObject.IsSpawned ? playerNetworkObject.IsOwner
            : NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening);

    private void Awake()
    {
        EnsureReferences();
        Hide();
    }

    private void OnEnable()
    {
        EnsureReferences();
        BridgeStageInfoManager.MessageRequested += Show;
        if (playerInput != null) playerInput.OnDismissInfoOverlay += PlayerInput_OnDismissInfoOverlay;
        if (requirements != null) requirements.VisibilityChanged += OnRequirementsVisibilityChanged;
        RefreshConfiguration();
    }

    private void OnDisable()
    {
        BridgeStageInfoManager.MessageRequested -= Show;
        if (playerInput != null) playerInput.OnDismissInfoOverlay -= PlayerInput_OnDismissInfoOverlay;
        if (requirements != null) requirements.VisibilityChanged -= OnRequirementsVisibilityChanged;
        ReleaseGuidance();
        Hide();
    }

    protected void OnDestroy()
    {
        if (guidanceHud != null) Destroy(guidanceHud);
    }

    protected void Update()
    {
        if (!IsLocalPlayer)
        {
            if (guidanceSource != null || guidanceScene != null) ReleaseGuidance();
            Hide();
            return;
        }
        float now = Time.unscaledTime;
        if (now >= nextConfigurationRead)
        {
            nextConfigurationRead = now + BridgeGuidanceSource.ReadInterval;
            RefreshConfiguration();
            RenderGuidance(); // Binding overrides can change without bridge state changing.
        }
        if (guidanceSource != null) guidanceSource.Tick(now);
    }

    private void EnsureReferences()
    {
        if (playerInput == null) playerInput = GetComponentInParent<PlayerInputNew>();
        if (playerInput == null) return;
        if (playerNetworkObject == null) playerInput.TryGetComponent(out playerNetworkObject);
        if (requirements == null) requirements = playerInput.GetComponentInChildren<BridgeRequirementsUI>(true);
    }

    private void RefreshConfiguration()
    {
        if (!IsLocalPlayer) return;
        BridgeStageInfoManager nextScene = BridgeStageInfoManager.GetCurrentGuidance();
        GameplayManager nextGameplay = nextScene != null ? GameplayManager.Instance : null;
        if (nextScene == guidanceScene && nextGameplay == guidanceGameplay)
        {
            UpdateHudVisibility();
            return;
        }
        ReleaseGuidance();
        guidanceScene = nextScene;
        guidanceGameplay = nextGameplay;
        if (guidanceScene == null) return;
        guidanceScene.PresentationPreferenceChanged += OnPreferenceChanged;
        Hide();
        EnsureGuidanceHud();
        PlayerInteractionNew interaction = playerInput.GetComponent<PlayerInteractionNew>();
        if (guidanceGameplay != null && interaction != null)
        {
            guidanceSource = new BridgeGuidanceSource(guidanceGameplay, interaction);
            guidanceSource.Changed += OnSnapshotChanged;
            guidanceSource.Tick(Time.unscaledTime);
            snapshot = guidanceSource.Snapshot;
        }
        RenderGuidance();
    }

    private void ReleaseGuidance()
    {
        if (guidanceSource != null)
        {
            guidanceSource.Changed -= OnSnapshotChanged;
            guidanceSource.Dispose();
            guidanceSource = null;
        }
        if (guidanceScene != null) guidanceScene.PresentationPreferenceChanged -= OnPreferenceChanged;
        guidanceScene = null;
        guidanceGameplay = null;
        snapshot = default;
        previousCompact = string.Empty;
        previousDetails = string.Empty;
        if (guidanceHud != null) guidanceHud.SetActive(false);
        if (requirements != null) requirements.SetTutorialHelp(string.Empty);
    }

    private void OnSnapshotChanged(BridgeGuidanceSnapshot value)
    {
        snapshot = value;
        RenderGuidance();
    }

    private void OnPreferenceChanged()
    {
        RefreshConfiguration();
        UpdateHudVisibility();
    }

    private void OnRequirementsVisibilityChanged(bool visible) => UpdateHudVisibility();

    private void UpdateHudVisibility()
    {
        bool visible = IsTutorialMode && IsLocalPlayer && guidanceScene.ShowTutorialHud &&
            (requirements == null || !requirements.IsVisible);
        if (guidanceHud != null && guidanceHud.activeSelf != visible) guidanceHud.SetActive(visible);
    }

    private void RenderGuidance()
    {
        if (!IsTutorialMode || !IsLocalPlayer) return;
        EnsureGuidanceHud();
        Func<string, string> binding = playerInput.GetInitializedBindingDisplay;
        string compact = BridgeGuidancePresentation.Compact(snapshot, binding);
        string details = BridgeGuidancePresentation.Details(snapshot, binding);
        if (guidanceText != null && compact != previousCompact)
        {
            previousCompact = compact;
            guidanceText.text = compact;
            float height = guidanceText.GetPreferredValues(compact, 348f, 0f).y + 24f;
            guidanceRect.sizeDelta = new Vector2(372f, Mathf.Max(72f, height));
        }
        if (requirements != null && details != previousDetails)
        {
            previousDetails = details;
            requirements.SetTutorialHelp(details);
        }
        UpdateHudVisibility();
    }

    private void EnsureGuidanceHud()
    {
        if (guidanceHud != null) return;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null && playerInput != null) canvas = playerInput.GetComponentInChildren<Canvas>(true);
        if (canvas == null) return;
        guidanceHud = new GameObject("BridgeTutorialHUD", typeof(RectTransform), typeof(Image));
        guidanceRect = guidanceHud.GetComponent<RectTransform>();
        guidanceRect.SetParent(canvas.transform, false);
        guidanceRect.anchorMin = Vector2.one;
        guidanceRect.anchorMax = Vector2.one;
        guidanceRect.pivot = Vector2.one;
        // GameTimerUI occupies (-24,-24), 176x52. Reserve its position even while hidden.
        guidanceRect.anchoredPosition = new Vector2(-24f, -88f);
        Image background = guidanceHud.GetComponent<Image>();
        background.color = new Color(.035f, .05f, .065f, .9f);
        background.raycastTarget = false;
        GameObject textObject = new GameObject("Guidance", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(guidanceRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 12f);
        textRect.offsetMax = new Vector2(-12f, -12f);
        guidanceText = textObject.GetComponent<TextMeshProUGUI>();
        guidanceText.font = TMP_Settings.defaultFontAsset;
        guidanceText.fontSize = 17f;
        guidanceText.color = Color.white;
        guidanceText.textWrappingMode = TextWrappingModes.Normal;
        guidanceText.alignment = TextAlignmentOptions.TopLeft;
        guidanceText.raycastTarget = false;
        guidanceHud.SetActive(false);
    }

    public void Show(string title, string message)
    {
        EnsureReferences();
        bool remote = playerInput != null ? !IsLocalPlayer : NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (remote || IsTutorialMode || BridgeStageInfoManager.GetCurrentGuidance() != null) return;
        if (titleText != null) titleText.text = title ?? string.Empty;
        if (messageText != null) messageText.text = message ?? string.Empty;
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void PlayerInput_OnDismissInfoOverlay(object sender, EventArgs e)
    {
        if (IsVisible) Hide();
    }
}

public static class BridgeGuidancePresentation
{
    public static string Compact(BridgeGuidanceSnapshot snapshot, Func<string, string> binding)
    {
        string help = Binding(binding, "ToggleBridgeRequirements");
        if (!snapshot.IsReady) return "<b>Bridge tutorial</b>\nWaiting for bridge state\n[" + help + "] for help";
        if (snapshot.IsComplete) return "<b>Bridge complete</b>\n[" + help + "] for help";
        StringBuilder text = new StringBuilder();
        text.Append("<b>").Append(snapshot.Goal).Append("</b> ").Append(snapshot.CompletedCount).Append('/').Append(snapshot.RequiredCount);
        BridgeGuidanceInstruction instruction = snapshot.Instruction;
        if (instruction.HasValue)
        {
            text.Append('\n').Append(instruction.FocusName).Append("\n").Append(instruction.ShortText);
            string progress = Progress(instruction);
            if (progress.Length > 0) text.Append('\n').Append(progress);
        }
        text.Append("\n[").Append(help).Append("] for help");
        return text.ToString();
    }

    public static string Details(BridgeGuidanceSnapshot snapshot, Func<string, string> binding)
    {
        if (!snapshot.IsReady) return "<b>Tutorial help</b>\nWaiting for initialized bridge state. Guidance will update automatically.";
        if (snapshot.IsComplete) return "<b>Tutorial help</b>\nBridge complete. All required bridge work is complete.";
        BridgeGuidanceInstruction instruction = snapshot.Instruction;
        StringBuilder text = new StringBuilder("<b>Tutorial help</b>\n");
        text.Append(snapshot.Goal).Append(": ").Append(snapshot.CompletedCount).Append('/').Append(snapshot.RequiredCount);
        if (!instruction.HasValue) return text.Append("\nWaiting for actionable bridge work.").ToString();
        text.Append('\n').Append(instruction.FocusName).Append("\n<b>").Append(instruction.ShortText).Append("</b>\n").Append(instruction.DetailedHelp);
        if (!string.IsNullOrEmpty(instruction.Tool)) text.Append("\nTools: ").Append(instruction.Tool);
        string progress = Progress(instruction);
        if (progress.Length > 0) text.Append('\n').Append(progress);
        AppendBinding(text, instruction.Actions, BridgeGuidanceActions.Interact, "Interact / confirm", "Interact", binding);
        AppendBinding(text, instruction.Actions, BridgeGuidanceActions.Primary, "Use tool", "Action", binding);
        AppendBinding(text, instruction.Actions, BridgeGuidanceActions.Secondary, "Alternate / bucket / crowbar", "ActionAlt", binding);
        AppendBinding(text, instruction.Actions, BridgeGuidanceActions.PlacementToggle, "Hand placement", "ToggleSingleCarryPlacement", binding);
        AppendBinding(text, instruction.Actions, BridgeGuidanceActions.PlacementConfirm, "Confirm placement", "Interact", binding);
        AppendBinding(text, instruction.Actions, BridgeGuidanceActions.PlacementRotation, "Rotate placement", "SingleCarryPlacementRotation", binding);
        if ((instruction.Actions & BridgeGuidanceActions.PlacementRotation) != 0)
            text.Append("\nPlacement modifier: [").Append(Binding(binding, "SingleCarryPlacementControlModifier")).Append(']');
        AppendBinding(text, instruction.Actions, BridgeGuidanceActions.PourControl, "Pour control", "Look", binding);
        return text.ToString();
    }

    public static string Progress(BridgeGuidanceInstruction instruction)
    {
        StringBuilder text = new StringBuilder();
        if (instruction.RequiredWork > 0)
            text.Append(instruction.StepKey == "panel.nail" ? "Quarters: " : "Progress: ")
                .Append(instruction.CompletedWork.ToString("0.#")).Append('/').Append(instruction.RequiredWork.ToString("0.#"));
        else if (instruction.Progress > 0) text.Append("Progress: ").Append(Mathf.RoundToInt(instruction.Progress * 100f)).Append('%');
        if (instruction.RequiredNailHits > 0) text.Append(text.Length > 0 ? " | " : "").Append("Nail: ")
            .Append(instruction.NailHits).Append('/').Append(instruction.RequiredNailHits).Append(" strikes");
        if (instruction.RemainingSeconds > 0) text.Append(text.Length > 0 ? " | " : "").Append(instruction.RemainingSeconds.ToString("0")).Append(" s remaining");
        return text.ToString();
    }

    private static string Binding(Func<string, string> binding, string action)
    {
        string value = binding != null ? binding(action) : string.Empty;
        return string.IsNullOrEmpty(value) ? "bindings unavailable" : value;
    }

    private static void AppendBinding(StringBuilder text, BridgeGuidanceActions actions, BridgeGuidanceActions flag,
        string label, string action, Func<string, string> binding)
    {
        if ((actions & flag) != 0) text.Append('\n').Append(label).Append(": [").Append(Binding(binding, action)).Append(']');
    }
}
