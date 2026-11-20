using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using UnityEngine.Serialization;

public class FoundationExcavationVolume : MonoBehaviour
{
    [SerializeField] private Transform soilSurface;
    [SerializeField] private BridgeConstructionSite constructionSite;
    [SerializeField] private Renderer soilRenderer;
    [SerializeField] private Material compactSoilMaterial;
    [SerializeField] private Material loosenedSoilMaterial;
    [SerializeField] private float surfaceStartLocalY;
    [Header("Loose soil V1")]
    [SerializeField] private bool useLooseSoilHeightField;
    [SerializeField] private FoundationLooseSoilField looseSoilField;
    [SerializeField] private BoxCollider soilSupportCollider;
    [Header("Concrete")]
    [SerializeField] private Transform concreteFillVisual;
    [SerializeField] private Renderer concreteRenderer;
    [SerializeField] private Collider driedConcreteCollider;
    [SerializeField] private Material wetConcreteMaterial;
    [SerializeField] private Material dryConcreteMaterial;
    [SerializeField] private Vector2 concreteFootprintSize = new Vector2(6.5f, 4f);
    [FormerlySerializedAs("concreteEmptyLocalY")]
    [SerializeField] private float concreteBottomLocalY = -1.2f;
    [FormerlySerializedAs("concreteFullLocalY")]
    [SerializeField] private float concreteFullTopLocalY = 0.08f;
    [SerializeField] private Renderer exitRampRenderer;
    [SerializeField] private Collider exitRampCollider;
    [SerializeField, Min(0f)] private float exitRampClearanceMargin = 0.02f;
    private Collider[] exitRampOverlapBuffer = new Collider[64];
    [Header("Critical Failure")]
    [SerializeField] private FoundationFailedConcreteTarget failedConcreteTarget;
    [SerializeField] private GameObject[] failedConcreteCrackVisuals;
    [SerializeField] private Transform failedWheelbarrowPose;
    [SerializeField] private BoxCollider recoveryVolume;
    [SerializeField] private NavMeshObstacle pitNavMeshObstacle;
    [SerializeField] private GameObject navMeshBakeProxy;

    private Material runtimeMaterial;
    private Vector3[] crackBaseLocalPositions;
    private Vector3[] crackBaseLocalScales;
    private Vector3 driedColliderBaseCenter;
    private Vector3 driedColliderBaseSize = Vector3.one;
    private bool driedColliderBaselineCaptured;
    private bool soilRendererBaselineCaptured;
    private bool soilRendererBaselineEnabled;
    private bool looseSoilConfigurationErrorLogged;
    private int appliedLooseSoilCycle = int.MinValue;
    private FoundationDiggingSubstage appliedLooseSoilSubstage;

