using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerTransportCollisionController : MonoBehaviour
{
    private readonly Dictionary<Collider, LayerMask> previousExcludeLayers = new Dictionary<Collider, LayerMask>();
    private CharacterController characterController;
    private bool previousDetectCollisions;
    private bool previousOverlapRecovery;
    private Object activeTransport;

    public bool IsTransportCollisionSuppressed => HasTransportReference;
    private bool HasTransportReference => !ReferenceEquals(activeTransport, null);

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    public bool BeginTransport(Object transport)
    {
        if (transport == null) return false;
        if (HasTransportReference && activeTransport == null)
            RestorePreviousState();
        if (HasTransportReference && !ReferenceEquals(activeTransport, transport)) return false;
        if (!HasTransportReference)
        {
            activeTransport = transport;
            previousExcludeLayers.Clear();
            if (characterController != null)
            {
                previousDetectCollisions = characterController.detectCollisions;
                previousOverlapRecovery = characterController.enableOverlapRecovery;
            }
        }

        EnsureSuppressed();
        return true;
    }

    public void EnsureSuppressed()
    {
        if (!HasTransportReference) return;
        if (activeTransport == null)
        {
            RestorePreviousState();
            return;
        }
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || collider.isTrigger) continue;
            if (!previousExcludeLayers.ContainsKey(collider))
                previousExcludeLayers.Add(collider, collider.excludeLayers);
            collider.excludeLayers = Physics.AllLayers;
        }

        if (characterController != null)
        {
            characterController.detectCollisions = false;
            characterController.enableOverlapRecovery = false;
        }
    }

    public void EndTransport(Object transport)
    {
        if (!HasTransportReference) return;
        if (transport != null && !ReferenceEquals(activeTransport, transport)) return;
        RestorePreviousState();
    }

    private void LateUpdate()
    {
        if (HasTransportReference && activeTransport == null)
            RestorePreviousState();
    }

    private void RestorePreviousState()
    {
        foreach (KeyValuePair<Collider, LayerMask> pair in previousExcludeLayers)
            if (pair.Key != null) pair.Key.excludeLayers = pair.Value;
        previousExcludeLayers.Clear();

        if (characterController != null)
        {
            characterController.detectCollisions = previousDetectCollisions;
            characterController.enableOverlapRecovery = previousOverlapRecovery;
        }
        activeTransport = null;
    }

    private void OnDestroy()
    {
        RestorePreviousState();
    }
}
