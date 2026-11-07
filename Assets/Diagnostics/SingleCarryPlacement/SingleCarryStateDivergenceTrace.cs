#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
using System;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>Opt-in, bounded value recorder for Single Carry pose divergence investigations.</summary>
public static class SingleCarryStateDivergenceTrace
{
    public const int MaxRecordsPerPeer = 8192;
    private const int MaxPeers = 8;

    public enum EventKind
    {
        MovementUpdateEnd,
        PreviewRefresh,
        RequestSubmitted,
        AuthorityPush,
        ReceiveCallbackBeforeBase,
        ReceiveCallbackAfterBase,
        ReceiveCacheUpdated,
        ReceiveCacheCleared,
        ServerValidationRead,
        PlacementResponse,
        SessionBegin,
        SessionFrozen
    }

    [Serializable]
    public struct Record
    {
        public int sequence;
        public int eventKind;
        public string runId;
        public ulong peerLocalClientId;
        public bool peerIsServer;
        public bool peerListening;
        public ulong playerObjectId;
        public ulong ownerClientId;
        public int managerInstanceId;
        public int playerInstanceId;
        public int parentInstanceId;
        public ulong parentNetworkObjectId;
        public bool hasParentNetworkObjectId;
        public bool spaceContextAvailable;
        public int localSpawnGeneration;
        public int frame;
        public double realtime;
        public int localTick;
        public int serverTick;
        public int sourceTick;
        public bool hasLocalTick;
        public bool hasServerTick;
        public bool hasSourceTick;
        public int flags;
        public ulong requestId;
        public Vector3 bodyPosition;
        public Quaternion bodyRotation;
        public Vector3 horizontalVelocity;
        public Vector2 movementInput;
        public bool hasMovementInput;
        public bool placementActive;
        public bool requestPending;
        public bool movementBlocked;
        public bool hasBodyPose;
        public bool hasCameraPose;
        public bool hasRay;
        public bool hasGhostPose;
        public bool hasStatePose;
        public bool statePositionAxesComplete;
        public bool stateRotationAxesComplete;
        public bool quaternionSync;
        public bool hasCachedPose;
        public bool hasParentContext;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public Vector3 rayOrigin;
        public Vector3 rayDirection;
        public Vector3 ghostPosition;
        public Quaternion ghostRotation;
        public Vector3 statePosition;
        public Quaternion stateRotation;
        public Vector3 cachedPositionBefore;
        public Quaternion cachedRotationBefore;
        public Vector3 cachedPositionAfter;
        public Quaternion cachedRotationAfter;
        public Vector3 trustedAnchor;
        public Vector3 residual;
        public float residualSquared;
        public int result;
        public int branch;
        public int positionChangeMask;
        public int rotationChangeMask;
        public bool isAuthority;
        public bool isOwner;
        public bool isInitialSynchronization;
        public bool inLocalSpace;
        public bool hasCompleteCache;
        public bool useUnreliableDeltas;
        public bool hasAuthoritativePoseRead;
        public bool hasTrustedAnchor;
        public bool hasResidual;
        public bool overflow;
    }

    [Serializable]
    private sealed class TraceFile
    {
        public string runId;
        public string unityVersion;
        public string outputDirectory;
        public int capacityPerPeer;
        public bool overflow;
        public string metadataJson;
        public double measuredResponseMilliseconds;
        public Record[] records;
    }

    public static bool Enabled { get; set; }
    public static string OutputDirectory { get; set; }
    public static double MeasuredResponseMilliseconds { get; set; }
    private static Record[] records;
    private static ulong[] peerIds;
    private static int[] peerManagerIds;
    private static int[] peerPlayerInstanceIds;
    private static int[] peerCounts;
    private static int recordCount;
    private static int nextSequence;
    private static ulong trackedObjectId;
    private static ulong trackedOwnerId;
    private static string runId;
    private static string metadataJson;
    private static bool frozen;
    private static bool exported;
    private static bool overflow;

