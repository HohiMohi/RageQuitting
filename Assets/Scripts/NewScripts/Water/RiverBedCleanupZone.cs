using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RiverBedCleanupZone : MonoBehaviour
{
    private readonly HashSet<int> removingResources = new HashSet<int>();

    private void Reset()
    {
        Collider zone = GetComponent<Collider>();
        zone.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other) => TryRemoveResource(other);
    private void OnTriggerStay(Collider other) => TryRemoveResource(other);

    private void TryRemoveResource(Collider other)
    {
        if (NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsListening
            && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        EquippableWorldPhysics equippablePhysics = other != null
            ? other.GetComponentInParent<EquippableWorldPhysics>()
            : null;
        if (equippablePhysics != null)
        {
            equippablePhysics.ReturnToRespawnPoint();
            return;
        }

        PortableSubstanceContainer container = other != null
            ? other.GetComponentInParent<PortableSubstanceContainer>()
            : null;
        if (container != null)
        {
            container.ReturnToRespawnPoint(2f);
            return;
        }

        LooseSubstancePile loosePile = other != null ? other.GetComponentInParent<LooseSubstancePile>() : null;
        if (loosePile != null)
        {
            loosePile.RemoveUnitsFromWorld(loosePile.CurrentUnits);
            return;
        }

        if (GameplayManager.Instance != null
            && !GameplayManager.Instance.EnableRiverBedResourceRemoval)
        {
            return;
        }

        BaseResourceNew resource = other != null
            ? other.GetComponentInParent<BaseResourceNew>()
            : null;
        if (resource == null || !removingResources.Add(resource.GetInstanceID()))
        {
            return;
        }

        resource.RemoveFromWorld(EnvironmentalRemovalReason.RiverBed);
    }
}
