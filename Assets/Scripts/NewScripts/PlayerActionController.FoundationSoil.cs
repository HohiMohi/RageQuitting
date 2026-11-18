using System;
using Unity.Netcode;
using UnityEngine;

public partial class PlayerActionController
{
    private double nextServerFoundationSoilImpactTime;

    // Samples the camera ray once. A Digging site on the exact nearest solid hit consumes
    // shovel input even when the hit is a side, a later layer, or outside the action box.
    private bool TryHandleFoundationSoilImpact(
        EquippableItemSO item, out bool accepted, out BridgeConstructionSite site, out RaycastHit soilHit)
    {
        accepted = false;
        site = null;
        soilHit = default;
        if (_playerInteraction == null) return false;

        bool hasAimRay = _playerInteraction.TryGetAimRay(out Ray aimRay);
        bool hasHit = hasAimRay && TryFindNearestSolidHit(aimRay, _playerInteraction.InteractionDistance, out soilHit);
        if (hasHit)
        {
            FoundationLooseSoilField.TryGetSiteForSupportCollider(soilHit.collider, out site);
        }

        if (!IsOptedInDiggingSite(site))
        {
            site = null;
            if (!TryGetAnyOptedInFoundationSite(_playerInteraction.CurrentTarget, out BridgeConstructionSite staleSite) ||
                !IsOptedInDiggingSite(staleSite)) return false;
            site = staleSite;
        }

        if (item == null || item.itemType != EquippableItemType.Shovel ||
            site.CurrentStage != BridgeConstructionStage.Digging ||
            site.DiggingSubstage != FoundationDiggingSubstage.Loosening || !hasHit ||
            !IsFoundationSoilHitValid(site, item.actionRange, 0f, soilHit)) return true;

        accepted = TryApplyFoundationSoilImpact(site, item, aimRay);
        return true;
    }

    private static bool IsOptedInDiggingSite(BridgeConstructionSite site) =>
        site != null && site.CurrentStage == BridgeConstructionStage.Digging &&
        site.ExcavationVolume != null && site.ExcavationVolume.UsesLooseSoilHeightField;

    private static bool TryGetFoundationSoilSite(MonoBehaviour target, out BridgeConstructionSite site)
    {
        site = target as BridgeConstructionSite;
        if (site == null && target != null) site = target.GetComponentInParent<BridgeConstructionSite>();
        return site != null && site.CurrentStage == BridgeConstructionStage.Digging &&
            site.DiggingSubstage == FoundationDiggingSubstage.Loosening &&
            site.ExcavationVolume != null && site.ExcavationVolume.UsesLooseSoilHeightField;
    }

    private static bool TryGetAnyOptedInFoundationSite(MonoBehaviour target, out BridgeConstructionSite site)
    {
        site = target as BridgeConstructionSite;
        if (site == null && target != null) site = target.GetComponentInParent<BridgeConstructionSite>();
        return site != null && site.ExcavationVolume != null && site.ExcavationVolume.UsesLooseSoilHeightField;
    }

    private bool TryApplyFoundationSoilImpact(BridgeConstructionSite site, EquippableItemSO item, Ray aimRay)
    {
        if (site == null || item == null || !HasReciprocalLooseSoilConfiguration(site) ||
            GameplayManager.Instance == null || !site.TryGetComponent(out BridgeComponent bridgeComponent)) return false;

        NetworkManager manager = NetworkManager;
        if (manager != null && manager.IsListening && !IsSpawned) return false;
        if (manager == null || !manager.IsListening)
        {
            return GameplayManager.Instance.TryApplyFoundationSoilWorkOffline(
                bridgeComponent, item.itemType, item.ConstructionWorkPower);
        }

        if (IsServer)
        {
            return TryApplyFoundationSoilImpactServer(
                OwnerClientId, bridgeComponent.ComponentID, aimRay.origin, aimRay.direction, item.itemType);
        }

        RequestFoundationSoilImpactServerRpc(bridgeComponent.ComponentID, aimRay.origin, aimRay.direction);
        return true;
    }

    private bool IsFoundationSoilHitValid(
        BridgeConstructionSite site, float actionRange, float tolerance, RaycastHit hit)
    {
        if (site == null || actionTransformHolder == null || hit.collider == null ||
            site.CurrentStage != BridgeConstructionStage.Digging ||
            site.DiggingSubstage != FoundationDiggingSubstage.Loosening ||
            !HasReciprocalLooseSoilConfiguration(site) ||
            !site.ExcavationVolume.TryGetSoilSupportCollider(out Collider support) || hit.collider != support ||
            Vector3.Dot(hit.normal, support.transform.up) < 0.999f) return false;

        Vector3 localPoint = Quaternion.Inverse(actionTransformHolder.rotation) *
            (hit.point - actionTransformHolder.position);
        float halfExtent = Mathf.Max(0f, actionRange) + Mathf.Max(0f, tolerance);
        return Mathf.Abs(localPoint.x) <= halfExtent && Mathf.Abs(localPoint.y) <= halfExtent &&
            Mathf.Abs(localPoint.z) <= halfExtent;
    }