    public static string CurrentRunId => runId;
    public static bool IsRecording => Enabled && records != null && !frozen;
    public static bool IsFrozen => records != null && frozen;
    public static bool WasExported => exported;
    public static bool Tracks(NetworkObject player)
    {
        if (!IsRecording || player == null || player.NetworkObjectId != trackedObjectId || player.OwnerClientId != trackedOwnerId)
            return false;
        int managerId = player.NetworkManager != null ? player.NetworkManager.GetInstanceID() : 0;
        int instanceId = player.GetInstanceID();
        for (int i = 0; i < MaxPeers; i++)
            if (peerCounts[i] > 0 && peerManagerIds[i] == managerId && peerIds[i] == trackedObjectId
                && peerPlayerInstanceIds[i] == instanceId) return true;
        return false;
    }
    public static bool HasOverflow => overflow;
    public static int RecordCount => recordCount;

    public static string Begin(NetworkManager manager, NetworkObject player, string metadata)
    {
        if (!Enabled || manager == null || player == null || !player.IsSpawned)
            return null;

        runId = Guid.NewGuid().ToString("N");
        trackedObjectId = player.NetworkObjectId;
        trackedOwnerId = player.OwnerClientId;
        metadataJson = metadata ?? string.Empty;
        records = new Record[MaxPeers * MaxRecordsPerPeer];
        peerIds = new ulong[MaxPeers];
        peerManagerIds = new int[MaxPeers];
        peerPlayerInstanceIds = new int[MaxPeers];
        peerCounts = new int[MaxPeers];
        recordCount = 0;
        nextSequence = 0;
        overflow = false;
        frozen = false;
        exported = false;
        pendingRequest = false;
        trackedRequestId = 0;
        MeasuredResponseMilliseconds = 0;
        RegisterReplica(manager, player, player.GetComponent<ClientNetworkTransform>() != null
            ? player.GetComponent<ClientNetworkTransform>().EditorLocalSpawnGeneration : 0);
        return runId;
    }

    public static bool RegisterReplica(NetworkManager manager, NetworkObject player, int generation)
    {
        if (!IsRecording || manager == null || player == null || player.NetworkObjectId != trackedObjectId
            || player.OwnerClientId != trackedOwnerId) return false;
        int managerId = manager.GetInstanceID();
        int instanceId = player.GetInstanceID();
        int slot = -1;
        for (int i = 0; i < MaxPeers; i++)
        {
            if (peerCounts[i] > 0 && peerManagerIds[i] == managerId)
                return peerIds[i] == trackedObjectId && peerPlayerInstanceIds[i] == instanceId;
            if (peerCounts[i] == 0 && slot < 0) slot = i;
        }
        if (slot < 0) { overflow = true; return false; }
        peerIds[slot] = trackedObjectId;
        peerManagerIds[slot] = managerId;
        peerPlayerInstanceIds[slot] = instanceId;
        Record r = NewRecord(manager, player, generation, EventKind.SessionBegin);
        ClientNetworkTransform networkTransform = player.GetComponent<ClientNetworkTransform>();
        if (networkTransform != null)
        {
            ClientNetworkTransform.EditorReceiveDiagnostic snapshot = networkTransform.GetEditorReceiveDiagnostic();
            r.cachedPositionAfter = snapshot.cachedAuthoritativeSpacePosition;
            r.cachedRotationAfter = snapshot.cachedAuthoritativeSpaceRotation;
            r.hasCachedPose = snapshot.hasCompletePose;
            r.hasCompleteCache = snapshot.hasCompletePose;
            r.sourceTick = snapshot.lastReceivedNetworkTick;
            r.hasSourceTick = snapshot.receiveCount > 0;
            r.inLocalSpace = snapshot.currentInLocalSpace;
        }
        Append(r);
        return true;
    }

    public static void RecordMovement(NetworkManager manager, NetworkObject player, int generation,
        Vector3 bodyPosition, Quaternion bodyRotation, Vector3 horizontalVelocity,
        Vector2 movementInput, bool hasMovementInput, bool placementActive, bool requestPending,
        bool movementBlocked, int flags)
    {
        Record r = NewRecord(manager, player, generation, EventKind.MovementUpdateEnd);
        r.hasBodyPose = true;
        r.bodyPosition = bodyPosition; r.bodyRotation = bodyRotation;
        r.horizontalVelocity = horizontalVelocity; r.movementInput = movementInput;
        r.hasMovementInput = hasMovementInput; r.placementActive = placementActive;
        r.requestPending = requestPending; r.movementBlocked = movementBlocked; r.flags = flags; Append(r);
    }

