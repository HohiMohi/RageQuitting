using UnityEngine;
using UnityEngine.Serialization;

public sealed class SpiritLevelVial : MonoBehaviour
{
    private const float MaximumDisplayedLogicalTilt = 7f;

    [SerializeField] private Transform bubble;
    [FormerlySerializedAs("centerMarkLeft")]
    [SerializeField] private Transform greenMarkLeft;
    [FormerlySerializedAs("centerMarkRight")]
    [SerializeField] private Transform greenMarkRight;
    [SerializeField] private Transform yellowMarkLeft;
    [SerializeField] private Transform yellowMarkRight;
    [SerializeField] private Vector3 bubbleTravelAxis = Vector3.right;
    [SerializeField] private float bubbleSmoothTime = 0.12f;
    [SerializeField] private float measurementTransitionDuration = 0.2f;
    [SerializeField] private float physicalSensitivity = 2.5f;

    private Vector3 bubbleBaseLocalPosition;
    private float displayedOffset;
    private float offsetVelocity;
    private float measurementBlend;
    private float requestedLogicalTilt;
    private float bubbleDiameter = 0.055f;
    private float greenMarkOffset = 0.035f;
    private float yellowMarkOffset = 0.105f;
    private float maximumBubbleOffset = 0.14f;
    private bool measuring;
    private SpiritLevelProfileSO configuredProfile;
    private MaterialPropertyBlock markPropertyBlock;

    private void Awake()
    {
        ResolveVisualReferences();
        bubbleBaseLocalPosition = bubble != null ? bubble.localPosition : Vector3.zero;
        bubbleTravelAxis = bubbleTravelAxis.sqrMagnitude > 0.001f ? bubbleTravelAxis.normalized : Vector3.right;
        bubbleDiameter = CalculateBubbleDiameter();
    }

    private void Start()
    {
        if (configuredProfile != null) return;

        EquippableItem item = GetComponentInParent<EquippableItem>();
        EquippableItemSO itemData = item != null ? item.GetEquippableItemSO() : null;
        if (itemData != null && itemData.spiritLevelProfile != null)
            Configure(itemData.spiritLevelProfile);
    }

    private void LateUpdate()
    {
        if (bubble == null)
        {
            return;
        }

        float blendDuration = Mathf.Max(0.01f, measurementTransitionDuration);
        measurementBlend = Mathf.MoveTowards(measurementBlend, measuring ? 1f : 0f,
            Time.unscaledDeltaTime / blendDuration);

        Vector3 worldTravelAxis = transform.TransformDirection(bubbleTravelAxis);
        float physicalValue = Mathf.Clamp(Vector3.Dot(worldTravelAxis, Vector3.up) * physicalSensitivity, -1f, 1f);
        float physicalOffset = physicalValue * maximumBubbleOffset;
        float measurementOffset = EvaluateMeasurementOffset(requestedLogicalTilt);
        float targetOffset = Mathf.Lerp(physicalOffset, measurementOffset, measurementBlend);

        displayedOffset = Mathf.SmoothDamp(displayedOffset, targetOffset, ref offsetVelocity,
            Mathf.Max(0.01f, bubbleSmoothTime), Mathf.Infinity, Time.unscaledDeltaTime);
        bubble.localPosition = bubbleBaseLocalPosition + bubbleTravelAxis * displayedOffset;
    }

    public void Configure(SpiritLevelProfileSO profile)
    {
        if (profile == null || configuredProfile == profile)
        {
            return;
        }

        configuredProfile = profile;
        bubbleSmoothTime = profile.bubbleSmoothTime;
        measurementTransitionDuration = profile.measurementTransitionDuration;
        physicalSensitivity = profile.physicalSensitivity;
        ConfigureMarks(profile);
    }

    public void SetMeasurement(bool active, float logicalTilt)
    {
        measuring = active;
        requestedLogicalTilt = Mathf.Clamp(logicalTilt, -MaximumDisplayedLogicalTilt, MaximumDisplayedLogicalTilt);
    }

    internal float EvaluateMeasurementOffset(float logicalTilt)
    {
        float sign = Mathf.Sign(logicalTilt);
        float magnitude = Mathf.Clamp(Mathf.Abs(logicalTilt), 0f, MaximumDisplayedLogicalTilt);
        if (magnitude <= Mathf.Epsilon)
        {
            return 0f;
        }

        int lowerIndex = Mathf.FloorToInt(magnitude);
        int upperIndex = Mathf.Min(lowerIndex + 1, (int)MaximumDisplayedLogicalTilt);
        float lowerOffset = EvaluateMeasurementMagnitude(lowerIndex);
        float upperOffset = EvaluateMeasurementMagnitude(upperIndex);
        return sign * Mathf.Lerp(lowerOffset, upperOffset, magnitude - lowerIndex);
    }

    private float EvaluateMeasurementMagnitude(int logicalTilt)
    {
        switch (Mathf.Clamp(logicalTilt, 0, (int)MaximumDisplayedLogicalTilt))
        {
            case 0: return 0f;
            case 1: return greenMarkOffset;
            case 2: return greenMarkOffset + bubbleDiameter * 0.25f;
            case 3: return (greenMarkOffset + yellowMarkOffset) * 0.5f;
            case 4: return yellowMarkOffset - bubbleDiameter * 0.25f;
            case 5: return yellowMarkOffset;
            case 6: return yellowMarkOffset + bubbleDiameter * 0.25f;
            default: return maximumBubbleOffset;
        }
    }

