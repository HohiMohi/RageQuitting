using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BridgeLevelingAdjustmentPointVisualizer : MonoBehaviour
{
    [Header("Target marker")]
    [SerializeField] private Color markerColor = new Color(1f, 0.48f, 0.08f, 0.58f);
    [SerializeField] private Color targetedColor = new Color(1f, 0.72f, 0.16f, 0.95f);
    [SerializeField, Min(0.002f)] private float lineWidth = 0.018f;
    [SerializeField, Min(0.02f)] private float minimumRadius = 0.1f;
    [SerializeField, Range(0f, 0.25f)] private float pulseAmount = 0.07f;
    [SerializeField, Min(0f)] private float pulseSpeed = 2.5f;

    private BridgeConstructionSite site;
    private IInteractableNew interactionTarget;
    private PlayerInventory localInventory;
    private PlayerInteractionNew localInteraction;
    private PlayerHealth localHealth;
    private LineRenderer outerRing;
    private LineRenderer innerRing;
    private Material markerMaterial;
    private Vector3 markerCenter;
    private float markerRadius;
    private float nextPlayerLookupTime;

    private void Awake()
    {
        site = GetComponentInParent<BridgeConstructionSite>();
        interactionTarget = GetComponent<BridgeAbutmentWorkPoint>();
        if (interactionTarget == null) interactionTarget = GetComponent<BridgeGirderWorkPoint>();
        ResolveGeometry();
        BuildTarget();
        SetVisible(false);
    }

    private void LateUpdate()
    {
        if (!TryResolveLocalPlayer() || site is not ILevelingMeasurementTarget target ||
            !target.IsLevelingActive || localHealth != null && localHealth.IsDowned ||
            !HasIndustrialHammerSelected())
        {
            SetVisible(false);
            return;
        }

        bool targeted = localInteraction != null && ReferenceEquals(localInteraction.CurrentTarget, interactionTarget);
        Color color = targeted ? targetedColor : markerColor;
        float pulse = targeted
            ? 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) * pulseAmount
            : 1f;

        SetVisible(true);
        ConfigureRenderer(outerRing, markerRadius * pulse, color);
        ConfigureRenderer(innerRing, markerRadius * 0.45f * pulse, color);
        SetMaterialColor(color);
    }

    private bool HasIndustrialHammerSelected()
    {
        EquippableItemSO selected = localInventory != null ? localInventory.GetCurrentSelectedItem() : null;
        return selected != null && selected.itemType == EquippableItemType.IndustrialHammer;
    }

    private bool TryResolveLocalPlayer()
    {
        if (localInventory != null && localInventory.isActiveAndEnabled)
        {
            return true;
        }

        if (Time.unscaledTime < nextPlayerLookupTime)
        {
            return false;
        }
        nextPlayerLookupTime = Time.unscaledTime + 0.5f;

        NetworkManager manager = NetworkManager.Singleton;
        GameObject playerObject = manager != null && manager.IsListening && manager.LocalClient != null &&
                                  manager.LocalClient.PlayerObject != null
            ? manager.LocalClient.PlayerObject.gameObject
            : null;
        localInventory = playerObject != null
            ? playerObject.GetComponent<PlayerInventory>()
            : FindFirstObjectByType<PlayerInventory>();
        if (localInventory == null)
        {
            return false;
        }

        localInteraction = localInventory.GetComponent<PlayerInteractionNew>();
        localHealth = localInventory.GetComponent<PlayerHealth>();
        return true;
    }

    private void ResolveGeometry()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
        {
            markerCenter = box.center + Vector3.up * (box.size.y * 0.5f + 0.015f);
            markerRadius = Mathf.Max(minimumRadius, Mathf.Min(box.size.x, box.size.z) * 0.42f);
            return;
        }

        Collider pointCollider = GetComponent<Collider>();
        markerCenter = Vector3.up * 0.03f;
        markerRadius = pointCollider != null
            ? Mathf.Max(minimumRadius, Mathf.Min(pointCollider.bounds.size.x, pointCollider.bounds.size.z) * 0.35f)
            : minimumRadius;
    }

    private void BuildTarget()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (shader == null)
        {
            enabled = false;
            return;
        }

        markerMaterial = new Material(shader)
        {
            name = "BridgeLevelingAdjustmentMarker_Runtime",
            hideFlags = HideFlags.DontSave,
            renderQueue = (int)RenderQueue.Transparent
        };
        ConfigureTransparentMaterial(markerMaterial);
        outerRing = CreateRing("AdjustmentTargetOuter_Runtime");
        innerRing = CreateRing("AdjustmentTargetInner_Runtime");
    }

    private LineRenderer CreateRing(string objectName)
    {
        GameObject ringObject = new GameObject(objectName)
        {
            layer = LayerMask.NameToLayer("Ignore Raycast"),
            hideFlags = HideFlags.DontSave
        };
        ringObject.transform.SetParent(transform, false);
        LineRenderer ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 32;
        ring.alignment = LineAlignment.View;
        ring.textureMode = LineTextureMode.Stretch;
        ring.shadowCastingMode = ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.sharedMaterial = markerMaterial;
        return ring;
    }

    private void ConfigureRenderer(LineRenderer ring, float radius, Color color)
    {
        if (ring == null)
        {
            return;
        }

        ring.startWidth = ring.endWidth = Mathf.Max(0.002f, lineWidth);
        ring.startColor = ring.endColor = color;
        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i / (float)ring.positionCount * Mathf.PI * 2f;
            ring.SetPosition(i, markerCenter + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    private void SetMaterialColor(Color color)
    {
        if (markerMaterial == null)
        {
            return;
        }
        if (markerMaterial.HasProperty("_BaseColor")) markerMaterial.SetColor("_BaseColor", color);
        if (markerMaterial.HasProperty("_Color")) markerMaterial.SetColor("_Color", color);
    }

    private void SetVisible(bool visible)
    {
        if (outerRing != null && outerRing.gameObject.activeSelf != visible) outerRing.gameObject.SetActive(visible);
        if (innerRing != null && innerRing.gameObject.activeSelf != visible) innerRing.gameObject.SetActive(visible);
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

#if UNITY_EDITOR
    public void ConfigureEditor(Color normal, Color targeted, float width, float radius)
    {
        markerColor = normal;
        targetedColor = targeted;
        lineWidth = Mathf.Max(0.002f, width);
        minimumRadius = Mathf.Max(0.02f, radius);
    }
#endif

    private void OnDisable()
    {
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (markerMaterial != null) Destroy(markerMaterial);
    }
}