    private static bool pendingRequest;
    private static ulong trackedRequestId;
    public static void RecordMovementForGameObject(GameObject playerObject, Vector3 bodyPosition,
        Quaternion bodyRotation, Vector3 horizontalVelocity, Vector2 movementInput, bool hasMovementInput,
        bool placementActive, bool movementBlocked)
    {
        NetworkObject player = playerObject != null ? playerObject.GetComponent<NetworkObject>() : null;
        if (!Tracks(player)) return;
        ClientNetworkTransform networkTransform = player.GetComponent<ClientNetworkTransform>();
        RecordMovement(player.NetworkManager, player,
            networkTransform != null ? networkTransform.EditorLocalSpawnGeneration : 0,
            bodyPosition, bodyRotation, horizontalVelocity, movementInput, hasMovementInput,
            placementActive, pendingRequest, movementBlocked, 0);
    }

    public static int GetLocalSpawnGeneration(NetworkObject player)
    {
        return player != null && player.TryGetComponent(out ClientNetworkTransform networkTransform)
            ? networkTransform.EditorLocalSpawnGeneration : 0;
    }

    public static int RecordPreview(NetworkManager manager, NetworkObject player, int generation, int revision, int callbackKind,
        Vector3 bodyPosition, Quaternion bodyRotation, Vector3 cameraPosition, Quaternion cameraRotation,
        Vector3 rayOrigin, Vector3 rayDirection, Vector3 ghostPosition, Quaternion ghostRotation, int flags)
    {
        Record r = NewRecord(manager, player, generation, EventKind.PreviewRefresh);
        r.flags = flags | (callbackKind << 16);
        r.branch = revision; r.hasBodyPose = true; r.hasRay = (flags & 1) == 0;
        r.hasCameraPose = (flags & 2) == 0; r.hasGhostPose = (flags & 4) == 0;
        r.bodyPosition = bodyPosition; r.bodyRotation = bodyRotation;
        r.cameraPosition = cameraPosition; r.cameraRotation = cameraRotation;
        r.rayOrigin = rayOrigin; r.rayDirection = rayDirection;
        r.ghostPosition = ghostPosition; r.ghostRotation = ghostRotation;
        return Append(r) ? r.sequence : -1;
    }

    public static void RecordRequest(NetworkManager manager, NetworkObject player, int generation,
        int previewRevision, ulong requestId, Vector3 bodyPosition, Quaternion bodyRotation,
        Vector3 horizontalVelocity, Vector3 rayOrigin, Vector3 rayDirection,
        Vector3 submittedPosition, Quaternion submittedRotation, int flags)
    {
        Record r = NewRecord(manager, player, generation, EventKind.RequestSubmitted);
        r.flags = flags; r.branch = previewRevision; r.requestId = requestId;
        r.hasBodyPose = true; r.hasRay = true; r.hasGhostPose = true;
        r.placementActive = true; r.requestPending = true; r.movementBlocked = true;
        r.bodyPosition = bodyPosition; r.bodyRotation = bodyRotation; r.horizontalVelocity = horizontalVelocity;
        r.rayOrigin = rayOrigin; r.rayDirection = rayDirection;
        r.ghostPosition = submittedPosition; r.ghostRotation = submittedRotation;
        if (Append(r))
        {
            pendingRequest = true;
            trackedRequestId = requestId;
        }
    }

