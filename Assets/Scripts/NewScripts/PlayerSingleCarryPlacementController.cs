using Cinemachine;
using StarterAssets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Local-only single-carry placement preview with discrete one-degree world-axis rotation and F-mode camera stabilization.</summary>
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
    private PlayerCameraFeedbackComposer cameraFeedbackComposer;
    private PlayerSingleCarryPlacementNetworkPreview networkPreview;
    private SingleCarryPlacementGhost ghost;
    private SingleCarryPlacementGeometrySolver geometrySolver;
    private SingleCarryPlacementValidator validator;
    private SingleCarryPlacementHeldVisual heldVisual;
    private GameObject heldObject;
    private ISingleCarryPlaceable heldPlaceable;
    private readonly SingleCarryPlacementRotationState rotationState = new();
    private Quaternion placementOffset = Quaternion.identity;
    private CinemachineBasicMultiChannelPerlin mutedCameraNoise;
    private float originalCameraNoiseAmplitude;
    private SingleCarryPlacementPose lastRenderedPose;
    private Ray lastRenderedAimRay;
    private bool hasLastRenderedPose;
    private bool requestPending;
    private bool rotationBlockedUntilNeutral;
    private ulong pendingRequestId;
    private ulong lastRequestId;
    private bool enteredInNetworkSession;
    private bool heldSourceOwnershipTracked;
    private bool hasNetworkSurfaceAnchor;
    private bool networkSurfaceAnchorRejected;
    private NetworkObject anchoredSurface;
    private NetworkObjectReference anchoredSurfaceReference;
    private Vector3 localPosePosition;
    private Quaternion localPoseRotation;
    private Vector3 localSurfacePoint;
    private Vector3 localSurfaceNormal;
    private string physicalRootPath;
    private bool physicalRootHasRigidbody;
    private string placementResultMessage;
    private int worldRenderLayer = -1;
    private BridgeDeckPanelPlacementGuidance panelPlacementGuidance;

    public bool IsActive { get; private set; }
    public bool IsRequestPending => requestPending;
    public bool CanConfirmPlacement => !requestPending && (!IsNetworkSessionListening() || IsNetworkPlacementAvailable());
    public string PlacementConfirmationHint => requestPending ? "Waiting for server" : CanConfirmPlacement
        ? "E Place" : "E Unavailable in multiplayer";
    public SingleCarryPlacementGeometryStatus LastGeometryStatus { get; private set; }
    public SingleCarryPlacementValidationResult LastValidation { get; private set; }
    public bool IsCurrentPlacementValid => IsActive && LastValidation.IsValid;
    public string PanelMountingGuidanceLabel => panelPlacementGuidance != null ? panelPlacementGuidance.Label : string.Empty;
    public bool IsHoldingAreaPanel => heldObject != null && heldObject.TryGetComponent(out BridgeDeckAreaPanel _);
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
        panelPlacementGuidance = new BridgeDeckPanelPlacementGuidance(this);
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
            else if (attached is PlayerCameraFeedbackComposer composer) cameraFeedbackComposer = composer;
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
            input.OnSingleCarryPlacementRotationChanged += PlacementRotationInputChanged;
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
            input.OnSingleCarryPlacementRotationChanged -= PlacementRotationInputChanged;
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

    private void OnDestroy()
    {
        RestorePlacementCameraNoise();
        if (cameraFeedbackComposer != null) cameraFeedbackComposer.SetPlacementModeSuppressed(false);
        if (panelPlacementGuidance != null) panelPlacementGuidance.Dispose();
        panelPlacementGuidance = null;
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

        UpdatePlacementCameraNoiseMute();

        Vector2 rotationInput = input.GetSingleCarryPlacementRotationInput();
        bool controlModifier = input.IsSingleCarryPlacementControlModifierPressed();
        if (rotationInput.sqrMagnitude < 0.0001f) rotationBlockedUntilNeutral = false;
        if (requestPending) return;
        if (!rotationBlockedUntilNeutral)
            rotationState.Advance(rotationInput, controlModifier, Time.realtimeSinceStartupAsDouble, rotationDegreesPerSecond);
        RefreshPreview();
    }

    private void CinemachineCameraUpdated(CinemachineBrain brain)
    {
        if (!IsActive || !CanContinue()) return;
        if (brain == null || !interaction.TryGetAimRay(out _, out Camera aimCamera, out _) || aimCamera == null
            || brain.OutputCamera != aimCamera) return;
        UpdatePlacementCameraNoiseMute(brain);
        if (requestPending) return;
        RefreshPreview();
    }

    private void TogglePlacement(object sender, System.EventArgs args)
    {
        if (requestPending) return;
        if (IsActive) { ExitPlacement(); return; }
        if (!CanEnter()) return;

        heldObject = interaction.GetPickedUpGameObject();
        placementOffset = CarryPlacementOrientation.ResolveOffset(heldObject);
        heldPlaceable = ResolvePlaceable(heldObject);
        ghost = new SingleCarryPlacementGhost(heldObject);
        geometrySolver = new SingleCarryPlacementGeometrySolver(heldObject);
        validator = new SingleCarryPlacementValidator(heldObject);
        heldVisual = new SingleCarryPlacementHeldVisual(heldObject);
        rotationState.Reset();
        rotationBlockedUntilNeutral = false;
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
        if (cameraFeedbackComposer != null) cameraFeedbackComposer.SetPlacementModeSuppressed(true);
        UpdatePlacementCameraNoiseMute();
        interaction.CancelBucketActionHoldForPlacement();
        if (actionController != null) actionController.CancelActionForStateChange();
        if (firstPersonController != null) firstPersonController.StopHorizontalMovementForPlacement();
        input.SetSingleCarryPlacementActive(true);
        RefreshPreview();
    }

    private void PlacementRotationInputChanged(Vector2 direction, bool controlModifier, double timestamp)
    {
        if (!IsActive) return;
        if (requestPending)
        {
            rotationBlockedUntilNeutral = direction.sqrMagnitude >= 0.0001f;
            return;
        }
        if (rotationBlockedUntilNeutral)
        {
            if (direction.sqrMagnitude < 0.0001f) rotationBlockedUntilNeutral = false;
            return;
        }
        rotationState.SetInput(direction, controlModifier, timestamp);
    }

    private void RefreshPreview()
    {
        if (ghost == null || geometrySolver == null || validator == null)
        {
            hasLastRenderedPose = false;
            return;
        }
        // NetworkTransform and carried-object movement can update collider Transforms outside
        // the physics step. Keep the preview raycast and subsequent penetration check on the
        // same current pose while placement mode is active.
        ghost.SyncSourceAppearance();
        Physics.SyncTransforms();
        if (!interaction.TryGetAimRay(out Ray aimRay, out Camera aimCamera, out Quaternion baseYaw))
        {
            hasLastRenderedPose = false;
            ClearPanelMountingPreview();
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
        Quaternion itemRotation = CarryPlacementOrientation.Compose(rotationState.BuildWorldRotation(baseYaw), placementOffset);
        SingleCarryPlacementPose pose = geometrySolver.Calculate(aimRay, PlacementDistance, itemRotation, Quaternion.identity, transform);
        bool renderedPoseChanged = !hasLastRenderedPose
            || Vector3.Distance(lastRenderedPose.Position, pose.Position) > 0.001f
            || Quaternion.Angle(lastRenderedPose.Rotation, pose.Rotation) > 0.05f;
        if (renderedPoseChanged) placementResultMessage = null;
        LastGeometryStatus = pose.Status;
        ghost.SetPose(pose.Position, pose.Rotation);
        lastRenderedPose = pose;
        lastRenderedAimRay = aimRay;
        hasLastRenderedPose = true;
        CaptureNetworkSurfaceAnchor(pose);
        LastValidation = validator.Validate(pose);
        ghost.SetPlacementValid(LastValidation.IsValid);
        RefreshPanelMountingPreview(pose, aimCamera);
        PublishPreviewState(pose.Position, pose.Rotation, LastValidation.IsValid);
    }

    private void ConfirmPlacement(object sender, System.EventArgs args)
    {
        if (!IsActive || requestPending || !CanConfirmPlacement || !CanContinue() || !hasLastRenderedPose
            || validator == null || ghost == null || ghost.Root == null)
            return;

        // Confirmation can arrive between preview refreshes, so recheck the latest world
        // transforms before validating and submitting the placement.
        Physics.SyncTransforms();
        SingleCarryPlacementPose poseToCommit = lastRenderedPose;
        bool useSurfaceAnchor = IsNetworkSessionListening() && hasNetworkSurfaceAnchor;
        if (useSurfaceAnchor && !TryRebaseNetworkSurfacePose(out poseToCommit))
        {
            LastValidation = new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);
            ghost.SetPlacementValid(false);
            return;
        }
        if (networkSurfaceAnchorRejected && IsNetworkSessionListening())
        {
            LastValidation = new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);
            ghost.SetPlacementValid(false);
            return;
        }
        LastValidation = validator.Validate(poseToCommit);
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
            rotationBlockedUntilNeutral = true;
            rotationState.ResetRepeatState();
            placementResultMessage = null;
            ClearPanelMountingPreview();
            PublishPreviewState(poseToCommit.Position, poseToCommit.Rotation, LastValidation.IsValid);
            if (useSurfaceAnchor)
                networkPreview.RequestNetworkSurfacePlacement(source, pendingRequestId, anchoredSurfaceReference,
                    localPosePosition, localPoseRotation, localSurfacePoint, localSurfaceNormal,
                    physicalRootPath, physicalRootHasRigidbody, poseToCommit.Position,
                    poseToCommit.Rotation, lastRenderedAimRay);
            else
                networkPreview.RequestNetworkPlacement(source, pendingRequestId, poseToCommit.Position,
                    poseToCommit.Rotation, lastRenderedAimRay);
            return;
        }

        if (!interaction.TryReleaseSingleCarryPlacement(heldObject, heldPlaceable,
                poseToCommit.Position, poseToCommit.Rotation))
        {
            LastValidation = new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);
            ghost.SetPlacementValid(false);
            return;
        }

        if (IsActive) ExitPlacement();
    }

    private void CaptureNetworkSurfaceAnchor(SingleCarryPlacementPose pose)
    {
        hasNetworkSurfaceAnchor = false;
        networkSurfaceAnchorRejected = false;
        anchoredSurface = null;
        if (!IsNetworkSessionListening() || pose.SurfaceCollider == null) return;

        NetworkObject surfaceObject = pose.SurfaceCollider.GetComponentInParent<NetworkObject>();
        if (surfaceObject == null) return; // Keep the strict legacy world-pose path for static surfaces.

        networkSurfaceAnchorRejected = true;
        if (networkPreview == null || !TryGetSourceNetworkObject(out NetworkObject source)
            || !SingleCarryPlacementSurfaceAnchor.TryCapture(pose.SurfaceCollider, networkPreview.NetworkManager,
                source, pose.Position, pose.Rotation, pose.SurfacePoint, pose.SurfaceNormal,
                out anchoredSurface, out anchoredSurfaceReference, out localPosePosition, out localPoseRotation,
                out localSurfacePoint, out localSurfaceNormal, out physicalRootPath,
                out physicalRootHasRigidbody)) return;
        hasNetworkSurfaceAnchor = true;
        networkSurfaceAnchorRejected = false;
    }

    private bool TryRebaseNetworkSurfacePose(out SingleCarryPlacementPose rebasedPose)
    {
        rebasedPose = default;
        NetworkManager manager = networkPreview != null ? networkPreview.NetworkManager : null;
        if (!hasNetworkSurfaceAnchor || anchoredSurface == null || !anchoredSurface.IsSpawned
            || manager == null || anchoredSurface.NetworkManager != manager
            || !anchoredSurfaceReference.TryGet(out NetworkObject capturedSurface, manager)
            || capturedSurface != anchoredSurface
            || !interaction.TryGetAimRay(out Ray aimRay, out _, out _)) return false;

        if (!SingleCarryPlacementSurfaceAnchor.TryResolveAnchorTransform(anchoredSurface, physicalRootPath,
                physicalRootHasRigidbody, out Transform anchorTransform)) return false;
        Vector3 worldPoint = anchorTransform.TransformPoint(localSurfacePoint);
        Vector3 worldNormal = anchorTransform.TransformDirection(localSurfaceNormal).normalized;
        Quaternion worldRotation = anchorTransform.rotation * localPoseRotation;
        Vector3 expectedWorldPosition = anchorTransform.TransformPoint(localPosePosition);
        if (!IsFinite(worldPoint) || !IsFinite(worldNormal) || !IsFinite(worldRotation)
            || !IsFinite(expectedWorldPosition)) return false;
        if (!SingleCarryPlacementSurfaceAnchor.TryResolveContactCollider(anchoredSurface, heldObject.transform,
                transform, worldPoint, worldNormal, physicalRootPath, physicalRootHasRigidbody,
                out Collider currentCollider)) return false;
        if (!SingleCarryPlacementSurfaceAnchor.HasLineOfSightToContact(currentCollider, anchoredSurface,
                physicalRootPath, physicalRootHasRigidbody, heldObject.transform, transform,
                aimRay.origin, worldPoint, PlacementDistance)) return false;

        rebasedPose = geometrySolver.CalculateAtSurfaceContact(worldPoint, worldNormal, worldRotation, currentCollider);
        return rebasedPose.Status == SingleCarryPlacementGeometryStatus.SurfaceSupported
            && IsFinite(rebasedPose.Position) && IsFinite(rebasedPose.Rotation)
            && Vector3.Distance(rebasedPose.Position, expectedWorldPosition) <= 0.002f
            && Quaternion.Angle(rebasedPose.Rotation, worldRotation) <= 0.1f;
    }

    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsFinite(Quaternion value) => IsFinite(value.x) && IsFinite(value.y)
        && IsFinite(value.z) && IsFinite(value.w);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

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
        ClearPanelMountingPreview();
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
        RestorePlacementCameraNoise();
        if (cameraFeedbackComposer != null) cameraFeedbackComposer.SetPlacementModeSuppressed(false);
        ClearPanelMountingPreview();
        if (networkPreview != null) networkPreview.ClearOwnerPreview();
        if (ghost != null) ghost.Dispose();
        ghost = null;
        if (heldVisual != null) heldVisual.Dispose();
        heldVisual = null;
        geometrySolver = null;
        validator = null;
        heldObject = null;
        placementOffset = Quaternion.identity;
        heldPlaceable = null;
        IsActive = false;
        requestPending = false;
        pendingRequestId = 0;
        enteredInNetworkSession = false;
        heldSourceOwnershipTracked = false;
        placementResultMessage = null;
        hasLastRenderedPose = false;
        hasNetworkSurfaceAnchor = false;
        networkSurfaceAnchorRejected = false;
        anchoredSurface = null;
        physicalRootPath = null;
        physicalRootHasRigidbody = false;
        LastValidation = new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);
        rotationState.Reset();
        rotationBlockedUntilNeutral = false;
        worldRenderLayer = -1;
        if (input != null) input.SetSingleCarryPlacementActive(false);
    }

    internal void OnLocalVirtualCameraBindingChanged(CinemachineVirtualCamera virtualCamera)
    {
        if (!IsActive || !IsLocalPlayer())
        {
            RestorePlacementCameraNoise();
            return;
        }
        MutePlacementCameraNoise(virtualCamera);
    }

    private void UpdatePlacementCameraNoiseMute(CinemachineBrain preferredBrain = null)
    {
        if (!IsActive || !IsLocalPlayer())
        {
            RestorePlacementCameraNoise();
            return;
        }

        CinemachineBrain brain = preferredBrain;
        if (brain == null && interaction != null
            && interaction.TryGetAimRay(out _, out Camera camera, out _) && camera != null)
            brain = camera.GetComponent<CinemachineBrain>();
        if (brain == null || brain.OutputCamera == null)
        {
            RestorePlacementCameraNoise();
            return;
        }
        MutePlacementCameraNoise(brain.ActiveVirtualCamera as CinemachineVirtualCamera);
    }

    private void MutePlacementCameraNoise(CinemachineVirtualCamera virtualCamera)
    {
        Transform expectedTarget = cameraFeedbackComposer != null ? cameraFeedbackComposer.OutputTarget
            : firstPersonController != null && firstPersonController.CinemachineCameraTarget != null
                ? firstPersonController.CinemachineCameraTarget.transform : null;
        if (virtualCamera == null || expectedTarget == null || virtualCamera.Follow != expectedTarget)
        {
            RestorePlacementCameraNoise();
            return;
        }

        CinemachineBasicMultiChannelPerlin noise = virtualCamera.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
        if (noise == null)
        {
            RestorePlacementCameraNoise();
            return;
        }
        if (noise != mutedCameraNoise)
        {
            RestorePlacementCameraNoise();
            mutedCameraNoise = noise;
            originalCameraNoiseAmplitude = noise.m_AmplitudeGain;
        }
        mutedCameraNoise.m_AmplitudeGain = 0f;
    }

    private void RestorePlacementCameraNoise()
    {
        if (mutedCameraNoise != null) mutedCameraNoise.m_AmplitudeGain = originalCameraNoiseAmplitude;
        mutedCameraNoise = null;
        originalCameraNoiseAmplitude = 0f;
    }

    private void RefreshPanelMountingPreview(SingleCarryPlacementPose pose, Camera aimCamera)
    {
        if (heldObject == null || !heldObject.TryGetComponent(out MountableBridgeComponent panel) || !panel.IsPhysicalDeckPanel)
        {
            ClearPanelMountingPreview();
            return;
        }

        if (heldObject.TryGetComponent(out BridgeDeckAreaPanel areaPanel))
        {
            string message = BridgeDeckAreaRequirement.GetPlacementCandidateMessage(areaPanel,
                pose.Position, pose.Rotation, out bool areaScope, out bool stageActive);
            if (areaScope)
            {
                if (!stageActive) { ClearPanelMountingPreview(); return; }
                if (panelPlacementGuidance == null) panelPlacementGuidance = new BridgeDeckPanelPlacementGuidance(this);
                panelPlacementGuidance.SetMessage(message);
                return;
            }
            ClearPanelMountingPreview();
            return;
        }

        if (!heldObject.TryGetComponent(out BridgeDeckPanelMounting marker))
        { ClearPanelMountingPreview(); return; }

        if (!BridgeDeckPanelSectionNetworkState.TryAssessPlacementPreview(panel, pose.Position,
                pose.Rotation, out BridgeDeckPanelPlacementAssessment assessment))
        {
            ClearPanelMountingPreview("Mount: No slot");
            return;
        }
        if (panelPlacementGuidance == null) panelPlacementGuidance = new BridgeDeckPanelPlacementGuidance(this);
        panelPlacementGuidance.Refresh(assessment, pose.Position, aimCamera, worldRenderLayer);
    }

    private void ClearPanelMountingPreview(string message = null)
    {
        if (panelPlacementGuidance != null) panelPlacementGuidance.Clear(message);
    }
}
