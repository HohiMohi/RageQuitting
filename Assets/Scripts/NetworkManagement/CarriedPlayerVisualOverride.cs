using Unity.Netcode;
using UnityEngine;

public class CarriedPlayerVisualOverride : MonoBehaviour
{
    private Transform playerBodyVisual;
    private Transform carrierTransform;
    private Vector3 carriedPlayerLocalOffset;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private bool isOverriding;

    private void Awake()
    {
        CachePlayerBodyVisual();
    }

    public void StartOverride(ulong carrierNetworkObjectId, Vector3 newCarriedPlayerLocalOffset)
    {
        CachePlayerBodyVisual();
        if (playerBodyVisual == null || NetworkManager.Singleton == null)
        {
            return;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(carrierNetworkObjectId, out NetworkObject carrierNetworkObject))
        {
            return;
        }

        carrierTransform = carrierNetworkObject.transform;
        carriedPlayerLocalOffset = newCarriedPlayerLocalOffset;
        isOverriding = true;
    }

    public void StopOverride()
    {
        if (playerBodyVisual != null)
        {
            playerBodyVisual.localPosition = originalLocalPosition;
            playerBodyVisual.localRotation = originalLocalRotation;
        }

        carrierTransform = null;
        isOverriding = false;
    }

    private void LateUpdate()
    {
        if (!isOverriding || playerBodyVisual == null || carrierTransform == null)
        {
            return;
        }

        Transform anchor = carrierTransform;
        if (carrierTransform.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            anchor = playerInteraction.GetCarriedPlayerAnchor();
        }

        Quaternion yawRotation = Quaternion.Euler(0f, carrierTransform.eulerAngles.y, 0f);
        Vector3 targetPosition = anchor.position + yawRotation * carriedPlayerLocalOffset;
        playerBodyVisual.SetPositionAndRotation(targetPosition, yawRotation * originalLocalRotation);
    }

    private void CachePlayerBodyVisual()
    {
        if (playerBodyVisual != null)
        {
            return;
        }

        if (TryGetComponent(out PlayerNetworkSetup playerNetworkSetup) && playerNetworkSetup.PlayerBodyVisual != null)
        {
            playerBodyVisual = playerNetworkSetup.PlayerBodyVisual.transform;
            originalLocalPosition = playerBodyVisual.localPosition;
            originalLocalRotation = playerBodyVisual.localRotation;
        }
    }
}
