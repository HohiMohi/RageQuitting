using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Owns one physical joint per unordered target pair and tracks graph degree.</summary>
internal sealed class MountingNailConnectionRegistry
{
    private readonly struct NetworkTransformPose
    {
        internal readonly NetworkTransform Component;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly Vector3 Scale;

        internal NetworkTransformPose(NetworkTransform component)
        {
            Component = component;
            Position = component.GetSpaceRelativePosition();
            Rotation = component.GetSpaceRelativeRotation();
            Scale = component.transform.localScale;
        }
    }

    private sealed class Edge
    {
        public Transform First;
        public Transform Second;
        public MountingNailTargetReference FirstReference;
        public MountingNailTargetReference SecondReference;
        public FixedJoint Joint;
        public readonly HashSet<MountingNail> Nails = new();
    }

    private readonly MountingNailSceneState sceneState;
    private readonly List<Edge> edges = new();
    private readonly Dictionary<NetworkObject, ulong> savedOwners = new();
    private static readonly Dictionary<Scene, MountingNailConnectionRegistry> OfflineByScene = new();
    private static bool subscribedToSceneUnload;

    internal MountingNailConnectionRegistry(MountingNailSceneState sceneState = null) => this.sceneState = sceneState;

    internal static MountingNailConnectionRegistry ForOfflineScene(Scene scene)
    {
        if (!scene.IsValid()) return null;
        if (!OfflineByScene.TryGetValue(scene, out MountingNailConnectionRegistry registry))
        {
            registry = new MountingNailConnectionRegistry();
            OfflineByScene.Add(scene, registry);
        }
        if (!subscribedToSceneUnload)
        {
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            subscribedToSceneUnload = true;
        }
        return registry;
    }

    internal bool Add(MountingNail nail, Transform first, Transform second,
        MountingNailTargetReference firstReference, MountingNailTargetReference secondReference,
        Vector3 worldAnchor)
    {
        if (nail == null || first == null || second == null || first == second) return false;
        if (sceneState != null && (!sceneState.IsSpawned || !sceneState.IsServer)) return false;
        NetworkObject firstNetworkObject = first.GetComponentInParent<Unity.Netcode.NetworkObject>();
        NetworkObject secondNetworkObject = second.GetComponentInParent<Unity.Netcode.NetworkObject>();
        if (sceneState != null && (firstNetworkObject != null && firstNetworkObject.NetworkManager != sceneState.ScopedNetworkManager
            || secondNetworkObject != null && secondNetworkObject.NetworkManager != sceneState.ScopedNetworkManager)) return false;

        Edge edge = FindEdge(first, second);
        if (edge == null)
        {
            edge = new Edge
            {
                First = first,
                Second = second,
                FirstReference = firstReference,
                SecondReference = secondReference
            };
            edge.Joint = CreateJoint(first, second, worldAnchor);
            edges.Add(edge);
            CaptureServerOwnership(edge, true);
            CaptureServerOwnership(edge, false);
            UpdateLocks(edge.First, edge.FirstReference);
            UpdateLocks(edge.Second, edge.SecondReference);
        }
        edge.Nails.Add(nail);
        return true;
    }

    internal void Remove(MountingNail nail)
    {
        RemoveCore(nail, null);
    }

    internal void RemoveForPanelReset(MountingNail nail, NetworkObject deferOwnershipRestoreFor)
    {
        RemoveCore(nail, deferOwnershipRestoreFor);
    }

    private void RemoveCore(MountingNail nail, NetworkObject deferOwnershipRestoreFor)
    {
        if (nail == null) return;
        for (int i = edges.Count - 1; i >= 0; i--)
        {
            Edge edge = edges[i];
            if (!edge.Nails.Remove(nail) || edge.Nails.Count != 0) continue;
            edges.RemoveAt(i);
            DestroyJoint(edge.Joint);
            UpdateLocks(edge.First, edge.FirstReference);
            UpdateLocks(edge.Second, edge.SecondReference);
            RestoreOwnership(edge, true, deferOwnershipRestoreFor);
            RestoreOwnership(edge, false, deferOwnershipRestoreFor);
        }
    }

    internal void ConsumeSavedOwnershipForPanelReset(NetworkObject networkObject)
    {
        if (networkObject == null) return;
        savedOwners.Remove(networkObject);
    }

    internal int PairJointCount(Transform first, Transform second)
    {
        Edge edge = FindEdge(first, second);
        return edge != null && edge.Joint != null ? 1 : 0;
    }

