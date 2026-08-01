using System.Collections.Generic;
using UnityEngine;

public struct SharedCarryPhysicsHolder
{
    public Transform BodyAnchor;
    public Vector3 BaseAttachLocalPoint;
    public Vector3 AttachLocalPoint;
    public Vector3 DesiredLateralInput;
    public float DesiredYawInput;
    public float DesiredGripHeightInput;
}

[RequireComponent(typeof(Rigidbody))]
public class SharedCarryPhysicsBody : MonoBehaviour
{
    [SerializeField] private CarryPhysicsProfileSO profile;
    [SerializeField] private float defaultMass = 20f;
    [SerializeField] private float defaultLinearDrag = 1.5f;
    [SerializeField] private float defaultAngularDrag = 4f;
    [SerializeField] private float defaultHorizontalConstraintSpring = 140f;
    [SerializeField] private float defaultHorizontalConstraintDampingRatio = 1.05f;
    [SerializeField] private float defaultMaxHorizontalConstraintForce = 650f;
    [SerializeField] private float defaultHorizontalConstraintDeadZone = 0.03f;
    [SerializeField] private float defaultHorizontalConstraintForceResponse = 18f;
    [SerializeField] private float defaultMaxHolderAnchorVelocity = 8f;
    [SerializeField] private float defaultVerticalSupportSpring = 220f;
    [SerializeField] private float defaultVerticalSupportDampingRatio = 1.1f;
    [SerializeField] private float defaultMaxVerticalSupportForce = 1600f;
    [SerializeField] private float defaultMaxGripDistance = 1.25f;
    [SerializeField] private float defaultMaxVelocity = 6f;
    [SerializeField] private float defaultMovementForce = 450f;
    [SerializeField] private float defaultMovementDamper = 65f;
    [SerializeField] private float defaultMaxGripTorque = 250f;
    [SerializeField] private float defaultSharedCarryYawTorque = 60f;

    private Rigidbody body;
    private bool sharedCarryActive;
    private float normalMass;
    private float normalLinearDamping;
    private float normalAngularDamping;
    private bool normalPhysicsCaptured;
    private bool usesCustomRotationConstraint;
    private Quaternion sharedCarryTiltOffset = Quaternion.identity;
    private Quaternion lockedSharedCarryYaw = Quaternion.identity;
    private Collider[] gripColliders;
    private MeshRenderer[] previewMeshRenderers;
    private SkinnedMeshRenderer[] previewSkinnedRenderers;
    private readonly Dictionary<Transform, AnchorMotionState> anchorMotionStates = new Dictionary<Transform, AnchorMotionState>();
    private readonly Dictionary<Transform, Vector3> smoothedConstraintForces = new Dictionary<Transform, Vector3>();

    private struct AnchorMotionState
    {
        public Vector3 Position;
        public bool IsInitialized;
    }

    public Rigidbody Body => body;
    public CarryPhysicsProfileSO Profile => profile;
    public SharedCarryControlMode ControlMode => profile != null ? profile.controlMode : SharedCarryControlMode.DirectYaw;
    public float OrbitArcDegrees => profile != null ? Mathf.Max(0f, profile.orbitArcDegrees) : 0f;
    public float OrbitAngularSpeed => profile != null ? Mathf.Max(0f, profile.orbitAngularSpeed) : 0f;
    public float OrbitPredictionCorrectionSpeed => profile != null ? Mathf.Max(0f, profile.orbitPredictionCorrectionSpeed) : 0f;
    public float MaxGripDistance => GetMaxGripDistance();
    public float MaximumGripHeightOffset => profile != null ? Mathf.Max(0f, profile.maximumGripHeightOffset) : 0f;
    public float SoftTetherDeadZone => profile != null ? Mathf.Max(0f, profile.softTetherDeadZone) : 0f;
    public float SoftTetherPullSpeed => profile != null ? Mathf.Max(0f, profile.softTetherPullSpeed) : 0f;
    public float SoftTetherVelocityInfluence => profile != null ? Mathf.Max(0f, profile.softTetherVelocityInfluence) : 0f;
    public bool PreventGroundedUpwardTether => profile != null && profile.preventGroundedUpwardTether;
    public float HardTetherDistance => profile != null ? Mathf.Max(0.01f, profile.hardTetherDistance) : GetMaxGripDistance();
    public float TetherBreakDelay => profile != null ? Mathf.Max(0f, profile.tetherBreakDelay) : 0f;

