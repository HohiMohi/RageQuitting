using StarterAssets;
using Unity.Netcode;
using UnityEngine;
[RequireComponent(typeof(PlayerHealth))]
public class PlayerWaterExposureController : NetworkBehaviour
{
    private readonly RaycastHit[] groundProbeHits = new RaycastHit[12];
    private readonly NetworkVariable<WaterExposureState> stateNetwork = new NetworkVariable<WaterExposureState>();
    private readonly NetworkVariable<double> unsafeStartedAtNetwork = new NetworkVariable<double>(-1d);
    private readonly NetworkVariable<double> exhaustionStartedAtNetwork = new NetworkVariable<double>(-1d);
    [SerializeField, Min(0.02f)] private float serverCheckInterval = 0.1f;
    [SerializeField, Min(0.05f)] private float groundProbeRadius = 0.32f;
    [SerializeField, Min(0.1f)] private float groundProbeDistance = 0.7f;
    private PlayerHealth health;
    private PlayerStaminaController stamina;
    private FirstPersonController firstPersonController;
    private DownedPlayerCarryable downedCarryable;
    private WaterExposureState localState;
    private float nextServerCheckTime;
    private WaterBody currentWaterBody;
    public WaterExposureState CurrentState => IsNetworkStateActive ? stateNetwork.Value : localState;
    public bool IsInWater => CurrentState != WaterExposureState.None;
    public bool IsInUnsafeWater => CurrentState == WaterExposureState.Unsafe || CurrentState == WaterExposureState.Exhausted;
    public float RemainingUnsupportedTime => GetRemainingTime(unsafeStartedAtNetwork.Value, GetProfileDuration(false));
    public float RemainingExhaustionGrace => GetRemainingTime(exhaustionStartedAtNetwork.Value, GetProfileDuration(true));
    private bool IsNetworkStateActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private double CurrentServerTime => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
        ? NetworkManager.Singleton.ServerTime.Time
        : Time.timeAsDouble;
    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        stamina = GetComponent<PlayerStaminaController>();
        firstPersonController = GetComponent<FirstPersonController>();
        downedCarryable = GetComponent<DownedPlayerCarryable>();
    }
    private void Update()
    {
        if (IsNetworkStateActive)
        {
            if (IsServer && Time.time >= nextServerCheckTime)
            {
                nextServerCheckTime = Time.time + serverCheckInterval;
                EvaluateServerExposure();
            }
            return;
        }
        EvaluateLocalExposure();
    }
    private void LateUpdate()
    {
        if ((IsNetworkStateActive && !IsOwner) || health == null || !health.IsDowned || !IsInUnsafeWater)
        {
            return;
        }
        if (downedCarryable != null && downedCarryable.IsCarried)
        {
            return;
        }
        if (!WaterBody.TryGetContaining(transform.position, out WaterBody body) || body.Profile == null)
        {
            return;
        }
        firstPersonController?.ApplyWaterFloatHeight(body.SurfaceHeight - body.Profile.DownedFloatDepth);
    }
    private void EvaluateServerExposure()
    {
        EvaluateExposure(out WaterExposureState state, out WaterBody body);
        currentWaterBody = body;
        stateNetwork.Value = state;
        ApplyHazardState(state, body, true);
    }
    private void EvaluateLocalExposure()
    {
        EvaluateExposure(out localState, out WaterBody body);
        currentWaterBody = body;
        ApplyHazardState(localState, body, false);
    }
    private void EvaluateExposure(out WaterExposureState state, out WaterBody body)
    {
        state = WaterExposureState.None;
        if (!WaterBody.TryGetContaining(transform.position, out body) || body.Profile == null)
        {
            return;
        }
        WaterBodyProfileSO profile = body.Profile;
        bool supported = TryGetSupportingGround(profile, out RaycastHit hit);
        float supportDepth = supported ? Mathf.Max(0f, body.SurfaceHeight - hit.point.y) : float.PositiveInfinity;
        state = supported && supportDepth <= profile.MaximumSafeWadingDepth
            ? WaterExposureState.Wading
            : WaterExposureState.Unsafe;
    }
    private bool TryGetSupportingGround(WaterBodyProfileSO profile, out RaycastHit supportHit)
    {
        supportHit = default;
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        float probeDistance = Mathf.Max(
            groundProbeDistance,
            profile.MaximumSafeWadingDepth + groundProbeRadius + 0.5f);
        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            groundProbeRadius,
            Vector3.down,
            groundProbeHits,
            probeDistance,
            profile.GroundMask,
            QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = groundProbeHits[i];
            if (candidate.collider == null
                || candidate.collider.transform.root == transform.root
                || candidate.distance >= closestDistance)
            {
                continue;
            }
            closestDistance = candidate.distance;
            supportHit = candidate;
        }
        return closestDistance < float.PositiveInfinity;
    }
    private void ApplyHazardState(WaterExposureState state, WaterBody body, bool networkAuthority)
    {
        WaterBodyProfileSO profile = body != null ? body.Profile : null;
        bool canApplyWaterHazard = profile != null && health != null && !health.IsDowned;
        stamina?.SetAuthoritativeDrainSource(
            StaminaDrainSource.Water,
            canApplyWaterHazard && state != WaterExposureState.None ? profile.StaminaDrainPerSecond : 0f);
        if (!canApplyWaterHazard)
        {
            ResetHazardTimers(networkAuthority);
            return;
        }
        double now = CurrentServerTime;
        if (state == WaterExposureState.Unsafe)
        {
            SetTimerIfNeeded(unsafeStartedAtNetwork, ref localUnsafeStartedAt, now, networkAuthority);
            if (GetElapsed(unsafeStartedAtNetwork.Value, localUnsafeStartedAt, now, networkAuthority) >= profile.UnsupportedGraceDuration)
            {
                DownPlayer();
                return;
            }
        }
        else
        {
            ClearTimer(unsafeStartedAtNetwork, ref localUnsafeStartedAt, networkAuthority);
        }
        if (stamina != null && stamina.CurrentStamina <= 0.001f && state != WaterExposureState.None)
        {
            if (networkAuthority)
            {
                stateNetwork.Value = WaterExposureState.Exhausted;
            }
            else
            {
                localState = WaterExposureState.Exhausted;
            }
            SetTimerIfNeeded(exhaustionStartedAtNetwork, ref localExhaustionStartedAt, now, networkAuthority);
            if (GetElapsed(exhaustionStartedAtNetwork.Value, localExhaustionStartedAt, now, networkAuthority) >= profile.ExhaustionWarningDuration)
            {
                DownPlayer();
            }
        }
        else
        {
            ClearTimer(exhaustionStartedAtNetwork, ref localExhaustionStartedAt, networkAuthority);
        }
    }
    private double localUnsafeStartedAt = -1d;
    private double localExhaustionStartedAt = -1d;
    private void DownPlayer()
    {
        if (health != null && !health.IsDowned)
        {
            health.DamageReceived(Mathf.Max(health.CurrentHealth, health.MaxHealth));
        }
    }
    private void ResetHazardTimers(bool networkAuthority)
    {
        ClearTimer(unsafeStartedAtNetwork, ref localUnsafeStartedAt, networkAuthority);
        ClearTimer(exhaustionStartedAtNetwork, ref localExhaustionStartedAt, networkAuthority);
    }
    private void SetTimerIfNeeded(NetworkVariable<double> networkTimer, ref double localTimer, double now, bool networkAuthority)
    {
        if (networkAuthority)
        {
            if (networkTimer.Value < 0d)
            {
                networkTimer.Value = now;
            }
        }
        else if (localTimer < 0d)
        {
            localTimer = now;
        }
    }
    private void ClearTimer(NetworkVariable<double> networkTimer, ref double localTimer, bool networkAuthority)
    {
        if (networkAuthority)
        {
            networkTimer.Value = -1d;
        }
        else
        {
            localTimer = -1d;
        }
    }
    private static double GetElapsed(double networkTimer, double localTimer, double now, bool networkAuthority)
    {
        double startedAt = networkAuthority ? networkTimer : localTimer;
        return startedAt < 0d ? 0d : now - startedAt;
    }
    private float GetRemainingTime(double startedAt, float duration)
    {
        return startedAt < 0d ? duration : Mathf.Max(0f, duration - (float)(CurrentServerTime - startedAt));
    }
    private float GetProfileDuration(bool exhaustion)
    {
        WaterBodyProfileSO profile = currentWaterBody != null ? currentWaterBody.Profile : null;
        if (profile == null)
        {
            return 0f;
        }
        return exhaustion ? profile.ExhaustionWarningDuration : profile.UnsupportedGraceDuration;
    }
}