    internal int Degree(Transform target)
    {
        int degree = 0;
        foreach (Edge edge in edges)
            if (edge.First == target || edge.Second == target) degree++;
        return degree;
    }

    internal static bool IsOfflineTargetConnected(UnityEngine.SceneManagement.Scene scene, Transform target)
    {
        if (target == null || !scene.IsValid()
            || !OfflineByScene.TryGetValue(scene, out MountingNailConnectionRegistry registry) || registry == null) return false;
        foreach (Edge edge in registry.edges)
            if (IsWithin(edge.First, target) || IsWithin(edge.Second, target)) return true;
        return false;
    }

    private static bool IsWithin(Transform candidate, Transform root) => candidate != null
        && (candidate == root || candidate.IsChildOf(root));

    internal void Tick()
    {
        PruneDestroyedOwners();
        for (int i = edges.Count - 1; i >= 0; i--)
        {
            Edge edge = edges[i];
            MountingNail[] nails = new MountingNail[edge.Nails.Count];
            edge.Nails.CopyTo(nails);
            foreach (MountingNail nail in nails)
            {
                if (nail == null) continue;
                if (!nail.IsConnectionTargetAlive(true))
                {
                    edge.Nails.Remove(nail);
                    MountingNailTargetReference removedReference = nail.ConnectionTargetReference(true);
                    if (sceneState != null && removedReference.IsNetworkObject)
                        sceneState.RecordTargetTombstone(removedReference);
                    nail.ConnectionFirstTargetRemoved();
                }
                else if (!nail.IsConnectionTargetAlive(false))
                {
                    edge.Nails.Remove(nail);
                    MountingNailTargetReference removedReference = nail.ConnectionTargetReference(false);
                    if (sceneState != null && removedReference.IsNetworkObject)
                        sceneState.RecordTargetTombstone(removedReference);
                    nail.ConnectionSecondTargetRemoved();
                }
            }
            if (edge.Nails.Count != 0) continue;
            edges.RemoveAt(i);
            DestroyJoint(edge.Joint);
            UpdateLocks(edge.First, edge.FirstReference);
            UpdateLocks(edge.Second, edge.SecondReference);
            RestoreOwnership(edge, true);
            RestoreOwnership(edge, false);
        }
    }

    internal void Clear()
    {
        Edge[] oldEdges = edges.ToArray();
        edges.Clear();
        foreach (Edge edge in oldEdges)
        {
            DestroyJoint(edge.Joint);
            UpdateLocks(edge.First, edge.FirstReference);
            UpdateLocks(edge.Second, edge.SecondReference);
            RestoreOwnership(edge, true);
            RestoreOwnership(edge, false);
            foreach (MountingNail nail in edge.Nails)
                if (nail != null) nail.ConnectionRegistryCleared(this);
        }
        foreach (KeyValuePair<NetworkObject, ulong> entry in savedOwners)
            if (sceneState != null && entry.Key != null && entry.Key.IsSpawned
                && entry.Key.NetworkManager == sceneState.ScopedNetworkManager
                && entry.Key.NetworkManager.ConnectedClients.ContainsKey(entry.Value))
                entry.Key.ChangeOwnership(entry.Value);
        savedOwners.Clear();
    }

    private void UpdateLocks(Transform target, MountingNailTargetReference reference)
    {
        if (sceneState == null || !reference.IsValid) return;
        int degree = target == null ? 0 : Degree(target);
        sceneState.SetTargetConnectionDegree(reference, target, degree);
    }

    private void CaptureServerOwnership(Edge edge, bool first)
    {
        if (sceneState == null || (first ? Degree(edge.First) : Degree(edge.Second)) != 1) return;
        Transform target = first ? edge.First : edge.Second;
        NetworkObject networkObject = target != null ? target.GetComponentInParent<Unity.Netcode.NetworkObject>() : null;
        if (networkObject == null || !networkObject.IsSpawned
            || networkObject.NetworkManager != sceneState.ScopedNetworkManager
            || networkObject.OwnerClientId == NetworkManager.ServerClientId
            || savedOwners.ContainsKey(networkObject)) return;
        List<NetworkTransformPose> poses = new();
        foreach (NetworkTransform networkTransform in networkObject.GetComponentsInChildren<NetworkTransform>(true))
            if (networkTransform != null && networkTransform.IsSpawned
                && networkTransform.NetworkObject == networkObject)
                poses.Add(new NetworkTransformPose(networkTransform));
        savedOwners.Add(networkObject, networkObject.OwnerClientId);
        networkObject.ChangeOwnership(NetworkManager.ServerClientId);
        foreach (NetworkTransformPose pose in poses)
            if (pose.Component != null && pose.Component.IsSpawned)
                pose.Component.Teleport(pose.Position, pose.Rotation, pose.Scale);
    }