    private void ResolveVisualReferences()
    {
        if (bubble == null)
        {
            bubble = transform.Find("Bubble");
        }

        Transform visualRoot = transform.parent;
        if (visualRoot == null)
        {
            return;
        }

        if (greenMarkLeft == null)
            greenMarkLeft = visualRoot.Find("GreenMarkLeft") ?? visualRoot.Find("CenterMarkLeft");
        if (greenMarkRight == null)
            greenMarkRight = visualRoot.Find("GreenMarkRight") ?? visualRoot.Find("CenterMarkRight");
        if (yellowMarkLeft == null) yellowMarkLeft = visualRoot.Find("YellowMarkLeft");
        if (yellowMarkRight == null) yellowMarkRight = visualRoot.Find("YellowMarkRight");
    }

    private void ConfigureMarks(SpiritLevelProfileSO profile)
    {
        ResolveVisualReferences();
        Transform visualRoot = transform.parent;
        if (visualRoot == null || bubble == null)
        {
            return;
        }

        bubbleDiameter = CalculateBubbleDiameter();
        float thickness = Mathf.Max(0.002f, profile.markThickness);
        float length = Mathf.Max(0.01f, profile.markLength);
        float clearance = Mathf.Max(0f, profile.bubbleToGreenMarkClearance);
        greenMarkOffset = bubbleDiameter * 0.5f + thickness * 0.5f + clearance;
        yellowMarkOffset = greenMarkOffset * 3f;
        maximumBubbleOffset = yellowMarkOffset + thickness * 0.5f + bubbleDiameter * 0.5f +
                              Mathf.Max(0f, profile.bubbleEndMargin);

        Vector3 bubbleCenter = visualRoot.InverseTransformPoint(bubble.parent.TransformPoint(bubbleBaseLocalPosition));
        float depthOffset = Mathf.Max(0.012f, thickness * 1.5f);
        Vector3 scale = new Vector3(thickness, length, Mathf.Max(0.006f, thickness * 0.65f));

        ConfigureMark(greenMarkLeft, bubbleCenter + new Vector3(-greenMarkOffset, 0f, -depthOffset), scale,
            profile.greenMarkColor);
        ConfigureMark(greenMarkRight, bubbleCenter + new Vector3(greenMarkOffset, 0f, -depthOffset), scale,
            profile.greenMarkColor);
        ConfigureMark(yellowMarkLeft, bubbleCenter + new Vector3(-yellowMarkOffset, 0f, -depthOffset), scale,
            profile.yellowMarkColor);
        ConfigureMark(yellowMarkRight, bubbleCenter + new Vector3(yellowMarkOffset, 0f, -depthOffset), scale,
            profile.yellowMarkColor);
    }

    private float CalculateBubbleDiameter()
    {
        if (bubble == null)
        {
            return 0.055f;
        }

        MeshFilter meshFilter = bubble.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            return Mathf.Max(0.005f, Mathf.Abs(Vector3.Dot(bubble.localScale, bubbleTravelAxis)));
        }

        Bounds bounds = meshFilter.sharedMesh.bounds;
        Matrix4x4 bubbleToVial = transform.worldToLocalMatrix * bubble.localToWorldMatrix;
        Vector3 xExtent = bubbleToVial.MultiplyVector(Vector3.right * bounds.extents.x);
        Vector3 yExtent = bubbleToVial.MultiplyVector(Vector3.up * bounds.extents.y);
        Vector3 zExtent = bubbleToVial.MultiplyVector(Vector3.forward * bounds.extents.z);
        float halfWidth = Mathf.Abs(Vector3.Dot(bubbleTravelAxis, xExtent)) +
                          Mathf.Abs(Vector3.Dot(bubbleTravelAxis, yExtent)) +
                          Mathf.Abs(Vector3.Dot(bubbleTravelAxis, zExtent));
        return Mathf.Max(0.005f, halfWidth * 2f);
    }

    private void ConfigureMark(Transform mark, Vector3 localPosition, Vector3 localScale, Color color)
    {
        if (mark == null)
        {
            return;
        }

        mark.localPosition = localPosition;
        mark.localRotation = Quaternion.identity;
        mark.localScale = localScale;
        Renderer markerRenderer = mark.GetComponent<Renderer>();
        if (markerRenderer == null)
        {
            return;
        }

        markPropertyBlock ??= new MaterialPropertyBlock();
        markerRenderer.GetPropertyBlock(markPropertyBlock);
        if (markerRenderer.sharedMaterial != null && markerRenderer.sharedMaterial.HasProperty("_BaseColor"))
            markPropertyBlock.SetColor("_BaseColor", color);
        if (markerRenderer.sharedMaterial != null && markerRenderer.sharedMaterial.HasProperty("_Color"))
            markPropertyBlock.SetColor("_Color", color);
        markerRenderer.SetPropertyBlock(markPropertyBlock);
    }
}
