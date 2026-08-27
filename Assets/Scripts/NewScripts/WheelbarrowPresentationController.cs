using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public class WheelbarrowPresentationController : MonoBehaviour
{
    private sealed class VisualProxy
    {
        public Transform Source;
        public GameObject Proxy;
        public Renderer[] SourceRenderers;
        public bool[] SourceRendererStates;
        public Transform[] SourceTransforms;
        public Transform[] ProxyTransforms;
    }

    private readonly List<VisualProxy> visualProxies = new List<VisualProxy>();
    private readonly List<WheelbarrowMotionSnapshot> snapshots = new List<WheelbarrowMotionSnapshot>(24);
    private WheelbarrowController wheelbarrow;
    private Transform driverAnchor;
    private Transform[] visualRoots = Array.Empty<Transform>();
    private Vector3 observedVelocity;
    private float observedYawRate;
    private Vector3 presentedPosition;
    private Quaternion presentedRotation = Quaternion.identity;
    private float snapshotArrivalInterval;
    private float extrapolationTime;
    private float presentationDelay;
    private float correctionDistance;
    private double lastSnapshotArrivalTime = -1d;
    private float nextDiagnosticsTime;
    private WheelbarrowState previousState;
    private uint currentEpoch;
    private uint latestSequence;
    private bool hasSequence;
    private float presentedSteeringAngle;
    private float presentedWheelSpin;
    private bool active;

    public bool IsActive => active;
    public Vector3 PresentedPosition => active ? presentedPosition : wheelbarrow != null ? wheelbarrow.transform.position : transform.position;
    public Quaternion PresentedRotation => active ? presentedRotation : wheelbarrow != null ? wheelbarrow.transform.rotation : transform.rotation;
    public float PositionLead => 0f;
    public float YawLead => 0f;
    public int SnapshotBufferDepth => snapshots.Count;
    public float SnapshotArrivalInterval => snapshotArrivalInterval;
    public float PresentationDelay => presentationDelay;
    public float ExtrapolationTime => extrapolationTime;
    public float CorrectionDistance => correctionDistance;

    public void Initialize(
        WheelbarrowController owner,
        Transform anchor,
        params Transform[] roots)
    {
        wheelbarrow = owner;
        driverAnchor = anchor;
        visualRoots = roots ?? Array.Empty<Transform>();
        previousState = wheelbarrow != null ? wheelbarrow.State : WheelbarrowState.Free;
        ResetPresentation();
    }

    public void SetLocalDriveInput(float throttle, float steering)
    {
        // The local physics owner consumes input directly in WheelbarrowController.
    }

    public void ReceiveSnapshot(WheelbarrowMotionSnapshot snapshot)
    {
        if (wheelbarrow == null || wheelbarrow.HasLocalPhysicsAuthority) return;
        if (snapshot.AuthorityEpoch < currentEpoch) return;
        if (snapshot.AuthorityEpoch > currentEpoch)
        {
            snapshots.Clear();
            currentEpoch = snapshot.AuthorityEpoch;
            hasSequence = false;
        }
        if (hasSequence && !IsSequenceNewer(snapshot.Sequence, latestSequence)) return;
        latestSequence = snapshot.Sequence;
        hasSequence = true;

        double arrivalTime = Time.unscaledTimeAsDouble;
        if (lastSnapshotArrivalTime >= 0d)
            snapshotArrivalInterval = (float)(arrivalTime - lastSnapshotArrivalTime);
        lastSnapshotArrivalTime = arrivalTime;

        WheelbarrowProfileSO profile = wheelbarrow.Profile;
        if (snapshots.Count > 0)
        {
            WheelbarrowMotionSnapshot latest = snapshots[snapshots.Count - 1];
            float teleportDistance = profile != null ? profile.ClientPresentationTeleportDistance : 2f;
            float teleportAngle = profile != null ? profile.ClientPresentationTeleportAngle : 45f;
            if (Vector3.Distance(latest.Position, snapshot.Position) > teleportDistance ||
                Quaternion.Angle(latest.Rotation, snapshot.Rotation) > teleportAngle)
                snapshots.Clear();
        }

        snapshots.Add(snapshot);

        int capacity = profile != null ? profile.ClientPresentationSnapshotCapacity : 24;
        while (snapshots.Count > capacity) snapshots.RemoveAt(0);
    }

    public void ResetPresentation()
    {
        snapshots.Clear();
        currentEpoch = wheelbarrow != null ? wheelbarrow.AuthorityEpoch : 0;
        latestSequence = 0;
        hasSequence = false;
        observedVelocity = Vector3.zero;
        observedYawRate = 0f;
        extrapolationTime = 0f;
        presentationDelay = 0f;
        correctionDistance = 0f;
        if (wheelbarrow == null) return;
        presentedPosition = wheelbarrow.transform.position;
        presentedRotation = wheelbarrow.transform.rotation;
        presentedSteeringAngle = wheelbarrow.CurrentSteeringAngle;
        presentedWheelSpin = wheelbarrow.CurrentWheelSpinDegrees;
    }

    public bool TryGetDriverAnchorPose(out Vector3 position, out Quaternion rotation)
    {
        return TryGetPresentedAnchorPose(driverAnchor, out position, out rotation);
    }

    public bool TryGetPresentedAnchorPose(Transform anchor, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (anchor == null || wheelbarrow == null) return false;

        Quaternion sourceRootRotation = wheelbarrow.transform.rotation;
        Vector3 localAnchor = wheelbarrow.transform.InverseTransformPoint(anchor.position);
        Quaternion localRotation = Quaternion.Inverse(sourceRootRotation) * anchor.rotation;
        position = PresentedPosition + PresentedRotation * localAnchor;
        rotation = PresentedRotation * localRotation;
        return true;
    }

    private void Update()
    {
        if (wheelbarrow == null) return;

        bool shouldBeActive = ShouldUseClientPresentation();
        if (shouldBeActive != active) SetActive(shouldBeActive);
        if (!active) return;

        WheelbarrowState state = wheelbarrow.State;
        if (state != previousState)
        {
            previousState = state;
            ResetPresentation();
        }

        ResolveBufferedPose(
            out presentedPosition,
            out presentedRotation,
            out presentedSteeringAngle,
            out presentedWheelSpin);
        correctionDistance = Vector3.Distance(wheelbarrow.transform.position, presentedPosition);
        LogDiagnostics();
    }

    private void LateUpdate()
    {
        if (!active || wheelbarrow == null) return;
        wheelbarrow.ApplyPresentedWheelVisual(presentedSteeringAngle, presentedWheelSpin);
        UpdateVisualProxies(presentedPosition, presentedRotation);
        wheelbarrow.ApplyPresentedCargoTransforms(presentedPosition, presentedRotation);
    }

    private bool ShouldUseClientPresentation()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && manager.IsListening && wheelbarrow.IsSpawned && !wheelbarrow.HasLocalPhysicsAuthority;
    }

    private void SetActive(bool value)
    {
        active = value;
        ResetPresentation();

        if (value)
        {
            EnsureVisualProxies();
            SetSourceRenderersVisible(false);
        }
        else
        {
            SetSourceRenderersVisible(true);
            foreach (VisualProxy proxy in visualProxies)
                if (proxy.Proxy != null) proxy.Proxy.SetActive(false);
        }
    }

    private void ResolveBufferedPose(
        out Vector3 position,
        out Quaternion rotation,
        out float steeringAngle,
        out float wheelSpin)
    {
        position = wheelbarrow.transform.position;
        rotation = wheelbarrow.transform.rotation;
        steeringAngle = wheelbarrow.CurrentSteeringAngle;
        wheelSpin = wheelbarrow.CurrentWheelSpinDegrees;
        extrapolationTime = 0f;
        presentationDelay = wheelbarrow.Profile != null ? wheelbarrow.Profile.ObserverPresentationDelay : 0.04f;
        if (snapshots.Count == 0) return;

        NetworkManager manager = NetworkManager.Singleton;
        double serverNow = manager != null ? manager.ServerTime.Time : snapshots[snapshots.Count - 1].ServerTimestamp;
        double renderTime = serverNow - presentationDelay;

        WheelbarrowMotionSnapshot first = snapshots[0];
        WheelbarrowMotionSnapshot latest = snapshots[snapshots.Count - 1];
        if (renderTime <= first.ServerTimestamp)
        {
            position = first.Position;
            rotation = first.Rotation;
            steeringAngle = first.SteeringAngle;
            wheelSpin = first.WheelSpinDegrees;
            observedVelocity = first.LinearVelocity;
            observedYawRate = Vector3.Dot(first.AngularVelocity, Vector3.up) * Mathf.Rad2Deg;
            return;
        }

        for (int i = 1; i < snapshots.Count; i++)
        {
            WheelbarrowMotionSnapshot next = snapshots[i];
            if (renderTime > next.ServerTimestamp) continue;
            WheelbarrowMotionSnapshot previous = snapshots[i - 1];
            float duration = Mathf.Max(0.0001f, (float)(next.ServerTimestamp - previous.ServerTimestamp));
            float t = Mathf.Clamp01((float)((renderTime - previous.ServerTimestamp) / duration));
            position = Hermite(previous.Position, previous.LinearVelocity * duration,
                next.Position, next.LinearVelocity * duration, t);
            rotation = Quaternion.Slerp(previous.Rotation, next.Rotation, t);
            steeringAngle = Mathf.Lerp(previous.SteeringAngle, next.SteeringAngle, t);
            wheelSpin = previous.WheelSpinDegrees + Mathf.DeltaAngle(previous.WheelSpinDegrees, next.WheelSpinDegrees) * t;
            observedVelocity = Vector3.Lerp(previous.LinearVelocity, next.LinearVelocity, t);
            observedYawRate = Vector3.Dot(
                Vector3.Lerp(previous.AngularVelocity, next.AngularVelocity, t),
                Vector3.up) * Mathf.Rad2Deg;
            return;
        }

        float requestedExtrapolation = Mathf.Max(0f, (float)(renderTime - latest.ServerTimestamp));
        float maximumExtrapolation = wheelbarrow.Profile != null
            ? wheelbarrow.Profile.ClientPresentationMaximumExtrapolation
            : 0.1f;
        extrapolationTime = Mathf.Min(requestedExtrapolation, maximumExtrapolation);
        position = latest.Position + latest.LinearVelocity * extrapolationTime;
        rotation = IntegrateRotation(latest.Rotation, latest.AngularVelocity, extrapolationTime);
        steeringAngle = latest.SteeringAngle;
        float wheelRadius = wheelbarrow.Profile != null ? wheelbarrow.Profile.WheelRadius : 0.44f;
        wheelSpin = latest.WheelSpinDegrees +
            Vector3.Dot(latest.LinearVelocity, latest.Rotation * Vector3.forward) /
            Mathf.Max(0.01f, wheelRadius) * Mathf.Rad2Deg * extrapolationTime;
        observedVelocity = latest.LinearVelocity;
        observedYawRate = Vector3.Dot(latest.AngularVelocity, Vector3.up) * Mathf.Rad2Deg;
    }

    private static bool IsSequenceNewer(uint sequence, uint previous) =>
        sequence != previous && unchecked(sequence - previous) < 0x80000000u;

    private static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return (2f * t3 - 3f * t2 + 1f) * p0 +
            (t3 - 2f * t2 + t) * m0 +
            (-2f * t3 + 3f * t2) * p1 +
            (t3 - t2) * m1;
    }

    private static Quaternion IntegrateRotation(Quaternion rotation, Vector3 angularVelocity, float deltaTime)
    {
        float angularSpeed = angularVelocity.magnitude;
        if (angularSpeed <= 0.0001f || deltaTime <= 0f) return rotation;
        return Quaternion.AngleAxis(
            angularSpeed * Mathf.Rad2Deg * deltaTime,
            angularVelocity / angularSpeed) * rotation;
    }

    private void EnsureVisualProxies()
    {
        if (visualProxies.Count > 0) return;
        foreach (Transform source in visualRoots)
        {
            if (source == null) continue;
            GameObject proxyObject = Instantiate(source.gameObject);
            proxyObject.name = source.name + "_NetworkPresentation";
            proxyObject.SetActive(false);
            foreach (Collider collider in proxyObject.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody body in proxyObject.GetComponentsInChildren<Rigidbody>(true))
                body.isKinematic = true;

            Transform[] sourceTransforms = source.GetComponentsInChildren<Transform>(true);
            Transform[] proxyTransforms = proxyObject.GetComponentsInChildren<Transform>(true);
            Renderer[] sourceRenderers = source.GetComponentsInChildren<Renderer>(true);
            bool[] sourceStates = new bool[sourceRenderers.Length];
            for (int i = 0; i < sourceRenderers.Length; i++)
                sourceStates[i] = sourceRenderers[i] != null && sourceRenderers[i].enabled;

            visualProxies.Add(new VisualProxy
            {
                Source = source,
                Proxy = proxyObject,
                SourceRenderers = sourceRenderers,
                SourceRendererStates = sourceStates,
                SourceTransforms = sourceTransforms,
                ProxyTransforms = proxyTransforms
            });
        }
    }

    private void UpdateVisualProxies(Vector3 rootPosition, Quaternion rootRotation)
    {
        Quaternion sourceRootRotation = wheelbarrow.transform.rotation;

        foreach (VisualProxy entry in visualProxies)
        {
            if (entry.Source == null || entry.Proxy == null) continue;
            bool visible = entry.Source.gameObject.activeInHierarchy;
            entry.Proxy.SetActive(visible);
            if (!visible) continue;

            int transformCount = Mathf.Min(entry.SourceTransforms.Length, entry.ProxyTransforms.Length);
            for (int i = 1; i < transformCount; i++)
            {
                entry.ProxyTransforms[i].localPosition = entry.SourceTransforms[i].localPosition;
                entry.ProxyTransforms[i].localRotation = entry.SourceTransforms[i].localRotation;
                entry.ProxyTransforms[i].localScale = entry.SourceTransforms[i].localScale;
            }

            Vector3 localPosition = wheelbarrow.transform.InverseTransformPoint(entry.Source.position);
            Quaternion localRotation = Quaternion.Inverse(sourceRootRotation) * entry.Source.rotation;
            entry.Proxy.transform.SetPositionAndRotation(
                rootPosition + rootRotation * localPosition,
                rootRotation * localRotation);
            entry.Proxy.transform.localScale = entry.Source.lossyScale;
        }
    }

    private void SetSourceRenderersVisible(bool visible)
    {
        foreach (VisualProxy proxy in visualProxies)
        {
            for (int i = 0; i < proxy.SourceRenderers.Length; i++)
            {
                Renderer renderer = proxy.SourceRenderers[i];
                if (renderer != null)
                    renderer.enabled = visible && proxy.SourceRendererStates[i];
            }
        }
    }

    private void LogDiagnostics()
    {
        if (wheelbarrow.Profile == null || !wheelbarrow.Profile.EnableDiagnostics ||
            Time.unscaledTime < nextDiagnosticsTime) return;
        nextDiagnosticsTime = Time.unscaledTime + 1f;
        Debug.Log($"[WheelbarrowPresentation] buffer={snapshots.Count} arrival={snapshotArrivalInterval:F3}s " +
            $"delay={presentationDelay:F3}s extrapolation={extrapolationTime:F3}s correction={correctionDistance:F3}m", wheelbarrow);
    }

    private void OnDisable()
    {
        if (active) SetActive(false);
    }

    private void OnDestroy()
    {
        SetSourceRenderersVisible(true);
        foreach (VisualProxy proxy in visualProxies)
            if (proxy.Proxy != null) Destroy(proxy.Proxy);
        visualProxies.Clear();
    }
}
