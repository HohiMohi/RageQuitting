#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
public sealed partial class SingleCarryPlacementMovementNetworkPlayModeTests
{
    private static readonly Type TraceType = Type.GetType("SingleCarryStateDivergenceTrace, Assembly-CSharp");


    private readonly List<TrialDiagnostic> trialDiagnostics = new List<TrialDiagnostic>();
    private string diagnosticPath;
    private string checkpointPath;
    private string tracePath;
    private string traceOutputDirectory;
    private NetworkManager traceOwnerManager;
    private NetworkObject traceOwnerPlayer;
    private NetworkAimDiagnostic lastNetworkAimDiagnostic;

    partial void InitializeDiagnosticOutputs()
    {
        string configuredOutput = Environment.GetEnvironmentVariable("SINGLE_CARRY_TRACE_DIRECTORY");
        string outputDirectory = string.IsNullOrWhiteSpace(configuredOutput)
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts/SingleCarryDiagnostics/Traces"))
            : Path.GetFullPath(configuredOutput);
        Directory.CreateDirectory(outputDirectory);
        checkpointPath = Path.Combine(outputDirectory, $"cp-{Guid.NewGuid():N}.log");
        diagnosticPath = Path.Combine(outputDirectory, $"d-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(checkpointPath));
    }

    private object GetLastNetworkAimDiagnostic() => lastNetworkAimDiagnostic;

    partial void CaptureNetworkAimCheckpoint(NetworkObject ownerPlayer, NetworkObject serverOwnerPlayer,
        Component controller, int delayMs)
    {
        lastNetworkAimDiagnostic = CaptureNetworkAimDiagnostic(ownerPlayer, serverOwnerPlayer, controller);
        NetworkAimDiagnostic d = lastNetworkAimDiagnostic;
        AppendCheckpoint($"RemoteTest:pre-E-snapshot delay={delayMs}ms phase={d.poseSamplePhase} poseSource={d.serverPoseSampleSource} owner={d.ownerPositionAtE.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} renderedServerOwner={d.serverOwnerRenderedPositionPreE.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} ownerAimAnchor={d.ownerAimAnchor.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} renderedServerAimAnchor={d.serverRenderedAimAnchorPreE.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} rayOrigin={d.rayOrigin.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} rayDirection={d.rayDirection.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} renderedServerAnchorEstimate={d.preERenderedServerAimAnchor.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} residual={d.preERenderedAimOriginResidual.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} serverNetworkTransform={d.serverNetworkTransformType} spawned={d.serverNetworkTransformSpawned} canCommit={d.serverCanCommit} validation={d.localValidation}");
    }


    [Serializable]
    private sealed class DiagnosticReport { public TrialDiagnostic[] trials; }

    [Serializable]
    private sealed class TraceReport
    {
        public bool overflow;
        public string metadataJson;
        public double measuredResponseMilliseconds;
        public TraceRecord[] records;
    }

    [Serializable]
    private sealed class TraceRecord
    {
        public int sequence, eventKind, localSpawnGeneration, frame, localTick, serverTick, sourceTick, flags, branch, result;
        public string runId;
        public ulong peerLocalClientId, playerObjectId, ownerClientId, requestId, parentNetworkObjectId;
        public int managerInstanceId, playerInstanceId, parentInstanceId;
        public bool peerIsServer, peerListening, hasLocalTick, hasServerTick, hasSourceTick, hasRay, hasGhostPose;
        public bool spaceContextAvailable, inLocalSpace;
        public bool hasCachedPose, hasCompleteCache, hasAuthoritativePoseRead, hasTrustedAnchor, hasResidual, hasParentContext;
        public bool hasBodyPose, hasCameraPose, hasStatePose, isInitialSynchronization, useUnreliableDeltas;
        public bool hasMovementInput, placementActive, requestPending, movementBlocked;
        public Vector2 movementInput;
        public Vector3 bodyPosition, horizontalVelocity;
        public Vector3 rayOrigin, rayDirection, ghostPosition;
        public Quaternion ghostRotation;
    }

    [Serializable]
    private sealed class NetworkAimDiagnostic
    {
        public string poseSamplePhase;
        public string serverPoseSampleSource;
        public Vector3 ownerPositionAtE;
        public Vector3 serverOwnerRenderedPositionPreE;
        public Vector3 ownerAimAnchor;
        public Vector3 serverRenderedAimAnchorPreE;
        public Vector3 rayOrigin;
        public Vector3 rayDirection;
        public Vector3 preERenderedServerAimAnchor;
        public Quaternion ownerRotation;
        public Quaternion serverRenderedRotationPreE;
        public string serverNetworkTransformType;
        public string localValidation;
        public bool serverNetworkTransformSpawned;
        public bool serverCanCommit;
        public bool serverInLocalSpace;
        public float preERenderedAimOriginForwardOffset;
        public float preERenderedAimOriginResidual;
        public float ownerServerHorizontalSeparationPreE;
        public int ownerFrameAtCapture;
        public double ownerRealtimeAtCapture;
        public int ownerNetworkTickAtCapture;
        public float ownerDeltaTimeAtCapture;
        public float ownerUnscaledDeltaTimeAtCapture;
    }

    [Serializable]
    private sealed class TrialDiagnostic
    {
        public string kind;
        public int packetDelayMs;
        public int jitterMs;
        public int lossPercent;
        public string result;
        public string requestId;
        public float measuredResponseMs;
        public float pendingHorizontalDrift;
        public float resumedHorizontalDisplacement;
        public Vector3 previewPose;
        public Vector3 authoritativePose;
        public Vector3 movedOwnerPose;
        public Vector3 ownerAtPendingPose;
        public NetworkAimDiagnostic networkAim;
        public string serverRpcAimDiagnostic;
        public string tracePath;
    }

    [Serializable]
    private sealed class TraceMetadata
    {
        public string caseName, unityVersion, networkTransport, defaultRpcDelivery, tickSemantics, sourceHashesJson, simulatorPresetName;
        public int tickRate, packetDelayMs, jitterMs, lossPercent, lossInterval;
        public bool ownerUseUnreliableDeltas, serverUseUnreliableDeltas;
        public bool simulatorConnected;
        public int serverTickRate, ownerSyncAxes, serverSyncAxes;
        public float ownerPositionThreshold, serverPositionThreshold, ownerRotationThreshold, serverRotationThreshold;
        public bool ownerQuaternionSync, serverQuaternionSync, ownerHalfFloat, serverHalfFloat;
        public bool ownerInterpolate, serverInterpolate, ownerInLocalSpace, serverInLocalSpace;
    }


    partial void AppendCheckpoint(string checkpoint)
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        File.AppendAllText(checkpointPath, $"{DateTime.UtcNow:O} {checkpoint}{Environment.NewLine}");
#endif
    }


