using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum BridgeMountAlignmentState
{
    Inactive,
    WaitingForComponent,
    WaitingForCarriers,
    Positioning,
    Rotating,
    MovingTooFast,
    Settling,
    Complete
}

public readonly struct BridgeMountPoseError
{
    public readonly Vector3 LocalPositionError;
    public readonly Vector3 LocalRotationError;
    public readonly float LinearVelocity;
    public readonly float AngularVelocityDegrees;
    public readonly int OrientationIndex;

    public BridgeMountPoseError(
        Vector3 localPositionError,
        Vector3 localRotationError,
        float linearVelocity,
        float angularVelocityDegrees,
        int orientationIndex)
    {
        LocalPositionError = localPositionError;
        LocalRotationError = localRotationError;
        LinearVelocity = linearVelocity;
        AngularVelocityDegrees = angularVelocityDegrees;
        OrientationIndex = orientationIndex;
    }
}

[DisallowMultipleComponent]
public sealed class BridgeMountSocket : MonoBehaviour
{
    public const ulong NoCandidateNetworkObjectId = ulong.MaxValue;

    [Header("References")]
    [SerializeField] private BridgeComponent bridgeComponent;
    [SerializeField] private Transform targetPose;
    [SerializeField] private BoxCollider componentCaptureVolume;
    [SerializeField] private BoxCollider carrierStagingVolume;
    [SerializeField] private GameObject ghostVisualRoot;

    [Header("Acceptance")]
    [SerializeField] private Vector3 positionTolerance = Vector3.one * 0.25f;
    [SerializeField] private Vector3 rotationToleranceDegrees = Vector3.one * 12f;
    [SerializeField, Min(0f)] private float maximumLinearVelocity = 0.35f;
    [SerializeField, Min(0f)] private float maximumAngularVelocityDegrees = 15f;
    [SerializeField, Min(0f)] private float settleDuration = 1f;
    [SerializeField] private bool requireRecommendedCarrierCount = true;
    [SerializeField] private Vector3[] allowedOrientationOffsetsEuler = { Vector3.zero };

    [Header("Soft Assist")]
    [SerializeField, Min(0f)] private float positionSpring = 12f;
    [SerializeField, Min(0f)] private float positionDamping = 7f;
    [SerializeField, Min(0f)] private float maximumPositionAcceleration = 6f;
    [SerializeField, Min(0f)] private float rotationSpring = 8f;
    [SerializeField, Min(0f)] private float rotationDamping = 4f;
    [SerializeField, Min(0f)] private float maximumAngularAcceleration = 4f;
    [SerializeField, Min(0f)] private float mountingCollisionClearancePadding = 0.5f;

    [Header("Feedback")]
    [SerializeField, Min(0f)] private float feedbackVisibilityDistance = 12f;
    [SerializeField, Min(0.005f)] private float indicatorWidth = 0.035f;
    [SerializeField] private Color invalidColor = new Color(0.95f, 0.16f, 0.12f, 0.75f);
    [SerializeField] private Color positioningColor = new Color(1f, 0.68f, 0.08f, 0.75f);
    [SerializeField] private Color settlingColor = new Color(0.15f, 0.95f, 0.3f, 0.8f);

    private readonly Collider[] overlapResults = new Collider[64];
    private readonly List<GameObject> carrierRoots = new List<GameObject>(8);
    private readonly List<MountableBridgeComponent> candidateBuffer = new List<MountableBridgeComponent>(8);
    private readonly List<MountingCollisionPair> ignoredMountingCollisionPairs = new List<MountingCollisionPair>(32);
    private MountableBridgeComponent authoritativeCandidate;
    private MountableBridgeComponent clearanceCandidate;
    private MountableBridgeComponent visualCandidate;
    private BridgeMountAlignmentState currentAlignmentState = BridgeMountAlignmentState.Inactive;
    private BridgeMountPoseError currentPoseError;
    private double settleStartedAt = -1d;
    private ulong synchronizedCandidateId = NoCandidateNetworkObjectId;
    private bool mountRequested;
    private BridgeMountSocketVisualizer visualizer;