    public static void RecordTransform(NetworkManager manager, NetworkObject player, int generation,
        EventKind kind, int sourceTick, bool hasSourceTick, Vector3 statePosition, Quaternion stateRotation,
        int positionMask, int rotationMask, bool quaternionSync, bool authority, bool owner, bool initialSync, bool localSpace,
        Vector3 currentPosition, Quaternion currentRotation, Vector3 cacheBefore, Quaternion cacheRotationBefore,
        Vector3 cacheAfter, Quaternion cacheRotationAfter, bool hasCache, bool useUnreliableDeltas, int flags)
    {
        Record r = NewRecord(manager, player, generation, kind);
        r.sourceTick = sourceTick; r.hasSourceTick = hasSourceTick;
        r.statePosition = statePosition; r.stateRotation = stateRotation;
        r.hasStatePose = kind != EventKind.ReceiveCacheCleared; r.hasBodyPose = true; r.hasCachedPose = hasCache;
        r.statePositionAxesComplete = positionMask == 7;
        r.quaternionSync = quaternionSync;
        r.stateRotationAxesComplete = quaternionSync ? (rotationMask & 8) != 0 : (rotationMask & 7) == 7;
        r.positionChangeMask = positionMask; r.rotationChangeMask = rotationMask;
        r.isAuthority = authority; r.isOwner = owner; r.isInitialSynchronization = initialSync;
        r.inLocalSpace = localSpace; r.bodyPosition = currentPosition; r.bodyRotation = currentRotation;
        r.cachedPositionBefore = cacheBefore; r.cachedRotationBefore = cacheRotationBefore;
        r.cachedPositionAfter = cacheAfter; r.cachedRotationAfter = cacheRotationAfter;
        r.hasCompleteCache = hasCache; r.useUnreliableDeltas = useUnreliableDeltas; r.flags = flags; Append(r);
    }

    public static void RecordValidation(NetworkManager manager, NetworkObject player, int generation,
        ulong requestId, int sourceTick, bool hasSourceTick, Vector3 bodyPosition, Quaternion bodyRotation,
        Vector3 trustedAnchor, Vector3 rayOrigin, Vector3 rayDirection, Vector3 residual,
        float residualSquared, int branch, int result, bool hasCache, int availabilityFlags)
    {
        Record r = NewRecord(manager, player, generation, EventKind.ServerValidationRead);
        r.requestId = requestId; r.sourceTick = sourceTick; r.hasSourceTick = hasSourceTick;
        r.hasBodyPose = true; r.hasRay = true; r.hasCachedPose = hasCache;
        r.bodyPosition = bodyPosition; r.bodyRotation = bodyRotation; r.trustedAnchor = trustedAnchor;
        r.rayOrigin = rayOrigin; r.rayDirection = rayDirection; r.residual = residual;
        r.residualSquared = residualSquared; r.branch = branch; r.result = result; r.hasCompleteCache = hasCache;
        r.hasAuthoritativePoseRead = (availabilityFlags & 1) != 0;
        r.hasTrustedAnchor = (availabilityFlags & 2) != 0;
        r.hasResidual = (availabilityFlags & 4) != 0;
        Append(r);
    }

    public static void RecordResponse(NetworkManager manager, NetworkObject player, int generation,
        ulong requestId, int result, int flags)
    {
        if (!IsRecording || !pendingRequest || requestId == 0 || requestId != trackedRequestId) return;
        Record r = NewRecord(manager, player, generation, EventKind.PlacementResponse);
        r.requestId = requestId; r.result = result; r.flags = flags; Append(r);
        pendingRequest = false;
        trackedRequestId = 0;
        Freeze(manager, player, generation, requestId, result);
    }

    public static bool Freeze(NetworkManager manager, NetworkObject player, int generation,
        ulong requestId, int result)
    {
        if (records == null || frozen) return false;
        Record r = NewRecord(manager, player, generation, EventKind.SessionFrozen);
        r.requestId = requestId; r.result = result; Append(r);
        frozen = true;
        return true;
    }

