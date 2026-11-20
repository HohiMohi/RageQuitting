using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal enum BridgeDeckPanelPlacementHudKind : byte
{
    Hidden,
    Occupied,
    Blocked,
    Aligned,
    Corrections
}

internal enum BridgeDeckPanelPlacementHudBlockReason : byte
{
    None,
    PhysicalPlacementBlocked,
    ConfirmationUnavailable
}

internal readonly struct BridgeDeckPanelPlacementHudState
{
    public readonly BridgeDeckPanelPlacementHudKind Kind;
    public readonly BridgeDeckPanelPlacementHudBlockReason BlockReason;
    public readonly int RightCentimeters;
    public readonly int ForwardCentimeters;
    public readonly int HeightCentimeters;
    public readonly int LocalYRotationDegrees;
    public readonly bool RightSubCentimeter;
    public readonly bool ForwardSubCentimeter;
    public readonly bool HeightSubCentimeter;
    public readonly bool ResetTiltRequired;

    public BridgeDeckPanelPlacementHudState(BridgeDeckPanelPlacementHudKind kind,
        BridgeDeckPanelPlacementHudBlockReason blockReason, int rightCentimeters, int forwardCentimeters,
        int heightCentimeters, int localYRotationDegrees, bool rightSubCentimeter,
        bool forwardSubCentimeter, bool heightSubCentimeter, bool resetTiltRequired)
    {
        Kind = kind;
        BlockReason = blockReason;
        RightCentimeters = rightCentimeters;
        ForwardCentimeters = forwardCentimeters;
        HeightCentimeters = heightCentimeters;
        LocalYRotationDegrees = localYRotationDegrees;
        RightSubCentimeter = rightSubCentimeter;
        ForwardSubCentimeter = forwardSubCentimeter;
        HeightSubCentimeter = heightSubCentimeter;
        ResetTiltRequired = resetTiltRequired;
    }

    public static BridgeDeckPanelPlacementHudState Hidden => default;

    internal static BridgeDeckPanelPlacementHudState Blocked(BridgeDeckPanelPlacementHudBlockReason reason)
        => new(BridgeDeckPanelPlacementHudKind.Blocked, reason, 0, 0, 0, 0, false, false, false, false);

    internal bool Equals(BridgeDeckPanelPlacementHudState other) => Kind == other.Kind
        && BlockReason == other.BlockReason && RightCentimeters == other.RightCentimeters
        && ForwardCentimeters == other.ForwardCentimeters && HeightCentimeters == other.HeightCentimeters
        && LocalYRotationDegrees == other.LocalYRotationDegrees
        && RightSubCentimeter == other.RightSubCentimeter
        && ForwardSubCentimeter == other.ForwardSubCentimeter
        && HeightSubCentimeter == other.HeightSubCentimeter
        && ResetTiltRequired == other.ResetTiltRequired;
}

/// <summary>Top-center, non-interactive readout for the locked physical deck-panel target.</summary>
public sealed class BridgeDeckPanelPlacementHud : MonoBehaviour
{
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private GameObject statusCard;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Image statusIcon;
    [SerializeField] private GameObject rightCard;
    [SerializeField] private Image rightIcon;
    [SerializeField] private TMP_Text rightText;
    [SerializeField] private GameObject forwardCard;
    [SerializeField] private Image forwardIcon;
    [SerializeField] private TMP_Text forwardText;
    [SerializeField] private GameObject heightCard;
    [SerializeField] private Image heightIcon;
    [SerializeField] private TMP_Text heightText;
    [SerializeField] private GameObject rotationCard;
    [SerializeField] private Image rotationIcon;
    [SerializeField] private TMP_Text rotationText;
    [SerializeField] private Sprite arrowSprite;
    [SerializeField] private Sprite tiltSprite;
    [SerializeField] private Sprite rotateSprite;
    [SerializeField] private Sprite checkSprite;

    private BridgeDeckPanelPlacementHudState lastState;
    private bool hasLastState;
    private Color originalStatusColor = Color.white;

    private void Awake()
    {
        if (statusText != null) originalStatusColor = statusText.color;
        SetState(BridgeDeckPanelPlacementHudState.Hidden);
    }

