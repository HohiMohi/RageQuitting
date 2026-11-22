using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerTemporaryStatusController))]
public sealed class PlayerMudStatusController : MonoBehaviour
{
    [SerializeField] private PlayerStatusCatalogSO catalog;
    [SerializeField] private CanvasGroup mudOverlay;
    private PlayerMudOverlay mudPresenter;
    [SerializeField, Min(0.02f)] private float sampleInterval = 0.1f;
    [SerializeField, Min(0f)] private float minimumMovement = 0.01f;
    [SerializeField, Min(0.1f)] private float maximumMovementPerSample = 1.5f;
    private PlayerTemporaryStatusController statuses;
    private NetworkObject networkObject;
    private PlayerHealth health;
    private PlayerTransportCollisionController transport;
    private DownedPlayerCarryable downedCarryable;
    private CharacterController characterController;
    private ClientNetworkTransform clientTransform;
    private Vector3 previousPosition;
    private float nextSampleTime;
    private float previousSampleTime;
    private bool hasSample;
    private bool priorEligible;
    private bool statusSubscribed;
    private bool observedSpawned;
    private ulong observedOwner;

    public float Amount => statuses != null ? statuses.MudAmount : 0f;
    private PlayerStatusCatalogSO.MudProfile Profile => catalog != null ? catalog.Mud : DefaultMudProfile.Value;

    private static class DefaultMudProfile
    {
        internal static readonly PlayerStatusCatalogSO.MudProfile Value = new PlayerStatusCatalogSO.MudProfile();
    }

    private void Awake()
    {
        statuses = GetComponent<PlayerTemporaryStatusController>();
        TryGetComponent(out networkObject);
        TryGetComponent(out health);
        TryGetComponent(out transport);
        TryGetComponent(out downedCarryable);
        TryGetComponent(out characterController);
        TryGetComponent(out clientTransform);
        if (mudOverlay != null) mudPresenter = mudOverlay.GetComponent<PlayerMudOverlay>();
    }

    private void OnEnable()
    {
        ResetMudPositionSample();
        SubscribeToStatusChanges();
        RefreshMudOverlay();
    }

    private void OnDisable()
    {
        UnsubscribeFromStatusChanges();
        HideMudOverlay();
    }

    private void Update()
    {
        if (networkObject == null) TryGetComponent(out networkObject);
        bool isSpawned = networkObject != null && networkObject.IsSpawned;
        ulong owner = isSpawned ? networkObject.OwnerClientId : 0UL;
        if (isSpawned != observedSpawned || owner != observedOwner)
        {
            observedSpawned = isSpawned;
            observedOwner = owner;
            ResetMudPositionSample();
            RefreshMudOverlay();
        }
        if (!CanUpdateMud()) return;
        float now = Time.time;
        if (now < nextSampleTime) return;
        float elapsed = hasSample ? Mathf.Clamp(now - previousSampleTime, 0f, Mathf.Max(sampleInterval, 0.25f)) : 0f;
        nextSampleTime = now + Mathf.Max(0.02f, sampleInterval);
        previousSampleTime = now;
        if (!TryGetAuthoritativePose(out Vector3 position, out Quaternion rotation))
        {
            hasSample = false;
            priorEligible = false;
            return;
        }
        bool eligible = IsEligible();
        if (!hasSample || !eligible || !priorEligible)
        {
            previousPosition = position;
            hasSample = true;
            priorEligible = eligible;
            return;
        }
        Vector3 delta = position - previousPosition;
        previousPosition = position;
        delta.y = 0f;
        float distance = delta.magnitude;
        if (!IsFinite(distance) || distance <= 0f || distance < minimumMovement ||
            distance > maximumMovementPerSample || elapsed <= 0f) return;

        Vector3 footPosition = GetFootPosition(position, rotation);
        float immersion = FoundationLooseSoilField.GetMaximumImmersion(footPosition);
        // Wading, Unsafe and Exhausted share the same mud rate. Volume membership
        // is sampled at the latest accepted root pose used by PlayerWaterExposureController.
        WaterExposureState sampledWaterState = WaterBody.TryGetContaining(position, out WaterBody body)
            && body.Profile != null ? WaterExposureState.Wading : WaterExposureState.None;
        ApplyMovementSample(distance, elapsed, immersion, sampledWaterState);
    }

    private void SubscribeToStatusChanges()
    {
        if (statuses == null) TryGetComponent(out statuses);
        if (statuses == null || statusSubscribed) return;
        statuses.StatusChanged += HandleStatusChanged;
        statusSubscribed = true;
    }

    private void UnsubscribeFromStatusChanges()
    {
        if (statuses != null && statusSubscribed) statuses.StatusChanged -= HandleStatusChanged;
        statusSubscribed = false;
    }

    private void HandleStatusChanged(PlayerTemporaryStatusSnapshot snapshot) => RefreshMudOverlay();

