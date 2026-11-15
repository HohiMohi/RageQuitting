using StarterAssets;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(PlayerTemporaryStatusController))]
public class PlayerWaterExposureController : NetworkBehaviour
{
    private const PlayerDomainStatusKind ExposureStatus = PlayerDomainStatusKind.WaterExposure;
    private const PlayerDomainStatusKind UnsupportedTimerStatus = PlayerDomainStatusKind.WaterUnsupportedTimer;
    private const PlayerDomainStatusKind ExhaustionTimerStatus = PlayerDomainStatusKind.WaterExhaustionTimer;

    private readonly RaycastHit[] groundProbeHits = new RaycastHit[12];
    [SerializeField, Min(0.02f)] private float serverCheckInterval = 0.1f;
    [SerializeField, Min(0.05f)] private float groundProbeRadius = 0.32f;
    [SerializeField, Min(0.1f)] private float groundProbeDistance = 0.7f;
    private PlayerHealth health;
    private PlayerStaminaController stamina;
    private FirstPersonController firstPersonController;
    private DownedPlayerCarryable downedCarryable;
    private PlayerTemporaryStatusController temporaryStatusController;
    private float nextServerCheckTime;
    private ulong exposureInstance;
    private ulong unsupportedTimerInstance;
    private ulong exhaustionTimerInstance;

    private bool IsNetworkStateActive => IsSpawned && NetworkManager != null && NetworkManager.IsListening;
    private bool HasAuthority => !IsNetworkStateActive || IsServer;
    private double CurrentServerTime => IsNetworkStateActive ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
    private PlayerTemporaryStatusEntry ExposureEntry => temporaryStatusController != null
        ? temporaryStatusController.GetDomainStatus(ExposureStatus) : default;
    private PlayerTemporaryStatusEntry UnsafeEntry => temporaryStatusController != null
        ? temporaryStatusController.GetDomainStatus(UnsupportedTimerStatus) : default;
    private PlayerTemporaryStatusEntry ExhaustionEntry => temporaryStatusController != null
        ? temporaryStatusController.GetDomainStatus(ExhaustionTimerStatus) : default;

    public WaterExposureState CurrentState => ExposureEntry.InstanceId == 0
        ? WaterExposureState.None : (WaterExposureState)ExposureEntry.DomainData;
    public bool IsInWater => CurrentState != WaterExposureState.None;
    public bool IsInUnsafeWater => CurrentState == WaterExposureState.Unsafe || CurrentState == WaterExposureState.Exhausted;
    public float RemainingUnsupportedTime => GetRemaining(UnsafeEntry);
    public float RemainingExhaustionGrace => GetRemaining(ExhaustionEntry);

    internal void RefreshAfterRespawn()
    {
        if (isActiveAndEnabled && HasAuthority) EvaluateExposureState();
    }