    private static bool HasReciprocalLooseSoilConfiguration(BridgeConstructionSite site)
    {
        FoundationExcavationVolume volume = site != null ? site.ExcavationVolume : null;
        FoundationLooseSoilField field = volume != null ? volume.LooseSoilField : null;
        return volume != null && volume.UsesLooseSoilHeightField && volume.ConstructionSite == site &&
            site.ExcavationVolume == volume && volume.HasValidLooseSoilConfiguration && field != null &&
            field.transform.parent == volume.transform && field.HasHeightField;
    }

    private bool CanResolveFoundationSoilAim(
        BridgeConstructionSite site, float actionRange, float tolerance, out RaycastHit soilHit)
    {
        soilHit = default;
        if (site == null || _playerInteraction == null || actionTransformHolder == null ||
            !_playerInteraction.TryGetAimRay(out Ray aimRay) ||
            !TryFindNearestSolidHit(aimRay, _playerInteraction.InteractionDistance, out soilHit)) return false;
        return IsFoundationSoilHitValid(site, actionRange, tolerance, soilHit);
    }

    private bool TryFindNearestSolidHit(Ray ray, float maximumDistance, out RaycastHit nearestHit)
    {
        nearestHit = default;
        if (!IsFinite(ray.origin) || !IsFinite(ray.direction) || !IsFinite(maximumDistance) || maximumDistance <= 0f)
            return false;
        float directionLength = ray.direction.magnitude;
        if (directionLength < 0.95f || directionLength > 1.05f) return false;
        ray.direction /= directionLength;

        RaycastHit[] hits = Physics.RaycastAll(ray, maximumDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        bool found = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.transform.root == transform.root || hit.distance >= closestDistance) continue;
            closestDistance = hit.distance;
            nearestHit = hit;
            found = true;
        }
        return found;
    }

    private void SpawnImpactEffectAt(Vector3 point, Vector3 normal, ActionImpactSurfaceType surfaceType)
    {
        if (_impactEffectSpawner == null) return;
        float feedbackStrength = actionItem != null && actionItem.actionProfile != null
            ? actionItem.actionProfile.impactFeedbackStrength
            : 1f;
        Vector3 safeNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : -transform.forward;
        _impactEffectSpawner.SpawnImpact(point, safeNormal, surfaceType, feedbackStrength);
    }

    [ServerRpc]
    private void RequestFoundationSoilImpactServerRpc(
        int componentID, Vector3 aimOrigin, Vector3 aimDirection, ServerRpcParams rpcParams = default)
    {
        TryApplyFoundationSoilImpactServer(
            rpcParams.Receive.SenderClientId, componentID, aimOrigin, aimDirection, EquippableItemType.Shovel);
    }

    private bool TryApplyFoundationSoilImpactServer(
        ulong senderClientId, int componentID, Vector3 aimOrigin, Vector3 aimDirection, EquippableItemType requestedTool)
    {
        NetworkManager manager = NetworkManager;
        if (manager == null || !manager.IsServer || !IsSpawned || NetworkObject == null || !NetworkObject.IsSpawned ||
            senderClientId != OwnerClientId || IsWorldActionBlocked(senderClientId) || IsDowned() ||
            _concreteTrap != null && _concreteTrap.IsTrapped || serverCrowbarNail != null ||
            _inventory == null || !IsFinite(aimOrigin) || !IsFinite(aimDirection) || GameplayManager.Instance == null) return false;

        float directionLength = aimDirection.magnitude;
        if (directionLength < 0.95f || directionLength > 1.05f ||
            !IsTrustedMountingNailAimOrigin(aimOrigin, aimDirection / directionLength)) return false;
        Ray aimRay = new Ray(aimOrigin, aimDirection / directionLength);
        EquippableItemSO serverItem = _inventory.GetSelectedItemForServerValidation();
        if (requestedTool != EquippableItemType.Shovel || serverItem == null ||
            serverItem.itemType != EquippableItemType.Shovel ||
            !GameplayManager.Instance.TryGetConstructionSite(componentID, out BridgeConstructionSite site) ||
            !IsOptedInDiggingSite(site) ||
            !TryFindNearestSolidHit(aimRay, _playerInteraction != null ? _playerInteraction.InteractionDistance : 0f,
                out RaycastHit serverHit) || !IsFoundationSoilHitValid(site, serverItem.actionRange, serverActionRangeTolerance, serverHit))
            return false;
        if (!site.TryGetComponent(out BridgeComponent bridgeComponent) || bridgeComponent.ComponentID != componentID) return false;

        double serverTime = manager.ServerTime.Time;
        if (serverTime < nextServerFoundationSoilImpactTime ||
            !GameplayManager.Instance.TryApplyValidatedFoundationSoilWork(componentID, senderClientId)) return false;
        nextServerFoundationSoilImpactTime = serverTime + GetMinimumCombatImpactInterval(serverItem);
        return true;
    }
}
