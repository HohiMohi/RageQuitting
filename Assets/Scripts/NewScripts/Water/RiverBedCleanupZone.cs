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
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        BaseResourceNew resource = other != null ? other.GetComponentInParent<BaseResourceNew>() : null;
        if (resource == null || !removingResources.Add(resource.GetInstanceID()))
        {
            return;
        }

        resource.RemoveFromWorld(EnvironmentalRemovalReason.RiverBed);
    }
}
