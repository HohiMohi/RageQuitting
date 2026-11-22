using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

public sealed class BeaverScoutMudballPlayModeTests
{
    private sealed class TemporaryScoutNavMesh : IDisposable
    {
        private readonly GameObject surface;
        private readonly NavMeshData data;
        private NavMeshDataInstance instance;

        public TemporaryScoutNavMesh(Vector3 center, float size)
        {
            surface = GameObject.CreatePrimitive(PrimitiveType.Plane);
            surface.name = "Scout test NavMesh surface";
            surface.transform.position = center;
            surface.transform.localScale = Vector3.one * (size / 10f);
            NavMeshBuildSource source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Mesh,
                sourceObject = surface.GetComponent<MeshFilter>().sharedMesh,
                transform = surface.transform.localToWorldMatrix,
                area = 0
            };
            data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> { source },
                new Bounds(center, new Vector3(size + 4f, 4f, size + 4f)), Vector3.zero, Quaternion.identity);
            Assert.That(data, Is.Not.Null, "The test fixture must build its temporary NavMesh.");
            instance = NavMesh.AddNavMeshData(data);
            Assert.That(instance.valid, Is.True, "The test fixture must register its temporary NavMesh.");
        }

        public void Dispose()
        {
            if (instance.valid) instance.Remove();
            if (data != null) UnityEngine.Object.DestroyImmediate(data);
            if (surface != null) UnityEngine.Object.DestroyImmediate(surface);
        }
    }

    private static readonly Type ThrowType = Type.GetType("BeaverScoutMudThrow, Assembly-CSharp");
    private static readonly Type MudballType = Type.GetType("BeaverScoutMudball, Assembly-CSharp");
    private static readonly Type PlayerHealthType = Type.GetType("PlayerHealth, Assembly-CSharp");
    private static readonly Type ScoutBehaviorType = Type.GetType("BeaverScoutBehaviorSO, Assembly-CSharp");
    private static readonly Type FactionType = Type.GetType("NPCFactionSO, Assembly-CSharp");
    private static readonly Type FactionMemberType = Type.GetType("NPCFactionMember, Assembly-CSharp");
    private static readonly Type BrainType = Type.GetType("NPCBrain, Assembly-CSharp");
    private static readonly Type AnimationControllerType = Type.GetType("NPCAnimationController, Assembly-CSharp");
    private static readonly Type VisualControllerType = Type.GetType("NPCVisualController, Assembly-CSharp");
    private static readonly Type MudControllerType = Type.GetType("PlayerMudStatusController, Assembly-CSharp");
    private static readonly Type StatusControllerType = Type.GetType("PlayerTemporaryStatusController, Assembly-CSharp");
    private static readonly Type ImpulseControllerType = Type.GetType("PlayerExternalImpulseController, Assembly-CSharp");
    private static readonly Type PlayerInputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
    private static readonly Type NetworkObjectType = Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime");
    private PlayModeTestScope scope;
    private TemporaryScoutNavMesh scopedScoutNavMesh;

    [UnityTearDown]
    public IEnumerator CleanupNetworkFixture()
    {
        if (scope != null)
        {
            PlayModeTestScope cleanup = scope;
            yield return cleanup.Cleanup();
            scope = null;
            cleanup.AssertCleanupSucceeded();
        }
        if (scopedScoutNavMesh != null)
        {
            scopedScoutNavMesh.Dispose();
            scopedScoutNavMesh = null;
        }
    }

    [Test]
    public void TargetValidationSkipsShooterCollidersAndRejectsOcclusionAndOutOfRangeTargets()
    {
        GameObject scout = new GameObject("Mudball scout");
        Vector3 offset = new Vector3(100f, 50f, 100f);
        scout.transform.position = offset;
        scout.AddComponent<CapsuleCollider>();
        Component thrower = scout.AddComponent(ThrowType);
        Transform release = new GameObject("Release point").transform;
        release.SetParent(scout.transform, false);
        release.localPosition = new Vector3(0f, 1.1f, 0.45f);
        SetField(thrower, "releasePoint", release);

        GameObject behind = CreatePlayer("Player behind scout", offset + new Vector3(0f, 0f, -4f));
        GameObject covered = CreatePlayer("Covered nearer player", offset + new Vector3(0f, 0f, 4f));
        GameObject visible = CreatePlayer("Visible farther player", offset + new Vector3(4f, 0f, 6f));
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Cover wall";
        wall.transform.position = offset + new Vector3(0f, 1f, 2f);
        wall.transform.localScale = new Vector3(1f, 3f, 0.4f);
        Physics.SyncTransforms();

        try
        {
            Assert.That(CanTarget(thrower, behind, 10f, true), Is.True,
                "The ray must ignore the scout's own body while validating a target behind it.");
            Assert.That(CanTarget(thrower, covered, 10f, true), Is.False,
                "A closer player behind solid cover is not a valid aim target.");
            Assert.That(CanTarget(thrower, visible, 10f, true), Is.True,
                "An occluded nearer candidate must not prevent validating a farther clear candidate.");

            GameObject outOfRange = CreatePlayer("Out of range", offset + new Vector3(0f, 0f, 10.1f));
            Physics.SyncTransforms();
            Assert.That(CanTarget(thrower, outOfRange, 10f, false), Is.False,
                "Range is measured from the scout root and is strict at the configured limit.");
            UnityEngine.Object.DestroyImmediate(outOfRange);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(scout);
            UnityEngine.Object.DestroyImmediate(behind);
            UnityEngine.Object.DestroyImmediate(covered);
            UnityEngine.Object.DestroyImmediate(visible);
            UnityEngine.Object.DestroyImmediate(wall);
        }
    }

    [Test]
    public void CancellingWindupLeavesCooldownAvailable()
    {
        GameObject scout = new GameObject("Mudball cooldown scout");
        Component thrower = scout.AddComponent(ThrowType);
        Transform release = new GameObject("Release point").transform;
        release.SetParent(scout.transform, false);
        release.localPosition = Vector3.up;
        SetField(thrower, "releasePoint", release);
        GameObject player = CreatePlayer("Locked target", new Vector3(0f, 0f, 2f));
        Physics.SyncTransforms();
        UnityEngine.Object ballPrefab = LoadAsset("Assets/Prefabs/New/NPC/BeaverScoutMudball.prefab", Type.GetType("UnityEngine.GameObject, UnityEngine.CoreModule"));
        Component ball = ((GameObject)ballPrefab).GetComponent(Type.GetType("BeaverScoutMudball, Assembly-CSharp"));
        UnityEngine.Object profile = LoadAsset("Assets/ScriptableObjectAssets/New/NPC/BeaverScoutMudImpulse.asset", Type.GetType("ExternalImpulseProfileSO, Assembly-CSharp"));

        try
        {
            MethodInfo begin = ThrowType.GetMethod("TryBeginWindup");
            object[] arguments = { player.GetComponent(PlayerHealthType), player.transform, 10f, false, (LayerMask)~0,
                5f, 45f, ball, 15f, profile, new Func<bool>(() => true) };
            Assert.That((bool)begin.Invoke(thrower, arguments), Is.True);
            ThrowType.GetMethod("CancelWindup").Invoke(thrower, null);
            Assert.That((float)ThrowType.GetProperty("NextThrowAt").GetValue(thrower), Is.Zero,
                "Cancellation must not start the per-scout cooldown.");
            Assert.That((bool)begin.Invoke(thrower, arguments), Is.True,
                "The scout can acquire a new target after a cancelled windup.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(scout);
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [UnityTest]
    public IEnumerator ScoutVisitMemoryOnlyRearmsAfterLeavingConfiguredRadius()
    {
        GameObject scout = null;
        GameObject player = null;
        GameObject unseenPlayer = null;
        TemporaryScoutNavMesh scoutNavMesh = null;
        ScriptableObject behavior = null;
        ScriptableObject playerFaction = null;
        object controller = null;
        Component thrower = null;
        Component factionMember = null;

        try
        {
            scout = new GameObject("Visit memory scout");
            player = CreatePlayer("Visit memory player", new Vector3(0f, 0f, 4f));
            behavior = (ScriptableObject)ScriptableObject.CreateInstance(ScoutBehaviorType);
            playerFaction = (ScriptableObject)ScriptableObject.CreateInstance(FactionType);
            UnityEngine.Object mudballPrefab = LoadAsset("Assets/Prefabs/New/NPC/BeaverScoutMudball.prefab", typeof(GameObject));
            Component mudball = ((GameObject)mudballPrefab).GetComponent(MudballType);
            SetField(playerFaction, "factionId", "Player");
            SetField(behavior, "playerFaction", playerFaction);
            SetField(behavior, "mudballPrefab", mudball);
            SetField(behavior, "mudballRequireLineOfSight", false);
            factionMember = player.AddComponent(FactionMemberType);
            FactionMemberType.GetMethod("SetFaction").Invoke(factionMember, new object[] { playerFaction });
            Component brain = scout.AddComponent(BrainType);
            scoutNavMesh = new TemporaryScoutNavMesh(Vector3.zero, 20f);
            Assert.That(((NavMeshAgent)brain.GetComponent(typeof(NavMeshAgent))).Warp(Vector3.zero), Is.True,
                "The fixture scout must be placed on its temporary NavMesh.");
            thrower = scout.AddComponent(ThrowType);
            Transform releasePoint = new GameObject("Release point").transform;
            releasePoint.SetParent(scout.transform, false);
            releasePoint.localPosition = Vector3.up;
            SetField(thrower, "releasePoint", releasePoint);
            controller = ScoutBehaviorType.GetMethod("CreateController").Invoke(behavior, new object[] { brain });

            Type controllerType = controller.GetType();
            MethodInfo updateVisitMemory = controllerType.GetMethod("UpdatePlayerVisitMemory", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo scan = controllerType.GetMethod("TryFindAndEnterIdleTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Component health = player.GetComponent(PlayerHealthType);
            Physics.SyncTransforms();
            controllerType.GetMethod("Enter").Invoke(controller, null);
            Assert.That((bool)scan.Invoke(controller, null), Is.True);
            Assert.That(controllerType.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller).ToString(),
                Is.EqualTo("NoticingPlayer"));
            Assert.That((bool)ThrowType.GetProperty("IsWindingUp").GetValue(thrower), Is.False,
                "The first encounter notices and follows without starting a mudball windup.");

            FieldInfo memoryField = controllerType.GetField("playerVisitMemory", BindingFlags.Instance | BindingFlags.NonPublic);
            IDictionary memories = (IDictionary)memoryField.GetValue(controller);
            object memory = memories[health];
            Assert.That(memory.GetType().GetField("State").GetValue(memory).ToString(), Is.EqualTo("InitialVisit"),
                "The initial visit cannot trigger a mudball.");
            updateVisitMemory.Invoke(controller, null);
            Assert.That(memory.GetType().GetField("State").GetValue(memory).ToString(), Is.EqualTo("InitialVisit"),
                "Continued presence in range does not rearm a visit.");
            ((Behaviour)factionMember).enabled = false;
            updateVisitMemory.Invoke(controller, null);
            Assert.That(memory.GetType().GetField("State").GetValue(memory).ToString(), Is.EqualTo("InitialVisit"),
                "Temporary target unavailability at the same position is not a geometric exit.");
            ((Behaviour)factionMember).enabled = true;

            controllerType.GetMethod("EnterFollowingPlayer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            player.transform.position = new Vector3(15f, 0f, 0f);
            Physics.SyncTransforms();
            updateVisitMemory.Invoke(controller, null);
            Assert.That(memory.GetType().GetField("State").GetValue(memory).ToString(), Is.EqualTo("Outside"));
            player.transform.position = new Vector3(0f, 0f, 4f);
            Physics.SyncTransforms();
            updateVisitMemory.Invoke(controller, null);
            Assert.That(memory.GetType().GetField("State").GetValue(memory).ToString(), Is.EqualTo("ReturnReady"),
                "Leaving and re-entering during follow makes a return visit available.");

            unseenPlayer = CreatePlayer("Unseen priority player", new Vector3(0f, 0f, 7f));
            Component unseenFactionMember = unseenPlayer.AddComponent(FactionMemberType);
            FactionMemberType.GetMethod("SetFaction").Invoke(unseenFactionMember, new object[] { playerFaction });
            Physics.SyncTransforms();
            Assert.That((bool)scan.Invoke(controller, null), Is.True);
            Assert.That(controllerType.GetField("followTargetPlayerHealth", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller), Is.SameAs(unseenPlayer.GetComponent(PlayerHealthType)),
                "A newly seen player takes priority over a closer return-visit player.");
            Assert.That((bool)ThrowType.GetProperty("IsWindingUp").GetValue(thrower), Is.False);

            UnityEngine.Object.DestroyImmediate(unseenPlayer);
            unseenPlayer = null;
            controllerType.GetMethod("EnterIdleSearching", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            Physics.SyncTransforms();
            SetField(thrower, "nextThrowAt", Time.time + 0.05f);
            scan.Invoke(controller, null);
            Assert.That(memory.GetType().GetField("State").GetValue(memory).ToString(), Is.EqualTo("ReturnReady"),
                "A return visit remains unused while the global cooldown is active.");

            yield return new WaitForSeconds(0.1f);
            Assert.That((bool)scan.Invoke(controller, null), Is.True);
            Assert.That((bool)ThrowType.GetProperty("IsWindingUp").GetValue(thrower), Is.True,
                "A ready subsequent visit starts one windup when the cooldown permits it.");
            Assert.That(memory.GetType().GetField("State").GetValue(memory).ToString(), Is.EqualTo("ReturnAttempted"));
            float cooldownBeforeCancel = (float)ThrowType.GetProperty("NextThrowAt").GetValue(thrower);
            ThrowType.GetMethod("CancelWindup").Invoke(thrower, null);
            Assert.That((float)ThrowType.GetProperty("NextThrowAt").GetValue(thrower), Is.EqualTo(cooldownBeforeCancel),
                "Cancelling a windup does not change the global cooldown timestamp.");
            Assert.That((bool)scan.Invoke(controller, null), Is.False);
            Assert.That(memory.GetType().GetField("State").GetValue(memory).ToString(), Is.EqualTo("ReturnAttempted"),
                "The same return visit cannot start a second windup after cancellation.");

            player.transform.position = new Vector3(15f, 0f, 0f);
            Physics.SyncTransforms();
            updateVisitMemory.Invoke(controller, null);
            player.transform.position = new Vector3(0f, 0f, 4f);
            Physics.SyncTransforms();
            updateVisitMemory.Invoke(controller, null);
            Assert.That(memory.GetType().GetField("State").GetValue(memory).ToString(), Is.EqualTo("ReturnReady"),
                "A fresh geometric exit and re-entry rearms the return visit.");
            Assert.That((bool)scan.Invoke(controller, null), Is.True);
            Assert.That((bool)ThrowType.GetProperty("IsWindingUp").GetValue(thrower), Is.True);
        }
        finally
        {
            if (controller != null) controller.GetType().GetMethod("Exit").Invoke(controller, null);
            if (scout != null) UnityEngine.Object.DestroyImmediate(scout);
            if (player != null) UnityEngine.Object.DestroyImmediate(player);
            if (unseenPlayer != null) UnityEngine.Object.DestroyImmediate(unseenPlayer);
            if (scoutNavMesh != null) scoutNavMesh.Dispose();
            if (behavior != null) UnityEngine.Object.DestroyImmediate(behavior);
            if (playerFaction != null) UnityEngine.Object.DestroyImmediate(playerFaction);
        }
    }

    [Test]
    public void ScoutPrefabWiresWindupAnimationSpatialCueAndMatchingAuthoritativeDuration()
    {
        GameObject scoutPrefab = (GameObject)LoadAsset("Assets/Prefabs/New/NPC/NPC_BeaverScout.prefab", typeof(GameObject));
        Component thrower = scoutPrefab.GetComponent(ThrowType);
        AnimationClip clip = (AnimationClip)ThrowType.GetField("windupAnimationClip", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(thrower);
        AudioSource source = (AudioSource)ThrowType.GetField("windupAudioSource", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(thrower);
        AudioClip cue = (AudioClip)ThrowType.GetField("windupCue", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(thrower);
        UnityEngine.Object behavior = LoadAsset("Assets/ScriptableObjectAssets/New/NPC/BeaverScoutBehavior.asset",
            Type.GetType("BeaverScoutBehaviorSO, Assembly-CSharp"));

        Assert.That(clip, Is.Not.Null);
        Assert.That(clip.length, Is.EqualTo(0.7f).Within(0.02f));
        Assert.That(cue, Is.Not.Null);
        Assert.That(cue.length, Is.InRange(0.2f, 0.3f));
        Assert.That(cue.channels, Is.EqualTo(1));
        Assert.That(source, Is.Not.Null);
        Assert.That(source.spatialBlend, Is.EqualTo(1f));
        Assert.That(source.playOnAwake, Is.False);
        Assert.That(source.loop, Is.False);
        Assert.That((float)behavior.GetType().GetProperty("MudballWindup").GetValue(behavior), Is.EqualTo(0.7f).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator WindupPresentationReplicatesToObserverAndLateJoinSkipsOldCue()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("The windup presentation fixture requires no pre-existing network session.");

        scope = new PlayModeTestScope("BeaverScoutWindupPresentation");
        GameObject playerPrefab = CreateNetworkPlayerPrefab();
        scope.Own(playerPrefab.transform.parent.gameObject);
        GameObject hostRoot = new GameObject("Scout windup host manager");
        GameObject observerRoot = new GameObject("Scout windup observer manager");
        scope.Own(hostRoot);
        scope.Own(observerRoot);
        ushort port = ReservePort();
        NetworkManager host = CreateManager(hostRoot, playerPrefab, port);
        NetworkManager observer = CreateManager(observerRoot, playerPrefab, port);
        scope.OwnManager(host);
        scope.OwnManager(observer);
        GameObject scoutPrefab = (GameObject)LoadAsset("Assets/Prefabs/New/NPC/NPC_BeaverScout.prefab", typeof(GameObject));
        host.AddNetworkPrefab(scoutPrefab);
        observer.AddNetworkPrefab(scoutPrefab);

        GameObject scout = null;
        GameObject lateJoinRoot = null;
        NetworkManager lateJoin = null;
        GameObject target = null;
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.LocalClient != null && host.LocalClient.PlayerObject != null, "windup host player spawn");
        Assert.That(observer.StartClient(), Is.True);
        observer.SetSingleton();
        yield return Until(() => observer.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "windup observer connect");

        host.SetSingleton();
        scopedScoutNavMesh = new TemporaryScoutNavMesh(Vector3.zero, 24f);
        scout = UnityEngine.Object.Instantiate(scoutPrefab, new Vector3(5f, 0f, 5f), Quaternion.identity);
        scope.Own(scout);
        NetworkObject scoutNetworkObject = scout.GetComponent<NetworkObject>();
        Assert.That(scoutNetworkObject, Is.Not.Null);
        scoutNetworkObject.Spawn();
        NetworkObject observerScout = null;
        Component observerThrower = null;
        yield return Until(() =>
        {
            if (observer.SpawnManager.SpawnedObjects.TryGetValue(scoutNetworkObject.NetworkObjectId, out observerScout))
            {
                observerThrower = observerScout.GetComponent(ThrowType);
                return observerThrower != null;
            }
            return false;
        }, "observer Scout spawn");

        target = CreatePlayer("Windup cue target", new Vector3(9f, 0f, 5f));
        scope.Own(target);
        Component ball = ((GameObject)LoadAsset("Assets/Prefabs/New/NPC/BeaverScoutMudball.prefab", typeof(GameObject)))
            .GetComponent(MudballType);
        UnityEngine.Object impulseProfile = LoadAsset("Assets/ScriptableObjectAssets/New/NPC/BeaverScoutMudImpulse.asset",
            Type.GetType("ExternalImpulseProfileSO, Assembly-CSharp"));
        Component serverThrower = scout.GetComponent(ThrowType);
        bool began = (bool)ThrowType.GetMethod("TryBeginWindup").Invoke(serverThrower, new object[]
        {
            target.GetComponent(PlayerHealthType), target.transform, 10f, false, (LayerMask)~0,
            2f, 45f, ball, 15f, impulseProfile, new Func<bool>(() => true)
        });
        Assert.That(began, Is.True, "The server must accept a valid windup before it can replicate presentation state.");
        yield return Until(() => (bool)ThrowType.GetField("hasActivePresentation", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(observerThrower), "observer windup cue");
        Assert.That((bool)ThrowType.GetField("hasActivePresentation", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(observerThrower), Is.True);

        lateJoinRoot = new GameObject("Scout windup late join manager");
        scope.Own(lateJoinRoot);
        lateJoin = CreateManager(lateJoinRoot, playerPrefab, port);
        scope.OwnManager(lateJoin);
        lateJoin.AddNetworkPrefab(scoutPrefab);
        Assert.That(lateJoin.StartClient(), Is.True);
        lateJoin.SetSingleton();
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3, "late observer connect");
        NetworkObject lateScout = null;
        Component lateThrower = null;
        yield return Until(() =>
        {
            if (lateJoin.SpawnManager.SpawnedObjects.TryGetValue(scoutNetworkObject.NetworkObjectId, out lateScout))
            {
                lateThrower = lateScout.GetComponent(ThrowType);
                return lateThrower != null && (bool)ThrowType.GetField("hasActivePresentation", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(lateThrower);
            }
            return false;
        }, "late observer windup state");
        AudioSource lateAudioSource = (AudioSource)ThrowType.GetField("windupAudioSource", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(lateThrower);
        Assert.That(lateAudioSource.isPlaying, Is.False,
            "A late observer receives the active animation phase without replaying the old cue.");

        ThrowType.GetMethod("CancelWindup").Invoke(serverThrower, null);
        yield return Until(() => !(bool)ThrowType.GetField("hasActivePresentation", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(observerThrower)
            && !(bool)ThrowType.GetField("hasActivePresentation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lateThrower),
            "windup cancel state on observers");
    }

    [Test]
    public void StaleWindupStopTokenCannotInterruptHitReactionReplacingItsPose()
    {
        GameObject scoutPrefab = (GameObject)LoadAsset("Assets/Prefabs/New/NPC/NPC_BeaverScout.prefab", typeof(GameObject));
        using (new TemporaryScoutNavMesh(new Vector3(120f, 0f, 120f), 24f))
        {
            GameObject scout = UnityEngine.Object.Instantiate(scoutPrefab, new Vector3(120f, 0f, 120f), Quaternion.identity);
            try
            {
                AnimationClip clip = (AnimationClip)LoadAsset("Assets/Prefabs/New/NPC/BeaverScoutMudballWindup.anim", typeof(AnimationClip));
                Component animationController = scout.GetComponent(AnimationControllerType);
                Component visualController = scout.GetComponentInChildren(VisualControllerType, true);
                SetField(visualController, "hitReactionClip", clip);

                int token = (int)AnimationControllerType.GetMethod("PlayScopedOneShot").Invoke(animationController,
                    new object[] { clip, 1f, 0f });
                Assert.That(token, Is.GreaterThan(0), "The active Generic-rig Animator must accept a scoped windup pose.");

                AnimationControllerType.GetMethod("PlayHitReaction").Invoke(animationController, null);
                Assert.That((int)AnimationControllerType.GetField("activeScopedOneShotToken", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(animationController), Is.Zero);
                float hitReactionTimer = (float)AnimationControllerType.GetField("oneShotTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(animationController);
                Assert.That(hitReactionTimer, Is.GreaterThan(0f));

                AnimationControllerType.GetMethod("StopScopedOneShot").Invoke(animationController, new object[] { token });
                Assert.That((float)AnimationControllerType.GetField("oneShotTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(animationController), Is.EqualTo(hitReactionTimer),
                    "Ending the superseded windup pose must leave the replacing hit reaction intact.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scout);
            }
        }
    }

    [Test]
    public void MudGainUsesPercentagePointsAndClampsAtOneHundredPercent()
    {
        GameObject player = CreatePlayer("Mud status player", Vector3.zero);
        player.AddComponent(StatusControllerType);
        Component mud = player.AddComponent(MudControllerType);
        try
        {
            MethodInfo add = MudControllerType.GetMethod("AddMudPercentagePoints");
            Assert.That((bool)add.Invoke(mud, new object[] { 15f }), Is.True);
            Assert.That((float)MudControllerType.GetProperty("Amount").GetValue(mud), Is.EqualTo(0.15f).Within(0.0001f));
            MudControllerType.GetMethod("SetAmount", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(mud, new object[] { 0.95f });
            add.Invoke(mud, new object[] { 15f });
            Assert.That((float)MudControllerType.GetProperty("Amount").GetValue(mud), Is.EqualTo(1f));
        }
        finally { UnityEngine.Object.DestroyImmediate(player); }
    }

    [UnityTest]
    public IEnumerator LaunchedBallHitsThroughTriggerOnceAndAppliesMudAndHorizontalImpulse()
    {
        Vector3 offset = new Vector3(100f, 50f, 100f);
        GameObject player = CreatePlayer("Projectile target", offset + new Vector3(0f, 0f, 3f));
        player.AddComponent(NetworkObjectType);
        player.AddComponent(PlayerInputType);
        player.AddComponent(StatusControllerType);
        Component mud = player.AddComponent(MudControllerType);
        Component impulseReceiver = player.AddComponent(ImpulseControllerType);
        GameObject waterTrigger = new GameObject("Unrelated water trigger");
        BoxCollider trigger = waterTrigger.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(4f, 4f, 0.5f);
        waterTrigger.transform.position = offset + new Vector3(0f, 1f, 1.5f);

        GameObject prefab = (GameObject)LoadAsset("Assets/Prefabs/New/NPC/BeaverScoutMudball.prefab",
            Type.GetType("UnityEngine.GameObject, UnityEngine.CoreModule"));
        UnityEngine.Object profile = LoadAsset("Assets/ScriptableObjectAssets/New/NPC/BeaverScoutMudImpulse.asset",
            Type.GetType("ExternalImpulseProfileSO, Assembly-CSharp"));
        Component ball = UnityEngine.Object.Instantiate(prefab, offset + new Vector3(0f, 0.9f, 0f), Quaternion.identity)
            .GetComponent(Type.GetType("BeaverScoutMudball, Assembly-CSharp"));
        try
        {
            ball.GetType().GetMethod("ConfigureImpact").Invoke(ball, new object[] { 15f, profile });
            ball.GetType().GetMethod("Initialize").Invoke(ball,
                new object[] { new Vector3(0f, 0f, 9f), null, 3f });
            ball.GetType().GetMethod("Launch").Invoke(ball, new object[] { new Vector3(0f, 0f, 9f) });

            float deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline &&
                   (float)MudControllerType.GetProperty("Amount").GetValue(mud) < 0.14f)
                yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That((float)MudControllerType.GetProperty("Amount").GetValue(mud), Is.EqualTo(0.15f).Within(0.001f));
            Vector3 applied = (Vector3)ImpulseControllerType.GetProperty("CurrentExternalVelocity").GetValue(impulseReceiver);
            Assert.That(applied.z, Is.GreaterThan(3.4f));
            Assert.That(Mathf.Abs(applied.y), Is.LessThan(0.001f));
            Assert.That(ball == null || !ball.gameObject.activeInHierarchy, Is.True,
                "The first solid collision consumes the projectile exactly once.");
        }
        finally
        {
            if (ball != null) UnityEngine.Object.DestroyImmediate(ball.gameObject);
            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(waterTrigger);
        }
    }

    [UnityTest]
    public IEnumerator HostProjectileReplicatesMudAndOwnerImpulseWhileClientColliderStaysDisabled()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("The projectile replication fixture requires no pre-existing network session.");

        scope = new PlayModeTestScope("BeaverScoutMudballNetwork");
        Vector3 offset = new Vector3(100f, 50f, 100f);
        GameObject playerPrefab = CreateNetworkPlayerPrefab();
        scope.Own(playerPrefab.transform.parent.gameObject);
        GameObject hostRoot = new GameObject("Mudball host manager");
        GameObject ownerRoot = new GameObject("Mudball remote manager");
        scope.Own(hostRoot);
        scope.Own(ownerRoot);
        ushort port = ReservePort();
        NetworkManager host = CreateManager(hostRoot, playerPrefab, port);
        scope.OwnManager(host);
        NetworkManager owner = CreateManager(ownerRoot, playerPrefab, port);
        scope.OwnManager(owner);

        GameObject ballPrefab = (GameObject)LoadAsset("Assets/Prefabs/New/NPC/BeaverScoutMudball.prefab",
            Type.GetType("UnityEngine.GameObject, UnityEngine.CoreModule"));
        host.AddNetworkPrefab(ballPrefab);
        owner.AddNetworkPrefab(ballPrefab);
        try
        {
            Assert.That(host.StartHost(), Is.True);
            host.SetSingleton();
            yield return Until(() => host.LocalClient != null && host.LocalClient.PlayerObject != null, "host player spawn");

            Assert.That(owner.StartClient(), Is.True);
            owner.SetSingleton();
            yield return Until(() => owner.IsConnectedClient && owner.LocalClient != null &&
                owner.LocalClient.PlayerObject != null && host.ConnectedClientsIds.Count == 2, "remote player spawn");

            NetworkObject victim = host.ConnectedClients[owner.LocalClientId].PlayerObject;
            victim.transform.position = offset + new Vector3(0f, 0f, 3f);
            Physics.SyncTransforms();
            host.SetSingleton();

            UnityEngine.Object profile = LoadAsset("Assets/ScriptableObjectAssets/New/NPC/BeaverScoutMudImpulse.asset",
                Type.GetType("ExternalImpulseProfileSO, Assembly-CSharp"));
            Component ball = UnityEngine.Object.Instantiate(ballPrefab, offset + new Vector3(0f, 0.9f, 0f), Quaternion.identity)
                .GetComponent(Type.GetType("BeaverScoutMudball, Assembly-CSharp"));
            ball.GetType().GetMethod("ConfigureImpact").Invoke(ball, new object[] { 15f, profile });
            ball.GetType().GetMethod("Initialize").Invoke(ball, new object[] { new Vector3(0f, 0f, 9f), null, 3f });
            NetworkObject projectileNetworkObject = ball.GetComponent<NetworkObject>();
            projectileNetworkObject.Spawn(true);
            ball.GetType().GetMethod("Launch").Invoke(ball, new object[] { new Vector3(0f, 0f, 9f) });
            ulong projectileId = projectileNetworkObject.NetworkObjectId;

            yield return Until(() => owner.SpawnManager.SpawnedObjects.ContainsKey(projectileId),
                "remote projectile spawn replication");
            NetworkObject remoteProjectile = owner.SpawnManager.SpawnedObjects[projectileId];
            Assert.That(remoteProjectile.GetComponent<SphereCollider>().enabled, Is.False,
                "The observing client's projectile is a non-colliding visual replica.");
            NetworkObject remoteVictim = owner.SpawnManager.SpawnedObjects[victim.NetworkObjectId];
            yield return Until(() => GetMud(victim.GetComponent(StatusControllerType)) > 0.14f,
                "server applies mud to collided remote player");
            yield return Until(() => ((Vector3)ImpulseControllerType.GetProperty("CurrentExternalVelocity")
                    .GetValue(remoteVictim.GetComponent(ImpulseControllerType))).z > 3.4f,
                "owner receives the horizontal impulse");
            yield return Until(() => GetMud(remoteVictim.GetComponent(StatusControllerType)) > 0.14f,
                "mud status replicates to remote owner");
            Assert.That(GetMud(victim.GetComponent(StatusControllerType)), Is.EqualTo(0.15f).Within(0.001f));
            Assert.That(GetMud(remoteVictim.GetComponent(StatusControllerType)), Is.EqualTo(0.15f).Within(0.001f));
            Assert.That(Mathf.Abs(((Vector3)ImpulseControllerType.GetProperty("CurrentExternalVelocity")
                .GetValue(remoteVictim.GetComponent(ImpulseControllerType))).y), Is.LessThan(0.001f));
        }
        finally
        {
            // PlayModeTestScope tears down host and client after the coroutine completes.
        }
    }

    private static bool CanTarget(Component thrower, GameObject player, float range, bool lineOfSight)
    {
        return (bool)ThrowType.GetMethod("CanTarget").Invoke(thrower, new object[]
        {
            player.GetComponent(PlayerHealthType), player.transform, range, lineOfSight, (LayerMask)~0
        });
    }

    private static float GetMud(Component statuses) =>
        (float)StatusControllerType.GetProperty("MudAmount").GetValue(statuses);

    private static GameObject CreatePlayer(string name, Vector3 position)
    {
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = name;
        player.transform.position = position;
        player.AddComponent(PlayerHealthType);
        return player;
    }

    private static GameObject CreateNetworkPlayerPrefab()
    {
        GameObject staging = new GameObject("Mudball player prefab staging");
        staging.SetActive(false);
        GameObject prefab = new GameObject("Mudball network player prefab");
        prefab.transform.SetParent(staging.transform, false);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .SetValue(networkObject, 0x4D554432u);
        networkObject.SetSceneObjectStatus(false);
        prefab.AddComponent<CapsuleCollider>();
        prefab.AddComponent(PlayerHealthType);
        prefab.AddComponent(PlayerInputType);
        prefab.AddComponent(StatusControllerType);
        prefab.AddComponent(MudControllerType);
        prefab.AddComponent(ImpulseControllerType);
        return prefab;
    }

    private static NetworkManager CreateManager(GameObject root, GameObject playerPrefab, ushort port)
    {
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = playerPrefab,
            EnableSceneManagement = false
        };
        return manager;
    }

    private static ushort ReservePort()
    {
        using UdpClient socket = new UdpClient(0);
        return (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
    }

    private static IEnumerator Until(Func<bool> predicate, string label)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(predicate(), Is.True, "Timed out: " + label);
    }

    private static UnityEngine.Object LoadAsset(string path, Type type)
    {
        Type assetDatabase = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
        return (UnityEngine.Object)assetDatabase.GetMethod("LoadAssetAtPath", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(string), typeof(Type) }, null).Invoke(null, new object[] { path, type });
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
