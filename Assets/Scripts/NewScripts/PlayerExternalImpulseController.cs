using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerInteractionNew))]
public class PlayerExternalImpulseController : NetworkBehaviour, IExternalImpulseReceiver
{
    private PlayerInteractionNew playerInteraction;
    private Vector3 externalVelocity;
    private float horizontalDeceleration;
    private float gravityMultiplier = 1f;
    private float activeTimeRemaining;
    private float movementControlMultiplier = 1f;
    private float maximumHorizontalSpeed;
    private float maximumVerticalSpeed;

    public Vector3 CurrentExternalVelocity => externalVelocity;
    public float MovementControlMultiplier => IsImpulseActive ? movementControlMultiplier : 1f;
    public bool IsImpulseActive => activeTimeRemaining > 0f || externalVelocity.sqrMagnitude > 0.01f;

    private void Awake()
    {
        playerInteraction = GetComponent<PlayerInteractionNew>();
    }

    public bool TryApplyExternalImpulse(ExternalImpulseData impulse, NetworkObject source)
    {
        if (!impulse.IsValid)
        {
            return false;
        }

        if (IsNetworkSessionActive())
        {
            if (!IsServer)
            {
                return false;
            }

            ApplyExternalImpulseClientRpc(impulse, CreateOwnerRpcParams());
            return true;
        }

        ApplyImpulseLocally(impulse);
        return true;
    }

    internal bool ApplyServerAuthorizedImpulse(ExternalImpulseData impulse)
    {
        if (!impulse.IsValid || (IsNetworkSessionActive() && !IsOwner))
        {
            return false;
        }

        ApplyImpulseLocally(impulse);
        return true;
    }

    public Vector3 TickImpulse(float deltaTime, bool grounded)
    {
        if (!IsImpulseActive)
        {
            return Vector3.zero;
        }

        activeTimeRemaining = Mathf.Max(0f, activeTimeRemaining - deltaTime);

        Vector3 horizontal = Vector3.ProjectOnPlane(externalVelocity, Vector3.up);
        horizontal = Vector3.MoveTowards(
            horizontal,
            Vector3.zero,
            Mathf.Max(0f, horizontalDeceleration) * deltaTime);

        float vertical = externalVelocity.y;
        if (grounded && vertical < 0f)
        {
            vertical = 0f;
        }
        else
        {
            vertical += Physics.gravity.y * Mathf.Max(0f, gravityMultiplier) * deltaTime;
        }

        externalVelocity = horizontal + Vector3.up * vertical;
        if (activeTimeRemaining <= 0f && horizontal.sqrMagnitude <= 0.01f && grounded)
        {
            ClearImpulse();
        }

        return externalVelocity;
    }

    public void ReportCollision(CollisionFlags collisionFlags)
    {
        if ((collisionFlags & CollisionFlags.Above) != 0 && externalVelocity.y > 0f)
        {
            externalVelocity.y = 0f;
        }

        if ((collisionFlags & CollisionFlags.Below) != 0 && externalVelocity.y < 0f)
        {
            externalVelocity.y = 0f;
        }
    }

    public override void OnNetworkDespawn()
    {
        ClearImpulse();
    }

    private void ApplyImpulseLocally(ExternalImpulseData impulse)
    {
        if (impulse.ForceDropHeldObject)
        {
            playerInteraction?.DropHeldObjectForStateChange();
        }

        externalVelocity += impulse.InitialVelocity;
        maximumHorizontalSpeed = Mathf.Max(maximumHorizontalSpeed, impulse.MaximumHorizontalSpeed);
        maximumVerticalSpeed = Mathf.Max(maximumVerticalSpeed, impulse.MaximumVerticalSpeed);

        Vector3 horizontal = Vector3.ProjectOnPlane(externalVelocity, Vector3.up);
        float horizontalLimit = Mathf.Max(0f, maximumHorizontalSpeed);
        if (horizontalLimit > 0f)
        {
            horizontal = Vector3.ClampMagnitude(horizontal, horizontalLimit);
        }

        float verticalLimit = Mathf.Max(0f, maximumVerticalSpeed);
        float vertical = verticalLimit > 0f
            ? Mathf.Clamp(externalVelocity.y, -verticalLimit, verticalLimit)
            : externalVelocity.y;
        externalVelocity = horizontal + Vector3.up * vertical;

        horizontalDeceleration = Mathf.Max(horizontalDeceleration, impulse.HorizontalDeceleration);
        gravityMultiplier = Mathf.Max(gravityMultiplier, impulse.GravityMultiplier);
        movementControlMultiplier = IsImpulseActive
            ? Mathf.Min(movementControlMultiplier, impulse.MovementControlMultiplier)
            : Mathf.Clamp01(impulse.MovementControlMultiplier);
        activeTimeRemaining = Mathf.Max(activeTimeRemaining, impulse.MaximumDuration);
    }

    private void ClearImpulse()
    {
        externalVelocity = Vector3.zero;
        horizontalDeceleration = 0f;
        gravityMultiplier = 1f;
        activeTimeRemaining = 0f;
        movementControlMultiplier = 1f;
        maximumHorizontalSpeed = 0f;
        maximumVerticalSpeed = 0f;
    }

    [ClientRpc]
    private void ApplyExternalImpulseClientRpc(ExternalImpulseData impulse, ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner)
        {
            ApplyImpulseLocally(impulse);
        }
    }

    private ClientRpcParams CreateOwnerRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }
}
