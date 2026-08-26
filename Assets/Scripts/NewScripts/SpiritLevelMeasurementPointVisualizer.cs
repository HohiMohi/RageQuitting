using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(SpiritLevelMeasurementPoint))]
public sealed class SpiritLevelMeasurementPointVisualizer : MonoBehaviour
{
    private SpiritLevelMeasurementPoint point;
    private PlayerSpiritLevelController localController;
    private PlayerInteractionNew localInteraction;
    private PlayerHealth localHealth;
    private LineRenderer outline;
    private Material outlineMaterial;
    private float nextPlayerLookupTime;

    private void Awake()
    {
        point = GetComponent<SpiritLevelMeasurementPoint>();
        BuildOutline();
        SetVisible(false);
    }

    private void LateUpdate()
    {
        if (!TryResolveLocalPlayer() || point == null)
        {
            SetVisible(false);
            return;
        }

        SpiritLevelProfileSO profile = localController.SelectedProfile;
        bool visible = profile != null && point.IsAvailable && (localHealth == null || !localHealth.IsDowned);
        SetVisible(visible);
        if (!visible)
        {
            return;
        }

        bool measuring = localController.IsMeasuringPoint(point);
        bool targeted = localInteraction != null && localInteraction.CurrentTarget == point;
        Color color = measuring
            ? profile.markerMeasuringColor
            : targeted ? profile.markerTargetedColor : profile.markerColor;
        float pulse = targeted || measuring
            ? 1f + Mathf.Sin(Time.unscaledTime * profile.markerPulseSpeed * Mathf.PI * 2f) * profile.markerPulseAmount
            : 1f;

        outline.startWidth = outline.endWidth = Mathf.Max(0.002f, profile.markerLineWidth);
        outline.startColor = outline.endColor = color;
        SetMaterialColor(color);
        UpdateOutlineGeometry(pulse);
    }

    private bool TryResolveLocalPlayer()
    {
        if (localController != null && localController.isActiveAndEnabled)
        {
            return true;
        }

        if (Time.unscaledTime < nextPlayerLookupTime)
        {
            return false;
        }
        nextPlayerLookupTime = Time.unscaledTime + 0.5f;

        NetworkManager manager = NetworkManager.Singleton;
        GameObject playerObject = manager != null && manager.IsListening && manager.LocalClient != null
            ? manager.LocalClient.PlayerObject != null ? manager.LocalClient.PlayerObject.gameObject : null
            : null;
        localController = playerObject != null
            ? playerObject.GetComponent<PlayerSpiritLevelController>()
            : FindFirstObjectByType<PlayerSpiritLevelController>();
        if (localController == null)
        {
            return false;
        }

        localInteraction = localController.GetComponent<PlayerInteractionNew>();
        localHealth = localController.GetComponent<PlayerHealth>();
        return true;
    }

    private void BuildOutline()
    {
        GameObject outlineObject = new GameObject("SpiritLevelMeasurementOutline_Runtime")
        {
            layer = LayerMask.NameToLayer("Ignore Raycast"),
            hideFlags = HideFlags.DontSave
        };
        outlineObject.transform.SetParent(transform, false);
        outline = outlineObject.AddComponent<LineRenderer>();
        outline.useWorldSpace = false;
        outline.loop = true;
        outline.positionCount = 4;
        outline.textureMode = LineTextureMode.Stretch;
        outline.alignment = LineAlignment.View;
        outline.shadowCastingMode = ShadowCastingMode.Off;
        outline.receiveShadows = false;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (shader == null)
        {
            enabled = false;
            return;
        }

        outlineMaterial = new Material(shader)
        {
            name = "SpiritLevelMeasurementOutline_Runtime",
            hideFlags = HideFlags.DontSave,
            renderQueue = (int)RenderQueue.Transparent
        };
        ConfigureTransparentMaterial(outlineMaterial);
        outline.sharedMaterial = outlineMaterial;
        UpdateOutlineGeometry(1f);
    }

    private void UpdateOutlineGeometry(float scale)
    {
        if (outline == null || point == null)
        {
            return;
        }

        Vector2 size = point.MarkerSize * Mathf.Max(0.8f, scale);
        Vector3 center = point.MarkerLocalCenter;
        float halfX = size.x * 0.5f;
        float halfZ = size.y * 0.5f;
        outline.SetPosition(0, center + new Vector3(-halfX, 0f, -halfZ));
        outline.SetPosition(1, center + new Vector3(halfX, 0f, -halfZ));
        outline.SetPosition(2, center + new Vector3(halfX, 0f, halfZ));
        outline.SetPosition(3, center + new Vector3(-halfX, 0f, halfZ));
    }

    private void SetMaterialColor(Color color)
    {
        if (outlineMaterial == null)
        {
            return;
        }
        if (outlineMaterial.HasProperty("_BaseColor")) outlineMaterial.SetColor("_BaseColor", color);
        if (outlineMaterial.HasProperty("_Color")) outlineMaterial.SetColor("_Color", color);
    }

    private void SetVisible(bool visible)
    {
        if (outline != null && outline.gameObject.activeSelf != visible)
        {
            outline.gameObject.SetActive(visible);
        }
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    private void OnDisable()
    {
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (outlineMaterial != null)
        {
            Destroy(outlineMaterial);
        }
    }
}
