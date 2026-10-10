using Cinemachine;
using StarterAssets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Local-only single-carry placement preview and continuous local X/Z rotation.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1100)]
public sealed class PlayerSingleCarryPlacementController : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float placementDistance = 2f;
    [SerializeField, Min(0f)] private float rotationDegreesPerSecond = 90f;
    private PlayerInputNew input;
    private PlayerInteractionNew interaction;
    private PlayerHealth health;
    private PlayerConcreteTrapController concreteTrap;
    private PlayerWheelbarrowController wheelbarrow;
    private PlayerRopeConstraintController ropeConstraint;
    private PlayerActionController actionController;
    private FirstPersonController firstPersonController;
    private PlayerSingleCarryPlacementNetworkPreview networkPreview;
    private SingleCarryPlacementGhost ghost;
    private SingleCarryPlacementGeometrySolver geometrySolver;
    private SingleCarryPlacementValidator validator;
    private SingleCarryPlacementHeldVisual heldVisual;
    private GameObject heldObject;
    private ISingleCarryPlaceable heldPlaceable;
    private Quaternion localRotation = Quaternion.identity;
    private SingleCarryPlacementPose lastRenderedPose;
    private Ray lastRenderedAimRay;
    private bool hasLastRenderedPose;
    private bool requestPending;
    private ulong pendingRequestId;
    private ulong lastRequestId;
    private bool enteredInNetworkSession;
    private bool heldSourceOwnershipTracked;
    private string placementResultMessage;
    private int worldRenderLayer = -1;

    public bool IsActive { get; private set; }
    public bool IsRequestPending => requestPending;
    public bool CanConfirmPlacement => !requestPending && (!IsNetworkSessionListening() || IsNetworkPlacementAvailable());
    public string PlacementConfirmationHint => requestPending ? "Waiting for server" : CanConfirmPlacement
        ? "E Place" : "E Unavailable in multiplayer";
    public SingleCarryPlacementGeometryStatus LastGeometryStatus { get; private set; }
    public SingleCarryPlacementValidationResult LastValidation { get; private set; }
    public bool IsCurrentPlacementValid => IsActive && LastValidation.IsValid;
    public float PlacementDistance => IsNetworkSessionListening() && networkPreview != null
        ? networkPreview.PlacementDistance : placementDistance;

    public string PlacementStatusLabel => requestPending ? "Waiting for server"
        : !string.IsNullOrEmpty(placementResultMessage) ? placementResultMessage
        : LastValidation.Status switch
        {
            SingleCarryPlacementValidationStatus.Valid => "Placement valid",
            SingleCarryPlacementValidationStatus.Occupied => "Placement blocked",
            SingleCarryPlacementValidationStatus.QueryBufferSaturated => "Too many nearby objects",
            _ => "Unsupported shape"
        };

    private void Awake()
    {
        Component[] attachedComponents = GetComponents<Component>();
        foreach (Component attached in attachedComponents)
        {
            if (attached is PlayerInputNew playerInput) input = playerInput;
            else if (attached is PlayerInteractionNew playerInteraction) interaction = playerInteraction;
            else if (attached is PlayerHealth playerHealth) health = playerHealth;
            else if (attached is PlayerConcreteTrapController trap) concreteTrap = trap;
            else if (attached is PlayerWheelbarrowController cart) wheelbarrow = cart;
            else if (attached is PlayerRopeConstraintController rope) ropeConstraint = rope;
            else if (attached is PlayerActionController action) actionController = action;
            else if (attached is FirstPersonController firstPerson) firstPersonController = firstPerson;
            else if (attached is PlayerSingleCarryPlacementNetworkPreview preview) networkPreview = preview;
        }
    }

    private void OnEnable()
    {
        if (input != null)
        {
            input.OnToggleSingleCarryPlacement += TogglePlacement;
            input.OnCancelSingleCarryPlacement += CancelPlacement;
            input.OnConfirmSingleCarryPlacement += ConfirmPlacement;
            input.OnGameplayMenuRequested += ForceExitPlacement;
            input.OnForcedSingleCarryPlacementExit += ForceExitPlacement;
        }
        if (networkPreview != null)
        {
            networkPreview.PlacementRequestCompleted += PlacementRequestCompleted;
            networkPreview.PlacementChannelLost += PlacementChannelLost;
        }
        if (interaction != null) interaction.OnHeldObjectChanged += HeldObjectChanged;
        if (health != null) health.OnDownedStateChanged += Health_DownedStateChanged;
        CinemachineCore.CameraUpdatedEvent.AddListener(CinemachineCameraUpdated);
        SceneManager.activeSceneChanged += ActiveSceneChanged;
    }

    private void OnDisable()
    {
        if (input != null)
        {
            input.OnToggleSingleCarryPlacement -= TogglePlacement;
            input.OnCancelSingleCarryPlacement -= CancelPlacement;
            input.OnConfirmSingleCarryPlacement -= ConfirmPlacement;
            input.OnGameplayMenuRequested -= ForceExitPlacement;
            input.OnForcedSingleCarryPlacementExit -= ForceExitPlacement;
        }
        if (networkPreview != null)
        {
            networkPreview.PlacementRequestCompleted -= PlacementRequestCompleted;
            networkPreview.PlacementChannelLost -= PlacementChannelLost;
        }
        if (interaction != null) interaction.OnHeldObjectChanged -= HeldObjectChanged;
        if (health != null) health.OnDownedStateChanged -= Health_DownedStateChanged;
        CinemachineCore.CameraUpdatedEvent.RemoveListener(CinemachineCameraUpdated);
        SceneManager.activeSceneChanged -= ActiveSceneChanged;
        ForceExitPlacement();
    }

    private void LateUpdate()
    {
        if (!IsActive) return;
        // Lifecycle safety is evaluated before the pending guard: a server reply may never
        // arrive after despawn, ownership loss, menu, or network shutdown.
        if (!CanContinue())
        {
            ForceExitPlacement();
            return;
        }

        if (requestPending) return;

        ApplyRotationInput(Time.deltaTime);
        RefreshPreview();
    }

    private void CinemachineCameraUpdated(CinemachineBrain brain)
    {
        if (!IsActive || requestPending || brain == null || !CanContinue()
            || !interaction.TryGetAimRay(out _, out Camera aimCamera, out _) || aimCamera == null
            || brain.OutputCamera != aimCamera) return;
        RefreshPreview();
    }

    private void TogglePlacement(object sender, System.EventArgs args)
    {
        if (requestPending) return;
        if (IsActive) { ExitPlacement(); return; }
        if (!CanEnter()) return;

        heldObject = interaction.GetPickedUpGameObject();
        heldPlaceable = ResolvePlaceable(heldObject);
        ghost = new SingleCarryPlacementGhost(heldObject);
        geometrySolver = new SingleCarryPlacementGeometrySolver(heldObject);
        validator = new SingleCarryPlacementValidator(heldObject);
        heldVisual = new SingleCarryPlacementHeldVisual(heldObject);
        localRotation = Quaternion.identity;
        worldRenderLayer = -1;
        LastGeometryStatus = SingleCarryPlacementGeometryStatus.NoObstacle;
        LastValidation = new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);
        hasLastRenderedPose = false;
        requestPending = false;
        pendingRequestId = 0;
        placementResultMessage = null;
        enteredInNetworkSession = IsNetworkSessionListening();
        heldSourceOwnershipTracked = false;
        IsActive = true;
        interaction.CancelBucketActionHoldForPlacement();
        if (actionController != null) actionController.CancelActionForStateChange();
        if (firstPersonController != null) firstPersonController.StopHorizontalMovementForPlacement();
        input.SetSingleCarryPlacementActive(true);
        RefreshPreview();
    }

    private void ApplyRotationInput(float deltaTime)
    {
        if (requestPending) return;
        Vector2 direction = input.GetSingleCarryPlacementRotationInput();
        if (direction.sqrMagnitude < 0.000001f || rotationDegreesPerSecond <= 0f) return;
        float degrees = rotationDegreesPerSecond * deltaTime;
        Quaternion localX = Quaternion.AngleAxis(direction.y * degrees, Vector3.right);
        Quaternion localZ = Quaternion.AngleAxis(-direction.x * degrees, Vector3.forward);
        localRotation = (localRotation * localX * localZ).normalized;
    }

    private void RefreshPreview()
    {
        if (ghost == null || geometrySolver == null || validator == null)
        {
            hasLastRenderedPose = false;
            return;
        }
        ghost.SyncSourceAppearance();
        if (!interaction.TryGetAimRay(out Ray aimRay, out Camera aimCamera, out Quaternion baseYaw))
        {
            hasLastRenderedPose = false;
            LastValidation = new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);
            ghost.SetPlacementValid(false);
            if (ghost.Root != null)
                PublishPreviewState(ghost.Root.transform.position, ghost.Root.transform.rotation, false);
            return;
        }
        if (aimCamera != null && worldRenderLayer < 0)
        {
            worldRenderLayer = ResolveWorldRenderLayer(aimCamera.cullingMask, heldObject.layer);
            ghost.SetWorldRenderLayer(worldRenderLayer);
        }
        SingleCarryPlacementPose pose = geometrySolver.Calculate(aimRay, PlacementDistance, baseYaw, localRotation, transform);
        bool renderedPoseChanged = !hasLastRenderedPose
            || Vector3.Distance(lastRenderedPose.Position, pose.Position) > 0.001f
            || Quaternion.Angle(lastRenderedPose.Rotation, pose.Rotation) > 0.05f;
        if (renderedPoseChanged) placementResultMessage = null;
        LastGeometryStatus = pose.Status;
        ghost.SetPose(pose.Position, pose.Rotation);
        lastRenderedPose = pose;
        lastRenderedAimRay = aimRay;
        hasLastRenderedPose = true;
        LastValidation = validator.Validate(pose);
        ghost.SetPlacementValid(LastValidation.IsValid);
        PublishPreviewState(pose.Position, pose.Rotation, LastValidation.IsValid);
    }

    private void ConfirmPlacement(object sender, System.EventArgs args)
    {
        if (!IsActive || requestPending || !CanConfirmPlacement || !CanContinue() || !hasLastRenderedPose
            || validator == null || ghost == null || ghost.Root == null)
            return;

        // Physics transforms may have changed since the last green preview. Sync only at
        // confirmation so this query sees current world occupancy without per-frame cost.
        Physics.SyncTransforms();
        LastValidation = validator.Validate(lastRenderedPose);
        ghost.SetPlacementValid(LastValidation.IsValid);
        if (!LastValidation.IsValid) return;

        if (IsNetworkSessionListening())
        {
            if (!TryGetSourceNetworkObject(out NetworkObject source))
            {
                placementResultMessage = "Held item unavailable";
                return;
            }
            lastRequestId = lastRequestId == ulong.MaxValue ? 1 : lastRequestId + 1;
            pendingRequestId = lastRequestId;
            requestPending = true;
            placementResultMessage = null;
            PublishPreviewState(lastRenderedPose.Position, lastRenderedPose.Rotation, LastValidation.IsValid);
            networkPreview.RequestNetworkPlacement(source, pendingRequestId, lastRenderedPose.Position,
                lastRenderedPose.Rotation, lastRenderedAimRay);
            return;
        }

        if (!interaction.TryReleaseSingleCarryPlacement(heldObject, heldPlaceable,
                lastRenderedPose.Position, lastRenderedPose.Rotation))
        {
            LastValidation = new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);
            ghost.SetPlacementValid(false);
            return;
        }

        if (IsActive) ExitPlacement();
    }

    private bool IsNetworkSessionListening()
    {
        NetworkManager manager = networkPreview != null ? networkPreview.NetworkManager : null;
        if (manager == null && input != null) manager = input.NetworkManager;
        if (manager == null) manager = NetworkManager.Singleton;
        return manager != null && manager.IsListening;
    }

    private static int ResolveWorldRenderLayer(int cameraMask, int sourceLayer)
    {
        if (IsVisibleLayer(cameraMask, sourceLayer)) return sourceLayer;
        int firstPersonViewLayer = LayerMask.NameToLayer("FirstPersonView");
        if (IsVisibleLayer(cameraMask, 0)) return 0;
        for (int layer = 0; layer < 32; layer++)
            if (layer != firstPersonViewLayer && IsVisibleLayer(cameraMask, layer)) return layer;
        return sourceLayer;
    }

    private static bool IsVisibleLayer(int mask, int layer) => layer >= 0 && layer < 32 && (mask & (1 << layer)) != 0;

    private bool CanEnter()
    {
        GameObject held = interaction != null ? interaction.GetPickedUpGameObject() : null;
        if (!IsLocalPlayer() || input == null || !input.isActiveAndEnabled || input.IsGameplayUiOpen || interaction == null
            || !interaction.isActiveAndEnabled || held == null || !held.activeInHierarchy
            || health != null && health.IsDowned || interaction.IsHoldingDownedPlayer
            || interaction.IsHoldingSelfPositionedObject || interaction.IsSharedCarryMovementActive
            || interaction.IsFlexibleSaplingInteractionActive
            || concreteTrap != null && concreteTrap.BlocksGameplayInput
            || wheelbarrow != null && wheelbarrow.BlocksStandardMovement
            || ropeConstraint != null && ropeConstraint.IsSuspended)
            return false;
        ISingleCarryPlaceable placeable = ResolvePlaceable(held);
        return placeable != null && placeable.CanEnterSingleCarryPlacement;
    }

    private bool CanContinue()
    {
        if (enteredInNetworkSession && TryGetCurrentHeldNetworkObject(out NetworkObject currentSource)
            && currentSource.IsSpawned && input != null && currentSource.OwnerClientId == input.OwnerClientId)
            heldSourceOwnershipTracked = true;
        if (heldSourceOwnershipTracked
            && (!TryGetCurrentHeldNetworkObject(out NetworkObject pendingSource) || pendingSource == null
                || !pendingSource.IsSpawned || input == null || pendingSource.OwnerClientId != input.OwnerClientId))
            return false;
        bool sessionStillMatches = enteredInNetworkSession == IsNetworkSessionListening();
        bool networkChannelReady = !enteredInNetworkSession || input != null && input.IsSpawned && input.IsOwner
            && networkPreview != null && networkPreview.IsSpawned && networkPreview.IsOwner;
        return isActiveAndEnabled && sessionStillMatches && networkChannelReady && IsLocalPlayer() && input != null && !input.IsGameplayUiOpen
            && input.IsLocalGameplayInputActive && heldObject != null
            && heldObject.activeInHierarchy && interaction != null && interaction.isActiveAndEnabled
            && (health == null || !health.IsDowned)
            && !interaction.IsHoldingDownedPlayer && !interaction.IsHoldingSelfPositionedObject
            && !interaction.IsSharedCarryMovementActive && !interaction.IsFlexibleSaplingInteractionActive
            && (concreteTrap == null || !concreteTrap.BlocksGameplayInput)
            && (wheelbarrow == null || !wheelbarrow.BlocksStandardMovement)
            && (ropeConstraint == null || !ropeConstraint.IsSuspended)
            && interaction.GetPickedUpGameObject() == heldObject
            && heldPlaceable != null && heldPlaceable.CanEnterSingleCarryPlacement
            && ghost != null && ghost.Root != null;
    }

    private static ISingleCarryPlaceable ResolvePlaceable(GameObject held)
    {
        return held != null ? held.GetComponent<ISingleCarryPlaceable>() : null;
    }

    private bool IsLocalPlayer()
    {
        if (input == null) return false;
        return !input.IsSpawned ? Unity.Netcode.NetworkManager.Singleton == null
            || !Unity.Netcode.NetworkManager.Singleton.IsListening : input.IsOwner;
    }

    private void HeldObjectChanged(object sender, System.EventArgs args)
    {
        if (IsActive && interaction.GetPickedUpGameObject() != heldObject) ForceExitPlacement();
    }

    private void Health_DownedStateChanged(object sender, System.EventArgs args)
    {
        if (health != null && health.IsDowned) ForceExitPlacement();
    }

    private void CancelPlacement(object sender, System.EventArgs args)
    {
        if (!requestPending) ExitPlacement();
    }

    private void ActiveSceneChanged(Scene previous, Scene next) => ForceExitPlacement();

    private void ForceExitPlacement(object sender, System.EventArgs args) => ForceExitPlacement();

    private void ForceExitPlacement() => ExitPlacement();

    private void PlacementChannelLost()
    {
        if (IsActive) ForceExitPlacement();
    }

    private void PlacementRequestCompleted(ulong requestId, SingleCarryPlacementNetworkResult result)
    {
        if (!IsActive || !requestPending || requestId != pendingRequestId) return;
        requestPending = false;
        pendingRequestId = 0;
        if (result == SingleCarryPlacementNetworkResult.Accepted)
        {
            ExitPlacement();
            return;
        }
        placementResultMessage = GetNetworkResultMessage(result);
        // Rebuild the local preview on the next frame; keep the held object and hidden visual.
    }

    private static string GetNetworkResultMessage(SingleCarryPlacementNetworkResult result) => result switch
    {
        SingleCarryPlacementNetworkResult.InvalidAimOrigin => "Aim out of sync; try again",
        SingleCarryPlacementNetworkResult.InvalidPose => "Pose out of sync; try again",
        SingleCarryPlacementNetworkResult.IneligibleHolder => "Held item unavailable",
        SingleCarryPlacementNetworkResult.MissingPlayer => "Player unavailable",
        SingleCarryPlacementNetworkResult.Occupied => "Placement blocked",
        SingleCarryPlacementNetworkResult.UnsupportedGeometry => "Unsupported shape",
        SingleCarryPlacementNetworkResult.StaleRequest => "Request stale; try again",
        _ => "Placement refused; try again"
    };

    private bool IsNetworkPlacementAvailable()
    {
        return IsActive && heldPlaceable != null && heldPlaceable.CanEnterSingleCarryPlacement
            && TryGetSourceNetworkObject(out _);
    }

    private bool TryGetSourceNetworkObject(out NetworkObject source)
    {
        source = null;
        if (heldObject == null || input == null || networkPreview == null || !networkPreview.IsSpawned || !networkPreview.IsOwner
            || !input.IsSpawned || !input.IsOwner || networkPreview.NetworkManager == null
            || !networkPreview.NetworkManager.IsListening)
            return false;
        source = heldObject.GetComponent<NetworkObject>();
        return source != null && source.IsSpawned && source.OwnerClientId == input.OwnerClientId;
    }

    private bool TryGetCurrentHeldNetworkObject(out NetworkObject source)
    {
        source = null;
        if (heldObject == null) return false;
        source = heldObject.GetComponent<NetworkObject>();
        return source != null;
    }

    private void PublishPreviewState(Vector3 position, Quaternion rotation, bool valid)
    {
        if (networkPreview != null) networkPreview.SetOwnerPreview(heldObject, position, rotation, valid);
    }

    private void ExitPlacement()
    {
        if (networkPreview != null) networkPreview.ClearOwnerPreview();
        if (ghost != null) ghost.Dispose();
        ghost = null;
        if (heldVisual != null) heldVisual.Dispose();
        heldVisual = null;
        geometrySolver = null;
        validator = null;
        heldObject = null;
        heldPlaceable = null;
        IsActive = false;
        requestPending = false;
        pendingRequestId = 0;
        enteredInNetworkSession = false;
        heldSourceOwnershipTracked = false;
        placementResultMessage = null;
        hasLastRenderedPose = false;
        LastValidation = new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);
        localRotation = Quaternion.identity;
        worldRenderLayer = -1;
        if (input != null) input.SetSingleCarryPlacementActive(false);
    }
}