    public BridgeMountAlignmentState CurrentAlignmentState => currentAlignmentState;
    public BridgeMountPoseError CurrentPoseError => currentPoseError;
    public Transform TargetPose => targetPose != null ? targetPose : transform;
    public bool RequireRecommendedCarrierCount => requireRecommendedCarrierCount;
    public double SettleStartedAt => settleStartedAt;
    public ulong CurrentCandidateNetworkObjectId => synchronizedCandidateId;

    private void Awake()
    {
        if (bridgeComponent == null)
        {
            bridgeComponent = GetComponent<BridgeComponent>();
        }

        if (targetPose == null)
        {
            targetPose = transform;
        }

        if (ghostVisualRoot == null && bridgeComponent != null)
        {
            ghostVisualRoot = bridgeComponent.ReadyForMountingVisualsGameObject;
        }

        ConfigureTrigger(componentCaptureVolume);
        ConfigureTrigger(carrierStagingVolume);
        visualizer = new BridgeMountSocketVisualizer(this, ghostVisualRoot);
    }

    private void OnDestroy()
    {
        ClearMountingCollisionClearance();
        visualizer?.Dispose();
    }

    private void OnDisable()
    {
        ClearMountingCollisionClearance();
    }

    private void FixedUpdate()
    {
        if (IsAuthoritative())
        {
            EvaluateAuthoritativeState();
        }
    }

    private void LateUpdate()
    {
        ResolveVisualCandidate();
        visualizer?.Refresh(ShouldShowFeedback(), currentAlignmentState, currentPoseError, visualCandidate);
    }

    public bool TryEvaluateCandidate(MountableBridgeComponent candidate, out BridgeMountPoseError poseError)
    {
        poseError = default;
        if (candidate == null || targetPose == null)
        {
            return false;
        }

        Transform alignmentTransform = candidate.MountAlignmentTransform;
        Rigidbody body = candidate.PhysicsBody;
        Vector3 localPositionError = targetPose.InverseTransformPoint(alignmentTransform.position);
        int orientationIndex = FindBestOrientation(alignmentTransform.rotation, out Vector3 rotationError, out _);
        poseError = new BridgeMountPoseError(
            localPositionError,
            rotationError,
            body != null ? body.linearVelocity.magnitude : 0f,
            body != null ? body.angularVelocity.magnitude * Mathf.Rad2Deg : 0f,
            orientationIndex);
        return true;
    }

    public bool IsCarrierInside(GameObject carrierRoot)
    {
        if (carrierRoot == null || carrierStagingVolume == null || !carrierStagingVolume.enabled)
        {
            return false;
        }

        Vector3 testPoint = carrierRoot.transform.position;
        if (carrierRoot.TryGetComponent(out CharacterController controller))
        {
            testPoint = controller.bounds.center;
        }

        Vector3 closest = carrierStagingVolume.ClosestPoint(testPoint);
        return (closest - testPoint).sqrMagnitude <= 0.0025f;
    }

    public void ApplyNetworkAlignmentState(BridgeComponentNetworkState state)
    {
        currentAlignmentState = (BridgeMountAlignmentState)state.mountAlignmentState;
        synchronizedCandidateId = state.mountAlignmentCandidateNetworkObjectId;
        settleStartedAt = state.mountAlignmentStartedAt;
        if (currentAlignmentState == BridgeMountAlignmentState.Complete)
        {
            mountRequested = true;
        }
        SetSocketVolumesActive(currentAlignmentState != BridgeMountAlignmentState.Complete);
    }

    public void PopulateNetworkState(ref BridgeComponentNetworkState state)
    {
        state.mountAlignmentState = (int)currentAlignmentState;
        state.mountAlignmentCandidateNetworkObjectId = synchronizedCandidateId;
        state.mountAlignmentStartedAt = settleStartedAt;
    }

