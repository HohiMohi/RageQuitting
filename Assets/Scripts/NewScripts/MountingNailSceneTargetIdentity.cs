using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>Author-baked stable identity for a physical root in a scene without a NetworkObject.</summary>
[DisallowMultipleComponent]
public sealed class MountingNailSceneTargetIdentity : MonoBehaviour
{
    [SerializeField, HideInInspector] private string stableTargetId;
    private Rigidbody cachedBody;

    public string StableTargetId => stableTargetId;
    public Rigidbody PhysicalBody
    {
        get
        {
            if (cachedBody == null) TryGetComponent(out cachedBody);
            return cachedBody;
        }
    }
    public bool HasValidIdentity => !string.IsNullOrWhiteSpace(stableTargetId);

    public MountingNailTargetReference CreateSceneReference()
    {
        return HasValidIdentity
            ? new MountingNailTargetReference { StableTargetId = new FixedString128Bytes(stableTargetId) }
            : default;
    }

    public bool Matches(MountingNailTargetReference reference) => !reference.IsNetworkObject
        && reference.IsValid && HasValidIdentity
        && string.Equals(stableTargetId, reference.StableTargetId.ToString(), System.StringComparison.Ordinal);

    internal void SetBakedId(string value) => stableTargetId = value;

    private void OnDestroy()
    {
        if (!Application.isPlaying || !HasValidIdentity || GetComponentInParent<NetworkObject>() != null) return;
        foreach (MountingNailSceneState state in FindObjectsByType<MountingNailSceneState>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (state != null && state.gameObject.scene == gameObject.scene)
                state.HandleSceneTargetDestroyed(this);
    }

    public static bool TryBuildPhysicalRootPath(Transform networkRoot, Transform physicalRoot,
        out FixedString64Bytes path)
    {
        path = default;
        if (networkRoot == null || physicalRoot == null
            || physicalRoot != networkRoot && !physicalRoot.IsChildOf(networkRoot)) return false;
        List<int> indices = new();
        Transform cursor = physicalRoot;
        while (cursor != networkRoot)
        {
            indices.Add(cursor.GetSiblingIndex());
            cursor = cursor.parent;
            if (cursor == null) return false;
        }
        StringBuilder builder = new();
        for (int i = indices.Count - 1; i >= 0; i--)
        {
            if (builder.Length > 0) builder.Append('/');
            builder.Append(indices[i]);
        }
        if (builder.Length > FixedString64Bytes.UTF8MaxLengthInBytes) return false;
        path = new FixedString64Bytes(builder.ToString());
        return true;
    }

    public static bool TryResolvePhysicalRoot(NetworkObject networkObject, MountingNailTargetReference reference,
        out Transform physicalRoot)
    {
        physicalRoot = null;
        if (networkObject == null || !networkObject.IsSpawned || !reference.IsValid || !reference.IsNetworkObject
            || networkObject.NetworkObjectId != reference.NetworkObjectId) return false;
        if (reference.PhysicalRootPath.Length == 0)
        {
            physicalRoot = networkObject.transform;
            return true;
        }
        string[] segments = reference.PhysicalRootPath.ToString().Split('/');
        Transform cursor = networkObject.transform;
        foreach (string segment in segments)
        {
            if (!int.TryParse(segment, out int siblingIndex) || siblingIndex < 0 || siblingIndex >= cursor.childCount)
                return false;
            cursor = cursor.GetChild(siblingIndex);
        }
        if (!TryBuildPhysicalRootPath(networkObject.transform, cursor, out FixedString64Bytes currentPath)
            || !currentPath.Equals(reference.PhysicalRootPath)) return false;
        physicalRoot = cursor;
        return true;
    }
}
