using System.Collections.Generic;
using UnityEngine;

public struct SharedCarryPhysicsHolder
{
    public Transform BodyAnchor;
    public Vector3 AttachLocalPoint;
    public Vector3 DesiredInput;
}

[RequireComponent(typeof(Rigidbody))]
public class SharedCarryPhysicsBody : MonoBehaviour
{
    [SerializeField] private CarryPhysicsProfileSO profile;
    [SerializeField] private float defaultMass = 20f;
    [SerializeField] private float defaultLinearDrag = 1.5f;
    [SerializeField] private float defaultAngularDrag = 4f;
    [SerializeField] private float defaultGripSpring = 900f;
    [SerializeField] private float defaultGripDamper = 90f;
    [SerializeField] private float defaultMaxGripForce = 1800f;
    [SerializeField] private float defaultMaxGripDistance = 1.25f;
    [SerializeField] private float defaultMaxVelocity = 6f;
    [SerializeField] private float defaultMovementForce = 450f;
    [SerializeField] private float defaultMovementDamper = 65f;

    private Rigidbody body;
    private bool sharedCarryActive;
    private float normalMass;
    private float normalLinearDamping;
    private float normalAngularDamping;
    private bool normalPhysicsCaptured;

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
        body.angularVelocity = new Vector3(0f, Mathf.Clamp(body.angularVelocity.y, -GetMaxAngularVelocity(), GetMaxAngularVelocity()), 0f);
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
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
    }

    public void Simulate(IReadOnlyList<SharedCarryPhysicsHolder> holders, Vector3 combinedInput, float fixedDeltaTime)
    {
        if (!sharedCarryActive || body == null || !body.gameObject.activeInHierarchy || holders == null || holders.Count == 0)
        {
            return;
        }

        float maxForce = GetMaxGripForce();
        float maxDistance = GetMaxGripDistance();
        float spring = GetGripSpring();
        float damper = GetGripDamper();

        for (int i = 0; i < holders.Count; i++)
        {
            SharedCarryPhysicsHolder holder = holders[i];
            if (holder.BodyAnchor == null)
            {
                continue;
            }

            Vector3 attachPoint = transform.TransformPoint(holder.AttachLocalPoint);
            Vector3 error = holder.BodyAnchor.position - attachPoint;
            if (error.magnitude > maxDistance)
            {
                error = error.normalized * maxDistance;
            }

            Vector3 desiredVelocity = holder.DesiredInput * GetMovementSpeed();
            Vector3 force = error * spring + (desiredVelocity - body.linearVelocity) * damper;
            force = Vector3.ClampMagnitude(force, maxForce);
            body.AddForceAtPosition(force, attachPoint, ForceMode.Force);
        }

        Vector3 movementForce = Vector3.ClampMagnitude(combinedInput, 1f) * GetMovementForce();
        body.AddForce(movementForce - body.linearVelocity * GetMovementDamper(), ForceMode.Force);
        body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, GetMaxVelocity());

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

    private float GetGripSpring() => profile != null ? profile.gripSpring : defaultGripSpring;
    private float GetGripDamper() => profile != null ? profile.gripDamper : defaultGripDamper;
    private float GetMaxGripForce() => profile != null ? profile.maxGripForce : defaultMaxGripForce;
    private float GetMaxGripDistance() => profile != null ? profile.maxGripDistance : defaultMaxGripDistance;
    private float GetMaxVelocity() => profile != null ? profile.maxVelocity : defaultMaxVelocity;
    private float GetMaxAngularVelocity() => profile != null ? profile.maxAngularVelocity : 3f;
    private float GetMovementForce() => profile != null ? profile.movementForce : defaultMovementForce;
    private float GetMovementDamper() => profile != null ? profile.movementDamper : defaultMovementDamper;
    private float GetMovementSpeed() => profile != null ? profile.maxVelocity : defaultMaxVelocity;
}