    public Transform FailedWheelbarrowPose => failedWheelbarrowPose;
    public FoundationFailedConcreteTarget FailedConcreteTarget => failedConcreteTarget;
    public NavMeshObstacle PitNavMeshObstacle => pitNavMeshObstacle;
    public GameObject NavMeshBakeProxy => navMeshBakeProxy;
    public Collider DriedConcreteCollider => driedConcreteCollider;
    public bool IsExitRampColliderEnabled => exitRampCollider != null && exitRampCollider.enabled &&
                                             exitRampCollider.gameObject.activeInHierarchy;
    public float ExitRampClearanceMargin => Mathf.Max(0f, exitRampClearanceMargin);
    public bool UsesLooseSoilHeightField => useLooseSoilHeightField;
    public FoundationLooseSoilField LooseSoilField => looseSoilField;
    public BridgeConstructionSite ConstructionSite => constructionSite;
    public bool HasValidLooseSoilConfiguration => !useLooseSoilHeightField ||
        (soilSurface != null && soilSupportCollider != null && looseSoilField != null &&
         soilSupportCollider.transform == soilSurface &&
         looseSoilField.transform.parent == transform && looseSoilField.transform.localScale == Vector3.one &&
         looseSoilField.transform.localRotation == Quaternion.identity);
    public IReadOnlyList<GameObject> FailedConcreteCrackVisuals => failedConcreteCrackVisuals;
    public bool ContainsRecoveryWheelbarrow(Vector3 worldPosition)
    {
        if (recoveryVolume == null) return false;
        Vector3 local = recoveryVolume.transform.InverseTransformPoint(worldPosition) - recoveryVolume.center;
        Vector3 half = recoveryVolume.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    public void ApplyDiggingState(BridgeConstructionStage stage, FoundationDiggingSubstage substage,
        float looseningProgress, float removedSoilUnits, BridgeConstructionWorkflowSO workflow, int cycleIndex = 0)
    {
        if (workflow == null)
        {
            return;
        }

        if (useLooseSoilHeightField)
        {
            if (!HasValidLooseSoilConfiguration)
            {
                if (!looseSoilConfigurationErrorLogged)
                {
                    Debug.LogError($"Loose soil is enabled on {name}, but its surface/support references or stable child transform are invalid.", this);
                    looseSoilConfigurationErrorLogged = true;
                }
                if (looseSoilField != null) looseSoilField.SetSurfaceState(false, loosenedSoilMaterial);
                return;
            }

            int safeCycleIndex = Mathf.Clamp(cycleIndex, 0, Mathf.Max(0, workflow.DiggingCycleCount - 1));
            bool returnedSoilOverlay = substage == FoundationDiggingSubstage.ReturnedSoilRemoval;
            bool looseLayerActive = stage == BridgeConstructionStage.Digging &&
                                    (substage == FoundationDiggingSubstage.SoilRemoval || returnedSoilOverlay) &&
                                    cycleIndex >= 0 && cycleIndex < workflow.DiggingCycleCount;
            int cycleCount = Mathf.Max(1, workflow.DiggingCycleCount);
            float firstLayerDepth = workflow.FinalExcavationDepth / cycleCount;
            int unitsPerCycle = Mathf.Max(1, workflow.SoilUnitsPerCycle);
            bool reconfigured = looseSoilField.EnsureConfiguration(concreteFootprintSize.x, concreteFootprintSize.y,
                looseSoilField.ConfiguredCellSize, firstLayerDepth, firstLayerDepth, unitsPerCycle);
            int configuredTotalUnits = Mathf.Max(1, cycleCount * unitsPerCycle);
            bool excavationComplete = removedSoilUnits >= configuredTotalUnits;
            int fieldCycle = stage == BridgeConstructionStage.Digging
                ? safeCycleIndex * 3 + (substage == FoundationDiggingSubstage.SoilRemoval ? 0 :
                    returnedSoilOverlay ? 2 : 1)
                : excavationComplete ? int.MinValue + 1 : int.MinValue + 2;
            bool previousWasReturnedOverlay = appliedLooseSoilCycle != int.MinValue &&
                appliedLooseSoilSubstage == FoundationDiggingSubstage.ReturnedSoilRemoval;
            bool stateChanged = appliedLooseSoilCycle != fieldCycle ||
                                appliedLooseSoilSubstage != substage;
            if (reconfigured || stateChanged)
            {
                // A new cycle gets a fresh layer exactly once. Reapplying the same
                // replicated body state must preserve the already-carved shape.
                if (returnedSoilOverlay || previousWasReturnedOverlay && substage == FoundationDiggingSubstage.Loosening ||
                    excavationComplete && (stage != BridgeConstructionStage.Digging ||
                                            substage == FoundationDiggingSubstage.SoilRemoval))
                    looseSoilField.Clear();
                else
                    looseSoilField.ResetToInitial();
                appliedLooseSoilCycle = fieldCycle;
                appliedLooseSoilSubstage = substage;
            }
            if (looseSoilField.HeightField != null)
                looseSoilField.HeightField.ExpandMaximumHeight(workflow.FinalExcavationDepth);

            float looseTotalUnits = configuredTotalUnits;
            float supportDepth = stage == BridgeConstructionStage.Digging
                ? (safeCycleIndex + (substage == FoundationDiggingSubstage.SoilRemoval ? 1 : 0)) * firstLayerDepth
                : CurrentSupportDepth(removedSoilUnits, looseTotalUnits, workflow.FinalExcavationDepth);
            if (soilSurface != null)
            {
                Vector3 localPosition = soilSurface.localPosition;
                localPosition.y = surfaceStartLocalY - supportDepth;
                soilSurface.localPosition = localPosition;
            }
            if (soilSupportCollider != null) soilSupportCollider.enabled = true;

            Vector3 supportTopWorld = soilSupportCollider.transform.TransformPoint(
                soilSupportCollider.center + Vector3.up * (soilSupportCollider.size.y * 0.5f));
            Vector3 fieldOrigin = transform.InverseTransformPoint(supportTopWorld);
            looseSoilField.transform.localPosition = new Vector3(fieldOrigin.x, fieldOrigin.y, fieldOrigin.z);

            if (soilRenderer != null)
            {
                if (!soilRendererBaselineCaptured)
                {
                    soilRendererBaselineEnabled = soilRenderer.enabled;
                    soilRendererBaselineCaptured = true;
                }
                soilRenderer.enabled = soilRendererBaselineEnabled;
            }

            if (looseSoilField != null) looseSoilField.SetSurfaceState(looseLayerActive, loosenedSoilMaterial);
            return;
        }

        int totalUnits = workflow.DiggingCycleCount * workflow.SoilUnitsPerCycle;
        float normalizedDepth = totalUnits > 0 ? Mathf.Clamp01(removedSoilUnits / totalUnits) : 0f;
        if (soilSurface != null)
        {
            Vector3 localPosition = soilSurface.localPosition;
            localPosition.y = surfaceStartLocalY - workflow.FinalExcavationDepth * normalizedDepth;
            soilSurface.localPosition = localPosition;
        }
        if (soilSupportCollider != null) soilSupportCollider.enabled = true;

        if (soilRenderer == null)
        {
            return;
        }

        float loosened = substage == FoundationDiggingSubstage.SoilRemoval
            ? 1f
            : Mathf.Clamp01(looseningProgress / workflow.LooseningProgressPerCycle);
        if (compactSoilMaterial != null && loosenedSoilMaterial != null)
        {
            if (runtimeMaterial == null)
            {
                runtimeMaterial = new Material(compactSoilMaterial) { name = compactSoilMaterial.name + " (Excavation Runtime)" };
                soilRenderer.material = runtimeMaterial;
            }

            runtimeMaterial.Lerp(compactSoilMaterial, loosenedSoilMaterial, loosened);
        }
    }

    public bool TryGetLooseSoilSurfaceHeight(Vector3 worldPosition, out float surfaceWorldY)
    {
        surfaceWorldY = 0f;
        return useLooseSoilHeightField && looseSoilField != null && looseSoilField.IsSurfaceActive &&
               looseSoilField.TryGetWorldSurfaceHeight(worldPosition, out surfaceWorldY);
    }

    private static float CurrentSupportDepth(float removedUnits, float totalUnits, float finalDepth)
    {
        if (float.IsNaN(removedUnits) || float.IsInfinity(removedUnits)) return 0f;
        return Mathf.Clamp01(removedUnits / totalUnits) * finalDepth;
    }

    public float GetLooseSoilSubmersionDepth(Vector3 worldPosition) =>
        TryGetLooseSoilSurfaceHeight(worldPosition, out float surfaceWorldY)
            ? Mathf.Max(0f, surfaceWorldY - worldPosition.y)
            : 0f;

    public void ResetLooseSoilState()
    {
        if (soilSurface != null)
        {
            Vector3 localPosition = soilSurface.localPosition;
            localPosition.y = surfaceStartLocalY;
            soilSurface.localPosition = localPosition;
        }
        if (soilRenderer != null && soilRendererBaselineCaptured)
            soilRenderer.enabled = soilRendererBaselineEnabled;
        if (looseSoilField != null)
        {
            looseSoilField.ResetToInitial();
            looseSoilField.SetSurfaceState(false, loosenedSoilMaterial);
        }
    }

    public void ApplyConcreteState(BridgeConstructionStage stage, int pouredLoads, int requiredLoads,
        float remainingDryingTime, float dryingDuration,
        FoundationConcreteFailureState failureState = FoundationConcreteFailureState.None,
        float failedBreakProgress = 0f,
        Vector3 failedCrackThresholds = default,
        float collapseProgress = 0f,
        bool rampClearanceGranted = false,
        bool rampClearancePending = false)
    {
        bool failureConcreteVisible = failureState == FoundationConcreteFailureState.CriticalSequence ||
                                      failureState == FoundationConcreteFailureState.HardenedFailure ||
                                      failureState == FoundationConcreteFailureState.Collapsing;
        float fill = failureConcreteVisible
            ? 1f
            : requiredLoads > 0 ? Mathf.Clamp01((float)pouredLoads / requiredLoads) : 0f;
        bool visible = fill > 0f;
        float collapse = failureState == FoundationConcreteFailureState.Collapsing
            ? Mathf.Clamp01(collapseProgress)
            : 0f;
        float horizontalScale = Mathf.Lerp(1f, 0.88f, collapse);
        float verticalScale = Mathf.Lerp(1f, 0.18f, collapse);
        float collapseFall = 0f;
        if (concreteFillVisual != null)
        {
            concreteFillVisual.gameObject.SetActive(visible);
            float concreteTop = Mathf.Lerp(concreteBottomLocalY, concreteFullTopLocalY, fill);
            float concreteHeight = Mathf.Max(0.001f, concreteTop - concreteBottomLocalY);
            collapseFall = concreteHeight * 0.42f * collapse;
            Vector3 position = concreteFillVisual.localPosition;
            position.y = concreteBottomLocalY + concreteHeight * 0.5f - collapseFall;
            concreteFillVisual.localPosition = position;

            Vector3 scale = concreteFillVisual.localScale;
            scale.x = concreteFootprintSize.x * horizontalScale;
            scale.y = concreteHeight * verticalScale;
            scale.z = concreteFootprintSize.y * horizontalScale;
            concreteFillVisual.localScale = scale;

            PreserveConcreteColliderVolumeDuringCollapse(
                concreteHeight,
                horizontalScale,
                verticalScale,
                collapseFall,
                failureState == FoundationConcreteFailureState.Collapsing);
        }

        bool dry = failureConcreteVisible || stage == BridgeConstructionStage.ReadyForMount ||
                   stage == BridgeConstructionStage.Hammering || stage == BridgeConstructionStage.Complete;
        bool fullyPoured = fill >= 0.999f;
        if (driedConcreteCollider != null) driedConcreteCollider.enabled = dry && visible;
        bool awaitingRecovery = failureState == FoundationConcreteFailureState.AwaitingWheelbarrowExit;
        bool hideExitRampDuringLooseSoilDigging = useLooseSoilHeightField && stage == BridgeConstructionStage.Digging;
        bool desiredRampRenderer = !hideExitRampDuringLooseSoilDigging && (awaitingRecovery || !fullyPoured);
        bool desiredRampCollider = !hideExitRampDuringLooseSoilDigging && (awaitingRecovery || !(fullyPoured && dry));
        bool safetyGated = useLooseSoilHeightField && !rampClearanceGranted &&
                           (rampClearancePending || desiredRampCollider);
        if (exitRampRenderer != null) exitRampRenderer.enabled = desiredRampRenderer && !safetyGated;
        if (exitRampCollider != null) exitRampCollider.enabled = desiredRampCollider && !safetyGated;
        if (concreteRenderer != null && wetConcreteMaterial != null && dryConcreteMaterial != null)
        {
            float drying = stage == BridgeConstructionStage.ConcreteDrying && dryingDuration > 0f
                ? 1f - Mathf.Clamp01(remainingDryingTime / dryingDuration)
                : dry ? 1f : 0f;
            if (concreteRenderer.sharedMaterial == null || runtimeConcreteMaterial == null)
            {
                runtimeConcreteMaterial = new Material(wetConcreteMaterial) { name = wetConcreteMaterial.name + " (Concrete Runtime)" };
                concreteRenderer.material = runtimeConcreteMaterial;
            }
            runtimeConcreteMaterial.Lerp(wetConcreteMaterial, dryConcreteMaterial, drying);
        }

        if (failedConcreteTarget != null)
        {
            Collider targetCollider = failedConcreteTarget.InteractionCollider;
            if (targetCollider != null)
                targetCollider.enabled = failureState == FoundationConcreteFailureState.HardenedFailure;
            failedConcreteTarget.enabled = failureState == FoundationConcreteFailureState.HardenedFailure;
        }

        UpdateCrackVisuals(
            failureState,
            failedBreakProgress,
            failedCrackThresholds,
            horizontalScale,
            verticalScale,
            collapseFall);
        UpdatePitNavMeshObstacle(stage, failureState, dry && visible);
    }

    public bool IsExitRampVolumeClear()
    {
        if (exitRampCollider == null || !TryGetBoxGeometry(exitRampCollider, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation))
            return false;

        // Pending clearance is the only path that calls this query during gameplay.
        // Make current Rigidbody transforms visible to the physics broadphase before
        // testing: Auto Sync Transforms is disabled in the project settings.
        Physics.SyncTransforms();
        Vector3 margin = Vector3.one * ExitRampClearanceMargin;
        halfExtents += margin;
        int count;
        while (true)
        {
            count = Physics.OverlapBoxNonAlloc(center, halfExtents, exitRampOverlapBuffer, rotation,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count < exitRampOverlapBuffer.Length) break;
            // Grow and requery because a full buffer may contain only ignored static
            // contacts. At the bounded cap, occupancy remains ambiguous and fails closed.
            if (exitRampOverlapBuffer.Length >= 4096) return false;
            exitRampOverlapBuffer = new Collider[exitRampOverlapBuffer.Length * 2];
        }
        for (int i = 0; i < count; i++)
        {
            Collider candidate = exitRampOverlapBuffer[i];
            if (candidate == null || candidate == exitRampCollider || candidate.isTrigger ||
                candidate.transform == transform || candidate.transform == exitRampCollider.transform ||
                candidate.transform.IsChildOf(exitRampCollider.transform) ||
                Physics.GetIgnoreLayerCollision(exitRampCollider.gameObject.layer, candidate.gameObject.layer) ||
                Physics.GetIgnoreCollision(exitRampCollider, candidate)) continue;
            if (candidate is CharacterController characterController)
            {
                if (CapsuleOverlapsBox(characterController, center, halfExtents, rotation)) return false;
            }
            else if (candidate.attachedRigidbody != null) return false;
        }

        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening && manager.SpawnManager != null)
        {
            foreach (NetworkObject playerObject in manager.SpawnManager.SpawnedObjectsList)
            {
                if (playerObject == null || !playerObject.IsPlayerObject || !playerObject.gameObject.activeInHierarchy) continue;
                CharacterController controller = playerObject.GetComponent<CharacterController>();
                if (controller == null || controller.isTrigger ||
                    Physics.GetIgnoreLayerCollision(exitRampCollider.gameObject.layer, controller.gameObject.layer) ||
                    Physics.GetIgnoreCollision(exitRampCollider, controller) ||
                    controller.transform == transform || controller.transform == exitRampCollider.transform ||
                    controller.transform.IsChildOf(exitRampCollider.transform)) continue;
                if (CapsuleOverlapsBox(controller, center, halfExtents, rotation)) return false;
            }
        }
        else
        {
            // Offline play and local authoring have no NGO spawn roster. The broadphase
            // cannot see a disabled CharacterController, so enumerate active controllers
            // only during the caller's throttled pending-clearance poll.
            CharacterController[] offlineControllers = FindObjectsByType<CharacterController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (CharacterController controller in offlineControllers)
            {
                if (controller == null || !controller.gameObject.activeInHierarchy ||
                    controller.isTrigger ||
                    Physics.GetIgnoreLayerCollision(exitRampCollider.gameObject.layer, controller.gameObject.layer) ||
                    Physics.GetIgnoreCollision(exitRampCollider, controller) ||
                    controller.transform == transform || controller.transform == exitRampCollider.transform ||
                    controller.transform.IsChildOf(exitRampCollider.transform)) continue;
                if (CapsuleOverlapsBox(controller, center, halfExtents, rotation)) return false;
            }
        }
        return true;
    }

