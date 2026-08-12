using System.Collections.Generic;
using StarterAssets;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-1000)]
public sealed class PlayerRopeConstraintController : NetworkBehaviour
{
    [SerializeField, Min(0f)] private float maximumConstraintCorrectionSpeed = 8f;

    private CharacterController characterController;
    private FirstPersonController firstPersonController;
    private PlayerInputNew playerInput;
    private PlayerHealth playerHealth;
    private DownedPlayerCarryable downedCarryable;
    private Vector3 frameStartPosition;
    private bool hasFrameStartPosition;
    private RopeToolController suspensionRope;
    private RopePlayerConstraintSettings suspensionSettings;
    private Vector3 suspensionAnchor;
    private Vector3 suspensionTarget;
    private float suspensionLength;
    private Vector3 previousAnchorPosition;
    private bool hasPreviousAnchorPosition;
    private Vector3 swingVelocity;
    private float positionCorrectionSpeed;
    private float groundedReleaseElapsed;
    private Vector3 wallNormal;
    private float lastWallContactTime = float.NegativeInfinity;
    private float nextWallJumpTime;

    public bool IsSuspended { get; private set; }
    public Vector3 CurrentSwingVelocity => swingVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        firstPersonController = GetComponent<FirstPersonController>();
        playerInput = GetComponent<PlayerInputNew>();
        playerHealth = GetComponent<PlayerHealth>();
        downedCarryable = GetComponent<DownedPlayerCarryable>();
    }

    private void OnDisable()
    {
        EndSuspension(false);
        wallNormal = Vector3.zero;
        lastWallContactTime = float.NegativeInfinity;
    }

    private void Update()
    {
        hasFrameStartPosition = CanMoveLocally() && characterController != null && characterController.enabled;
        if (hasFrameStartPosition)
        {
            frameStartPosition = transform.position;
        }

        RefreshSuspensionState();
    }

    private void LateUpdate()
    {
        if (!CanMoveLocally() || characterController == null || !characterController.enabled)
        {
            EndSuspension(false);
            return;
        }

        if (IsSuspended)
        {
            if (ShouldReleaseSuspensionToGround(Time.deltaTime))
            {
                CompleteGroundedLanding();
                return;
            }

            SimulateSuspension(Time.deltaTime);
            return;
        }

        Vector3 frameMovement = hasFrameStartPosition ? transform.position - frameStartPosition : Vector3.zero;
        Vector3 hardLimitCorrection = ResolveHardLimitCorrection(frameMovement);
        if (hardLimitCorrection.sqrMagnitude > 0.000001f)
        {
            characterController.Move(hardLimitCorrection);
        }

        Vector3 correctionVelocity = Vector3.zero;
        IReadOnlyCollection<RopeToolController> ropes = RopeToolController.ActiveRopes;
        foreach (RopeToolController rope in ropes)
        {
            if (rope != null && rope.TryGetPlayerConstraintVelocity(NetworkObject, out Vector3 velocity))
            {
                correctionVelocity += velocity;
            }
        }

        correctionVelocity = Vector3.ClampMagnitude(correctionVelocity, maximumConstraintCorrectionSpeed);
        if (correctionVelocity.sqrMagnitude > 0.0001f)
        {
            characterController.Move(correctionVelocity * Time.deltaTime);
        }
    }

    public bool TryHandleJumpInput()
    {
        if (!IsSuspended)
        {
            return false;
        }

        if (playerHealth == null || !playerHealth.IsDowned)
        {
            TryApplyWallJump();
        }

        return true;
    }

    private void RefreshSuspensionState()
    {
        if (!CanMoveLocally() || characterController == null || !characterController.enabled
            || downedCarryable != null && downedCarryable.IsCarried)
        {
            EndSuspension(false);
            return;
        }

        RopeToolController candidate = null;
        Vector3 anchor = Vector3.zero;
        Vector3 target = Vector3.zero;
        float length = 0f;
        RopePlayerConstraintSettings settings = default;
        foreach (RopeToolController rope in RopeToolController.ActiveRopes)
        {
            if (rope != null && rope.TryGetPlayerSuspensionData(NetworkObject, out anchor, out target,
                    out length, out settings))
            {
                candidate = rope;
                break;
            }
        }

        bool grounded = firstPersonController != null && firstPersonController.Grounded;
        if (candidate == null)
        {
            EndSuspension(IsSuspended && !grounded && (downedCarryable == null || !downedCarryable.IsCarried));
            return;
        }

        if (grounded && !IsSuspended)
        {
            EndSuspension(false);
            return;
        }

        if (!IsSuspended || suspensionRope != candidate)
        {
            BeginSuspension(candidate, anchor, target, settings);
        }

        suspensionRope = candidate;
        suspensionAnchor = anchor;
        suspensionTarget = target;
        suspensionLength = length;
        suspensionSettings = settings;
        IsSuspended = true;
    }

    private void BeginSuspension(
        RopeToolController rope,
        Vector3 anchor,
        Vector3 targetPoint,
        RopePlayerConstraintSettings settings)
    {
        if (IsSuspended && suspensionRope != rope)
        {
            EndSuspension(true);
        }

        swingVelocity = firstPersonController != null
            ? firstPersonController.GetRopeEntryVelocity()
            : Vector3.zero;

        Vector3 ropeVector = targetPoint - anchor;
        if (ropeVector.sqrMagnitude > 0.0001f)
        {
            Vector3 radialDirection = ropeVector.normalized;
            float outwardSpeed = Vector3.Dot(swingVelocity, radialDirection);
            if (outwardSpeed > 0f)
            {
                swingVelocity -= radialDirection * outwardSpeed;
            }
        }

        swingVelocity = Vector3.ClampMagnitude(swingVelocity, settings.MaximumSwingSpeed);
        positionCorrectionSpeed = 0f;
        groundedReleaseElapsed = 0f;
        previousAnchorPosition = anchor;
        hasPreviousAnchorPosition = true;
        suspensionRope = rope;
        IsSuspended = true;
    }

    private bool ShouldReleaseSuspensionToGround(float deltaTime)
    {
        if (firstPersonController == null || !firstPersonController.Grounded)
        {
            groundedReleaseElapsed = 0f;
            return false;
        }

        RopePlayerConstraintSettings settings = suspensionSettings;
        bool requiresUpwardCorrection = false;
        if (suspensionRope != null
            && suspensionRope.TryGetPlayerSuspensionData(NetworkObject, out Vector3 anchor,
                out Vector3 targetPoint, out float ropeLength, out settings))
        {
            Vector3 ropeVector = targetPoint - anchor;
            float distance = ropeVector.magnitude;
            if (distance > 0.0001f)
            {
                float overflow = distance - ropeLength - settings.PositionDeadZone;
                Vector3 towardAnchor = -ropeVector / distance;
                requiresUpwardCorrection = overflow > 0f
                    && towardAnchor.y > settings.UpwardPullThreshold;
            }
        }

        if (requiresUpwardCorrection)
        {
            groundedReleaseElapsed = 0f;
            return false;
        }

        groundedReleaseElapsed += Mathf.Max(0f, deltaTime);
        return groundedReleaseElapsed >= settings.GroundedReleaseDelay;
    }

    private void CompleteGroundedLanding()
    {
        if (firstPersonController != null)
        {
            Vector3 landingVelocity = swingVelocity;
            landingVelocity.y = Mathf.Max(0f, landingVelocity.y);
            firstPersonController.ApplyRopeReleaseVelocity(landingVelocity);
        }

        EndSuspension(false);
    }

    private void EndSuspension(bool preserveMomentum)
    {
        if (!IsSuspended)
        {
            suspensionRope = null;
            hasPreviousAnchorPosition = false;
            positionCorrectionSpeed = 0f;
            groundedReleaseElapsed = 0f;
            return;
        }

        if (preserveMomentum && firstPersonController != null)
        {
            firstPersonController.ApplyRopeReleaseVelocity(swingVelocity);
        }

        IsSuspended = false;
        suspensionRope = null;
        hasPreviousAnchorPosition = false;
        positionCorrectionSpeed = 0f;
        groundedReleaseElapsed = 0f;
        if (!preserveMomentum)
        {
            swingVelocity = Vector3.zero;
        }
    }

    private void SimulateSuspension(float deltaTime)
    {
        if (deltaTime <= 0f || suspensionRope == null
            || !suspensionRope.TryGetPlayerSuspensionData(NetworkObject, out Vector3 anchor,
                out Vector3 targetPoint, out float ropeLength, out RopePlayerConstraintSettings settings))
        {
            EndSuspension(true);
            return;
        }

        suspensionAnchor = anchor;
        suspensionTarget = targetPoint;
        suspensionLength = ropeLength;
        suspensionSettings = settings;

        Vector3 anchorDelta = hasPreviousAnchorPosition ? anchor - previousAnchorPosition : Vector3.zero;
        previousAnchorPosition = anchor;
        hasPreviousAnchorPosition = true;
        anchorDelta = Vector3.ClampMagnitude(
            anchorDelta,
            settings.MaximumAnchorTransferSpeed * deltaTime);

        Vector3 ropeVector = targetPoint - anchor;
        float distance = ropeVector.magnitude;
        Vector3 radialDirection = distance > 0.0001f ? ropeVector / distance : Vector3.down;

        swingVelocity += Physics.gravity * settings.SwingGravityMultiplier * deltaTime;
        if (playerHealth == null || !playerHealth.IsDowned)
        {
            Vector2 moveInput = Vector2.ClampMagnitude(playerInput != null
                ? playerInput.GetMoveVectorValue()
                : Vector2.zero, 1f);
            Vector3 desiredDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
            Vector3 tangentialInput = Vector3.ProjectOnPlane(desiredDirection, radialDirection);
            if (tangentialInput.sqrMagnitude > 0.0001f)
            {
                swingVelocity += tangentialInput.normalized
                    * settings.SwingInputAcceleration * moveInput.magnitude * deltaTime;
            }
        }

        swingVelocity *= Mathf.Exp(-settings.SwingDamping * deltaTime);
        swingVelocity = Vector3.ClampMagnitude(swingVelocity, settings.MaximumSwingSpeed);

        bool ropeIsTaut = distance >= ropeLength - settings.SwingTautThreshold;
        if (ropeIsTaut)
        {
            RemoveOutwardRopeVelocity(ref swingVelocity, radialDirection);
        }

        bool hasWallContact = HasRecentWallContact(settings.WallContactGraceDuration);
        if (hasWallContact)
        {
            RemoveVelocityIntoWall(ref swingVelocity);
        }

        Vector3 physicalMovement = swingVelocity * deltaTime;
        if (anchorDelta.sqrMagnitude > 0.000001f)
        {
            physicalMovement += Vector3.ProjectOnPlane(anchorDelta, radialDirection);
        }

        if (hasWallContact && Vector3.Dot(physicalMovement, wallNormal) < 0f)
        {
            physicalMovement = Vector3.ProjectOnPlane(physicalMovement, wallNormal);
        }

        Vector3 positionBeforePhysicalMove = transform.position;
        characterController.Move(physicalMovement);
        Vector3 actualPhysicalMovement = transform.position - positionBeforePhysicalMove;
        swingVelocity = actualPhysicalMovement / Mathf.Max(deltaTime, 0.0001f);

        Vector3 ropeAfterPhysicalMove = targetPoint + actualPhysicalMovement - anchor;
        float distanceAfterPhysicalMove = ropeAfterPhysicalMove.magnitude;
        if (distanceAfterPhysicalMove >= ropeLength - settings.SwingTautThreshold
            && distanceAfterPhysicalMove > 0.0001f)
        {
            RemoveOutwardRopeVelocity(
                ref swingVelocity,
                ropeAfterPhysicalMove / distanceAfterPhysicalMove);
        }

        if (HasRecentWallContact(settings.WallContactGraceDuration))
        {
            RemoveVelocityIntoWall(ref swingVelocity);
        }
        swingVelocity = Vector3.ClampMagnitude(swingVelocity, settings.MaximumSwingSpeed);

        Vector3 correctedTargetPoint = targetPoint + actualPhysicalMovement;
        Vector3 correctedRope = correctedTargetPoint - anchor;
        float correctedDistance = correctedRope.magnitude;
        float overflow = correctedDistance - ropeLength - settings.PositionDeadZone;
        if (overflow > 0f && correctedDistance > 0.0001f)
        {
            positionCorrectionSpeed = Mathf.MoveTowards(
                positionCorrectionSpeed,
                settings.PositionCorrectionSpeed,
                settings.PositionCorrectionAcceleration * deltaTime);

            Vector3 correction = -correctedRope / correctedDistance
                * Mathf.Min(overflow, positionCorrectionSpeed * deltaTime);
            bool correctionTouchesWall = HasRecentWallContact(settings.WallContactGraceDuration);
            if (correctionTouchesWall && Vector3.Dot(correction, wallNormal) < 0f)
            {
                correction = Vector3.ProjectOnPlane(correction, wallNormal);
            }

            Vector3 positionBeforeCorrection = transform.position;
            characterController.Move(correction);
            Vector3 actualCorrection = transform.position - positionBeforeCorrection;
            if (correction.sqrMagnitude > 0.000001f
                && actualCorrection.sqrMagnitude < correction.sqrMagnitude * 0.0625f)
            {
                positionCorrectionSpeed = 0f;
            }
        }
        else
        {
            positionCorrectionSpeed = Mathf.MoveTowards(
                positionCorrectionSpeed,
                0f,
                settings.PositionCorrectionAcceleration * deltaTime);
        }
    }

    private void TryApplyWallJump()
    {
        if (Time.time < nextWallJumpTime || !HasRecentWallContact(suspensionSettings.WallContactGraceDuration))
        {
            return;
        }

        RemoveVelocityIntoWall(ref swingVelocity);
        float outwardSpeed = Vector3.Dot(swingVelocity, wallNormal);
        if (outwardSpeed < suspensionSettings.WallJumpOutwardSpeed)
        {
            swingVelocity += wallNormal * (suspensionSettings.WallJumpOutwardSpeed - outwardSpeed);
        }
        swingVelocity.y = Mathf.Max(swingVelocity.y, suspensionSettings.WallJumpUpwardSpeed);
        nextWallJumpTime = Time.time + suspensionSettings.WallJumpCooldown;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit == null || Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) > 0.65f)
        {
            return;
        }

        wallNormal = hit.normal.normalized;
        lastWallContactTime = Time.time;
    }

    private bool HasRecentWallContact(float graceDuration)
    {
        return wallNormal.sqrMagnitude > 0.5f && Time.time - lastWallContactTime <= graceDuration;
    }

    private void RemoveVelocityIntoWall(ref Vector3 velocity)
    {
        float intoWall = Vector3.Dot(velocity, wallNormal);
        if (intoWall < 0f)
        {
            velocity -= wallNormal * intoWall;
        }
    }

    private static void RemoveOutwardRopeVelocity(ref Vector3 velocity, Vector3 radialDirection)
    {
        float outwardSpeed = Vector3.Dot(velocity, radialDirection);
        if (outwardSpeed > 0f)
        {
            velocity -= radialDirection * outwardSpeed;
        }
    }

    private Vector3 ResolveHardLimitCorrection(Vector3 frameMovement)
    {
        Vector3 movementRollback = Vector3.zero;
        Vector3 overflowCorrection = Vector3.zero;
        Vector3 remainingFrameMovement = frameMovement;
        IReadOnlyCollection<RopeToolController> ropes = RopeToolController.ActiveRopes;
        foreach (RopeToolController rope in ropes)
        {
            if (rope == null || !rope.TryGetPlayerMovementLimit(NetworkObject, out Vector3 towardOther,
                    out float overflow, out float correctionShare))
            {
                continue;
            }

            Vector3 awayFromOther = -towardOther;
            float outwardMovement = Mathf.Max(0f, Vector3.Dot(remainingFrameMovement, awayFromOther));
            if (outwardMovement > 0f)
            {
                Vector3 rollback = towardOther * outwardMovement;
                movementRollback += rollback;
                remainingFrameMovement += rollback;
            }

            float residualOverflow = Mathf.Max(0f, overflow - outwardMovement);
            if (residualOverflow > 0f)
            {
                overflowCorrection += towardOther * residualOverflow * Mathf.Clamp01(correctionShare);
            }
        }

        float maximumCorrection = maximumConstraintCorrectionSpeed * Time.deltaTime;
        return movementRollback + Vector3.ClampMagnitude(overflowCorrection, maximumCorrection);
    }

    private bool CanMoveLocally()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || IsOwner;
    }
}