    public static string ExportFrozenSnapshot(string path)
    {
        if (records == null || !frozen) return null;
        if (string.IsNullOrWhiteSpace(path))
        {
            string directory = string.IsNullOrWhiteSpace(OutputDirectory)
                ? Path.Combine(Application.dataPath, "..", "Artifacts", "SingleCarryStateDivergence", "Traces")
                : OutputDirectory;
            path = Path.Combine(directory, runId + ".json");
        }
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        Record[] copy = new Record[recordCount];
        Array.Copy(records, copy, recordCount);
        File.WriteAllText(path, JsonUtility.ToJson(new TraceFile
        {
            runId = runId,
            unityVersion = Application.unityVersion,
            outputDirectory = Path.GetDirectoryName(path),
            capacityPerPeer = MaxRecordsPerPeer,
            overflow = overflow,
            metadataJson = metadataJson,
            measuredResponseMilliseconds = MeasuredResponseMilliseconds,
            records = copy
        }, true), Encoding.UTF8);
        exported = true;
        return path;
    }

    public static void Reset()
    {
        Enabled = false;
        records = null; peerIds = null; peerManagerIds = null; peerPlayerInstanceIds = null; peerCounts = null;
        recordCount = 0; nextSequence = 0; trackedObjectId = 0; trackedOwnerId = 0;
        runId = null; metadataJson = null; frozen = false; overflow = false;
        exported = false;
        pendingRequest = false;
        trackedRequestId = 0;
        MeasuredResponseMilliseconds = 0;
    }

    private static Record NewRecord(NetworkManager manager, NetworkObject player, int generation, EventKind kind)
    {
        int localTick = 0, serverTick = 0;
        bool hasLocal = manager != null && manager.IsListening;
        bool hasServer = manager != null && manager.IsListening;
        if (hasLocal) localTick = manager.LocalTime.Tick;
        if (hasServer) serverTick = manager.ServerTime.Tick;
        Record record = new Record
        {
            sequence = nextSequence++, eventKind = (int)kind, runId = runId,
            peerLocalClientId = manager != null ? manager.LocalClientId : 0,
            peerIsServer = manager != null && manager.IsServer,
            peerListening = manager != null && manager.IsListening,
            playerObjectId = player != null ? player.NetworkObjectId : trackedObjectId,
            ownerClientId = player != null ? player.OwnerClientId : trackedOwnerId,
            managerInstanceId = manager != null ? manager.GetInstanceID() : 0,
            playerInstanceId = player != null ? player.GetInstanceID() : 0,
            parentInstanceId = player != null && player.transform.parent != null ? player.transform.parent.GetInstanceID() : 0,
            parentNetworkObjectId = player != null && player.transform.parent != null
                && player.transform.parent.GetComponentInParent<NetworkObject>() != null
                    ? player.transform.parent.GetComponentInParent<NetworkObject>().NetworkObjectId : 0,
            hasParentNetworkObjectId = player != null && player.transform.parent != null
                && player.transform.parent.GetComponentInParent<NetworkObject>() != null,
            hasParentContext = player != null,
            localSpawnGeneration = generation,
            frame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble,
            localTick = localTick, serverTick = serverTick, hasLocalTick = hasLocal, hasServerTick = hasServer
        };
        if (player != null)
        {
            ClientNetworkTransform networkTransform = player.GetComponent<ClientNetworkTransform>();
            if (networkTransform != null)
            {
                record.inLocalSpace = networkTransform.InLocalSpace;
                record.useUnreliableDeltas = networkTransform.UseUnreliableDeltas;
                record.spaceContextAvailable = true;
            }
        }
        return record;
    }

    private static bool Append(Record r)
    {
        if (!IsRecording || r.playerObjectId != trackedObjectId || r.ownerClientId != trackedOwnerId) return false;
        int peerIndex = -1;
        for (int i = 0; i < MaxPeers; i++)
        {
            if (peerCounts[i] == 0 && peerManagerIds[i] == r.managerInstanceId) { peerIndex = i; break; }
            if (peerCounts[i] > 0 && peerManagerIds[i] == r.managerInstanceId
                && peerIds[i] == r.playerObjectId && peerPlayerInstanceIds[i] == r.playerInstanceId)
            { peerIndex = i; break; }
        }
        if (peerIndex < 0) return false;
        if (peerCounts[peerIndex] >= MaxRecordsPerPeer) { overflow = true; return false; }
        peerCounts[peerIndex]++;
        if (recordCount >= records.Length) { overflow = true; return false; }
        records[recordCount++] = r;
        return true;
    }
}
#endif
