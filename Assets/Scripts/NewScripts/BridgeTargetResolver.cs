using UnityEngine;

public static class BridgeTargetResolver
{
    internal static bool TryGetClearingSiteFallback(
        Collider collider,
        out BridgeConstructionSite constructionSite)
    {
        constructionSite = collider != null
            ? collider.GetComponentInParent<BridgeConstructionSite>()
            : null;

        return collider != null
            && collider.isTrigger
            && constructionSite != null
            && constructionSite.CurrentStage == BridgeConstructionStage.Clearing
            && constructionSite.IsConstructionInteractionCollider(collider);
    }

    public static MonoBehaviour Resolve(Collider collider)
    {
        if (collider == null)
        {
            return null;
        }

        MonoBehaviour workPoint = ResolveWorkPoint(collider.transform);
        if (workPoint != null)
        {
            return workPoint;
        }

        BridgeConstructionSite constructionSite = collider.GetComponentInParent<BridgeConstructionSite>();
        if (constructionSite != null && constructionSite.IsConstructionInteractionCollider(collider))
        {
            return constructionSite.CurrentStage == BridgeConstructionStage.Clearing ||
                   constructionSite.CurrentStage == BridgeConstructionStage.Digging
                ? constructionSite
                : constructionSite.BridgeComponent;
        }

        IInteractableNew interactable = collider.GetComponent<IInteractableNew>();
        interactable ??= collider.GetComponentInParent<IInteractableNew>();
        if (interactable is MonoBehaviour interactableBehaviour)
        {
            return interactableBehaviour;
        }

        IDamageable damageable = collider.GetComponent<IDamageable>();
        damageable ??= collider.GetComponentInParent<IDamageable>();
        return damageable as MonoBehaviour;
    }

    public static bool TryGetConstructionWorkTarget(
        MonoBehaviour target,
        out BridgeConstructionSite site,
        out int workPointId)
    {
        site = target != null ? target.GetComponentInParent<BridgeConstructionSite>() : null;
        workPointId = -1;

        switch (target)
        {
            case FoundationFailedConcreteTarget failedConcreteTarget:
                site = failedConcreteTarget.ConstructionSite;
                workPointId = FoundationFailedConcreteTarget.WorkPointId;
                break;
            case BridgeAbutmentWorkPoint point:
                workPointId = point.RequestId;
                break;
            case BridgeGirderWorkPoint point:
                workPointId = point.RequestId;
                break;
            case BridgeCrossBeamWorkPoint point:
                workPointId = (int)point.WorkPointId;
                break;
            case BridgeDiagonalBracingWorkPoint point:
                workPointId = (int)point.WorkPointId;
                break;
            case BridgeDeckPanelWorkPoint point:
                workPointId = (int)point.WorkPointId;
                break;
        }

        return site != null;
    }

    public static IDamageable ResolveDamageable(MonoBehaviour target)
    {
        if (target == null)
        {
            return null;
        }

        if (ResolveWorkPoint(target.transform) is IDamageable workPoint)
        {
            return workPoint;
        }

        if (target is BridgeConstructionSite constructionSite)
        {
            if (constructionSite.CurrentStage == BridgeConstructionStage.Digging ||
                constructionSite.CurrentStage == BridgeConstructionStage.Clearing)
            {
                return constructionSite;
            }

            return constructionSite.BridgeComponent;
        }

        return target as IDamageable ??
               target.GetComponent<IDamageable>() ??
               target.GetComponentInParent<IDamageable>();
    }

    private static MonoBehaviour ResolveWorkPoint(Transform target)
    {
        if (target == null)
        {
            return null;
        }

        MonoBehaviour workPoint = target.GetComponentInParent<FoundationFailedConcreteTarget>();
        workPoint ??= target.GetComponentInParent<BridgeDeckPanelWorkPoint>();
        workPoint ??= target.GetComponentInParent<BridgeDiagonalBracingWorkPoint>();
        workPoint ??= target.GetComponentInParent<BridgeCrossBeamWorkPoint>();
        workPoint ??= target.GetComponentInParent<BridgeGirderWorkPoint>();
        workPoint ??= target.GetComponentInParent<BridgeAbutmentWorkPoint>();
        return workPoint;
    }
}