    partial void WritePanelColliderSnapshot(string stage, int delayMs, Component controller)
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        string path = Path.Combine(Path.GetDirectoryName(diagnosticPath), $"pc-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, $"stage={stage}{Environment.NewLine}delayMs={delayMs}{Environment.NewLine}capturedUtc={DateTime.UtcNow:O}{Environment.NewLine}{DescribeSurfaceConfirmState(controller)}{Environment.NewLine}");
#endif
    }


    partial void AddDiagnostic(string kind, int delay, int jitter, int loss, string result, Vector3 preview, Vector3 authoritative,
        float elapsedMs, string requestId, Vector3 movedOwner = default, Vector3 pendingOwner = default,
        float pendingDrift = 0f, float resumeDisplacement = 0f, object networkAim = null,
        string serverRpcAimDiagnostic = null)
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        trialDiagnostics.Add(new TrialDiagnostic
        {
            kind = kind, packetDelayMs = delay, jitterMs = jitter, lossPercent = loss, result = result, requestId = requestId,
            measuredResponseMs = elapsedMs,
            pendingHorizontalDrift = pendingDrift,
            resumedHorizontalDisplacement = resumeDisplacement,
            previewPose = preview, authoritativePose = authoritative, movedOwnerPose = movedOwner,
            ownerAtPendingPose = pendingOwner, networkAim = networkAim as NetworkAimDiagnostic, serverRpcAimDiagnostic = serverRpcAimDiagnostic
            , tracePath = tracePath
        });
        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath));
        File.WriteAllText(diagnosticPath, JsonUtility.ToJson(new DiagnosticReport { trials = trialDiagnostics.ToArray() }, true));
