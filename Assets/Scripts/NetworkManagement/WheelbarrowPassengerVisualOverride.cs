using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class WheelbarrowPassengerVisualOverride : MonoBehaviour
{
    private NetworkObject playerNetworkObject;
    private Transform playerBodyVisual;
    private WheelbarrowController wheelbarrow;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private bool hasOriginalPose;
    private bool isOverriding;
    private float nextDiagnosticsTime;

    public bool IsOverriding => isOverriding;
    public float AnchorError { get; private set; }

    private void Awake()
    {
        playerNetworkObject = GetComponent<NetworkObject>();
        CachePlayerBodyVisual();
    }

    private void LateUpdate()
    {
        CachePlayerBodyVisual();
        if (!ShouldOverride())
        {
            StopOverride();
            return;
        }

        Transform passengerAnchor = wheelbarrow.PassengerAnchor;
        if (passengerAnchor == null ||
            !wheelbarrow.TryGetPresentedAnchorPose(passengerAnchor, out Vector3 anchorPosition, out Quaternion anchorRotation))
        {
            StopOverride();
            return;
        }

        Vector3 targetPosition = anchorPosition + anchorRotation * originalLocalPosition;
        Quaternion targetRotation = anchorRotation * originalLocalRotation;
        AnchorError = Vector3.Distance(playerBodyVisual.position, targetPosition);
        playerBodyVisual.SetPositionAndRotation(targetPosition, targetRotation);
        isOverriding = true;

        if (wheelbarrow.Profile != null && wheelbarrow.Profile.EnableDiagnostics &&
            Time.unscaledTime >= nextDiagnosticsTime)
        {
            nextDiagnosticsTime = Time.unscaledTime + 1f;
            Debug.Log($"[WheelbarrowPassengerPresentation] client={playerNetworkObject.OwnerClientId} " +
                $"anchorError={AnchorError:F3}m buffer={wheelbarrow.GetComponent<WheelbarrowPresentationController>()?.SnapshotBufferDepth ?? 0}", this);
        }
    }

    private bool ShouldOverride()
    {
        if (playerNetworkObject == null || playerBodyVisual == null || !playerNetworkObject.IsSpawned || playerNetworkObject.IsOwner)
            return false;

        ulong playerClientId = playerNetworkObject.OwnerClientId;
        if (wheelbarrow != null && wheelbarrow.PassengerClientId == playerClientId)
            return true;

        wheelbarrow = WheelbarrowController.FindForPlayer(playerClientId);
        return wheelbarrow != null && wheelbarrow.PassengerClientId == playerClientId;
    }

    private void CachePlayerBodyVisual()
    {
        if (playerBodyVisual != null) return;
        if (!TryGetComponent(out PlayerNetworkSetup playerNetworkSetup) || playerNetworkSetup.PlayerBodyVisual == null) return;

        playerBodyVisual = playerNetworkSetup.PlayerBodyVisual.transform;
        originalLocalPosition = playerBodyVisual.localPosition;
        originalLocalRotation = playerBodyVisual.localRotation;
        hasOriginalPose = true;
    }

    private void StopOverride()
    {
        if (isOverriding && hasOriginalPose && playerBodyVisual != null)
        {
            playerBodyVisual.localPosition = originalLocalPosition;
            playerBodyVisual.localRotation = originalLocalRotation;
        }

        wheelbarrow = null;
        isOverriding = false;
        AnchorError = 0f;
    }

    private void OnDisable()
    {
        StopOverride();
    }
}