    private void RestoreOwnership(Edge edge, bool first, NetworkObject deferOwnershipRestoreFor = null)
    {
        if (sceneState == null) return;
        Transform target = first ? edge.First : edge.Second;
        NetworkObject networkObject = target != null ? target.GetComponentInParent<Unity.Netcode.NetworkObject>() : null;
        if (networkObject != null && networkObject == deferOwnershipRestoreFor) return;
        if (networkObject != null) RestoreOwnershipIfUnlocked(networkObject);
        else RestoreUnlockedSavedOwners(deferOwnershipRestoreFor);
    }

    private void RestoreUnlockedSavedOwners(NetworkObject deferOwnershipRestoreFor = null)
    {
        List<NetworkObject> candidates = new(savedOwners.Keys);
        foreach (NetworkObject candidate in candidates)
            if (candidate != null && candidate != deferOwnershipRestoreFor) RestoreOwnershipIfUnlocked(candidate);
    }

    private void RestoreOwnershipIfUnlocked(NetworkObject networkObject)
    {
        if (networkObject == null || DegreeForNetworkObject(networkObject) != 0
            || !savedOwners.TryGetValue(networkObject, out ulong previousOwner)) return;
        savedOwners.Remove(networkObject);
        if (networkObject.IsSpawned && networkObject.NetworkManager == sceneState.ScopedNetworkManager
            && networkObject.NetworkManager.ConnectedClients.ContainsKey(previousOwner))
            networkObject.ChangeOwnership(previousOwner);
    }

    private int DegreeForNetworkObject(NetworkObject networkObject)
    {
        int degree = 0;
        foreach (Edge activeEdge in edges)
            if (BelongsToNetworkObject(activeEdge.First, networkObject)
                || BelongsToNetworkObject(activeEdge.Second, networkObject)) degree++;
        return degree;
    }

    private void PruneDestroyedOwners()
    {
        List<NetworkObject> stale = null;
        foreach (KeyValuePair<NetworkObject, ulong> entry in savedOwners)
            if (sceneState == null || entry.Key == null || !entry.Key.IsSpawned
                || entry.Key.NetworkManager != sceneState.ScopedNetworkManager)
                (stale ??= new List<NetworkObject>()).Add(entry.Key);
        if (stale == null) return;
        foreach (NetworkObject networkObject in stale) savedOwners.Remove(networkObject);
    }

    private static bool BelongsToNetworkObject(Transform target, NetworkObject networkObject) =>
        target != null && networkObject != null && target.GetComponentInParent<NetworkObject>() == networkObject;

    private Edge FindEdge(Transform first, Transform second)
    {
        foreach (Edge edge in edges)
            if (edge.First == first && edge.Second == second || edge.First == second && edge.Second == first)
                return edge;
        return null;
    }

    private static FixedJoint CreateJoint(Transform first, Transform second, Vector3 worldAnchor)
    {
        Rigidbody firstBody = first.GetComponent<Rigidbody>();
        Rigidbody secondBody = second.GetComponent<Rigidbody>();
        if (firstBody == null && secondBody == null) return null;

        Rigidbody owner = firstBody != null && (!firstBody.isKinematic || secondBody == null) ? firstBody : secondBody;
        Rigidbody connected = owner == firstBody ? secondBody : firstBody;
        FixedJoint joint = owner.gameObject.AddComponent<FixedJoint>();
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = owner.transform.InverseTransformPoint(worldAnchor);
        joint.connectedBody = connected;
        joint.connectedAnchor = connected != null ? connected.transform.InverseTransformPoint(worldAnchor) : worldAnchor;
        joint.breakForce = Mathf.Infinity;
        joint.breakTorque = Mathf.Infinity;
        return joint;
    }

    private static void DestroyJoint(FixedJoint joint)
    {
        if (joint == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(joint);
        else UnityEngine.Object.DestroyImmediate(joint);
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        if (OfflineByScene.TryGetValue(scene, out MountingNailConnectionRegistry registry))
            registry.Clear();
        OfflineByScene.Remove(scene);
        if (OfflineByScene.Count == 0 && subscribedToSceneUnload)
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            subscribedToSceneUnload = false;
        }
    }
}
