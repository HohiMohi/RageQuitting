using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(210)]
public class PlayerSharedCarryAnchorPreview : MonoBehaviour
{
    [SerializeField] private PlayerInteractionNew playerInteraction;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private Color markerColor = new Color(0.2f, 1f, 0.25f, 0.72f);
    [SerializeField, Min(0.01f)] private float markerDiameter = 0.18f;
    [SerializeField, Min(0f)] private float surfacePadding = 0.02f;
    [SerializeField, Min(0.01f)] private float fadeInDuration = 0.12f;
    [SerializeField, Min(0.01f)] private float fadeOutDuration = 0.15f;
    [SerializeField, Range(0f, 0.2f)] private float pulseAmount = 0.05f;
    [SerializeField, Min(0f)] private float pulseSpeed = 3f;

    private GameObject marker;
    private Renderer markerRenderer;
    private Material markerMaterial;
    private float intensity;

    private void Awake()
    {
        playerInteraction ??= GetComponent<PlayerInteractionNew>();
        playerHealth ??= GetComponent<PlayerHealth>();
        CreateMarker();
    }

    private void LateUpdate()
    {
        bool hasPreview = TryGetPreview(out SharedCarryAnchorPreview preview);
        float duration = hasPreview ? fadeInDuration : fadeOutDuration;
        intensity = Mathf.MoveTowards(intensity, hasPreview ? 1f : 0f, Time.unscaledDeltaTime / duration);

        if (marker == null)
        {
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) * pulseAmount;
        float currentDiameter = markerDiameter * pulse;
        if (hasPreview)
        {
            marker.transform.position = preview.SurfaceWorldPosition
                + preview.SurfaceOutwardDirection * (currentDiameter * 0.5f + surfacePadding);
        }

        bool visible = intensity > 0.001f;
        if (marker.activeSelf != visible)
        {
            marker.SetActive(visible);
        }

        if (!visible)
        {
            return;
        }

        marker.transform.localScale = Vector3.one * currentDiameter;
        Color color = markerColor;
        color.a *= intensity;
        SetMarkerColor(color);
    }

    private bool TryGetPreview(out SharedCarryAnchorPreview preview)
    {
        preview = default;
        if (!isActiveAndEnabled || playerInteraction == null || playerInteraction.HasPickedUpObject
            || (playerHealth != null && playerHealth.IsDowned) || !IsLocalPlayer())
        {
            return false;
        }

        MonoBehaviour target = playerInteraction.CurrentTarget;
        ISharedCarryAnchorPreviewProvider provider = ResolveProvider(target);
        return provider != null && provider.SupportsAnchorPreview
            && provider.TryGetAnchorPreview(playerInteraction, out preview);
    }

    private bool IsLocalPlayer()
    {
        if (!TryGetComponent(out NetworkObject networkObject) || !networkObject.IsSpawned)
        {
            return true;
        }

        return networkObject.IsOwner;
    }

    private static ISharedCarryAnchorPreviewProvider ResolveProvider(MonoBehaviour target)
    {
        if (target is ISharedCarryAnchorPreviewProvider directProvider)
        {
            return directProvider;
        }

        if (target == null)
        {
            return null;
        }

        foreach (MonoBehaviour component in target.GetComponentsInParent<MonoBehaviour>(true))
        {
            if (component is ISharedCarryAnchorPreviewProvider provider)
            {
                return provider;
            }
        }

        return null;
    }

    private void CreateMarker()
    {
        marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "SharedCarryAnchorPreview";
        marker.layer = LayerMask.NameToLayer("Ignore Raycast");
        marker.transform.SetParent(null);

        if (marker.TryGetComponent(out Collider markerCollider))
        {
            Destroy(markerCollider);
        }

        markerRenderer = marker.GetComponent<Renderer>();
        markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
        markerRenderer.receiveShadows = false;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color");
        if (shader == null)
        {
            Debug.LogWarning("PlayerSharedCarryAnchorPreview: no compatible unlit shader was found.");
            marker.SetActive(false);
            return;
        }

        markerMaterial = new Material(shader)
        {
            name = "SharedCarryAnchorPreview_Runtime",
            renderQueue = (int)RenderQueue.Transparent
        };
        markerMaterial.SetFloat("_Surface", 1f);
        markerMaterial.SetFloat("_ZWrite", 0f);
        markerMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        markerMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        markerMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        markerMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        markerRenderer.sharedMaterial = markerMaterial;
        marker.SetActive(false);
    }

    private void SetMarkerColor(Color color)
    {
        if (markerMaterial == null)
        {
            return;
        }

        if (markerMaterial.HasProperty("_BaseColor"))
        {
            markerMaterial.SetColor("_BaseColor", color);
        }
        else
        {
            markerMaterial.color = color;
        }
    }

    private void OnDisable()
    {
        intensity = 0f;
        if (marker != null)
        {
            marker.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (marker != null)
        {
            Destroy(marker);
        }

        if (markerMaterial != null)
        {
            Destroy(markerMaterial);
        }
    }
}