#endif
    }

    partial void BeginTrace(NetworkManager ownerManager, NetworkObject ownerPlayer, NetworkManager serverManager,
        NetworkObject serverPlayer, int delayMs, string caseName)
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        Assert.That(TraceType, Is.Not.Null, "Editor-only divergence recorder must be present in Editor tests.");
        ResetTrace();
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputDirectory = Path.Combine(projectRoot, "Artifacts/SingleCarryDiagnostics/Traces");
        string configuredOutput = Environment.GetEnvironmentVariable("SINGLE_CARRY_TRACE_DIRECTORY");
        traceOutputDirectory = string.IsNullOrWhiteSpace(configuredOutput) ? outputDirectory : Path.GetFullPath(configuredOutput);
        TraceType.GetProperty("OutputDirectory").SetValue(null, traceOutputDirectory);
        TraceType.GetProperty("Enabled").SetValue(null, true);
        traceOwnerManager = ownerManager;
        traceOwnerPlayer = ownerPlayer;
        string[] sourcePaths =
        {
            "Assets/Scripts/NetworkManagement/ClientNetworkTransform.cs",
            "Assets/Scripts/NewScripts/PlayerSingleCarryPlacementController.cs",
            "Assets/Scripts/NewScripts/PlayerSingleCarryPlacementNetworkPreview.cs",
            "Assets/StarterAssets/FirstPersonController/Scripts/FirstPersonController.cs",
            "Assets/Diagnostics/SingleCarryPlacement/SingleCarryStateDivergenceTrace.cs",
            "Assets/Diagnostics/SingleCarryPlacement/ClientNetworkTransform.Diagnostics.cs",
            "Assets/Diagnostics/SingleCarryPlacement/PlayerSingleCarryPlacementController.Diagnostics.cs",
            "Assets/Diagnostics/SingleCarryPlacement/PlayerSingleCarryPlacementNetworkPreview.Diagnostics.cs",
            "Assets/Diagnostics/SingleCarryPlacement/FirstPersonController.Diagnostics.cs",
            "Assets/Tests/PlayMode/SingleCarryPlacementMovementNetworkPlayModeTests.cs",
            "Assets/Tests/PlayMode/Diagnostics/SingleCarryPlacementMovementNetworkPlayModeTests.Diagnostics.cs"
        };
        var hashes = new Dictionary<string, string>();
        foreach (string relativePath in sourcePaths)
            hashes[relativePath] = Sha256(Path.Combine(projectRoot, relativePath));
        Component ownerTransform = ownerPlayer.GetComponent(typeof(NetworkTransform));
        Component serverTransform = serverPlayer.GetComponent(typeof(NetworkTransform));
        bool ownerUnreliableDeltas = ReadUnreliableDeltas(ownerTransform);
        bool serverUnreliableDeltas = ReadUnreliableDeltas(serverTransform);
        int ownerSyncAxes = ReadSyncAxes(ownerTransform);
        int serverSyncAxes = ReadSyncAxes(serverTransform);
        object simulatorPreset = simulator != null ? simulator.GetType().GetProperty("ConnectionPreset").GetValue(simulator) : null;
        int actualDelay = ReadIntProperty(simulatorPreset, "PacketDelayMs");
        int actualJitter = ReadIntProperty(simulatorPreset, "PacketJitterMs");
        int actualLossInterval = ReadIntProperty(simulatorPreset, "PacketLossInterval");
        int actualLossPercent = ReadIntProperty(simulatorPreset, "PacketLossPercent");
        Assert.That(actualDelay, Is.EqualTo(delayMs), "Trace metadata must reflect the actual live simulator delay.");
        Assert.That(actualJitter, Is.EqualTo(0));
        Assert.That(actualLossInterval, Is.EqualTo(0));
        Assert.That(actualLossPercent, Is.EqualTo(0));
        var metadata = new TraceMetadata
        {
            caseName = caseName,
            unityVersion = Application.unityVersion,
            networkTransport = ownerManager.NetworkConfig.NetworkTransport.GetType().FullName,
            defaultRpcDelivery = "ReliableFragmentedSequenced (NGO default ServerRpc delivery)",
            tickSemantics = "Manager local/server ticks are public observers, not the exact NetworkTransform sampling tick; AuthorityPush is after UpdateTransformState and is not a UDP-send confirmation. Receiver callback records occur after NGO applied the state.",
            sourceHashesJson = JsonUtility.ToJson(new SourceHashReport { entries = ToHashEntries(hashes) }),
            tickRate = (int)ownerManager.NetworkConfig.TickRate,
            serverTickRate = (int)serverManager.NetworkConfig.TickRate,
            packetDelayMs = actualDelay,
            jitterMs = actualJitter,
            lossPercent = actualLossPercent,
            lossInterval = actualLossInterval,
            simulatorPresetName = simulatorPreset != null ? ReadStringProperty(simulatorPreset, "Name") : "none",
            simulatorConnected = simulator != null && (bool)simulator.GetType().GetProperty("IsConnected").GetValue(simulator),
            ownerUseUnreliableDeltas = ownerUnreliableDeltas,
            serverUseUnreliableDeltas = serverUnreliableDeltas,
            ownerSyncAxes = ownerSyncAxes,
            serverSyncAxes = serverSyncAxes,
            ownerPositionThreshold = ReadFloatField(ownerTransform, "PositionThreshold"),
            serverPositionThreshold = ReadFloatField(serverTransform, "PositionThreshold"),
            ownerRotationThreshold = ReadFloatField(ownerTransform, "RotAngleThreshold"),
            serverRotationThreshold = ReadFloatField(serverTransform, "RotAngleThreshold"),
            ownerQuaternionSync = ReadBoolField(ownerTransform, "UseQuaternionSynchronization"),
            serverQuaternionSync = ReadBoolField(serverTransform, "UseQuaternionSynchronization"),
            ownerHalfFloat = ReadBoolField(ownerTransform, "UseHalfFloatPrecision"),
            serverHalfFloat = ReadBoolField(serverTransform, "UseHalfFloatPrecision"),
            ownerInterpolate = ReadBoolField(ownerTransform, "Interpolate"),
            serverInterpolate = ReadBoolField(serverTransform, "Interpolate"),
            ownerInLocalSpace = ReadBoolField(ownerTransform, "InLocalSpace"),
            serverInLocalSpace = ReadBoolField(serverTransform, "InLocalSpace")
        };
        tracePath = null;
        TraceType.GetMethod("Begin").Invoke(null, new object[] { ownerManager, ownerPlayer, JsonUtility.ToJson(metadata) });
        string runId = (string)TraceType.GetProperty("CurrentRunId").GetValue(null);
        diagnosticPath = Path.Combine(traceOutputDirectory, runId + "-summary.json");
        int ownerGeneration = GetTraceGeneration(ownerPlayer);
        int serverGeneration = GetTraceGeneration(serverPlayer);
        Assert.That((bool)TraceType.GetMethod("RegisterReplica").Invoke(null,
            new object[] { serverManager, serverPlayer, serverGeneration }), Is.True,
            "The matching server-side replica must be explicitly mapped into this bounded session.");
        Assert.That(ownerGeneration, Is.GreaterThanOrEqualTo(0));
