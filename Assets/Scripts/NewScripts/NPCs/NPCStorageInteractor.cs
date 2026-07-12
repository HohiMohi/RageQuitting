using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NPCBrain))]
[RequireComponent(typeof(NPCCarrier))]
public class NPCStorageInteractor : NetworkBehaviour
{
    private NPCBrain brain;
    private NPCCarrier carrier;

    private void Awake()
    {
        brain = GetComponent<NPCBrain>();
        carrier = GetComponent<NPCCarrier>();
    }

    public bool CanWithdraw(BaseStorageNew storage, BaseResourceSO resourceSO, int amount = 1)
    {
        return storage != null
            && resourceSO != null
            && amount > 0
            && carrier != null
            && carrier.CanCarryObject
            && HasAuthorityToWithdraw()
            && storage.CanWithdrawBaseResource(resourceSO, amount);
    }

    public bool TryWithdrawAndCarry(BaseStorageNew storage, BaseResourceSO resourceSO, out GameObject withdrawnObject, int amount = 1)
    {
        withdrawnObject = null;
        if (!CanWithdraw(storage, resourceSO, amount))
        {
            return false;
        }

        return storage.TryWithdrawBaseResource(resourceSO, carrier, out withdrawnObject, amount)
            && withdrawnObject != null
            && carrier.CarriedObject == withdrawnObject;
    }

    public bool HasStorageTargetInRange(BaseStorageNew storage)
    {
        if (storage == null || brain == null)
        {
            return false;
        }

        return Vector3.Distance(transform.position, storage.transform.position) <= brain.InteractionDistance;
    }

    private bool HasAuthorityToWithdraw()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !IsSpawned || IsServer;
    }
}
