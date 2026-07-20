using System.Collections.Generic;
using UnityEngine;

public struct SharedCarryPhysicsHolder
{
    public Transform BodyAnchor;
    public Vector3 AttachLocalPoint;
    public float DesiredYawInput;
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
    private readonly Dictionary<Transform, AnchorMotionState> anchorMotionStates = new Dictionary<Transform, AnchorMotionState>();
    private readonly Dictionary<Transform, Vector3> smoothedConstraintForces = new Dictionary<Transform, Vector3>();

    private struct AnchorMotionState
    {
        public Vector3 Position;
        public bool IsInitialized;
    }

    public Rigidbody Body => body;
    public CarryPhysicsProfileSO Profile => profile;

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
        body.constraints = GetRotationConstraints();
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
        RestoreNormalPhysics();
        ResetConstraintState();
    }

    public void Simulate(IReadOnlyList<SharedCarryPhysicsHolder> holders, Vector3 combinedInput, int carrierNormalizationCount, float fixedDeltaTime)
    {
        if (!sharedCarryActive || body == null || !body.gameObject.activeInHierarchy || holders == null || holders.Count == 0)
        {
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

        if (AllowsYawRotation())
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

        horizontalVelocity = Vector3.ClampMagnitude(new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z), GetMaxVelocity());
        body.linearVelocity = new Vector3(horizontalVelocity.x, body.linearVelocity.y, horizontalVelocity.z);

        if (GetMaxAngularVelocity() > 0f)
        {
            body.angularVelocity = new Vector3(0f, Mathf.Clamp(body.angularVelocity.y, -GetMaxAngularVelocity(), GetMaxAngularVelocity()), 0f);
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
}