    internal void PrepareForTumbleweedCapture()
    {
        exposureInstance = 0;
        unsupportedTimerInstance = 0;
        exhaustionTimerInstance = 0;
        if (stamina != null) stamina.SetAuthoritativeDrainSource(StaminaDrainSource.Water, 0f);
    }

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        stamina = GetComponent<PlayerStaminaController>();
        firstPersonController = GetComponent<FirstPersonController>();
        downedCarryable = GetComponent<DownedPlayerCarryable>();
        TryGetComponent(out temporaryStatusController);
    }

    private void Update()
    {
        if (!HasAuthority) return;
        if (IsNetworkStateActive)
        {
            if (Time.time < nextServerCheckTime) return;
            nextServerCheckTime = Time.time + serverCheckInterval;
        }
        EvaluateExposureState();
    }

    private void LateUpdate()
    {
        if ((IsNetworkStateActive && !IsOwner) || health == null || !health.IsDowned || !IsInUnsafeWater
            || downedCarryable != null && downedCarryable.IsCarried) return;
        if (!WaterBody.TryGetContaining(transform.position, out WaterBody body) || body.Profile == null) return;
        if (firstPersonController != null)
            firstPersonController.ApplyWaterFloatHeight(body.SurfaceHeight - body.Profile.DownedFloatDepth);
    }

    private void EvaluateExposureState()
    {
        if (temporaryStatusController == null) TryGetComponent(out temporaryStatusController);
        if (temporaryStatusController == null) return;
        if (temporaryStatusController.IsTumbleweedCaptured)
        {
            SetExposure(WaterExposureState.None);
            ClearTimer(UnsupportedTimerStatus, ref unsupportedTimerInstance);
            ClearTimer(ExhaustionTimerStatus, ref exhaustionTimerInstance);
            if (stamina != null) stamina.SetAuthoritativeDrainSource(StaminaDrainSource.Water, 0f);
            return;
        }

        EvaluateExposure(out WaterExposureState state, out WaterBody body);
        WaterBodyProfileSO profile = body != null ? body.Profile : null;
        bool inHazard = profile != null && state != WaterExposureState.None;
        bool canDown = inHazard && health != null && !health.IsDowned;
        if (stamina != null) stamina.SetAuthoritativeDrainSource(StaminaDrainSource.Water,
            canDown ? profile.StaminaDrainPerSecond : 0f);
        if (!canDown)
        {
            ClearTimer(UnsupportedTimerStatus, ref unsupportedTimerInstance);
            ClearTimer(ExhaustionTimerStatus, ref exhaustionTimerInstance);
            SetExposure(state);
            return;
        }

        double now = CurrentServerTime;
        bool unsafeDowningEnabled = GameplayManager.Instance == null
            || GameplayManager.Instance.EnableUnsupportedWaterDowning;
        bool unsupported = state == WaterExposureState.Unsafe && unsafeDowningEnabled;
        UpdateTimer(UnsupportedTimerStatus, ref unsupportedTimerInstance, unsupported,
            profile.UnsupportedGraceDuration, now);
        bool exhausted = stamina != null && stamina.CurrentStamina <= 0.001f;
        UpdateTimer(ExhaustionTimerStatus, ref exhaustionTimerInstance, exhausted,
            profile.ExhaustionWarningDuration, now);

        if (unsupported && TimerExpired(UnsafeEntry, now)
            || exhausted && TimerExpired(ExhaustionEntry, now))
            DownPlayer();
        SetExposure(exhausted && state != WaterExposureState.None
            ? WaterExposureState.Exhausted : state);
    }

    private void EvaluateExposure(out WaterExposureState state, out WaterBody body)
    {
        state = WaterExposureState.None;
        if (!WaterBody.TryGetContaining(transform.position, out body) || body.Profile == null) return;
        WaterBodyProfileSO profile = body.Profile;
        bool supported = TryGetSupportingGround(profile, out RaycastHit hit);
        float supportDepth = supported ? Mathf.Max(0f, body.SurfaceHeight - hit.point.y) : float.PositiveInfinity;
        state = supported && supportDepth <= profile.MaximumSafeWadingDepth
            ? WaterExposureState.Wading : WaterExposureState.Unsafe;
    }

    private bool TryGetSupportingGround(WaterBodyProfileSO profile, out RaycastHit supportHit)
    {
        supportHit = default;
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        float probeDistance = Mathf.Max(groundProbeDistance, profile.MaximumSafeWadingDepth + groundProbeRadius + 0.5f);
        int hitCount = Physics.SphereCastNonAlloc(origin, groundProbeRadius, Vector3.down, groundProbeHits,
            probeDistance, profile.GroundMask, QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = groundProbeHits[i];
            if (candidate.collider == null || candidate.collider.transform.root == transform.root
                || candidate.distance >= closestDistance) continue;
            closestDistance = candidate.distance;
            supportHit = candidate;
        }
        return closestDistance < float.PositiveInfinity;
    }

    private void SetExposure(WaterExposureState state)
    {
        if (temporaryStatusController == null) return;
        if (state == WaterExposureState.None)
        {
            temporaryStatusController.ClearDomainStatus(ExposureStatus, ref exposureInstance);
            return;
        }
        temporaryStatusController.SetDomainStatus(ExposureStatus, ref exposureInstance, 0d, 0f, (int)state);
    }

    private void UpdateTimer(PlayerDomainStatusKind kind, ref ulong instance, bool active, float duration, double now)
    {
        if (!active) { ClearTimer(kind, ref instance); return; }
        PlayerTemporaryStatusEntry entry = temporaryStatusController.GetDomainStatus(kind);
        double startedAt = entry.InstanceId == instance && instance != 0 ? entry.DomainStartedAt : now;
        temporaryStatusController.SetDomainStatus(kind, ref instance, startedAt, Mathf.Max(0f, duration), 0);
    }

    private void ClearTimer(PlayerDomainStatusKind kind, ref ulong instance)
    {
        if (temporaryStatusController != null) temporaryStatusController.ClearDomainStatus(kind, ref instance);
    }

    private static bool TimerExpired(PlayerTemporaryStatusEntry entry, double now) =>
        entry.InstanceId != 0 && now >= entry.DomainStartedAt + Mathf.Max(0f, entry.DomainValue);

    private float GetRemaining(PlayerTemporaryStatusEntry entry) => entry.InstanceId == 0
        ? 0f : Mathf.Max(0f, entry.DomainValue - (float)(CurrentServerTime - entry.DomainStartedAt));

    private void DownPlayer()
    {
        if (health == null || health.IsDowned) return;
        health.DamageReceived(Mathf.Max(health.CurrentHealth, health.MaxHealth));
        if (!health.IsDowned) return;
        ClearTimer(UnsupportedTimerStatus, ref unsupportedTimerInstance);
        ClearTimer(ExhaustionTimerStatus, ref exhaustionTimerInstance);
        if (stamina != null) stamina.SetAuthoritativeDrainSource(StaminaDrainSource.Water, 0f);
    }
}