    public bool IsExitRampColliderRequested(BridgeConstructionStage stage, int pouredLoads, int requiredLoads,
        FoundationConcreteFailureState failureState)
    {
        bool visibleFailure = failureState == FoundationConcreteFailureState.CriticalSequence ||
                              failureState == FoundationConcreteFailureState.HardenedFailure ||
                              failureState == FoundationConcreteFailureState.Collapsing;
        float fill = visibleFailure ? 1f : requiredLoads > 0 ? Mathf.Clamp01((float)pouredLoads / requiredLoads) : 0f;
        bool dry = visibleFailure || stage is BridgeConstructionStage.ReadyForMount or
            BridgeConstructionStage.Hammering or BridgeConstructionStage.Complete;
        bool fullyPoured = fill >= 0.999f;
        bool awaitingRecovery = failureState == FoundationConcreteFailureState.AwaitingWheelbarrowExit;
        bool hiddenDuringDigging = useLooseSoilHeightField && stage == BridgeConstructionStage.Digging;
        return !hiddenDuringDigging && (awaitingRecovery || !(fullyPoured && dry));
    }

    private static bool TryGetBoxGeometry(Collider collider, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        if (collider is BoxCollider box)
        {
            Vector3 scale = box.transform.lossyScale;
            center = box.transform.TransformPoint(box.center);
            halfExtents = Vector3.Scale(box.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            rotation = box.transform.rotation;
            return halfExtents.sqrMagnitude > 0f;
        }
        center = default;
        halfExtents = default;
        rotation = Quaternion.identity;
        return false;
    }

    private static bool CapsuleOverlapsBox(CharacterController capsule, Vector3 boxCenter, Vector3 boxHalfExtents,
        Quaternion boxRotation)
    {
        Transform t = capsule.transform;
        Vector3 axis = t.up;
        float axialScale = Mathf.Abs(t.lossyScale.y);
        float radialScale = Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.z));
        float radius = capsule.radius * radialScale;
        float halfSegment = Mathf.Max(0f, capsule.height * axialScale * 0.5f - radius);
        Vector3 capsuleCenter = t.TransformPoint(capsule.center);
        Vector3 a = capsuleCenter - axis * halfSegment;
        Vector3 b = capsuleCenter + axis * halfSegment;
        Vector3 localA = Quaternion.Inverse(boxRotation) * (a - boxCenter);
        Vector3 localB = Quaternion.Inverse(boxRotation) * (b - boxCenter);
        // Squared distance from a segment to an AABB is convex in segment t; ternary
        // minimization avoids treating the oriented box's world AABB as occupied.
        float lo = 0f, hi = 1f;
        for (int i = 0; i < 20; i++)
        {
            float t1 = (2f * lo + hi) / 3f;
            float t2 = (lo + 2f * hi) / 3f;
            if (SegmentPointAabbDistanceSquared(Vector3.Lerp(localA, localB, t1), boxHalfExtents) <=
                SegmentPointAabbDistanceSquared(Vector3.Lerp(localA, localB, t2), boxHalfExtents)) hi = t2;
            else lo = t1;
        }
        float distanceSquared = Mathf.Min(
            SegmentPointAabbDistanceSquared(localA, boxHalfExtents),
            SegmentPointAabbDistanceSquared(localB, boxHalfExtents));
        distanceSquared = Mathf.Min(distanceSquared,
            SegmentPointAabbDistanceSquared(Vector3.Lerp(localA, localB, (lo + hi) * 0.5f), boxHalfExtents));
        float conservativeRadius = radius + 0.001f;
        return distanceSquared <= conservativeRadius * conservativeRadius;
    }

    private static float SegmentPointAabbDistanceSquared(Vector3 point, Vector3 halfExtents)
    {
        float x = Mathf.Max(Mathf.Abs(point.x) - halfExtents.x, 0f);
        float y = Mathf.Max(Mathf.Abs(point.y) - halfExtents.y, 0f);
        float z = Mathf.Max(Mathf.Abs(point.z) - halfExtents.z, 0f);
        return x * x + y * y + z * z;
    }

    private void PreserveConcreteColliderVolumeDuringCollapse(
        float concreteHeight,
        float horizontalScale,
        float verticalScale,
        float collapseFall,
        bool collapsing)
    {
        if (driedConcreteCollider is not BoxCollider box ||
            concreteFillVisual == null || box.transform != concreteFillVisual)
            return;

        if (!driedColliderBaselineCaptured)
        {
            driedColliderBaseCenter = box.center;
            driedColliderBaseSize = box.size;
            driedColliderBaselineCaptured = true;
        }

        if (!collapsing)
        {
            box.center = driedColliderBaseCenter;
            box.size = driedColliderBaseSize;
            return;
        }

        float safeHorizontal = Mathf.Max(0.01f, horizontalScale);
        float safeVertical = Mathf.Max(0.01f, verticalScale);
        box.size = new Vector3(
            driedColliderBaseSize.x / safeHorizontal,
            driedColliderBaseSize.y / safeVertical,
            driedColliderBaseSize.z / safeHorizontal);
        box.center = new Vector3(
            driedColliderBaseCenter.x / safeHorizontal,
            (driedColliderBaseCenter.y * concreteHeight + collapseFall) /
            Mathf.Max(0.001f, concreteHeight * safeVertical),
            driedColliderBaseCenter.z / safeHorizontal);
    }

    public void PrepareFailedConcreteCollapse()
    {
        if (pitNavMeshObstacle != null)
        {
            pitNavMeshObstacle.enabled = true;
            pitNavMeshObstacle.carving = true;
        }
        EvacuateConcreteSurfaceOccupants();
    }

    public void EvacuateConcreteSurfaceOccupants()
    {
        if (concreteRenderer == null) return;
        Bounds slabBounds = concreteRenderer.bounds;
        Vector3 halfExtents = slabBounds.extents + new Vector3(0.15f, 1.5f, 0.15f);
        Collider[] overlaps = Physics.OverlapBox(
            slabBounds.center + Vector3.up * 0.75f,
            halfExtents,
            Quaternion.identity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        HashSet<Transform> movedRoots = new HashSet<Transform>();
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null) continue;
            PlayerHealth player = overlap.GetComponentInParent<PlayerHealth>();
            if (player != null && movedRoots.Add(player.transform) &&
                TryResolvePerimeterPosition(player.transform, player.GetComponent<CharacterController>(), slabBounds, out Vector3 playerPosition))
            {
                player.ApplyTechnicalTransportExit(playerPosition, player.transform.rotation);
                continue;
            }

            NavMeshAgent agent = overlap.GetComponentInParent<NavMeshAgent>();
            if (agent != null && movedRoots.Add(agent.transform) &&
                TryResolveNpcPerimeterPosition(agent, slabBounds, out Vector3 npcPosition))
            {
                agent.Warp(npcPosition);
            }
        }
    }

    private void UpdatePitNavMeshObstacle(
        BridgeConstructionStage stage,
        FoundationConcreteFailureState failureState,
        bool hasHardSurface)
    {
        if (pitNavMeshObstacle == null) return;
        bool failureSurfaceWalkable = failureState == FoundationConcreteFailureState.CriticalSequence ||
                                      failureState == FoundationConcreteFailureState.HardenedFailure;
        bool collapseOrOpen = failureState == FoundationConcreteFailureState.Collapsing ||
                              failureState == FoundationConcreteFailureState.AwaitingWheelbarrowExit;
        bool normalHardSurface = failureState is FoundationConcreteFailureState.None or FoundationConcreteFailureState.Ready &&
                                 hasHardSurface;
        bool enabled = collapseOrOpen || !(failureSurfaceWalkable || normalHardSurface);
        pitNavMeshObstacle.enabled = enabled;
        pitNavMeshObstacle.carving = enabled;
    }

    private void UpdateCrackVisuals(
        FoundationConcreteFailureState failureState,
        float progress,
        Vector3 thresholds,
        float horizontalScale,
        float verticalScale,
        float collapseFall)
    {
        if (failedConcreteCrackVisuals == null) return;
        EnsureCrackVisualBaselines();
        float[] values =
        {
            thresholds.x > 0f ? thresholds.x : 1f,
            thresholds.y > 0f ? thresholds.y : 34f,
            thresholds.z > 0f ? thresholds.z : 67f
        };
        bool failureVisible = failureState == FoundationConcreteFailureState.HardenedFailure ||
                              failureState == FoundationConcreteFailureState.Collapsing;
        for (int i = 0; i < failedConcreteCrackVisuals.Length; i++)
        {
            GameObject visual = failedConcreteCrackVisuals[i];
            if (visual == null) continue;
            visual.SetActive(failureVisible && progress >= values[Mathf.Min(i, values.Length - 1)]);
            if (crackBaseLocalPositions == null || i >= crackBaseLocalPositions.Length) continue;
            visual.transform.localPosition = crackBaseLocalPositions[i] + Vector3.down * collapseFall;
            visual.transform.localScale = Vector3.Scale(
                crackBaseLocalScales[i],
                new Vector3(horizontalScale, verticalScale, horizontalScale));
        }
    }

    private void EnsureCrackVisualBaselines()
    {
        if (crackBaseLocalPositions != null &&
            crackBaseLocalPositions.Length == failedConcreteCrackVisuals.Length)
            return;

        crackBaseLocalPositions = new Vector3[failedConcreteCrackVisuals.Length];
        crackBaseLocalScales = new Vector3[failedConcreteCrackVisuals.Length];
        for (int i = 0; i < failedConcreteCrackVisuals.Length; i++)
        {
            GameObject visual = failedConcreteCrackVisuals[i];
            if (visual == null) continue;
            crackBaseLocalPositions[i] = visual.transform.localPosition;
            crackBaseLocalScales[i] = visual.transform.localScale;
        }
    }

    private static bool TryResolvePerimeterPosition(
        Transform root,
        CharacterController controller,
        Bounds slabBounds,
        out Vector3 resolved)
    {
        Vector3[] directions = { Vector3.left, Vector3.right, Vector3.back, Vector3.forward };
        foreach (Vector3 direction in directions)
        {
            float extent = Mathf.Abs(direction.x) > 0.5f ? slabBounds.extents.x : slabBounds.extents.z;
            Vector3 sample = slabBounds.center + direction * (extent + 1.1f) + Vector3.up * 2f;
            if (!Physics.Raycast(sample, Vector3.down, out RaycastHit hit, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < 0.5f) continue;
            resolved = hit.point;
            if (controller == null) return true;
            float radius = controller.radius + 0.05f;
            float halfSegment = Mathf.Max(0f, controller.height * 0.5f - radius);
            float bottomOffset = controller.center.y - controller.height * 0.5f;
            resolved.y -= bottomOffset;
            Vector3 center = resolved + controller.center;
            Vector3 bottom = center + Vector3.down * halfSegment;
            Vector3 top = center + Vector3.up * halfSegment;
            Collider[] blocked = Physics.OverlapCapsule(bottom, top, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool free = true;
            foreach (Collider collider in blocked)
            {
                if (collider == null || collider == hit.collider ||
                    collider.transform == root || collider.transform.IsChildOf(root)) continue;
                free = false;
                break;
            }
            if (free) return true;
        }
        resolved = default;
        return false;
    }

    private static bool TryResolveNpcPerimeterPosition(NavMeshAgent agent, Bounds slabBounds, out Vector3 resolved)
    {
        Vector3[] directions = { Vector3.left, Vector3.right, Vector3.back, Vector3.forward };
        foreach (Vector3 direction in directions)
        {
            float extent = Mathf.Abs(direction.x) > 0.5f ? slabBounds.extents.x : slabBounds.extents.z;
            Vector3 sample = slabBounds.center + direction * (extent + 1.25f);
            if (NavMesh.SamplePosition(sample, out NavMeshHit hit, 2f, agent.areaMask))
            {
                resolved = hit.position;
                return true;
            }
        }
        resolved = default;
        return false;
    }

    private Material runtimeConcreteMaterial;

    private void OnTriggerEnter(Collider other)
    {
        TryReturnPile(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryReturnPile(other);
    }

    private void TryReturnPile(Collider other)
    {
        if (!TryResolveLoosePileCollider(other, out LooseSubstancePile pile))
        {
            return;
        }

        BridgeConstructionSite site = constructionSite != null ? constructionSite : GetComponentInParent<BridgeConstructionSite>();
        if (site == null)
        {
            return;
        }

        pile.TryReturnTo(site);
    }

    private static bool TryResolveLoosePileCollider(Collider candidate, out LooseSubstancePile pile)
    {
        pile = null;
        if (candidate == null || candidate.GetComponentInParent<PortableSubstanceContainer>() != null)
        {
            return false;
        }

        Rigidbody attachedBody = candidate.attachedRigidbody;
        pile = attachedBody != null
            ? attachedBody.GetComponent<LooseSubstancePile>()
            : candidate.GetComponent<LooseSubstancePile>();

        return pile != null && pile.OwnsCollider(candidate);
    }
}
