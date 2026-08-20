using UnityEngine;

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
    [SerializeField] private float concreteEmptyLocalY = -1.2f;
    [SerializeField] private float concreteFullLocalY;

    private Material runtimeMaterial;

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
        float remainingDryingTime, float dryingDuration)
    {
        float fill = requiredLoads > 0 ? Mathf.Clamp01((float)pouredLoads / requiredLoads) : 0f;
        bool visible = fill > 0f;
        if (concreteFillVisual != null)
        {
            concreteFillVisual.gameObject.SetActive(visible);
            Vector3 position = concreteFillVisual.localPosition;
            position.y = Mathf.Lerp(concreteEmptyLocalY, concreteFullLocalY, fill);
            concreteFillVisual.localPosition = position;
        }

        bool dry = stage == BridgeConstructionStage.ReadyForMount || stage == BridgeConstructionStage.Hammering ||
                   stage == BridgeConstructionStage.Complete;
        if (driedConcreteCollider != null) driedConcreteCollider.enabled = dry && visible;
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