    public void SetProfile(CarryPhysicsProfileSO physicsProfile)
    {
        if (physicsProfile != null)
        {
            profile = physicsProfile;
        }
        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }
        if (sharedCarryActive)
        {
            ApplyProfile(true);
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        CacheGripColliders();
        CachePreviewRenderers();
        normalMass = body.mass;
        normalLinearDamping = body.linearDamping;
        normalAngularDamping = body.angularDamping;
        normalPhysicsCaptured = true;
    }

    public void BeginSharedCarry(bool simulatePhysics)
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        sharedCarryActive = simulatePhysics;
        ApplyProfile(true);
        if (!simulatePhysics)
        {
            body.useGravity = false;
            body.isKinematic = true;
        }
        body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, GetMaxVelocity());
        body.angularVelocity = Vector3.zero;
        ConfigureRotationConstraint();
        ResetConstraintState();
    }

    public void EndSharedCarry()
    {
        sharedCarryActive = false;
        if (body == null)
        {
            return;
        }

        body.constraints = RigidbodyConstraints.None;
        usesCustomRotationConstraint = false;
        sharedCarryTiltOffset = Quaternion.identity;
        lockedSharedCarryYaw = Quaternion.identity;
        RestoreNormalPhysics();
        ResetConstraintState();
    }

    public void Simulate(IReadOnlyList<SharedCarryPhysicsHolder> holders, Vector3 combinedInput, int carrierNormalizationCount, float fixedDeltaTime)
    {
        if (!sharedCarryActive || body == null || !body.gameObject.activeInHierarchy || holders == null || holders.Count == 0)
        {
            return;
        }

        ApplyCustomRotationConstraint();

        if (ControlMode == SharedCarryControlMode.PhysicalPointGrip)
        {
            SimulatePhysicalPointGrip(holders, combinedInput, carrierNormalizationCount, fixedDeltaTime);
            return;
        }

        int validHolderCount = 0;
        float targetHeight = 0f;
        for (int i = 0; i < holders.Count; i++)
        {
            SharedCarryPhysicsHolder holder = holders[i];
            if (holder.BodyAnchor == null)
            {
                continue;
            }

            targetHeight += holder.BodyAnchor.position.y - transform.TransformVector(holder.AttachLocalPoint).y;
            validHolderCount++;
        }

        if (validHolderCount == 0)
        {
            return;
        }

        float maxDistance = GetMaxGripDistance();
        float horizontalSpring = GetHorizontalConstraintSpring();
        float perHolderSpring = horizontalSpring / validHolderCount;
        float totalHorizontalDamper = GetCriticalDamper(horizontalSpring, GetHorizontalConstraintDampingRatio());
        float perHolderDamper = totalHorizontalDamper / validHolderCount;
        float perHolderMaxForce = GetMaxHorizontalConstraintForce() / validHolderCount;
        float deadZone = GetHorizontalConstraintDeadZone();
        float forceBlend = 1f - Mathf.Exp(-GetHorizontalConstraintForceResponse() * fixedDeltaTime);

        for (int i = 0; i < holders.Count; i++)
        {
            SharedCarryPhysicsHolder holder = holders[i];
            if (holder.BodyAnchor == null)
            {
                continue;
            }

            Vector3 attachPoint = transform.TransformPoint(holder.AttachLocalPoint);
            Vector3 error = holder.BodyAnchor.position - attachPoint;
            error.y = 0f;
            float errorMagnitude = error.magnitude;
            if (errorMagnitude > maxDistance)
            {
                error = error.normalized * maxDistance;
            }

            if (errorMagnitude <= deadZone)
            {
                error = Vector3.zero;
            }
            else
            {
                error *= (errorMagnitude - deadZone) / Mathf.Max(errorMagnitude, 0.0001f);
            }

            Vector3 pointVelocity = body.GetPointVelocity(attachPoint);
            pointVelocity.y = 0f;
            Vector3 anchorVelocity = GetAnchorVelocity(holder.BodyAnchor, fixedDeltaTime);
            Vector3 relativeVelocity = anchorVelocity - pointVelocity;
            Vector3 targetForce = error * perHolderSpring + relativeVelocity * perHolderDamper;
            targetForce = Vector3.ClampMagnitude(targetForce, perHolderMaxForce);
            Vector3 force = SmoothConstraintForce(holder.BodyAnchor, targetForce, forceBlend);
            body.AddForce(force, ForceMode.Force);
        }

        PruneConstraintState(holders);

        targetHeight /= validHolderCount;
        float verticalError = targetHeight - body.position.y;
        float verticalForce = verticalError * GetVerticalSupportSpring() - body.linearVelocity.y * GetCriticalDamper(GetVerticalSupportSpring(), GetVerticalSupportDampingRatio());
        verticalForce = Mathf.Clamp(verticalForce, -GetMaxVerticalSupportForce(), GetMaxVerticalSupportForce());
        body.AddForce(Vector3.up * verticalForce, ForceMode.Force);

        Vector3 horizontalVelocity = body.linearVelocity;
        horizontalVelocity.y = 0f;
        Vector3 movementForce = Vector3.ClampMagnitude(combinedInput, 1f) * GetMovementForce() - horizontalVelocity * GetMovementDamper();
        body.AddForce(movementForce, ForceMode.Force);

        if (AllowsYawRotation() && ControlMode == SharedCarryControlMode.DirectYaw)
        {
            float normalizationDivisor = Mathf.Max(1, carrierNormalizationCount);
            float combinedYawInput = 0f;
            for (int i = 0; i < holders.Count; i++)
            {
                combinedYawInput += Mathf.Clamp(holders[i].DesiredYawInput, -1f, 1f);
            }

            float yawTorque = combinedYawInput / normalizationDivisor * GetSharedCarryYawTorque();
            yawTorque = Mathf.Clamp(yawTorque, -GetMaxGripTorque(), GetMaxGripTorque());
            body.AddTorque(Vector3.up * yawTorque, ForceMode.Force);
        }
        else if (AllowsYawRotation() && ControlMode == SharedCarryControlMode.SpatialOrbit)
        {
            float normalizationDivisor = Mathf.Max(1, carrierNormalizationCount);
            Vector3 lateralForce = Vector3.zero;
            float lateralTorque = 0f;
            Vector3 worldCenterOfMass = body.worldCenterOfMass;

            for (int i = 0; i < holders.Count; i++)
            {
                SharedCarryPhysicsHolder holder = holders[i];
                Vector3 attachPoint = transform.TransformPoint(holder.AttachLocalPoint);
                Vector3 leverArm = Vector3.ProjectOnPlane(attachPoint - worldCenterOfMass, Vector3.up);
                if (leverArm.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                Vector3 tangent = Vector3.Cross(Vector3.up, leverArm.normalized);
                float tangentialInput = Mathf.Clamp(Vector3.Dot(holder.DesiredLateralInput, tangent), -1f, 1f);
                Vector3 holderForce = tangent * tangentialInput * GetSpatialLateralForce() / normalizationDivisor;
                lateralForce += holderForce;
                lateralTorque += Vector3.Cross(leverArm, holderForce).y;
            }

            body.AddForce(lateralForce, ForceMode.Force);
            lateralTorque = Mathf.Clamp(lateralTorque, -GetMaxGripTorque(), GetMaxGripTorque());
            body.AddTorque(Vector3.up * lateralTorque, ForceMode.Force);
        }

        horizontalVelocity = Vector3.ClampMagnitude(new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z), GetMaxVelocity());
        body.linearVelocity = new Vector3(horizontalVelocity.x, body.linearVelocity.y, horizontalVelocity.z);

        if (GetMaxAngularVelocity() > 0f)
        {
            body.angularVelocity = new Vector3(0f, Mathf.Clamp(body.angularVelocity.y, -GetMaxAngularVelocity(), GetMaxAngularVelocity()), 0f);
        }
    }

    private void SimulatePhysicalPointGrip(IReadOnlyList<SharedCarryPhysicsHolder> holders, Vector3 combinedInput, int carrierNormalizationCount, float fixedDeltaTime)
    {
        int validHolderCount = 0;
        float normalizationDivisor = Mathf.Max(1, carrierNormalizationCount);
        float forceBlend = 1f - Mathf.Exp(-GetHorizontalConstraintForceResponse() * fixedDeltaTime);

        for (int i = 0; i < holders.Count; i++)
        {
            if (holders[i].BodyAnchor != null)
            {
                validHolderCount++;
            }
        }

        if (validHolderCount == 0)
        {
            return;
        }

        Vector3 gravitySupportPerHolder = body.useGravity
            ? Vector3.up * Mathf.Max(0f, -Physics.gravity.y) * body.mass / normalizationDivisor
            : Vector3.zero;
        Vector3 worldLongAxis = Vector3.zero;
        bool compensateGripRoll = profile != null
            && profile.compensatePointGripRoll
            && TryGetWorldLongAxis(out worldLongAxis);
        float accumulatedGripRollTorque = 0f;
        Vector3 worldCenterOfMass = body.worldCenterOfMass;

        for (int i = 0; i < holders.Count; i++)
        {
            SharedCarryPhysicsHolder holder = holders[i];
            if (holder.BodyAnchor == null)
            {
                continue;
            }

            Vector3 attachPoint = transform.TransformPoint(holder.AttachLocalPoint);
            Vector3 forceApplicationPoint = ResolvePhysicalGripPoint(attachPoint);
            float heightOffset = Mathf.Clamp(holder.DesiredGripHeightInput, -1f, 1f) * MaximumGripHeightOffset;
            Vector3 targetAnchor = holder.BodyAnchor.position + Vector3.up * heightOffset;
            Vector3 error = Vector3.ClampMagnitude(targetAnchor - attachPoint, GetMaxGripDistance());
            float errorMagnitude = error.magnitude;
            float deadZone = GetHorizontalConstraintDeadZone();
            if (errorMagnitude <= deadZone)
            {
                error = Vector3.zero;
            }
            else
            {
                error *= (errorMagnitude - deadZone) / Mathf.Max(errorMagnitude, 0.0001f);
            }

            Vector3 anchorVelocity = GetAnchorVelocity3D(holder.BodyAnchor, fixedDeltaTime);
            Vector3 pointVelocity = body.GetPointVelocity(attachPoint);
            Vector3 gripForce = error * GetPointGripSpring() + (anchorVelocity - pointVelocity) * GetPointGripDamping();
            gripForce.y *= GetPointGripVerticalForceScale();
            gripForce += gravitySupportPerHolder;
            gripForce = Vector3.ClampMagnitude(gripForce, GetPointGripMaxForce() / normalizationDivisor);
            gripForce = LimitPointGripLiftCapacity(gripForce);
            gripForce = SmoothConstraintForce(holder.BodyAnchor, gripForce, forceBlend);
            gripForce = LimitPointGripLiftCapacity(gripForce);
            if (compensateGripRoll)
            {
                Vector3 gripTorque = Vector3.Cross(forceApplicationPoint - worldCenterOfMass, gripForce);
                accumulatedGripRollTorque += Vector3.Dot(gripTorque, worldLongAxis);
            }
            body.AddForceAtPosition(gripForce, forceApplicationPoint, ForceMode.Force);

            Vector3 lateralForce = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(holder.DesiredLateralInput, Vector3.up), 1f)
                * GetPointGripLateralForce() / normalizationDivisor;
            body.AddForceAtPosition(lateralForce, forceApplicationPoint, ForceMode.Force);
        }

        if (compensateGripRoll)
        {
            float compensationTorque = -accumulatedGripRollTorque
                * Mathf.Clamp01(profile.pointGripRollCompensation);
            float maximumCompensation = Mathf.Max(0f, profile.maximumPointGripRollCompensationTorque);
            compensationTorque = Mathf.Clamp(compensationTorque, -maximumCompensation, maximumCompensation);
            body.AddTorque(worldLongAxis * compensationTorque, ForceMode.Force);
        }

        PruneConstraintState(holders);
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        body.AddForce(Vector3.ClampMagnitude(combinedInput, 1f) * GetMovementForce() - horizontalVelocity * GetMovementDamper(), ForceMode.Force);
        ApplyLongAxisRollStabilization();
        ApplyOptionalTiltLimit();

        horizontalVelocity = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up), GetMaxVelocity());
        body.linearVelocity = new Vector3(horizontalVelocity.x, body.linearVelocity.y, horizontalVelocity.z);
        if (GetMaxAngularVelocity() > 0f)
        {
            body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, GetMaxAngularVelocity());
        }
    }

    private void ApplyProfile(bool active)
    {
        if (body == null)
        {
            return;
        }

        body.mass = profile != null ? Mathf.Max(0.01f, profile.mass) : defaultMass;
        body.linearDamping = profile != null ? Mathf.Max(0f, profile.linearDrag) : defaultLinearDrag;
        body.angularDamping = profile != null ? Mathf.Max(0f, profile.angularDrag) : defaultAngularDrag;
        body.useGravity = profile == null || profile.useGravity;
        body.isKinematic = false;
    }

    public Vector3 ResolvePhysicalGripPoint(Vector3 logicalWorldPoint)
    {
        if (profile == null || !profile.projectGripForcesToColliderSurface)
        {
            return logicalWorldPoint;
        }

        if (gripColliders == null || gripColliders.Length == 0)
        {
            CacheGripColliders();
        }

        Vector3 closestPoint = logicalWorldPoint;
        float closestSqrDistance = float.PositiveInfinity;
        for (int i = 0; i < gripColliders.Length; i++)
        {
            Collider candidate = gripColliders[i];
            if (candidate == null || !candidate.enabled || candidate.isTrigger || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 candidatePoint = candidate.ClosestPoint(logicalWorldPoint);
            float sqrDistance = (candidatePoint - logicalWorldPoint).sqrMagnitude;
            if (sqrDistance >= closestSqrDistance)
            {
                continue;
            }

            closestSqrDistance = sqrDistance;
            closestPoint = candidatePoint;
        }

        return closestPoint;
    }

    public Vector3 ResolvePreviewGripPointForPose(
        Vector3 logicalLocalPoint,
        Vector3 position,
        Quaternion rotation,
        out Vector3 surfaceOutwardDirection)
    {
        Vector3 surfaceLocalPoint;
        Vector3 outwardLocalDirection;
        if (TryGetPreviewVisualLocalBounds(out Bounds visualBounds))
        {
            surfaceLocalPoint = GetClosestPointOnBoundsSurface(visualBounds, logicalLocalPoint);
            outwardLocalDirection = logicalLocalPoint - surfaceLocalPoint;
            if (outwardLocalDirection.sqrMagnitude < 0.0001f)
            {
                outwardLocalDirection = surfaceLocalPoint - visualBounds.center;
            }
        }
        else
        {
            Vector3 logicalWorldPoint = transform.TransformPoint(logicalLocalPoint);
            Vector3 physicalWorldPoint = ResolvePhysicalGripPoint(logicalWorldPoint);
            surfaceLocalPoint = transform.InverseTransformPoint(physicalWorldPoint);
            outwardLocalDirection = logicalLocalPoint - surfaceLocalPoint;
            if (outwardLocalDirection.sqrMagnitude < 0.0001f)
            {
                outwardLocalDirection = surfaceLocalPoint;
            }
        }

        Vector3 scale = transform.lossyScale;
        Vector3 scaledOutwardDirection = Vector3.Scale(outwardLocalDirection, scale);
        surfaceOutwardDirection = scaledOutwardDirection.sqrMagnitude >= 0.0001f
            ? (rotation * scaledOutwardDirection).normalized
            : rotation * Vector3.forward;
        return position + rotation * Vector3.Scale(surfaceLocalPoint, scale);
    }

    private void CacheGripColliders()
    {
        gripColliders = GetComponentsInChildren<Collider>(true);
    }

    private void CachePreviewRenderers()
    {
        previewMeshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        previewSkinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
    }

    private bool TryGetPreviewVisualLocalBounds(out Bounds bounds)
    {
        if (previewMeshRenderers == null || previewSkinnedRenderers == null)
        {
            CachePreviewRenderers();
        }

        bool hasBounds = false;
        bounds = default;
        for (int i = 0; i < previewMeshRenderers.Length; i++)
        {
            MeshRenderer renderer = previewMeshRenderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                || !renderer.TryGetComponent(out MeshFilter meshFilter) || meshFilter.sharedMesh == null)
            {
                continue;
            }

            EncapsulateTransformedBounds(meshFilter.sharedMesh.bounds, meshFilter.transform, ref bounds, ref hasBounds);
        }

        for (int i = 0; i < previewSkinnedRenderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = previewSkinnedRenderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            EncapsulateTransformedBounds(renderer.localBounds, renderer.transform, ref bounds, ref hasBounds);
        }

        return hasBounds;
    }

    private void EncapsulateTransformedBounds(Bounds sourceBounds, Transform sourceTransform, ref Bounds targetBounds, ref bool hasBounds)
    {
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 sourcePoint = sourceBounds.center
                        + Vector3.Scale(sourceBounds.extents, new Vector3(x, y, z));
                    Vector3 localPoint = transform.InverseTransformPoint(sourceTransform.TransformPoint(sourcePoint));
                    if (!hasBounds)
                    {
                        targetBounds = new Bounds(localPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        targetBounds.Encapsulate(localPoint);
                    }
                }
            }
        }
    }

    private static Vector3 GetClosestPointOnBoundsSurface(Bounds bounds, Vector3 point)
    {
        Vector3 closestPoint = bounds.ClosestPoint(point);
        if (!bounds.Contains(point))
        {
            return closestPoint;
        }

        float minDistance = point.x - bounds.min.x;
        closestPoint = new Vector3(bounds.min.x, point.y, point.z);
        TrySelectCloserBoundsFace(bounds.max.x - point.x, new Vector3(bounds.max.x, point.y, point.z), ref minDistance, ref closestPoint);
        TrySelectCloserBoundsFace(point.y - bounds.min.y, new Vector3(point.x, bounds.min.y, point.z), ref minDistance, ref closestPoint);
        TrySelectCloserBoundsFace(bounds.max.y - point.y, new Vector3(point.x, bounds.max.y, point.z), ref minDistance, ref closestPoint);
        TrySelectCloserBoundsFace(point.z - bounds.min.z, new Vector3(point.x, point.y, bounds.min.z), ref minDistance, ref closestPoint);
        TrySelectCloserBoundsFace(bounds.max.z - point.z, new Vector3(point.x, point.y, bounds.max.z), ref minDistance, ref closestPoint);
        return closestPoint;
    }

    private static void TrySelectCloserBoundsFace(float distance, Vector3 candidate, ref float minDistance, ref Vector3 closestPoint)
    {
        if (distance >= minDistance)
        {
            return;
        }

        minDistance = distance;
        closestPoint = candidate;
    }

    private void RestoreNormalPhysics()
    {
        if (body == null)
        {
            return;
        }

        if (normalPhysicsCaptured)
        {
            body.mass = normalMass;
            body.linearDamping = normalLinearDamping;
            body.angularDamping = normalAngularDamping;
        }

        body.useGravity = true;
        body.isKinematic = false;
    }

    private float GetMaxGripDistance() => profile != null ? profile.maxGripDistance : defaultMaxGripDistance;
    private float GetMaxVelocity() => profile != null ? profile.maxVelocity : defaultMaxVelocity;
    private float GetMaxAngularVelocity() => profile != null ? profile.maxAngularVelocity : 3f;
    private float GetMovementForce() => profile != null ? profile.movementForce : defaultMovementForce;
    private float GetMovementDamper() => profile != null ? profile.movementDamper : defaultMovementDamper;
    private float GetMaxGripTorque() => profile != null ? Mathf.Max(0f, profile.maxGripTorque) : defaultMaxGripTorque;
    private float GetSharedCarryYawTorque() => profile != null ? Mathf.Max(0f, profile.sharedCarryYawTorque) : defaultSharedCarryYawTorque;
    private float GetSpatialLateralForce() => profile != null ? Mathf.Max(0f, profile.spatialLateralForce) : 0f;
    private float GetPointGripLateralForce() => profile != null ? Mathf.Max(0f, profile.pointGripLateralForce) : 0f;
    private float GetPointGripSpring() => profile != null ? Mathf.Max(0f, profile.pointGripSpring) : GetHorizontalConstraintSpring();
    private float GetPointGripDamping() => profile != null ? Mathf.Max(0f, profile.pointGripDamping) : 0f;
    private float GetPointGripMaxForce() => profile != null ? Mathf.Max(0f, profile.pointGripMaxForce) : GetMaxHorizontalConstraintForce();
    private float GetPointGripVerticalForceScale() => profile == null || profile.pointGripSpring <= 0f
        ? 1f
        : Mathf.Max(0f, profile.pointGripVerticalForce) / profile.pointGripSpring;

    private Vector3 LimitPointGripLiftCapacity(Vector3 force)
    {
        if (profile == null || !profile.limitPointGripLiftByCarrierCapacity || !body.useGravity)
        {
            return force;
        }

        float gravityMagnitude = Mathf.Max(0f, -Physics.gravity.y);
        if (gravityMagnitude <= 0f)
        {
            return force;
        }

        float maximumUpwardForce = body.mass
            * gravityMagnitude
            * Mathf.Clamp(profile.pointGripLiftCapacityPerCarrier, 0f, 2f);
        force.y = Mathf.Min(force.y, maximumUpwardForce);
        return force;
    }

    private bool AllowsYawRotation() => profile == null || profile.allowYawRotation;
    private float GetHorizontalConstraintSpring() => profile != null ? profile.horizontalConstraintSpring : defaultHorizontalConstraintSpring;
    private float GetHorizontalConstraintDampingRatio() => profile != null ? profile.horizontalConstraintDampingRatio : defaultHorizontalConstraintDampingRatio;
    private float GetMaxHorizontalConstraintForce() => profile != null ? profile.maxHorizontalConstraintForce : defaultMaxHorizontalConstraintForce;
    private float GetHorizontalConstraintDeadZone() => profile != null ? profile.horizontalConstraintDeadZone : defaultHorizontalConstraintDeadZone;
    private float GetHorizontalConstraintForceResponse() => profile != null ? profile.horizontalConstraintForceResponse : defaultHorizontalConstraintForceResponse;
    private float GetMaxHolderAnchorVelocity() => profile != null ? profile.maxHolderAnchorVelocity : defaultMaxHolderAnchorVelocity;
    private float GetVerticalSupportSpring() => profile != null ? profile.verticalSupportSpring : defaultVerticalSupportSpring;
    private float GetVerticalSupportDampingRatio() => profile != null ? profile.verticalSupportDampingRatio : defaultVerticalSupportDampingRatio;
    private float GetMaxVerticalSupportForce() => profile != null ? profile.maxVerticalSupportForce : defaultMaxVerticalSupportForce;

    private float GetCriticalDamper(float spring, float dampingRatio)
    {
        return 2f * Mathf.Sqrt(Mathf.Max(0.01f, body.mass * spring)) * dampingRatio;
    }

    private Vector3 GetAnchorVelocity(Transform anchor, float fixedDeltaTime)
    {
        Vector3 currentPosition = anchor.position;
        if (!anchorMotionStates.TryGetValue(anchor, out AnchorMotionState state) || !state.IsInitialized)
        {
            anchorMotionStates[anchor] = new AnchorMotionState { Position = currentPosition, IsInitialized = true };
            return Vector3.zero;
        }

        Vector3 velocity = (currentPosition - state.Position) / Mathf.Max(fixedDeltaTime, 0.0001f);
        velocity.y = 0f;
        anchorMotionStates[anchor] = new AnchorMotionState { Position = currentPosition, IsInitialized = true };
        return Vector3.ClampMagnitude(velocity, GetMaxHolderAnchorVelocity());
    }

    private Vector3 GetAnchorVelocity3D(Transform anchor, float fixedDeltaTime)
    {
        Vector3 currentPosition = anchor.position;
        if (!anchorMotionStates.TryGetValue(anchor, out AnchorMotionState state) || !state.IsInitialized)
        {
            anchorMotionStates[anchor] = new AnchorMotionState { Position = currentPosition, IsInitialized = true };
            return Vector3.zero;
        }

        Vector3 velocity = (currentPosition - state.Position) / Mathf.Max(fixedDeltaTime, 0.0001f);
        anchorMotionStates[anchor] = new AnchorMotionState { Position = currentPosition, IsInitialized = true };
        return Vector3.ClampMagnitude(velocity, GetMaxHolderAnchorVelocity());
    }

    private Vector3 SmoothConstraintForce(Transform anchor, Vector3 targetForce, float blend)
    {
        if (!smoothedConstraintForces.TryGetValue(anchor, out Vector3 currentForce))
        {
            currentForce = Vector3.zero;
        }

        Vector3 smoothedForce = Vector3.Lerp(currentForce, targetForce, Mathf.Clamp01(blend));
        smoothedConstraintForces[anchor] = smoothedForce;
        return smoothedForce;
    }

    private void PruneConstraintState(IReadOnlyList<SharedCarryPhysicsHolder> holders)
    {
        HashSet<Transform> activeAnchors = new HashSet<Transform>();
        for (int i = 0; i < holders.Count; i++)
        {
            if (holders[i].BodyAnchor != null)
            {
                activeAnchors.Add(holders[i].BodyAnchor);
            }
        }

        RemoveInactiveAnchorStates(anchorMotionStates, activeAnchors);
        RemoveInactiveAnchorStates(smoothedConstraintForces, activeAnchors);
    }

    private static void RemoveInactiveAnchorStates<TValue>(Dictionary<Transform, TValue> states, HashSet<Transform> activeAnchors)
    {
        List<Transform> anchorsToRemove = new List<Transform>();
        foreach (Transform anchor in states.Keys)
        {
            if (anchor == null || !activeAnchors.Contains(anchor))
            {
                anchorsToRemove.Add(anchor);
            }
        }

        foreach (Transform anchor in anchorsToRemove)
        {
            states.Remove(anchor);
        }
    }

    private void ResetConstraintState()
    {
        anchorMotionStates.Clear();
        smoothedConstraintForces.Clear();
    }

    private RigidbodyConstraints GetRotationConstraints()
    {
        RigidbodyConstraints constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        if (profile != null && !profile.allowYawRotation)
        {
            constraints |= RigidbodyConstraints.FreezeRotationY;
        }

        return constraints;
    }

    private void ConfigureRotationConstraint()
    {
        Quaternion yawRotation = ExtractYawRotation(body.rotation);
        sharedCarryTiltOffset = Quaternion.Inverse(yawRotation) * body.rotation;
        lockedSharedCarryYaw = yawRotation;
        if (ControlMode == SharedCarryControlMode.PhysicalPointGrip)
        {
            usesCustomRotationConstraint = false;
            body.constraints = RigidbodyConstraints.None;
            return;
        }
        usesCustomRotationConstraint = Quaternion.Angle(sharedCarryTiltOffset, Quaternion.identity) > 0.1f;
        body.constraints = usesCustomRotationConstraint ? RigidbodyConstraints.None : GetRotationConstraints();
    }

    private void ApplyCustomRotationConstraint()
    {
        if (ControlMode == SharedCarryControlMode.PhysicalPointGrip)
        {
            return;
        }

        if (!usesCustomRotationConstraint)
        {
            return;
        }

        Quaternion orientationWithoutTilt = body.rotation * Quaternion.Inverse(sharedCarryTiltOffset);
        Quaternion yawRotation = AllowsYawRotation()
            ? ExtractYawRotation(orientationWithoutTilt)
            : lockedSharedCarryYaw;
        body.rotation = yawRotation * sharedCarryTiltOffset;

        float yawVelocity = AllowsYawRotation() ? Vector3.Dot(body.angularVelocity, Vector3.up) : 0f;
        body.angularVelocity = Vector3.up * yawVelocity;
    }

    private void ApplyOptionalTiltLimit()
    {
        if (profile == null || !profile.limitTilt || profile.maximumTiltAngle <= 0f)
        {
            return;
        }

        Quaternion yaw = ExtractYawRotation(body.rotation);
        Quaternion targetRotation = yaw * sharedCarryTiltOffset;
        Quaternion errorRotation = targetRotation * Quaternion.Inverse(body.rotation);
        errorRotation.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
        {
            angle -= 360f;
        }

        float excessAngle = Mathf.Max(0f, Mathf.Abs(angle) - profile.maximumTiltAngle);
        Vector3 restoringAxis = Vector3.ProjectOnPlane(axis, Vector3.up);
        if (excessAngle <= 0f || restoringAxis.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 horizontalAngularVelocity = Vector3.ProjectOnPlane(body.angularVelocity, Vector3.up);
        Vector3 torque = restoringAxis.normalized * Mathf.Sign(angle) * excessAngle * profile.tiltRestoringTorque
            - horizontalAngularVelocity * profile.tiltDamping;
        body.AddTorque(torque, ForceMode.Force);
    }

    private void ApplyLongAxisRollStabilization()
    {
        if (profile == null || !profile.stabilizeLongAxisRoll || !TryGetWorldLongAxis(out Vector3 worldLongAxis))
        {
            return;
        }

        Vector3 localLongAxis = profile.rollLongAxisLocal.normalized;
        Vector3 localReferenceUp = Vector3.ProjectOnPlane(profile.rollReferenceUpLocal, localLongAxis).normalized;
        if (localReferenceUp.sqrMagnitude < 0.99f)
        {
            return;
        }

        Vector3 desiredReferenceUp = Vector3.ProjectOnPlane(Vector3.up, worldLongAxis);
        if (desiredReferenceUp.sqrMagnitude < 0.01f)
        {
            return;
        }

        desiredReferenceUp.Normalize();
        Vector3 currentReferenceUp = Vector3.ProjectOnPlane(body.rotation * localReferenceUp, worldLongAxis).normalized;
        float rollErrorRadians = Vector3.SignedAngle(currentReferenceUp, desiredReferenceUp, worldLongAxis) * Mathf.Deg2Rad;
        float rollAngularVelocity = Vector3.Dot(body.angularVelocity, worldLongAxis);
        float torqueMagnitude = rollErrorRadians * Mathf.Max(0f, profile.rollStabilizingTorque)
            - rollAngularVelocity * Mathf.Max(0f, profile.rollDamping);
        torqueMagnitude = Mathf.Clamp(
            torqueMagnitude,
            -Mathf.Max(0f, profile.maximumRollStabilizingTorque),
            Mathf.Max(0f, profile.maximumRollStabilizingTorque));
        body.AddTorque(worldLongAxis * torqueMagnitude, ForceMode.Force);
    }

    private bool TryGetWorldLongAxis(out Vector3 worldLongAxis)
    {
        worldLongAxis = Vector3.zero;
        if (profile == null || profile.rollLongAxisLocal.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        worldLongAxis = body.rotation * profile.rollLongAxisLocal.normalized;
        return worldLongAxis.sqrMagnitude >= 0.99f;
    }

    private static Quaternion ExtractYawRotation(Quaternion rotation)
    {
        Vector3 heading = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
        if (heading.sqrMagnitude < 0.0001f)
        {
            heading = Vector3.ProjectOnPlane(rotation * Vector3.right, Vector3.up);
        }

        return heading.sqrMagnitude >= 0.0001f
            ? Quaternion.LookRotation(heading.normalized, Vector3.up)
            : Quaternion.identity;
    }
}