    internal void SetState(BridgeDeckPanelPlacementHudState state)
    {
        if (hasLastState && lastState.Equals(state)) return;
        BridgeDeckPanelPlacementHudState previous = lastState;
        lastState = state;
        hasLastState = true;
        if (visualRoot != null) visualRoot.SetActive(state.Kind != BridgeDeckPanelPlacementHudKind.Hidden);
        if (state.Kind == BridgeDeckPanelPlacementHudKind.Hidden) return;

        bool correction = state.Kind == BridgeDeckPanelPlacementHudKind.Corrections;
        if (statusCard != null) statusCard.SetActive(!correction);
        if (rightCard != null) rightCard.SetActive(correction && state.RightCentimeters != 0);
        if (forwardCard != null) forwardCard.SetActive(correction && state.ForwardCentimeters != 0);
        if (heightCard != null) heightCard.SetActive(correction && state.HeightCentimeters != 0);
        if (rotationCard != null) rotationCard.SetActive(correction
            && (state.ResetTiltRequired || state.LocalYRotationDegrees != 0));

        if (statusText != null && !correction
            && (!hasLastState || previous.Kind != state.Kind || previous.BlockReason != state.BlockReason))
        {
            statusText.text = state.Kind switch
            {
                BridgeDeckPanelPlacementHudKind.Occupied => "Slot occupied  ·  F, then F to select another slot",
                BridgeDeckPanelPlacementHudKind.Blocked => GetBlockMessage(state.BlockReason),
                BridgeDeckPanelPlacementHudKind.Aligned => "Aligned",
                _ => string.Empty
            };
            statusText.color = state.Kind == BridgeDeckPanelPlacementHudKind.Aligned
                ? new Color(0.32f, 0.95f, 0.46f, originalStatusColor.a)
                : state.Kind == BridgeDeckPanelPlacementHudKind.Occupied
                    ? new Color(1f, 0.67f, 0.22f, originalStatusColor.a)
                    : new Color(1f, 0.38f, 0.32f, originalStatusColor.a);
        }
        if (statusIcon != null && !correction && (!hasLastState || previous.Kind != state.Kind))
        {
            bool showCheck = state.Kind == BridgeDeckPanelPlacementHudKind.Aligned;
            statusIcon.gameObject.SetActive(showCheck);
            statusIcon.sprite = showCheck ? checkSprite : null;
            statusIcon.color = state.Kind == BridgeDeckPanelPlacementHudKind.Aligned
                ? new Color(0.32f, 0.95f, 0.46f, 1f) : Color.white;
        }
        if (rightText != null && state.RightCentimeters != 0
            && (previous.RightCentimeters != state.RightCentimeters || previous.RightSubCentimeter != state.RightSubCentimeter))
        {
            rightText.text = $"{(state.RightCentimeters > 0 ? "Right" : "Left")} {FormatMagnitude(state.RightCentimeters, state.RightSubCentimeter)}";
            if (rightIcon != null) { rightIcon.sprite = arrowSprite; rightIcon.rectTransform.localScale = new Vector3(state.RightCentimeters < 0 ? -1f : 1f, 1f, 1f); }
        }
        if (forwardText != null && state.ForwardCentimeters != 0
            && (previous.ForwardCentimeters != state.ForwardCentimeters || previous.ForwardSubCentimeter != state.ForwardSubCentimeter))
        {
            forwardText.text = $"{(state.ForwardCentimeters > 0 ? "Forward" : "Back")} {FormatMagnitude(state.ForwardCentimeters, state.ForwardSubCentimeter)}";
            if (forwardIcon != null) { forwardIcon.sprite = arrowSprite; forwardIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, state.ForwardCentimeters > 0 ? 90f : -90f); }
        }
        if (heightText != null && state.HeightCentimeters != 0
            && (previous.HeightCentimeters != state.HeightCentimeters || previous.HeightSubCentimeter != state.HeightSubCentimeter))
        {
            heightText.text = $"{(state.HeightCentimeters > 0 ? "Raise" : "Lower")} {FormatMagnitude(state.HeightCentimeters, state.HeightSubCentimeter)}";
            if (heightIcon != null) { heightIcon.sprite = arrowSprite; heightIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, state.HeightCentimeters > 0 ? 90f : -90f); }
        }
        if (rotationText != null && (state.ResetTiltRequired || state.LocalYRotationDegrees != 0)
            && (previous.LocalYRotationDegrees != state.LocalYRotationDegrees || previous.ResetTiltRequired != state.ResetTiltRequired))
        {
            rotationText.text = state.ResetTiltRequired ? "F → F"
                : $"Rotate {(state.LocalYRotationDegrees > 0 ? "right" : "left")} {Mathf.Abs(state.LocalYRotationDegrees)}°";
            if (rotationIcon != null) { rotationIcon.sprite = state.ResetTiltRequired ? tiltSprite : rotateSprite; rotationIcon.rectTransform.localScale = new Vector3(state.LocalYRotationDegrees < 0 ? -1f : 1f, 1f, 1f); }
        }
    }

    private static string GetBlockMessage(BridgeDeckPanelPlacementHudBlockReason reason) => reason switch
    {
        BridgeDeckPanelPlacementHudBlockReason.ConfirmationUnavailable => "BLOCKED  ·  Placement unavailable",
        _ => "BLOCKED  ·  Check panel clearance"
    };

    private static string FormatMagnitude(int centimeters, bool subCentimeter)
    {
        if (subCentimeter) return "<1 cm";
        return $"{Mathf.Abs(centimeters)} cm";
    }
}