    public bool IsAuthoritativeCandidateReady(MountableBridgeComponent candidate)
    {
        return IsAuthoritative()
            && candidate != null
            && candidate == authoritativeCandidate
            && currentAlignmentState == BridgeMountAlignmentState.Settling
            && settleStartedAt >= 0d
            && GetSynchronizedTime() - settleStartedAt >= settleDuration
            && IsMatchingCarriedComponent(candidate)
            && AreCarrierRequirementsMet(candidate)
            && TryEvaluateCandidate(candidate, out BridgeMountPoseError error)
            && IsWithinTolerance(error.LocalPositionError, positionTolerance)
            && IsWithinTolerance(error.LocalRotationError, rotationToleranceDegrees)
            && error.LinearVelocity <= maximumLinearVelocity
            && error.AngularVelocityDegrees <= maximumAngularVelocityDegrees;
    }

    public void AddMountPrompt(List<InteractionPrompt> prompts)
    {
        if (prompts == null)
        {
            return;
        }

        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, GetStatusText()));
    }

    public bool IsSocketCollider(Collider collider)
    {
        return collider != null && (collider == componentCaptureVolume || collider == carrierStagingVolume);
    }

    public string GetStatusText()
    {
        switch (currentAlignmentState)
        {
            case BridgeMountAlignmentState.WaitingForComponent:
                return "Move the component into the marked mounting area";
            case BridgeMountAlignmentState.WaitingForCarriers:
                return "Move all carriers into the marked work area";
            case BridgeMountAlignmentState.Positioning:
                return "Align the component position";
            case BridgeMountAlignmentState.Rotating:
                return "Align the component rotation";
            case BridgeMountAlignmentState.MovingTooFast:
                return "Hold the component steady";
            case BridgeMountAlignmentState.Settling:
                return $"Stabilizing {GetSettleElapsed():0.0} / {settleDuration:0.0} s";
            case BridgeMountAlignmentState.Complete:
                return "Component delivered";
            default:
                return "Prepare the mounting area";
        }
    }

    private void EvaluateAuthoritativeState()
    {
        if (mountRequested || bridgeComponent == null || bridgeComponent.IsMounted || !bridgeComponent.CanBeMounted)
        {
            ClearMountingCollisionClearance();
            authoritativeCandidate = null;
            SetAuthoritativeState(bridgeComponent != null && bridgeComponent.IsMounted
                ? BridgeMountAlignmentState.Complete
                : BridgeMountAlignmentState.Inactive, null, -1d);
            return;
        }

        MountableBridgeComponent candidate = SelectCandidate();
        if (candidate == null)
        {
            ClearMountingCollisionClearance();
            authoritativeCandidate = null;
            SetAuthoritativeState(BridgeMountAlignmentState.WaitingForComponent, null, -1d);
            return;
        }

        authoritativeCandidate = candidate;
        EnsureMountingCollisionClearance(candidate);
        if (!AreCarrierRequirementsMet(candidate))
        {
            SetAuthoritativeState(BridgeMountAlignmentState.WaitingForCarriers, candidate, -1d);
            return;
        }

        if (!TryEvaluateCandidate(candidate, out BridgeMountPoseError poseError))
        {
            SetAuthoritativeState(BridgeMountAlignmentState.WaitingForComponent, null, -1d);
            return;
        }

        currentPoseError = poseError;
        ApplySoftAssist(candidate, poseError.OrientationIndex);

        if (!IsWithinTolerance(poseError.LocalPositionError, positionTolerance))
        {
            SetAuthoritativeState(BridgeMountAlignmentState.Positioning, candidate, -1d);
            return;
        }

        if (!IsWithinTolerance(poseError.LocalRotationError, rotationToleranceDegrees))
        {
            SetAuthoritativeState(BridgeMountAlignmentState.Rotating, candidate, -1d);
            return;
        }

        if (poseError.LinearVelocity > maximumLinearVelocity || poseError.AngularVelocityDegrees > maximumAngularVelocityDegrees)
        {
            SetAuthoritativeState(BridgeMountAlignmentState.MovingTooFast, candidate, -1d);
            return;
        }

        double now = GetSynchronizedTime();
        if (currentAlignmentState != BridgeMountAlignmentState.Settling
            || synchronizedCandidateId != GetCandidateNetworkObjectId(candidate)
            || settleStartedAt < 0d)
        {
            SetAuthoritativeState(BridgeMountAlignmentState.Settling, candidate, now);
            return;
        }

        if (now - settleStartedAt < settleDuration)
        {
            return;
        }

        mountRequested = GameplayManager.Instance != null
            && GameplayManager.Instance.TryAutoMountBridgeComponent(bridgeComponent, candidate);
        if (mountRequested)
        {
            ClearMountingCollisionClearance();
            SetSocketVolumesActive(false);
            SetAuthoritativeState(BridgeMountAlignmentState.Complete, candidate, settleStartedAt);
        }
    }

    private MountableBridgeComponent SelectCandidate()
    {
        candidateBuffer.Clear();
        CollectCandidates(candidateBuffer);
        if (authoritativeCandidate != null && candidateBuffer.Contains(authoritativeCandidate)
            && currentAlignmentState == BridgeMountAlignmentState.Settling)
        {
            return authoritativeCandidate;
        }

        MountableBridgeComponent bestCandidate = null;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            MountableBridgeComponent candidate = candidateBuffer[i];
            if (!IsMatchingCarriedComponent(candidate) || !TryEvaluateCandidate(candidate, out BridgeMountPoseError error))
            {
                continue;
            }

            float score = error.LocalPositionError.sqrMagnitude
                + error.LocalRotationError.sqrMagnitude * 0.0004f;
            if (score < bestScore || (Mathf.Approximately(score, bestScore)
                && GetCandidateNetworkObjectId(candidate) < GetCandidateNetworkObjectId(bestCandidate)))
            {
                bestScore = score;
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    private void CollectCandidates(List<MountableBridgeComponent> results)
    {
        if (componentCaptureVolume == null || !componentCaptureVolume.enabled)
        {
            return;
        }

        Transform volumeTransform = componentCaptureVolume.transform;
        Vector3 scale = Abs(volumeTransform.lossyScale);
        Vector3 halfExtents = Vector3.Scale(componentCaptureVolume.size, scale) * 0.5f;
        Vector3 center = volumeTransform.TransformPoint(componentCaptureVolume.center);
        int hitCount = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            overlapResults,
            volumeTransform.rotation,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapResults[i];
            MountableBridgeComponent candidate = hit != null ? hit.GetComponentInParent<MountableBridgeComponent>() : null;
            if (candidate != null && !results.Contains(candidate))
            {
                results.Add(candidate);
            }
        }
    }

    private bool IsMatchingCarriedComponent(MountableBridgeComponent candidate)
    {
        return candidate != null
            && candidate.IsActivelyCarried
            && candidate.GetMountableBridgeComponentSO() != null
            && candidate.GetMountableBridgeComponentSO().bridgeComponentSO == bridgeComponent.GetBridgeComponentSO();
    }

    private bool AreCarrierRequirementsMet(MountableBridgeComponent candidate)
    {
        if (candidate == null || candidate.ActiveCarrierCount <= 0)
        {
            return false;
        }

        if (requireRecommendedCarrierCount && candidate.ActiveCarrierCount < candidate.RecommendedCarriers)
        {
            return false;
        }

        carrierRoots.Clear();
        candidate.CollectActiveCarrierRoots(carrierRoots);
        if (carrierRoots.Count != candidate.ActiveCarrierCount)
        {
            return false;
        }

        for (int i = 0; i < carrierRoots.Count; i++)
        {
            if (!IsCarrierInside(carrierRoots[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void ApplySoftAssist(MountableBridgeComponent candidate, int orientationIndex)
    {
        Rigidbody body = candidate != null ? candidate.PhysicsBody : null;
        if (body == null || body.isKinematic)
        {
            return;
        }

        CalculateDesiredBodyPose(candidate, orientationIndex, out Vector3 desiredPosition, out Quaternion desiredRotation);
        Vector3 positionAcceleration = (desiredPosition - body.position) * positionSpring
            - body.linearVelocity * positionDamping;
        body.AddForce(Vector3.ClampMagnitude(positionAcceleration, maximumPositionAcceleration), ForceMode.Acceleration);

        Quaternion rotationError = desiredRotation * Quaternion.Inverse(body.rotation);
        rotationError.ToAngleAxis(out float angleDegrees, out Vector3 axis);
        if (angleDegrees > 180f)
        {
            angleDegrees -= 360f;
        }

        Vector3 angularAcceleration = axis.sqrMagnitude > 0.0001f
            ? axis.normalized * angleDegrees * Mathf.Deg2Rad * rotationSpring - body.angularVelocity * rotationDamping
            : -body.angularVelocity * rotationDamping;
        body.AddTorque(Vector3.ClampMagnitude(angularAcceleration, maximumAngularAcceleration), ForceMode.Acceleration);
    }

    private void EnsureMountingCollisionClearance(MountableBridgeComponent candidate)
    {
        if (candidate == null || clearanceCandidate == candidate)
        {
            return;
        }

        ClearMountingCollisionClearance();
        clearanceCandidate = candidate;

        Collider[] candidateColliders = candidate.GetComponentsInChildren<Collider>(true);
        if (candidateColliders.Length == 0 || componentCaptureVolume == null)
        {
            return;
        }

        Transform volumeTransform = componentCaptureVolume.transform;
        Vector3 scale = Abs(volumeTransform.lossyScale);
        Vector3 halfExtents = Vector3.Scale(componentCaptureVolume.size, scale) * 0.5f
            + Vector3.one * mountingCollisionClearancePadding;
        Vector3 center = volumeTransform.TransformPoint(componentCaptureVolume.center);
        int hitCount = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            overlapResults,
            volumeTransform.rotation,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            Collider mountedCollider = overlapResults[hitIndex];
            BridgeComponent mountedBridgeComponent = mountedCollider != null
                ? mountedCollider.GetComponentInParent<BridgeComponent>()
                : null;
            if (mountedCollider == null
                || !mountedCollider.enabled
                || mountedCollider.isTrigger
                || mountedBridgeComponent == null
                || mountedBridgeComponent == bridgeComponent
                || !mountedBridgeComponent.IsMounted)
            {
                continue;
            }

            for (int candidateIndex = 0; candidateIndex < candidateColliders.Length; candidateIndex++)
            {
                Collider candidateCollider = candidateColliders[candidateIndex];
                if (candidateCollider == null
                    || !candidateCollider.enabled
                    || candidateCollider.isTrigger
                    || candidateCollider == mountedCollider)
                {
                    continue;
                }

                Physics.IgnoreCollision(candidateCollider, mountedCollider, true);
                ignoredMountingCollisionPairs.Add(new MountingCollisionPair(candidateCollider, mountedCollider));
            }
        }
    }

    private void ClearMountingCollisionClearance()
    {
        for (int i = 0; i < ignoredMountingCollisionPairs.Count; i++)
        {
            MountingCollisionPair pair = ignoredMountingCollisionPairs[i];
            if (pair.CandidateCollider != null && pair.MountedCollider != null)
            {
                Physics.IgnoreCollision(pair.CandidateCollider, pair.MountedCollider, false);
            }
        }

        ignoredMountingCollisionPairs.Clear();
        clearanceCandidate = null;
    }

    private void CalculateDesiredBodyPose(
        MountableBridgeComponent candidate,
        int orientationIndex,
        out Vector3 desiredPosition,
        out Quaternion desiredRotation)
    {
        Transform alignment = candidate.MountAlignmentTransform;
        Quaternion alignmentRelativeRotation = Quaternion.Inverse(candidate.transform.rotation) * alignment.rotation;
        Vector3 alignmentLocalPosition = candidate.transform.InverseTransformPoint(alignment.position);
        Quaternion targetRotation = GetTargetRotation(orientationIndex);
        desiredRotation = targetRotation * Quaternion.Inverse(alignmentRelativeRotation);
        Vector3 scaledLocalPosition = Vector3.Scale(alignmentLocalPosition, candidate.transform.lossyScale);
        desiredPosition = targetPose.position - desiredRotation * scaledLocalPosition;
    }

    private int FindBestOrientation(Quaternion currentRotation, out Vector3 localEulerError, out Quaternion targetRotation)
    {
        int count = allowedOrientationOffsetsEuler != null && allowedOrientationOffsetsEuler.Length > 0
            ? allowedOrientationOffsetsEuler.Length
            : 1;
        int bestIndex = 0;
        float bestScore = float.PositiveInfinity;
        localEulerError = Vector3.zero;
        targetRotation = targetPose.rotation;
        for (int i = 0; i < count; i++)
        {
            Quaternion candidateTargetRotation = GetTargetRotation(i);
            Vector3 error = NormalizeEuler((Quaternion.Inverse(candidateTargetRotation) * currentRotation).eulerAngles);
            float score = error.sqrMagnitude;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
                localEulerError = error;
                targetRotation = candidateTargetRotation;
            }
        }

        return bestIndex;
    }

    private Quaternion GetTargetRotation(int orientationIndex)
    {
        Vector3 offset = allowedOrientationOffsetsEuler != null
            && orientationIndex >= 0
            && orientationIndex < allowedOrientationOffsetsEuler.Length
                ? allowedOrientationOffsetsEuler[orientationIndex]
                : Vector3.zero;
        return targetPose.rotation * Quaternion.Euler(offset);
    }

    private void SetAuthoritativeState(
        BridgeMountAlignmentState state,
        MountableBridgeComponent candidate,
        double startedAt)
    {
        ulong candidateId = GetCandidateNetworkObjectId(candidate);
        if (currentAlignmentState == state
            && synchronizedCandidateId == candidateId
            && Math.Abs(settleStartedAt - startedAt) < 0.0001d)
        {
            return;
        }

        currentAlignmentState = state;
        synchronizedCandidateId = candidateId;
        settleStartedAt = startedAt;
        GameplayManager.Instance?.ReportMountAlignmentState(bridgeComponent, state, candidateId, startedAt);
    }

    private void ResolveVisualCandidate()
    {
        if (IsAuthoritative())
        {
            visualCandidate = authoritativeCandidate;
        }
        else if (synchronizedCandidateId == NoCandidateNetworkObjectId)
        {
            visualCandidate = null;
        }
        else if (NetworkManager.Singleton != null
            && NetworkManager.Singleton.SpawnManager != null
            && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(synchronizedCandidateId, out NetworkObject networkObject))
        {
            visualCandidate = networkObject.GetComponent<MountableBridgeComponent>();
        }

        if (visualCandidate != null)
        {
            TryEvaluateCandidate(visualCandidate, out currentPoseError);
        }
    }

    private bool ShouldShowFeedback()
    {
        if (bridgeComponent == null || bridgeComponent.IsMounted || !bridgeComponent.CanBeMounted || Camera.main == null)
        {
            return false;
        }

        return Vector3.Distance(Camera.main.transform.position, TargetPose.position) <= feedbackVisibilityDistance;
    }

    private float GetSettleElapsed()
    {
        return settleStartedAt < 0d ? 0f : Mathf.Clamp((float)(GetSynchronizedTime() - settleStartedAt), 0f, settleDuration);
    }

    private double GetSynchronizedTime()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
            ? NetworkManager.Singleton.ServerTime.Time
            : Time.timeAsDouble;
    }

    private bool IsAuthoritative()
    {
        return NetworkManager.Singleton == null
            || !NetworkManager.Singleton.IsListening
            || NetworkManager.Singleton.IsServer;
    }

    private ulong GetCandidateNetworkObjectId(MountableBridgeComponent candidate)
    {
        return candidate != null && candidate.NetworkObject != null && candidate.NetworkObject.IsSpawned
            ? candidate.NetworkObjectId
            : NoCandidateNetworkObjectId;
    }

    private static bool IsWithinTolerance(Vector3 error, Vector3 tolerance)
    {
        return Mathf.Abs(error.x) <= Mathf.Max(0f, tolerance.x)
            && Mathf.Abs(error.y) <= Mathf.Max(0f, tolerance.y)
            && Mathf.Abs(error.z) <= Mathf.Max(0f, tolerance.z);
    }

    private static Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(Mathf.DeltaAngle(0f, euler.x), Mathf.DeltaAngle(0f, euler.y), Mathf.DeltaAngle(0f, euler.z));
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static void ConfigureTrigger(Collider trigger)
    {
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }
    }

    private void SetSocketVolumesActive(bool active)
    {
        if (componentCaptureVolume != null) componentCaptureVolume.enabled = active;
        if (carrierStagingVolume != null) carrierStagingVolume.enabled = active;
    }

    private readonly struct MountingCollisionPair
    {
        public readonly Collider CandidateCollider;
        public readonly Collider MountedCollider;

        public MountingCollisionPair(Collider candidateCollider, Collider mountedCollider)
        {
            CandidateCollider = candidateCollider;
            MountedCollider = mountedCollider;
        }
    }

    private sealed class BridgeMountSocketVisualizer : IDisposable
    {
        private readonly BridgeMountSocket socket;
        private readonly Transform root;
        private readonly List<LineRenderer> carrierLines = new List<LineRenderer>();
        private readonly LineRenderer[] positionLines = new LineRenderer[3];
        private readonly LineRenderer[] rotationLines = new LineRenderer[3];
        private readonly Renderer[] ghostRenderers;
        private readonly Color[] originalGhostColors;
        private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        private readonly Material lineMaterial;

        public BridgeMountSocketVisualizer(BridgeMountSocket socket, GameObject ghostVisualRoot)
        {
            this.socket = socket;
            GameObject rootObject = new GameObject("BridgeMountFeedback_Runtime");
            rootObject.hideFlags = HideFlags.DontSave;
            rootObject.layer = 2;
            rootObject.transform.SetParent(socket.transform, false);
            root = rootObject.transform;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            lineMaterial = shader != null ? new Material(shader) { hideFlags = HideFlags.DontSave } : null;

            for (int i = 0; i < 12; i++)
            {
                carrierLines.Add(CreateLine($"CarrierZone_{i}"));
            }
            for (int i = 0; i < 3; i++)
            {
                positionLines[i] = CreateLine($"PositionAxis_{i}");
                rotationLines[i] = CreateLine($"RotationAxis_{i}", 24);
            }

            ghostRenderers = ghostVisualRoot != null
                ? ghostVisualRoot.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            originalGhostColors = new Color[ghostRenderers.Length];
            for (int i = 0; i < ghostRenderers.Length; i++)
            {
                Material material = ghostRenderers[i] != null ? ghostRenderers[i].sharedMaterial : null;
                originalGhostColors[i] = material != null && material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor")
                    : material != null && material.HasProperty("_Color")
                        ? material.color
                        : Color.white;
            }
            SetVisible(false);
        }

        public void Refresh(bool visible, BridgeMountAlignmentState state, BridgeMountPoseError error, MountableBridgeComponent candidate)
        {
            SetVisible(visible);
            if (!visible)
            {
                RestoreGhostColors();
                return;
            }

            Color stateColor = state == BridgeMountAlignmentState.Settling
                ? socket.settlingColor
                : state == BridgeMountAlignmentState.Positioning || state == BridgeMountAlignmentState.Rotating || state == BridgeMountAlignmentState.MovingTooFast
                    ? socket.positioningColor
                    : socket.invalidColor;
            ApplyGhostColor(stateColor);
            DrawCarrierVolume(stateColor);
            DrawPositionIndicators(candidate != null ? error.LocalPositionError : Vector3.zero);
            DrawRotationIndicators(candidate != null ? error.LocalRotationError : Vector3.zero);
        }

        public void Dispose()
        {
            RestoreGhostColors();
            if (lineMaterial != null)
            {
                UnityEngine.Object.Destroy(lineMaterial);
            }
            if (root != null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
            }
        }

        private LineRenderer CreateLine(string name, int positionCount = 2)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.layer = 2;
            lineObject.transform.SetParent(root, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = positionCount;
            line.startWidth = socket.indicatorWidth;
            line.endWidth = socket.indicatorWidth;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = lineMaterial;
            return line;
        }

        private void SetVisible(bool visible)
        {
            if (root != null && root.gameObject.activeSelf != visible)
            {
                root.gameObject.SetActive(visible);
            }
        }

        private void DrawCarrierVolume(Color color)
        {
            if (socket.carrierStagingVolume == null)
            {
                for (int i = 0; i < carrierLines.Count; i++) carrierLines[i].enabled = false;
                return;
            }

            BoxCollider box = socket.carrierStagingVolume;
            Vector3 center = box.transform.TransformPoint(box.center);
            Vector3 extents = Vector3.Scale(box.size * 0.5f, Abs(box.transform.lossyScale));
            Vector3[] corners = new Vector3[8];
            int index = 0;
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            for (int x = -1; x <= 1; x += 2)
            {
                corners[index++] = center + box.transform.rotation * Vector3.Scale(extents, new Vector3(x, y, z));
            }

            int[,] edges = { {0,1},{0,2},{0,4},{1,3},{1,5},{2,3},{2,6},{3,7},{4,5},{4,6},{5,7},{6,7} };
            for (int i = 0; i < carrierLines.Count; i++)
            {
                LineRenderer line = carrierLines[i];
                line.enabled = true;
                line.startColor = line.endColor = color;
                line.SetPosition(0, corners[edges[i, 0]]);
                line.SetPosition(1, corners[edges[i, 1]]);
            }
        }

        private void DrawPositionIndicators(Vector3 localError)
        {
            Color[] colors = { Color.red, Color.green, Color.blue };
            Vector3[] axes = { socket.TargetPose.right, socket.TargetPose.up, socket.TargetPose.forward };
            float[] values = { localError.x, localError.y, localError.z };
            for (int i = 0; i < 3; i++)
            {
                LineRenderer line = positionLines[i];
                line.enabled = Mathf.Abs(values[i]) > 0.01f;
                line.startColor = line.endColor = colors[i];
                line.SetPosition(0, socket.TargetPose.position);
                line.SetPosition(1, socket.TargetPose.position + axes[i] * Mathf.Clamp(values[i], -1.5f, 1.5f));
            }
        }

        private void DrawRotationIndicators(Vector3 localError)
        {
            Color[] colors = { Color.red, Color.green, Color.blue };
            Vector3[] axes = { socket.TargetPose.right, socket.TargetPose.up, socket.TargetPose.forward };
            Vector3[] starts = { socket.TargetPose.up, socket.TargetPose.forward, socket.TargetPose.right };
            float[] values = { localError.x, localError.y, localError.z };
            for (int axisIndex = 0; axisIndex < 3; axisIndex++)
            {
                LineRenderer line = rotationLines[axisIndex];
                float angle = Mathf.Clamp(values[axisIndex], -120f, 120f);
                line.enabled = Mathf.Abs(angle) > 0.5f;
                line.startColor = line.endColor = colors[axisIndex];
                for (int pointIndex = 0; pointIndex < line.positionCount; pointIndex++)
                {
                    float t = pointIndex / (float)(line.positionCount - 1);
                    Vector3 radial = Quaternion.AngleAxis(angle * t, axes[axisIndex]) * starts[axisIndex];
                    line.SetPosition(pointIndex, socket.TargetPose.position + radial.normalized * 0.6f);
                }
            }
        }

        private void ApplyGhostColor(Color color)
        {
            for (int i = 0; i < ghostRenderers.Length; i++)
            {
                Renderer renderer = ghostRenderers[i];
                if (renderer == null) continue;
                Color applied = color;
                applied.a *= originalGhostColors[i].a;
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor("_BaseColor", applied);
                propertyBlock.SetColor("_Color", applied);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void RestoreGhostColors()
        {
            for (int i = 0; i < ghostRenderers.Length; i++)
            {
                Renderer renderer = ghostRenderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor("_BaseColor", originalGhostColors[i]);
                propertyBlock.SetColor("_Color", originalGhostColors[i]);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
