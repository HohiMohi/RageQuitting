using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

public class FoundationExcavationVolume : MonoBehaviour
{
    [SerializeField] private Transform soilSurface;
    [SerializeField] private BridgeConstructionSite constructionSite;
    [SerializeField] private Renderer soilRenderer;
    [SerializeField] private Material compactSoilMaterial;
    [SerializeField] private Material loosenedSoilMaterial;
    [SerializeField] private float surfaceStartLocalY;
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

    public Transform FailedWheelbarrowPose => failedWheelbarrowPose;
    public FoundationFailedConcreteTarget FailedConcreteTarget => failedConcreteTarget;
    public NavMeshObstacle PitNavMeshObstacle => pitNavMeshObstacle;
    public GameObject NavMeshBakeProxy => navMeshBakeProxy;
    public Collider DriedConcreteCollider => driedConcreteCollider;
    public IReadOnlyList<GameObject> FailedConcreteCrackVisuals => failedConcreteCrackVisuals;
    public bool ContainsRecoveryWheelbarrow(Vector3 worldPosition)
    {
        if (recoveryVolume == null) return false;
        Vector3 local = recoveryVolume.transform.InverseTransformPoint(worldPosition) - recoveryVolume.center;
        Vector3 half = recoveryVolume.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    public void ApplyDiggingState(BridgeConstructionStage stage, FoundationDiggingSubstage substage,
        float looseningProgress, int removedSoilUnits, BridgeConstructionWorkflowSO workflow)
    {
        if (workflow == null)
        {
            return;
        }

        int totalUnits = workflow.DiggingCycleCount * workflow.SoilUnitsPerCycle;
        float normalizedDepth = totalUnits > 0 ? Mathf.Clamp01((float)removedSoilUnits / totalUnits) : 0f;
        if (soilSurface != null)
        {
            Vector3 localPosition = soilSurface.localPosition;
            localPosition.y = surfaceStartLocalY - workflow.FinalExcavationDepth * normalizedDepth;
            soilSurface.localPosition = localPosition;
        }

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

    public void ApplyConcreteState(BridgeConstructionStage stage, int pouredLoads, int requiredLoads,
        float remainingDryingTime, float dryingDuration,
        FoundationConcreteFailureState failureState = FoundationConcreteFailureState.None,
        float failedBreakProgress = 0f,
        Vector3 failedCrackThresholds = default,
        float collapseProgress = 0f)
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
        if (exitRampRenderer != null) exitRampRenderer.enabled = awaitingRecovery || !fullyPoured;
        if (exitRampCollider != null) exitRampCollider.enabled = awaitingRecovery || !(fullyPoured && dry);
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
