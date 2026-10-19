using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public struct MountingNailSceneTargetState : INetworkSerializable, IEquatable<MountingNailSceneTargetState>
{
    public MountingNailTargetReference Target;
    public Vector3 Position;
    public Quaternion Rotation;
    public bool Locked;
    public bool Tombstone;
    public int FollowCount;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Target);
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref Rotation);
        serializer.SerializeValue(ref Locked);
        serializer.SerializeValue(ref Tombstone);
        serializer.SerializeValue(ref FollowCount);
    }

    public bool Equals(MountingNailSceneTargetState other) => Target.Equals(other.Target)
        && Position == other.Position && Rotation == other.Rotation && Locked == other.Locked
        && Tombstone == other.Tombstone && FollowCount == other.FollowCount;
}

/// <summary>Replicates nail target state for this scene and this NetworkManager instance only.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class MountingNailSceneState : NetworkBehaviour
{
    private static readonly HashSet<MountingNailSceneState> ActiveStates = new();
    private NetworkList<MountingNailSceneTargetState> targetStates;
    private readonly Dictionary<MountingNailTargetReference,
        (Rigidbody body, bool kinematic, bool gravity, RigidbodyConstraints constraints)> savedBodyFlags = new();
    private readonly Dictionary<MountingNailTargetReference, Transform> serverLockedRoots = new();
    private readonly Dictionary<MountingNailTargetReference, (Transform root, int count)> serverFollowedRoots = new();
    private readonly Dictionary<MountingNailTargetReference, (NetworkObject networkObject, Transform physicalRoot)> resolvedNetworkTargets = new();
    private readonly Dictionary<Transform, MountingNailTargetReference> knownNetworkReferences = new();
    private readonly Dictionary<NetworkObject, ulong> indexedNetworkObjectIds = new();
    private readonly HashSet<MountingNailTargetReference> unresolvedTargetStates = new();
    private readonly Dictionary<string, MountingNailSceneTargetIdentity> resolvedSceneTargets = new();
    private readonly HashSet<string> duplicateSceneTargetIds = new();
    private readonly List<MountingNailSceneTargetIdentity> sceneTargets = new();
    private bool subscribed;
    private bool explicitLocalTargetScope;
    private bool sceneIndexBuilt;
    private MountingNailConnectionRegistry connectionRegistry;

    private void Awake()
    {
        if (targetStates == null) targetStates = new NetworkList<MountingNailSceneTargetState>();
        connectionRegistry ??= new MountingNailConnectionRegistry(this);
    }

    public NetworkManager ScopedNetworkManager => NetworkManager;
    public int TargetStateCount => IsSpawned && targetStates != null ? targetStates.Count : 0;

    public override void OnNetworkSpawn()
    {
        if (targetStates == null) targetStates = new NetworkList<MountingNailSceneTargetState>();
        connectionRegistry ??= new MountingNailConnectionRegistry(this);
        targetStates.OnListChanged += TargetStatesChanged;
        subscribed = true;
        ActiveStates.Add(this);
        RebuildSceneTargetIndex();
        if (IsServer)
        {
            // A despawned and respawned scene service starts with no live graph,
            // while scene-lifetime deletion tombstones remain durable.
            for (int i = targetStates.Count - 1; i >= 0; i--)
                if (!targetStates[i].Tombstone) targetStates.RemoveAt(i);
            serverLockedRoots.Clear();
            serverFollowedRoots.Clear();
        }
        ApplyAllTargetStates();
    }

    public override void OnNetworkDespawn()
    {
        if (subscribed)
        {
            targetStates.OnListChanged -= TargetStatesChanged;
        }
        subscribed = false;
        ActiveStates.Remove(this);
        RestoreAllBodies();
        serverLockedRoots.Clear();
        serverFollowedRoots.Clear();
        connectionRegistry?.Clear();
        resolvedSceneTargets.Clear();
        duplicateSceneTargetIds.Clear();
        resolvedNetworkTargets.Clear();
        knownNetworkReferences.Clear();
        indexedNetworkObjectIds.Clear();
        sceneIndexBuilt = false;
        explicitLocalTargetScope = false;
    }

    public override void OnDestroy()
    {
        if (subscribed && targetStates != null) targetStates.OnListChanged -= TargetStatesChanged;
        subscribed = false;
        ActiveStates.Remove(this);
        RestoreAllBodies();
        serverLockedRoots.Clear();
        serverFollowedRoots.Clear();
        connectionRegistry?.Clear();
        base.OnDestroy();
    }

    private void FixedUpdate()
    {
        connectionRegistry?.Tick();
        if (IsSpawned && IsServer) PruneExpiredNetworkTargetStates();
        ApplyUnresolvedTargetStates();
        EnforceConnectedReplicaBodyModes();
        if (IsSpawned && IsServer) PublishChangedConnectedTargetPoses();
    }

    private void EnforceConnectedReplicaBodyModes()
    {
        if (IsServer || savedBodyFlags.Count == 0) return;
        foreach (var saved in savedBodyFlags.Values)
        {
            Rigidbody body = saved.body;
            if (body == null) continue;
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.None;
        }
    }

    public static bool IsTargetConnected(Transform target)
    {
        if (target == null) return false;
        Transform physicalRoot = ResolvePickupPhysicalRoot(target);
        NetworkObject networkObject = physicalRoot.GetComponentInParent<NetworkObject>();
        NetworkManager targetManager = networkObject != null ? networkObject.NetworkManager : null;
        foreach (MountingNailSceneState state in ActiveStates)
        {
            if (state == null || !state.IsSpawned || state.gameObject.scene != physicalRoot.gameObject.scene
                || targetManager != null && state.ScopedNetworkManager != targetManager) continue;
            if (state.TryCreateReference(physicalRoot, out MountingNailTargetReference reference)
                && state.IsTargetLocked(reference)) return true;
            if (state.HasLockedTargetWithin(target)) return true;
        }
        return MountingNailConnectionRegistry.IsOfflineTargetConnected(physicalRoot.gameObject.scene, target)
            || MountingNailConnectionRegistry.IsOfflineTargetConnected(physicalRoot.gameObject.scene, physicalRoot);
    }

    private bool HasLockedTargetWithin(Transform target)
    {
        if (target == null || targetStates == null) return false;
        for (int i = 0; i < targetStates.Count; i++)
        {
            MountingNailSceneTargetState state = targetStates[i];
            if (!state.Locked || state.Tombstone || !TryResolve(state.Target, out Transform lockedRoot)) continue;
            if (lockedRoot == target || lockedRoot.IsChildOf(target)) return true;
        }
        return false;
    }

    private static Transform ResolvePickupPhysicalRoot(Transform target)
    {
        if (target.GetComponent<Rigidbody>() != null) return target;
        if (target.TryGetComponent(out MountingNailSceneTargetIdentity identity)) return identity.transform;
        Rigidbody parentBody = target.GetComponentInParent<Rigidbody>();
        if (parentBody != null) return parentBody.transform;
        MountingNailSceneTargetIdentity parentIdentity = target.GetComponentInParent<MountingNailSceneTargetIdentity>();
        if (parentIdentity != null) return parentIdentity.transform;
        return target;
    }

    public bool TryCreateReference(Transform physicalRoot, out MountingNailTargetReference reference)
    {
        reference = default;
        if (physicalRoot == null) return false;
        NetworkObject networkObject = physicalRoot.GetComponentInParent<NetworkObject>();
        if (networkObject != null)
        {
            if (NetworkManager == null || !NetworkManager.IsListening) return false;
            if (!networkObject.IsSpawned || NetworkManager.SpawnManager == null
                || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObject.NetworkObjectId, out NetworkObject registered)
                || registered != networkObject) return false;
            IndexNetworkObject(networkObject);
            if (knownNetworkReferences.TryGetValue(physicalRoot, out MountingNailTargetReference knownReference)
                && resolvedNetworkTargets.TryGetValue(knownReference, out var knownTarget)
                && knownReference.NetworkObjectId == networkObject.NetworkObjectId
                && knownTarget.networkObject == networkObject && knownTarget.physicalRoot == physicalRoot)
            {
                reference = knownReference;
                return reference.IsValid && !IsTombstoned(reference);
            }
            if (!MountingNailSceneTargetIdentity.TryBuildPhysicalRootPath(networkObject.transform, physicalRoot,
                    out FixedString64Bytes path)) return false;
            reference = new MountingNailTargetReference
            {
                IsNetworkObject = true,
                NetworkObjectId = networkObject.NetworkObjectId,
                PhysicalRootPath = path
            };
            resolvedNetworkTargets[reference] = (networkObject, physicalRoot);
            knownNetworkReferences[physicalRoot] = reference;
            return reference.IsValid && !IsTombstoned(reference);
        }

        EnsureSceneTargetIndex();
        MountingNailSceneTargetIdentity identity = physicalRoot.GetComponent<MountingNailSceneTargetIdentity>();
        if (identity == null || !identity.HasValidIdentity
            || identity.GetComponentInParent<Rigidbody>() != identity.GetComponent<Rigidbody>()
            || duplicateSceneTargetIds.Contains(identity.StableTargetId)
            || !resolvedSceneTargets.TryGetValue(identity.StableTargetId, out MountingNailSceneTargetIdentity indexedIdentity)
            || indexedIdentity != identity) return false;
        reference = identity.CreateSceneReference();
        return reference.IsValid && !IsTombstoned(reference);
    }

    private bool IsTombstoned(MountingNailTargetReference reference)
    {
        int index = FindState(reference);
        return index >= 0 && targetStates[index].Tombstone;
    }

    public bool TryResolve(MountingNailTargetReference reference, out Transform physicalRoot)
    {
        physicalRoot = null;
        if (!reference.IsValid) return false;
        if (reference.IsNetworkObject)
        {
            if (NetworkManager == null || !NetworkManager.IsListening || NetworkManager.SpawnManager == null
                || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(reference.NetworkObjectId, out NetworkObject networkObject)
                || networkObject == null || networkObject.NetworkManager != NetworkManager) return false;
            IndexNetworkObject(networkObject);
            if (resolvedNetworkTargets.TryGetValue(reference, out var resolvedTarget))
            {
                if (resolvedTarget.networkObject != networkObject || resolvedTarget.physicalRoot == null
                    || resolvedTarget.physicalRoot.GetComponentInParent<NetworkObject>() != networkObject) return false;
                physicalRoot = resolvedTarget.physicalRoot;
                return true;
            }
            if (!MountingNailSceneTargetIdentity.TryResolvePhysicalRoot(networkObject, reference, out physicalRoot)) return false;
            resolvedNetworkTargets[reference] = (networkObject, physicalRoot);
            knownNetworkReferences[physicalRoot] = reference;
            return true;
        }

        EnsureSceneTargetIndex();
        if (duplicateSceneTargetIds.Contains(reference.StableTargetId.ToString())) return false;
        if (!resolvedSceneTargets.TryGetValue(reference.StableTargetId.ToString(), out MountingNailSceneTargetIdentity identity)
            || identity == null || !identity.Matches(reference)) return false;
        physicalRoot = identity.transform;
        return true;
    }

    private void IndexNetworkObject(NetworkObject networkObject)
    {
        if (networkObject == null) return;
        if (indexedNetworkObjectIds.TryGetValue(networkObject, out ulong indexedId)
            && indexedId == networkObject.NetworkObjectId) return;
        if (indexedId != 0)
        {
            List<MountingNailTargetReference> staleReferences = new();
            foreach (KeyValuePair<MountingNailTargetReference,
                         (NetworkObject networkObject, Transform physicalRoot)> entry in resolvedNetworkTargets)
                if (entry.Value.networkObject == networkObject)
                    staleReferences.Add(entry.Key);
            foreach (MountingNailTargetReference staleReference in staleReferences)
                resolvedNetworkTargets.Remove(staleReference);
            List<Transform> staleRoots = new();
            foreach (KeyValuePair<Transform, MountingNailTargetReference> entry in knownNetworkReferences)
                if (entry.Value.IsNetworkObject && entry.Value.NetworkObjectId == indexedId)
                    staleRoots.Add(entry.Key);
            foreach (Transform staleRoot in staleRoots) knownNetworkReferences.Remove(staleRoot);
        }
        indexedNetworkObjectIds[networkObject] = networkObject.NetworkObjectId;
        foreach (Transform child in networkObject.GetComponentsInChildren<Transform>(true))
        {
            if (!MountingNailSceneTargetIdentity.TryBuildPhysicalRootPath(networkObject.transform, child,
                    out FixedString64Bytes path)) continue;
            MountingNailTargetReference reference = new()
            {
                IsNetworkObject = true,
                NetworkObjectId = networkObject.NetworkObjectId,
                PhysicalRootPath = path
            };
            resolvedNetworkTargets[reference] = (networkObject, child);
            knownNetworkReferences[child] = reference;
        }
    }

    /// <summary>Overrides automatic scene indexing for one peer-local loopback fixture.</summary>
    public bool RegisterLocalTarget(MountingNailSceneTargetIdentity identity)
    {
        if (identity == null || !identity.HasValidIdentity || identity.GetComponentInParent<NetworkObject>() != null
            || identity.GetComponentInParent<Rigidbody>() != identity.GetComponent<Rigidbody>()) return false;
        if (!explicitLocalTargetScope)
        {
            explicitLocalTargetScope = true;
            sceneIndexBuilt = true;
            resolvedSceneTargets.Clear();
            duplicateSceneTargetIds.Clear();
        }
        string id = identity.StableTargetId;
        if (duplicateSceneTargetIds.Contains(id)) return false;
        if (resolvedSceneTargets.TryGetValue(id, out MountingNailSceneTargetIdentity previous) && previous != identity)
        {
            resolvedSceneTargets.Remove(id);
            duplicateSceneTargetIds.Add(id);
            return false;
        }
        resolvedSceneTargets[id] = identity;
        if (IsSpawned) ApplyAllTargetStates();
        return true;
    }

    public bool IsTargetLocked(MountingNailTargetReference reference)
    {
        int index = FindState(reference);
        return reference.IsValid && index >= 0 && targetStates[index].Locked && !targetStates[index].Tombstone;
    }

    internal bool SetTargetConnectionDegree(MountingNailTargetReference reference, Transform exactRoot, int degree)
    {
        if (!IsSpawned || !IsServer || !reference.IsValid) return false;
        int index = FindState(reference);
        if (degree <= 0)
        {
            serverLockedRoots.Remove(reference);
            if (index < 0) return true;
            MountingNailSceneTargetState state = targetStates[index];
            state.Locked = false;
            targetStates[index] = state;
            return true;
        }
        if (exactRoot == null || !TryResolve(reference, out Transform root) || root != exactRoot) return false;

        MountingNailSceneTargetState next = index >= 0 ? targetStates[index] : new MountingNailSceneTargetState
        {
            Target = reference
        };
        if (next.Tombstone) return false;
        next.Locked = true;
        next.Position = root.position;
        next.Rotation = root.rotation;
        serverLockedRoots[reference] = root;
        if (index < 0) targetStates.Add(next);
        else targetStates[index] = next;
        return true;
    }

    internal bool SetTargetFollowed(MountingNailTargetReference reference, Transform exactRoot, bool followed)
    {
        if (!IsSpawned || !IsServer || !reference.IsValid) return false;
        int index = FindState(reference);
        if (followed)
        {
            if (exactRoot == null || !TryResolve(reference, out Transform resolvedRoot) || resolvedRoot != exactRoot)
                return false;
            MountingNailSceneTargetState next = index >= 0 ? targetStates[index] : new MountingNailSceneTargetState
            {
                Target = reference
            };
            if (next.Tombstone) return false;
            if (serverFollowedRoots.TryGetValue(reference, out var current) && current.root == exactRoot)
                current.count++;
            else current = (exactRoot, 1);
            serverFollowedRoots[reference] = current;
            next.FollowCount = current.count;
            next.Position = exactRoot.position;
            next.Rotation = exactRoot.rotation;
            if (index < 0) targetStates.Add(next);
            else targetStates[index] = next;
            return true;
        }

        if (!serverFollowedRoots.TryGetValue(reference, out var tracked)) return true;
        if (exactRoot != null && tracked.root != exactRoot) return false;
        tracked.count--;
        if (tracked.count <= 0)
        {
            serverFollowedRoots.Remove(reference);
            if (index >= 0)
            {
                MountingNailSceneTargetState next = targetStates[index];
                next.FollowCount = 0;
                targetStates[index] = next;
            }
        }
        else
        {
            serverFollowedRoots[reference] = tracked;
            if (index >= 0)
            {
                MountingNailSceneTargetState next = targetStates[index];
                next.FollowCount = tracked.count;
                targetStates[index] = next;
            }
        }
        return true;
    }

    internal MountingNailConnectionRegistry Connections => connectionRegistry;

    public bool SetTargetConnected(MountingNailTargetReference reference, bool connected)
    {
        return IsSpawned && IsServer && TryResolve(reference, out Transform root)
            && SetTargetConnectionDegree(reference, root, connected ? 1 : 0);
    }

    /// <summary>Persists deletion for current peers and future joiners for this loaded scene.</summary>
    public bool RecordTargetTombstone(MountingNailTargetReference reference)
    {
        if (!IsSpawned || !IsServer || !reference.IsValid) return false;
        int index = FindState(reference);
        if (reference.IsNetworkObject)
        {
            // Child tombstones are useful only while this exact spawned NetworkObject lives.
            // A despawned root removes its records so its numeric ID can never affect a replacement.
            if (NetworkManager == null || NetworkManager.SpawnManager == null
                || !NetworkManager.SpawnManager.SpawnedObjects.ContainsKey(reference.NetworkObjectId))
            {
                if (index >= 0) targetStates.RemoveAt(index);
                return true;
            }
        }
        MountingNailSceneTargetState state = index >= 0 ? targetStates[index] : new MountingNailSceneTargetState
        {
            Target = reference
        };
        state.Locked = false;
        state.FollowCount = 0;
        state.Tombstone = true;
        serverFollowedRoots.Remove(reference);
        serverLockedRoots.Remove(reference);
        if (index < 0) targetStates.Add(state);
        else targetStates[index] = state;
        return true;
    }

    internal void HandleSceneTargetDestroyed(MountingNailSceneTargetIdentity identity)
    {
        if (!IsSpawned || !IsServer || identity == null || !identity.HasValidIdentity) return;
        if (!resolvedSceneTargets.TryGetValue(identity.StableTargetId, out MountingNailSceneTargetIdentity registered)
            || registered != identity) return;
        RecordTargetTombstone(identity.CreateSceneReference());
    }

    private void PublishChangedConnectedTargetPoses()
    {
        for (int i = targetStates.Count - 1; i >= 0; i--)
        {
            MountingNailSceneTargetState state = targetStates[i];
            bool needsPose = state.Locked || state.FollowCount > 0;
            Transform exactRoot = null;
            bool exactRootValid = state.Locked
                ? serverLockedRoots.TryGetValue(state.Target, out exactRoot)
                : serverFollowedRoots.TryGetValue(state.Target, out var followed) && (exactRoot = followed.root) != null;
            if (needsPose && !state.Tombstone
                && (!exactRootValid || exactRoot == null || !TryResolve(state.Target, out Transform resolvedRoot)
                    || resolvedRoot != exactRoot))
            {
                serverLockedRoots.Remove(state.Target);
                serverFollowedRoots.Remove(state.Target);
                state.Locked = false;
                state.FollowCount = 0;
                targetStates[i] = state;
                continue;
            }
            if (!needsPose || state.Tombstone || !TryResolve(state.Target, out Transform root)) continue;
            if ((root.position - state.Position).sqrMagnitude < 0.000001f
                && Quaternion.Angle(root.rotation, state.Rotation) < 0.05f) continue;
            state.Position = root.position;
            state.Rotation = root.rotation;
            targetStates[i] = state;
        }
    }

    private void PruneExpiredNetworkTargetStates()
    {
        if (targetStates == null || NetworkManager == null || NetworkManager.SpawnManager == null) return;
        for (int i = targetStates.Count - 1; i >= 0; i--)
        {
            MountingNailTargetReference reference = targetStates[i].Target;
            if (reference.IsNetworkObject
                && !NetworkManager.SpawnManager.SpawnedObjects.ContainsKey(reference.NetworkObjectId))
                targetStates.RemoveAt(i);
        }
    }

    private void TargetStatesChanged(NetworkListEvent<MountingNailSceneTargetState> changeEvent)
    {
        if (changeEvent.Type == NetworkListEvent<MountingNailSceneTargetState>.EventType.Remove
            || changeEvent.Type == NetworkListEvent<MountingNailSceneTargetState>.EventType.RemoveAt)
        {
            serverLockedRoots.Remove(changeEvent.Value.Target);
            serverFollowedRoots.Remove(changeEvent.Value.Target);
            RestoreSavedBody(changeEvent.Value.Target);
            ApplyAllTargetStates();
        }
        if (changeEvent.Type == NetworkListEvent<MountingNailSceneTargetState>.EventType.Clear)
        {
            serverLockedRoots.Clear();
            serverFollowedRoots.Clear();
            RestoreAllBodies();
        }
        if (changeEvent.Type == NetworkListEvent<MountingNailSceneTargetState>.EventType.Value
            || changeEvent.Type == NetworkListEvent<MountingNailSceneTargetState>.EventType.Add)
            ApplyTargetState(changeEvent.Value);
    }

    private void ApplyAllTargetStates()
    {
        if (targetStates == null) return;
        HashSet<MountingNailTargetReference> active = new();
        for (int i = 0; i < targetStates.Count; i++)
        {
            MountingNailSceneTargetState state = targetStates[i];
            ApplyTargetState(state);
            if ((state.Locked || state.FollowCount > 0) && !state.Tombstone) active.Add(state.Target);
        }
        RestoreBodiesNotIn(active);
    }

    private void ApplyTargetState(MountingNailSceneTargetState state)
    {
        if ((!state.Locked && state.FollowCount <= 0) || state.Tombstone)
        {
            RestoreSavedBody(state.Target);
            if (state.Tombstone && !IsServer)
            {
                if (TryResolve(state.Target, out Transform tombstonedRoot))
                {
                    unresolvedTargetStates.Remove(state.Target);
                    tombstonedRoot.gameObject.SetActive(false);
                }
                else unresolvedTargetStates.Add(state.Target);
            }
            else unresolvedTargetStates.Remove(state.Target);
            return;
        }
        if (!TryResolve(state.Target, out Transform root))
        {
            unresolvedTargetStates.Add(state.Target);
            return;
        }
        unresolvedTargetStates.Remove(state.Target);
        Rigidbody body = root.GetComponent<Rigidbody>();
        if (!IsServer)
        {
            if (body != null)
            {
                if (!savedBodyFlags.ContainsKey(state.Target))
                    savedBodyFlags.Add(state.Target, (body, body.isKinematic, body.useGravity, body.constraints));
                body.isKinematic = true;
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.None;
                NetworkTransform networkTransform = root.GetComponent<NetworkTransform>();
                bool rootPoseAlreadySynchronized = state.Target.IsNetworkObject && networkTransform != null;
                if (!rootPoseAlreadySynchronized)
                {
                    body.position = state.Position;
                    body.rotation = state.Rotation;
                }
            }
            else
            {
                NetworkTransform networkTransform = root.GetComponent<NetworkTransform>();
                bool rootPoseAlreadySynchronized = state.Target.IsNetworkObject && networkTransform != null;
                if (!rootPoseAlreadySynchronized) root.SetPositionAndRotation(state.Position, state.Rotation);
            }
        }
    }

    private void RebuildSceneTargetIndex()
    {
        if (explicitLocalTargetScope) return;
        resolvedSceneTargets.Clear();
        duplicateSceneTargetIds.Clear();
        sceneTargets.Clear();
        UnityEngine.SceneManagement.Scene targetScene = gameObject.scene;
        foreach (MountingNailSceneTargetIdentity identity in FindObjectsByType<MountingNailSceneTargetIdentity>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (identity == null || !identity.HasValidIdentity || identity.gameObject.scene != targetScene
                || identity.GetComponentInParent<NetworkObject>() != null) continue;
            sceneTargets.Add(identity);
            string id = identity.StableTargetId;
            if (resolvedSceneTargets.ContainsKey(id)) duplicateSceneTargetIds.Add(id);
            else resolvedSceneTargets.Add(id, identity);
        }
        foreach (string duplicateId in duplicateSceneTargetIds) resolvedSceneTargets.Remove(duplicateId);
        sceneIndexBuilt = true;
    }

    private void EnsureSceneTargetIndex()
    {
        if (!sceneIndexBuilt) RebuildSceneTargetIndex();
    }

    private void ApplyUnresolvedTargetStates()
    {
        if (IsServer || unresolvedTargetStates.Count == 0 || targetStates == null) return;
        List<MountingNailTargetReference> resolved = new();
        MountingNailTargetReference[] pending = new MountingNailTargetReference[unresolvedTargetStates.Count];
        unresolvedTargetStates.CopyTo(pending);
        foreach (MountingNailTargetReference reference in pending)
        {
            int index = FindState(reference);
            if (index < 0)
            {
                resolved.Add(reference);
                continue;
            }
            if (TryResolve(reference, out _))
            {
                ApplyTargetState(targetStates[index]);
                resolved.Add(reference);
            }
        }
        foreach (MountingNailTargetReference reference in resolved) unresolvedTargetStates.Remove(reference);
    }

    private int FindState(MountingNailTargetReference reference)
    {
        if (targetStates == null) return -1;
        for (int i = 0; i < targetStates.Count; i++)
            if (targetStates[i].Target.Equals(reference)) return i;
        return -1;
    }

    private void RestoreBodiesNotIn(HashSet<MountingNailTargetReference> active)
    {
        List<MountingNailTargetReference> restore = new();
        foreach (MountingNailTargetReference reference in savedBodyFlags.Keys)
            if (!active.Contains(reference)) restore.Add(reference);
        foreach (MountingNailTargetReference reference in restore)
        {
            RestoreSavedBody(reference);
        }
    }

    private void RestoreSavedBody(MountingNailTargetReference reference)
    {
        if (!savedBodyFlags.TryGetValue(reference, out var flags)) return;
        RestoreBody(reference, flags.body);
    }

    private void RestoreBody(MountingNailTargetReference reference, Rigidbody body)
    {
        if (!savedBodyFlags.TryGetValue(reference, out var flags)) return;
        if (body != null)
        {
            body.isKinematic = flags.kinematic;
            body.useGravity = flags.gravity;
            body.constraints = flags.constraints;
        }
        savedBodyFlags.Remove(reference);
    }

    private void RestoreAllBodies()
    {
        List<MountingNailTargetReference> references = new(savedBodyFlags.Keys);
        foreach (MountingNailTargetReference reference in references)
            RestoreSavedBody(reference);
        unresolvedTargetStates.Clear();
    }
}
