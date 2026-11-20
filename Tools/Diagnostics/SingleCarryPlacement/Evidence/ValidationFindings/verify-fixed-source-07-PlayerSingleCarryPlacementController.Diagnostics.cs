#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
using Unity.Netcode;
using UnityEngine;
public sealed partial class PlayerSingleCarryPlacementController
{
    private int previewRevision;
    private int lastRenderedPreviewRevision;
    private int editorRefreshCallbackKind;
    private int currentDiagnosticCallbackKind;

    partial void SetDiagnosticRefreshCallbackKind(int kind) => editorRefreshCallbackKind = kind;

    partial void BeginDiagnosticPreviewRefresh()
    {
        currentDiagnosticCallbackKind = editorRefreshCallbackKind;
        editorRefreshCallbackKind = 0;
        previewRevision = previewRevision == int.MaxValue ? 1 : previewRevision + 1;
    }

    partial void RecordDiagnosticPreview(Ray ray, Camera aimCamera, Vector3 previewPosition,
        Quaternion previewRotation, int availabilityFlags)
    {
        if (availabilityFlags == 0 || availabilityFlags == 2)
            lastRenderedPreviewRevision = previewRevision;
        if (networkPreview == null || !SingleCarryStateDivergenceTrace.Tracks(networkPreview.NetworkObject)) return;
        SingleCarryStateDivergenceTrace.RecordPreview(networkPreview.NetworkManager, networkPreview.NetworkObject,
            SingleCarryStateDivergenceTrace.GetLocalSpawnGeneration(networkPreview.NetworkObject),
            previewRevision, currentDiagnosticCallbackKind, transform.position, transform.rotation,
            aimCamera != null ? aimCamera.transform.position : default,
            aimCamera != null ? aimCamera.transform.rotation : Quaternion.identity,
            ray.origin, ray.direction, previewPosition, previewRotation, availabilityFlags);
    }

    partial void RecordDiagnosticPlacementRequest(ulong requestId, Vector3 position, Quaternion rotation, int flags)
    {
        if (networkPreview == null || !SingleCarryStateDivergenceTrace.Tracks(networkPreview.NetworkObject)) return;
        SingleCarryStateDivergenceTrace.RecordRequest(networkPreview.NetworkManager, networkPreview.NetworkObject,
            SingleCarryStateDivergenceTrace.GetLocalSpawnGeneration(networkPreview.NetworkObject),
            lastRenderedPreviewRevision, requestId, transform.position, transform.rotation,
            firstPersonController != null ? firstPersonController.HorizontalVelocity : Vector3.zero,
            lastRenderedAimRay.origin, lastRenderedAimRay.direction, position, rotation, flags);
    }
}
#endif
