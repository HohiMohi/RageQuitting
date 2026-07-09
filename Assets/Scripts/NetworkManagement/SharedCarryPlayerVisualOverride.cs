using Unity.Netcode;
using UnityEngine;

public class SharedCarryPlayerVisualOverride : MonoBehaviour
{
    private const float DefaultVisualFollowSpeed = 30f;

    private Transform playerBodyVisual;
    private Transform carriedObjectTransform;
    private Vector3 attachLocalPoint;
    private Vector3 bodyAnchorLocalOffset;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private bool isOverriding;

    private void Awake()
    {
        if (TryGetComponent(out PlayerNetworkSetup playerNetworkSetup) && playerNetworkSetup.PlayerBodyVisual != null)
        {
            playerBodyVisual = playerNetworkSetup.PlayerBodyVisual.transform;
            originalLocalPosition = playerBodyVisual.localPosition;
            originalLocalRotation = playerBodyVisual.localRotation;
        }
    }

    public void StartOverride(ulong carriedObjectNetworkId, Vector3 newAttachLocalPoint, Vector3 newBodyAnchorLocalOffset)
    {
        if (TryGetComponent(out NetworkObject playerNetworkObject) && playerNetworkObject.IsOwner)
        {
            return;
        }

        if (playerBodyVisual == null)
        {
            Awake();
        }

        if (playerBodyVisual == null || NetworkManager.Singleton == null)
        {
            return;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(carriedObjectNetworkId, out NetworkObject carriedObjectNetworkObject))
        {
            return;
        }

        carriedObjectTransform = carriedObjectNetworkObject.transform;
        attachLocalPoint = newAttachLocalPoint;
        bodyAnchorLocalOffset = newBodyAnchorLocalOffset;
        isOverriding = true;
    }

    public void StopOverride()
    {
        if (playerBodyVisual != null)
        {
            playerBodyVisual.localPosition = originalLocalPosition;
            playerBodyVisual.localRotation = originalLocalRotation;
        }

        carriedObjectTransform = null;
        isOverriding = false;
    }

    private void LateUpdate()
    {
        if (!isOverriding || playerBodyVisual == null || carriedObjectTransform == null)
        {
            return;
        }

        Quaternion targetRotation = transform.rotation;
        Vector3 targetAnchorPosition = carriedObjectTransform.TransformPoint(attachLocalPoint);
        Vector3 targetRootPosition = targetAnchorPosition - targetRotation * bodyAnchorLocalOffset;
        targetRootPosition.y = transform.position.y;
        Vector3 targetVisualPosition = targetRootPosition + targetRotation * originalLocalPosition;

        playerBodyVisual.SetPositionAndRotation(
            Vector3.Lerp(playerBodyVisual.position, targetVisualPosition, DefaultVisualFollowSpeed * Time.deltaTime),
            targetRotation * originalLocalRotation);
    }
}