#endif
    }

    [Serializable]
    private sealed class SourceHashEntry { public string path, sha256; }
    [Serializable]
    private sealed class SourceHashReport { public SourceHashEntry[] entries; }

    private static SourceHashEntry[] ToHashEntries(Dictionary<string, string> hashes)
    {
        var result = new SourceHashEntry[hashes.Count];
        int index = 0;
        foreach (KeyValuePair<string, string> pair in hashes)
            result[index++] = new SourceHashEntry { path = pair.Key, sha256 = pair.Value };
        return result;
    }

    private static string Sha256(string path)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }

    private static int GetTraceGeneration(NetworkObject player)
    {
        Component networkTransform = player.GetComponent(Type.GetType("ClientNetworkTransform, Assembly-CSharp"));
        return networkTransform != null
            ? (int)networkTransform.GetType().GetProperty("EditorLocalSpawnGeneration").GetValue(networkTransform)
            : 0;
    }

    private static bool ReadUnreliableDeltas(Component transform)
    {
        FieldInfo field = transform != null ? transform.GetType().GetField("UseUnreliableDeltas") : null;
        return field != null && (bool)field.GetValue(transform);
    }

    private static int ReadSyncAxes(Component transform)
    {
        int axes = 0;
        if (ReadBoolField(transform, "SyncPositionX")) axes |= 1;
        if (ReadBoolField(transform, "SyncPositionY")) axes |= 2;
        if (ReadBoolField(transform, "SyncPositionZ")) axes |= 4;
        if (ReadBoolField(transform, "SyncRotAngleX")) axes |= 8;
        if (ReadBoolField(transform, "SyncRotAngleY")) axes |= 16;
        if (ReadBoolField(transform, "SyncRotAngleZ")) axes |= 32;
        return axes;
    }

    private static bool ReadBoolField(Component target, string name)
    {
        FieldInfo field = target != null ? target.GetType().GetField(name) : null;
        return field != null && (bool)field.GetValue(target);
    }

    private static float ReadFloatField(Component target, string name)
    {
        FieldInfo field = target != null ? target.GetType().GetField(name) : null;
        return field != null ? Convert.ToSingle(field.GetValue(target)) : float.NaN;
    }

    private static int ReadIntProperty(object target, string propertyName)
    {
        PropertyInfo property = target != null ? target.GetType().GetProperty(propertyName) : null;
        return property != null ? Convert.ToInt32(property.GetValue(target)) : 0;
    }

    private static string ReadStringProperty(object target, string propertyName)
    {
        PropertyInfo property = target != null ? target.GetType().GetProperty(propertyName) : null;
        return property != null ? property.GetValue(target) as string : string.Empty;
    }

    partial void ExportAndValidateTrace(ulong requestId, string result, double responseMilliseconds,
        string expectedBranch = null, bool requireMovement = true, bool isHost = false)
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        string runId = (string)TraceType.GetProperty("CurrentRunId").GetValue(null);
        Assert.That(runId, Is.Not.Null.And.Not.Empty, "Trace session must have a unique run id.");
        TraceType.GetProperty("MeasuredResponseMilliseconds").SetValue(null, responseMilliseconds);
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string directory = string.IsNullOrWhiteSpace(traceOutputDirectory)
            ? Path.Combine(projectRoot, "Artifacts/SingleCarryDiagnostics/Traces")
            : traceOutputDirectory;
        tracePath = Path.Combine(directory, runId + ".json");
        string exported = (string)TraceType.GetMethod("ExportFrozenSnapshot").Invoke(null, new object[] { tracePath });
        Assert.That(exported, Is.Not.Null.And.Not.Empty, "Timeline export must complete after its response freeze.");
        Assert.That(File.Exists(tracePath), Is.True, "Timeline export must write the requested trace file.");
        TraceReport report = JsonUtility.FromJson<TraceReport>(File.ReadAllText(tracePath));
        Assert.That(report, Is.Not.Null);
        Assert.That(report.overflow, Is.False, "A truncated trace cannot be used to infer pose divergence.");
        Assert.That(Math.Abs(report.measuredResponseMilliseconds - responseMilliseconds), Is.LessThan(0.000001d));
        Assert.That(report.records, Is.Not.Null.And.Not.Empty);
        Assert.That(report.records.Length, Is.LessThanOrEqualTo(8 * 8192));
        TraceRecord[] movementRecords = Array.FindAll(report.records, r => r.eventKind == 0);
        Assert.That(Array.TrueForAll(movementRecords, r => r.hasBodyPose), Is.True,
            "Every movement-end record carries an available body pose.");
        TraceRecord[] baselineRecords = Array.FindAll(report.records, r => r.eventKind == 10);
        Assert.That(Array.TrueForAll(baselineRecords, r => r.hasCachedPose == r.hasCompleteCache), Is.True,
            "Peer baselines must report cache-value and cache-completeness availability consistently.");
        TraceRecord[] clearedCacheRecords = Array.FindAll(report.records, r => r.eventKind == 7);
        Assert.That(Array.TrueForAll(clearedCacheRecords, r => !r.hasStatePose), Is.True,
            "Cache clear records do not represent incoming NetworkTransform state poses.");
        TraceRecord request = Array.Find(report.records, r => r.eventKind == 2 && r.requestId == requestId);
        Assert.That(request, Is.Not.Null, "The outgoing placement request must be correlated by request id.");
        TraceRecord preview = Array.Find(report.records, r => r.eventKind == 1 && r.branch == request.branch
            && r.managerInstanceId == request.managerInstanceId && r.playerInstanceId == request.playerInstanceId);
        Assert.That(preview, Is.Not.Null, "The submitted preview revision must resolve on the same registered player instance.");
        Assert.That(request.hasRay && preview.hasRay, Is.True);
        Assert.That(request.rayOrigin, Is.EqualTo(preview.rayOrigin), "E must capture the actual ray from its selected preview revision.");
        Assert.That(request.rayDirection, Is.EqualTo(preview.rayDirection));
        Assert.That(request.placementActive && request.requestPending && request.movementBlocked, Is.True,
            "The exact outgoing request record captures the pending stop state after horizontal movement is stopped.");
        if (requireMovement)
            Assert.That(Array.Exists(report.records, r => r.eventKind == 0 && r.hasBodyPose && r.hasMovementInput
                && r.movementInput.sqrMagnitude > 0.01f), Is.True,
                "The timeline must include real movement input from the CharacterController Update loop.");
        if ((request.flags & 1) == 0)
        {
            Assert.That(request.hasGhostPose && preview.hasGhostPose, Is.True);
            Assert.That(Vector3.Distance(request.ghostPosition, preview.ghostPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(request.ghostRotation, preview.ghostRotation), Is.LessThan(0.001f));
        }
        Assert.That(Array.Exists(report.records, r => r.eventKind == 8 && r.requestId == requestId), Is.True,
            "Server validation must be represented by the real existing validation read.");
        TraceRecord[] serverTransformRecords = Array.FindAll(report.records, r => r.peerIsServer && r.eventKind >= 4 && r.eventKind <= 7);
        TraceRecord[] serverReceiveCallbacks = Array.FindAll(serverTransformRecords, r => r.eventKind == 4 || r.eventKind == 5);
        if (!isHost && serverReceiveCallbacks.Length > 0)
        {
            Assert.That(Array.Exists(serverTransformRecords, r => r.eventKind == 6 || r.eventKind == 7), Is.True,
                "Observed server receive callbacks must be accompanied by an authoritative cache update or clear.");
        }
        Assert.That(Array.Exists(report.records, r => r.eventKind == 9 && r.requestId == requestId && r.result == (result == "Accepted" ? 0 : ResultEnumValue(result))), Is.True,
            "The owner response must freeze the timeline before placement teardown.");
        Assert.That(Array.Exists(report.records, r => r.eventKind == 11 && r.requestId == requestId), Is.True);
        TraceRecord[] starts = Array.FindAll(report.records, r => r.eventKind == 10);
        Assert.That(starts.Length, Is.GreaterThanOrEqualTo(isHost ? 1 : 2), "Each owner/server replica needs an explicit baseline cache snapshot.");
        Assert.That(Array.Exists(starts, r => r.peerIsServer), Is.True);
        if (!isHost) Assert.That(Array.Exists(starts, r => !r.peerIsServer), Is.True);
        if (expectedBranch != null)
        {
            int expectedBranchHash = Animator.StringToHash(expectedBranch);
            TraceRecord validation = Array.Find(report.records, r => r.eventKind == 8 && r.requestId == requestId);
            Assert.That(validation.branch, Is.EqualTo(expectedBranchHash));
            if (expectedBranch == "MissingReceivedPose") Assert.That(validation.hasAuthoritativePoseRead, Is.False);
            if (expectedBranch == "ResidualExceeded") Assert.That(validation.hasResidual, Is.True);
        }
        foreach (IGrouping<int, TraceRecord> peer in report.records.GroupBy(r => r.managerInstanceId))
            Assert.That(peer.Count(), Is.LessThanOrEqualTo(8192), "Each recorded peer has a strict per-session record cap.");
        Assert.That(Array.Exists(report.records, r => r.hasParentContext && r.spaceContextAvailable), Is.True,
            "Timeline records must include parent and coordinate-space context.");
        return;
#else
        return new NetworkAimDiagnostic { poseSamplePhase = "diagnostics-disabled" };
#endif
    }

    private static int ResultEnumValue(string value)
    {
        Type resultType = Type.GetType("SingleCarryPlacementNetworkResult, Assembly-CSharp");
        return (int)Enum.Parse(resultType, value);
    }

    partial void ResetTrace()
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        trialDiagnostics.Clear();
        traceOwnerManager = null;
        traceOwnerPlayer = null;
        tracePath = null;
        traceOutputDirectory = null;
        lastNetworkAimDiagnostic = null;
        if (TraceType != null) TraceType.GetMethod("Reset")?.Invoke(null, null);