    private void RefreshMudOverlay()
    {
        if (mudOverlay == null) return;
        if (mudPresenter == null) mudPresenter = mudOverlay.GetComponent<PlayerMudOverlay>();
        NetworkManager manager = GetNetworkManager();
        bool isSpawned = networkObject != null && networkObject.IsSpawned;
        bool localPresentation = isSpawned ? networkObject.IsOwner : manager == null || !manager.IsListening;
        float amount = localPresentation && statuses != null ? statuses.MudAmount : 0f;
        mudOverlay.interactable = false;
        mudOverlay.blocksRaycasts = false;
        float maximumAlpha = Mathf.Clamp01(Profile.maximumAlpha);
        bool visible = localPresentation && amount > 0f;
        mudOverlay.gameObject.SetActive(visible);
        if (mudPresenter != null) mudPresenter.SetMudState(amount, localPresentation, maximumAlpha);
        else mudOverlay.alpha = Mathf.Clamp01(maximumAlpha * amount);
    }

    private void HideMudOverlay()
    {
        if (mudOverlay == null) return;
        mudOverlay.alpha = 0f;
        mudOverlay.gameObject.SetActive(false);
        if (mudPresenter != null) mudPresenter.SetMudState(0f, false, 0f);
    }

    public void AddTransferredLooseSoil(float units)
    {
        if (!CanUpdateMud() || !IsFinite(units) || units <= 0f) return;
        SetAmount(Amount + units * Profile.bucketPercentagePointsPerUnit / 100f);
    }

    /// <summary>Adds a percentage-point mud gain without exposing the status snapshot API.</summary>
    public bool AddMudPercentagePoints(float percentagePoints)
    {
        if (!CanUpdateMud() || !IsFinite(percentagePoints) || percentagePoints <= 0f) return false;
        return SetAmount(Amount + percentagePoints / 100f);
    }

    internal bool SetAmount(float amount)
    {
        float normalized = Mathf.Clamp01(IsFinite(amount) ? amount : 0f);
        return CanUpdateMud() && statuses != null && statuses.SetMudAmount(normalized,
            1f - Profile.maximumMovementPenalty * normalized);
    }

    internal bool ApplyMovementSample(float horizontalDistance, float elapsedSeconds, float mudImmersion,
        WaterExposureState waterState)
    {
        if (!CanUpdateMud() || !IsFinite(horizontalDistance) || !IsFinite(elapsedSeconds) ||
            !IsFinite(mudImmersion) || horizontalDistance <= 0f || elapsedSeconds <= 0f) return false;
        float amount = Amount;
        if (waterState != WaterExposureState.None)
            amount -= Profile.waterRemovalPercentagePointsPerSecond * elapsedSeconds / 100f;
        else if (mudImmersion > 0f)
            amount += horizontalDistance * Profile.soilPercentagePointsPerMetre / 100f;
        return SetAmount(amount);
    }

    private bool IsEligible()
    {
        if (transport == null) TryGetComponent(out transport);
        if (health != null && health.IsDowned || statuses != null && (statuses.IsTumbleweedCaptured || statuses.BlocksMovement) ||
            downedCarryable != null && downedCarryable.IsCarried ||
            transport != null && transport.IsTransportCollisionSuppressed) return false;
        bool isSpawned = networkObject != null && networkObject.IsSpawned;
        NetworkManager manager = isSpawned ? GetNetworkManager() : null;
        ulong clientId = isSpawned ? networkObject.OwnerClientId : 0UL;
        if (WheelbarrowController.FindForPlayer(clientId, manager) != null) return false;
        return true;
    }

    private bool TryGetAuthoritativePose(out Vector3 position, out Quaternion rotation)
    {
        position = transform.position;
        rotation = transform.rotation;
        bool isSpawned = networkObject != null && networkObject.IsSpawned;
        NetworkManager manager = GetNetworkManager();
        if (!isSpawned || manager == null || !manager.IsListening || !manager.IsServer)
            return IsFinite(position) && IsFinite(rotation);
        if (clientTransform == null)
            return networkObject.IsOwner && IsFinite(position) && IsFinite(rotation);
        if (!clientTransform.TryGetLatestAuthoritativeWorldPose(out position, out rotation)) return false;
        return IsFinite(position) && IsFinite(rotation);
    }

    private Vector3 GetFootPosition(Vector3 rootPosition, Quaternion rootRotation)
    {
        if (characterController == null) return rootPosition;
        Vector3 localFootOffset = characterController.center - Vector3.up * (characterController.height * 0.5f);
        return rootPosition + rootRotation * Vector3.Scale(localFootOffset, transform.lossyScale);
    }

    private bool CanUpdateMud()
    {
        bool isSpawned = networkObject != null && networkObject.IsSpawned;
        NetworkManager manager = GetNetworkManager();
        if (isSpawned) return manager != null && manager.IsListening && manager.IsServer;
        return manager == null || !manager.IsListening;
    }
    private NetworkManager GetNetworkManager() => networkObject != null && networkObject.NetworkManager != null
        ? networkObject.NetworkManager : Unity.Netcode.NetworkManager.Singleton;
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsFinite(Quaternion value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);

    internal void ResetMudPositionSample()
    {
        hasSample = false;
        priorEligible = false;
        nextSampleTime = Time.time + Mathf.Max(0.02f, sampleInterval);
    }

    private void OnDestroy()
    {
        UnsubscribeFromStatusChanges();
    }
}