#endif
    }

    partial void ExportFallbackTrace()
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        if (TraceType == null || traceOwnerManager == null || traceOwnerPlayer == null) return;
        bool recording = (bool)TraceType.GetProperty("IsRecording").GetValue(null);
        bool frozen = (bool)TraceType.GetProperty("IsFrozen").GetValue(null);
        bool exported = (bool)TraceType.GetProperty("WasExported").GetValue(null);
        if (exported || (!recording && !frozen)) return;
        if (recording)
        {
            TraceType.GetMethod("Freeze").Invoke(null,
                new object[] { traceOwnerManager, traceOwnerPlayer, GetTraceGeneration(traceOwnerPlayer), 0UL, -1 });
            TraceType.GetProperty("MeasuredResponseMilliseconds").SetValue(null, -1d);
        }
        string runId = (string)TraceType.GetProperty("CurrentRunId").GetValue(null);
        tracePath = Path.Combine(traceOutputDirectory, runId + (recording ? "-abort.json" : "-fallback.json"));
        TraceType.GetMethod("ExportFrozenSnapshot").Invoke(null, new object[] { tracePath });
#endif
    }

    partial void SetTaskDiagnosticPath(string caseName)
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        string configuredOutput = Environment.GetEnvironmentVariable("SINGLE_CARRY_TRACE_DIRECTORY");
        string outputDirectory = string.IsNullOrWhiteSpace(configuredOutput)
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts/SingleCarryDiagnostics/Traces"))
            : Path.GetFullPath(configuredOutput);
        diagnosticPath = Path.Combine(outputDirectory, $"d-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath));
#endif
    }

    private static NetworkAimDiagnostic CaptureNetworkAimDiagnostic(NetworkObject ownerPlayer,
        NetworkObject serverOwnerPlayer, Component controller)
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        Ray aimRay = (Ray)ControllerType.GetField("lastRenderedAimRay", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(controller);
        Component ownerInteraction = ownerPlayer.GetComponent(InteractionType);
        Component serverInteraction = serverOwnerPlayer.GetComponent(InteractionType);
        Transform ownerAnchor = (Transform)InteractionType.GetProperty("SingleCarryPlacementAimAnchor").GetValue(ownerInteraction);
        Transform serverAnchor = (Transform)InteractionType.GetProperty("SingleCarryPlacementAimAnchor").GetValue(serverInteraction);
        NetworkTransform serverNetworkTransform = serverOwnerPlayer.GetComponent<NetworkTransform>();
        // This capture runs before E and intentionally preserves the old GetSpaceRelative
        // diagnostic source. It is a render/interpolation snapshot, not the pose at RPC handling.
        Vector3 serverPosition = serverNetworkTransform.GetSpaceRelativePosition(true);
        Quaternion serverRotation = serverNetworkTransform.GetSpaceRelativeRotation(true);
        Vector3 anchorLocalOffset = serverOwnerPlayer.transform.InverseTransformPoint(serverAnchor.position);
        Vector3 trustedServerAnchor = serverPosition
            + serverRotation * Vector3.Scale(anchorLocalOffset, serverOwnerPlayer.transform.localScale);
        if (serverNetworkTransform.InLocalSpace && serverOwnerPlayer.transform.parent != null)
        {
            trustedServerAnchor = serverOwnerPlayer.transform.parent.TransformPoint(serverPosition)
                + serverOwnerPlayer.transform.parent.rotation * serverRotation
                * Vector3.Scale(anchorLocalOffset, serverOwnerPlayer.transform.localScale);
        }
        Vector3 offset = aimRay.origin - trustedServerAnchor;
        float forwardOffset = Vector3.Dot(offset, aimRay.direction);
        Vector3 lateralOffset = offset - aimRay.direction * forwardOffset;
        float residual = (lateralOffset + aimRay.direction * (forwardOffset - Mathf.Clamp(forwardOffset, 0f, 0.2f))).magnitude;
        object validation = ControllerType.GetProperty("LastValidation").GetValue(controller);
        string validationStatus = validation.GetType().GetProperty("Status").GetValue(validation).ToString();
        return new NetworkAimDiagnostic
        {
            poseSamplePhase = "pre-E",
            serverPoseSampleSource = "pre-E rendered/interpolated NetworkTransform.GetSpaceRelativePosition/Rotation(true) diagnostic only",
            ownerPositionAtE = ownerPlayer.transform.position,
            serverOwnerRenderedPositionPreE = serverOwnerPlayer.transform.position,
            ownerAimAnchor = ownerAnchor.position,
            serverRenderedAimAnchorPreE = serverAnchor.position,
            rayOrigin = aimRay.origin,
            rayDirection = aimRay.direction,
            preERenderedServerAimAnchor = trustedServerAnchor,
            ownerRotation = ownerPlayer.transform.rotation,
            serverRenderedRotationPreE = serverRotation,
            serverNetworkTransformType = serverNetworkTransform.GetType().FullName,
            localValidation = validationStatus,
            serverNetworkTransformSpawned = serverNetworkTransform.IsSpawned,
            serverCanCommit = serverNetworkTransform.CanCommitToTransform,
            serverInLocalSpace = serverNetworkTransform.InLocalSpace,
            preERenderedAimOriginForwardOffset = forwardOffset,
            preERenderedAimOriginResidual = residual,
            ownerServerHorizontalSeparationPreE = HorizontalDistance(ownerPlayer.transform.position, serverOwnerPlayer.transform.position),
            ownerFrameAtCapture = Time.frameCount,
            ownerRealtimeAtCapture = Time.realtimeSinceStartupAsDouble,
            ownerNetworkTickAtCapture = ownerPlayer.NetworkManager != null ? ownerPlayer.NetworkManager.LocalTime.Tick : 0,
            ownerDeltaTimeAtCapture = Time.deltaTime,
            ownerUnscaledDeltaTimeAtCapture = Time.unscaledDeltaTime
        };
#else
        return null;
#endif
    }

    private static string CaptureServerRpcAimDiagnostic(NetworkObject serverPlayer)
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        Component serverPreview = serverPlayer.GetComponent(PreviewType);
        object diagnostic = PreviewType.GetMethod("GetEditorAimValidationDiagnostic").Invoke(serverPreview, null);
        return JsonUtility.ToJson(diagnostic, true);
#else
        return string.Empty;
#endif
    }

    partial void AssertServerAimBranch(string json, string expectedBranch, ulong requestId, string expectedResult)
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        Assert.That(string.IsNullOrEmpty(json), Is.False, "The server RPC diagnostic must be attached to the trace.");
        Type diagnosticType = PreviewType.GetNestedType("EditorAimValidationDiagnostic", BindingFlags.Public);
        object diagnostic = JsonUtility.FromJson(json, diagnosticType);
        Assert.That(diagnostic.GetType().GetField("requestId").GetValue(diagnostic), Is.EqualTo(requestId));
        Assert.That(diagnostic.GetType().GetField("branch").GetValue(diagnostic).ToString(), Is.EqualTo(expectedBranch));
        Assert.That((bool)diagnostic.GetType().GetField("hasFinalResult").GetValue(diagnostic), Is.True);
        int finalResult = (int)diagnostic.GetType().GetField("finalResult").GetValue(diagnostic);
        Type resultType = PreviewType.Assembly.GetType("SingleCarryPlacementNetworkResult");
        Assert.That(Enum.GetName(resultType, finalResult), Is.EqualTo(expectedResult));
#endif
    }


}
#endif
