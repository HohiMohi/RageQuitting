using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace RageQuitting.Tests.PlayMode
{
    public sealed class TumbleweedCapturePlayModeTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private PlayModeTestScope scope;
        private NetworkManager host;
        private NetworkManager owner;
        private NetworkManager lateObserver;
        private GameObject playerPrefab;
        private Scene stage02Scene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scope = new PlayModeTestScope("TumbleweedCapture");
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (stage02Scene.IsValid() && stage02Scene.isLoaded)
            {
                if (scope != null && scope.OwnedScene.IsValid() && scope.OwnedScene.isLoaded)
                    SceneManager.SetActiveScene(scope.OwnedScene);
                AsyncOperation unloadStage = SceneManager.UnloadSceneAsync(stage02Scene);
                if (unloadStage != null) yield return unloadStage;
                stage02Scene = default;
            }
            if (scope == null) yield break;
            if (host != null) scope.OwnManager(host);
            if (owner != null) scope.OwnManager(owner);
            if (lateObserver != null) scope.OwnManager(lateObserver);
            PlayModeTestScope cleanup = scope;
            yield return cleanup.Cleanup();
            scope = null;
            host = owner = lateObserver = null;
            playerPrefab = null;
            cleanup.AssertCleanupSucceeded();
        }

        [UnityTest]
        public IEnumerator ActualOverlapCapturesHealthyAndDownedPlayersAndReleasesTheirState()
        {
#if !UNITY_EDITOR
            Assert.Ignore("Uses the authored Tumbleweed prefab available only in the Editor.");
            yield break;
#else
            AssertNoActiveSession();
            CreateGround();
            GameObject weed = CreateWeed(new Vector3(0f, 0.65f, 0f), 0.3f, 10);
            GameObject healthy = CreatePlayerFixture("Healthy capture target", new Vector3(0f, 0f, 0f));
            GameObject downed = CreatePlayerFixture("Downed capture target", new Vector3(0.2f, 0f, 0f));
            Behaviour healthyInventory = (Behaviour)healthy.AddComponent(TypeByName("PlayerInventory"));
            healthyInventory.enabled = false;
            GameObject axePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/EquippableItems/Axe.prefab");
            Component axeDefinition = axePrefab.GetComponent(TypeByName("EquippableItem"));
            object heldDefinition = axeDefinition.GetType().GetMethod("GetEquippableItemSO", Instance).Invoke(axeDefinition, null);
            Assert.That((bool)CallResult(healthyInventory, "AddItem", heldDefinition), Is.True);
            Component healthyHealthComponent = healthy.GetComponent(TypeByName("PlayerHealth"));
            Component downedHealth = downed.GetComponent(TypeByName("PlayerHealth"));
            Call(downedHealth, "DamageReceived", 1000f);
            Assert.That(Get<bool>(downedHealth, "IsDowned"), Is.True);

            yield return Until(() => Get<bool>(healthy.GetComponent(TypeByName("PlayerTemporaryStatusController")), "IsTumbleweedCaptured")
                && Get<bool>(downed.GetComponent(TypeByName("PlayerTemporaryStatusController")), "IsTumbleweedCaptured"), "both overlap captures");

            Assert.That(Get<bool>(healthy.GetComponent(TypeByName("PlayerTemporaryStatusController")), "BlocksControls"), Is.True);
            Assert.That(Get<bool>(healthy.GetComponent(TypeByName("PlayerTemporaryStatusController")), "BlocksActions"), Is.True);
            Assert.That(healthyInventory.GetType().GetMethod("GetItemInSlot", Instance).Invoke(healthyInventory, new object[] { 0 }),
                Is.SameAs(heldDefinition), "Capture preserves the player's held inventory item.");
            Assert.That(Get<bool>(downedHealth, "IsRespawnTimerPaused"), Is.True,
                "Capturing a downed target pauses its existing respawn timer.");
            Assert.That(Get<bool>(downedHealth, "CanBeRevived"), Is.False);
            float startingHealth = Get<float>(healthyHealthComponent, "CurrentHealth");
            Call(healthyHealthComponent, "DamageReceived", 35f);
            Assert.That(Get<float>(healthyHealthComponent, "CurrentHealth"), Is.EqualTo(startingHealth),
                "Incoming damage is ignored while captured.");

            yield return Until(() => !Get<bool>(healthy.GetComponent(TypeByName("PlayerTemporaryStatusController")), "IsTumbleweedCaptured")
                && !Get<bool>(downed.GetComponent(TypeByName("PlayerTemporaryStatusController")), "IsTumbleweedCaptured"), "timed capture release");

            Assert.That(Get<bool>(downedHealth, "IsRespawnTimerPaused"), Is.False);
            Assert.That(Get<bool>(downedHealth, "CanBeRevived"), Is.True);
            Assert.That(Vector3.Distance(healthy.transform.position, weed.transform.position), Is.GreaterThan(0.7f));
            AssertCapsuleBottomClearsGround(healthy.GetComponent<CharacterController>());
            AssertCapsuleBottomClearsGround(downed.GetComponent<CharacterController>());
            Assert.That(Get<int>(weed.GetComponent(TypeByName("TumbleweedCaptureController")), "OccupiedSlotCount"), Is.Zero);
#endif
        }

        [UnityTest]
        public IEnumerator ActualOverlapCapturesLooseToolAndRestoresEveryColliderWithoutAllowingRecallOrPickup()
        {
#if !UNITY_EDITOR
            Assert.Ignore("Uses authored item and Tumbleweed prefabs available only in the Editor.");
            yield break;
#else
            AssertNoActiveSession();
            CreateGround();
            GameObject weed = CreateWeed(new Vector3(0f, 0.65f, 0f), 0.35f, 10);
            GameObject itemPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/New/EquippableItems/Axe.prefab");
            Assert.That(itemPrefab, Is.Not.Null);
            GameObject item = Object.Instantiate(itemPrefab, weed.transform.position, Quaternion.identity);
            scope.Own(item);
            SceneManager.MoveGameObjectToScene(item, scope.OwnedScene);
            Physics.SyncTransforms();
            Component physics = item.GetComponent(TypeByName("EquippableWorldPhysics"));
            Component equippable = item.GetComponent(TypeByName("EquippableItem"));
            Assert.That(physics, Is.Not.Null);
            Assert.That(equippable, Is.Not.Null);
            GameObject pickupPlayer = CreatePlayerFixture("Pickup gate player", new Vector3(5f, 0f, 0f));
            Behaviour inventory = (Behaviour)pickupPlayer.AddComponent(TypeByName("PlayerInventory"));
            inventory.enabled = false;
            object itemDefinition = equippable.GetType().GetMethod("GetEquippableItemSO", Instance).Invoke(equippable, null);
            Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
            bool[] originalEnabled = Array.ConvertAll(colliders, collider => collider != null && collider.enabled);

            yield return Until(() => Get<bool>(physics, "IsTumbleweedCaptured"),
                "loose tool capture (returning=" + Get<bool>(physics, "IsReturning")
                + ", immune=" + Get<bool>(physics, "IsTumbleweedImmune")
                + ", colliders=" + item.GetComponentsInChildren<Collider>(true).Length + ")");
            Assert.That(Array.Exists(colliders, collider => collider != null && !collider.enabled), Is.True);
            Vector3 capturedPosition = item.transform.position;
            Call(physics, "ReturnToRespawnPoint");
            Call(equippable, "Interact", pickupPlayer.transform);
            Assert.That(equippable.GetType().GetMethod("GetEquippableItemSO", Instance).Invoke(equippable, null), Is.SameAs(itemDefinition));
            Assert.That(inventory.GetType().GetMethod("GetItemInSlot", Instance).Invoke(inventory, new object[] { 0 }), Is.Null,
                "A valid nearby player cannot pick up the captive tool.");
            Assert.That(Get<bool>(physics, "IsReturning"), Is.False, "Captured tools cannot enter the return routine.");
            Assert.That(item.activeInHierarchy, Is.True, "Captured tools cannot be picked up.");

            yield return Until(() => !Get<bool>(physics, "IsTumbleweedCaptured"), "loose tool timed release");
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) Assert.That(colliders[i].enabled, Is.EqualTo(originalEnabled[i]), colliders[i].name);
            Assert.That(Get<bool>(physics, "IsTumbleweedImmune"), Is.True);
            Assert.That(Vector3.Distance(item.transform.position, weed.transform.position), Is.GreaterThan(0.6f));
            Assert.That(item.transform.position, Is.Not.EqualTo(capturedPosition));
            Call(equippable, "Interact", pickupPlayer.transform);
            object heldItem = inventory.GetType().GetMethod("GetItemInSlot", Instance).Invoke(inventory, new object[] { 0 });
            Assert.That(heldItem, Is.SameAs(itemDefinition), "A released tool can be picked up again despite its capture immunity.");
#endif
        }

        [UnityTest]
        public IEnumerator CapacityStopsAtTenAndCooldownIsSharedAcrossTwoBalls()
        {
#if !UNITY_EDITOR
            Assert.Ignore("Uses the authored Tumbleweed prefab available only in the Editor.");
            yield break;
#else
            AssertNoActiveSession();
            CreateGround();
            GameObject weed = CreateWeed(new Vector3(0f, 0.65f, 0f), 1.2f, 10);
            GameObject[] targets = new GameObject[11];
            for (int i = 0; i < targets.Length; i++)
                targets[i] = CreatePlayerFixture("Capacity target " + i, new Vector3((i % 3) * 0.08f, 0f, (i / 3) * 0.08f));

            yield return Until(() => Get<int>(weed.GetComponent(TypeByName("TumbleweedCaptureController")), "CurrentCaptiveCount") == 10,
                "ten allocated capture slots");
            int captured = 0;
            foreach (GameObject target in targets)
                if (Get<bool>(target.GetComponent(TypeByName("PlayerTemporaryStatusController")), "IsTumbleweedCaptured")) captured++;
            Assert.That(captured, Is.EqualTo(10));
            GameObject cooledTarget = Array.Find(targets, target =>
                Get<bool>(target.GetComponent(TypeByName("PlayerTemporaryStatusController")), "IsTumbleweedCaptured"));
            Assert.That(cooledTarget, Is.Not.Null);
            foreach (GameObject target in targets)
            {
                if (target != cooledTarget
                    && !Get<bool>(target.GetComponent(TypeByName("PlayerTemporaryStatusController")), "IsTumbleweedCaptured"))
                    target.transform.position = new Vector3(50f + target.transform.position.x, 0f, target.transform.position.z);
            }

            yield return Until(() => Get<int>(weed.GetComponent(TypeByName("TumbleweedCaptureController")), "OccupiedSlotCount") == 0,
                "capacity fixture release");
            SetCaptureTiming(weed, 0.3f, 5f, 11);
            GameObject eleventhTarget = CreatePlayerFixture("Capacity target eleven", weed.transform.position);
            Component eleventhStatus = eleventhTarget.GetComponent(TypeByName("PlayerTemporaryStatusController"));
            yield return Until(() => Get<bool>(eleventhStatus, "IsTumbleweedCaptured"),
                "configured capacity above ten admits an eleventh captive");
            Assert.That(Get<int>(weed.GetComponent(TypeByName("TumbleweedCaptureController")), "CurrentCaptiveCount"), Is.EqualTo(1));
            yield return Until(() => !Get<bool>(eleventhStatus, "IsTumbleweedCaptured"), "eleventh capture release");
            GameObject secondWeed = CreateWeed(weed.transform.position, 0.3f, 10);
            cooledTarget.transform.position = secondWeed.transform.position;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.That(Get<bool>(cooledTarget.GetComponent(TypeByName("PlayerTemporaryStatusController")), "IsTumbleweedCaptured"),
                Is.False, "Cooldown is stored with the target, so a second ball cannot immediately capture it.");
#endif
        }

        [UnityTest]
        public IEnumerator CaptivesOnTwoBallsKeepIndependentReleaseDeadlines()
        {
#if !UNITY_EDITOR
            Assert.Ignore("Uses the authored Tumbleweed prefab available only in the Editor.");
            yield break;
#else
            AssertNoActiveSession();
            CreateGround();
            GameObject firstWeed = CreateWeed(new Vector3(0f, 0.65f, 0f), 0.8f, 10);
            GameObject firstTarget = CreatePlayerFixture("First deadline target", Vector3.zero);
            Component firstStatus = firstTarget.GetComponent(TypeByName("PlayerTemporaryStatusController"));
            yield return Until(() => Get<bool>(firstStatus, "IsTumbleweedCaptured"), "first ball capture");

            GameObject secondWeed = CreateWeed(new Vector3(8f, 0.65f, 0f), 1.5f, 10);
            GameObject secondTarget = CreatePlayerFixture("Second deadline target", new Vector3(8f, 0f, 0f));
            Component secondStatus = secondTarget.GetComponent(TypeByName("PlayerTemporaryStatusController"));
            yield return Until(() => Get<bool>(secondStatus, "IsTumbleweedCaptured"), "second ball capture");

            yield return Until(() => !Get<bool>(firstStatus, "IsTumbleweedCaptured"), "first deadline expires");
            Assert.That(Get<bool>(secondStatus, "IsTumbleweedCaptured"), Is.True,
                "The second ball's deadline is measured from its own capture commit.");
            yield return Until(() => !Get<bool>(secondStatus, "IsTumbleweedCaptured"), "second deadline expires");
            Assert.That(Get<int>(firstWeed.GetComponent(TypeByName("TumbleweedCaptureController")), "OccupiedSlotCount"), Is.Zero);
            Assert.That(Get<int>(secondWeed.GetComponent(TypeByName("TumbleweedCaptureController")), "OccupiedSlotCount"), Is.Zero);
#endif
        }

        [UnityTest]
        public IEnumerator FloatingCaptureChoosesOpenSpaceOnItsWaterBodyWhenAReleaseExitIsBlocked()
        {
#if !UNITY_EDITOR
            Assert.Ignore("Uses the authored Tumbleweed prefab available only in the Editor.");
            yield break;
#else
            AssertNoActiveSession();
            CreateGround();
            GameObject waterObject = scope.Own(new GameObject("Tumbleweed test river"));
            SceneManager.MoveGameObjectToScene(waterObject, scope.OwnedScene);
            waterObject.transform.position = new Vector3(8f, 1f, 0f);
            BoxCollider waterVolume = waterObject.AddComponent<BoxCollider>();
            waterVolume.isTrigger = true;
            waterVolume.size = new Vector3(12f, 2f, 12f);
            Component waterBody = waterObject.AddComponent(TypeByName("WaterBody"));
            Set(waterBody, "gameplayVolume", waterVolume);

            GameObject weed = CreateWeed(new Vector3(8f, 1.26f, 0f), 0.5f, 10);
            GameObject player = CreatePlayerFixture("Floating capture target", new Vector3(8f, 1f, 0f));
            Component status = player.GetComponent(TypeByName("PlayerTemporaryStatusController"));
            yield return Until(() => Get<bool>(status, "IsTumbleweedCaptured"), "floating water capture");

            GameObject wall = scope.Own(new GameObject("Blocked water exit"));
            SceneManager.MoveGameObjectToScene(wall, scope.OwnedScene);
            wall.transform.position = weed.transform.position + new Vector3(1.2f, 1f, 0.5f);
            BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
            wallCollider.size = new Vector3(1f, 3f, 1f);
            Physics.SyncTransforms();

            yield return Until(() => !Get<bool>(status, "IsTumbleweedCaptured"), "safe floating release beside the ball");
            Assert.That(player.transform.position.y, Is.InRange(1f, 1.2f),
                "A floating captive releases at the same water surface instead of being sent to dry ground.");
            Assert.That(waterBody.GetType().GetMethod("Contains", Instance).Invoke(waterBody,
                new object[] { player.transform.position }), Is.EqualTo(true));
            CharacterController releasedCapsule = player.GetComponent<CharacterController>();
            Vector3 scale = releasedCapsule.transform.lossyScale;
            float radius = releasedCapsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(radius * 2f, releasedCapsule.height * Mathf.Abs(scale.y));
            Vector3 capsuleCenter = player.transform.position + player.transform.rotation * Vector3.Scale(releasedCapsule.center, scale);
            float endpoint = Mathf.Max(0f, height * 0.5f - radius);
            releasedCapsule.enabled = false;
            Collider[] releaseOverlaps = Physics.OverlapCapsule(capsuleCenter + Vector3.up * endpoint,
                capsuleCenter - Vector3.up * endpoint, radius, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            bool wallCollision = Array.Exists(releaseOverlaps, collider => collider == wallCollider);
            releasedCapsule.enabled = true;
            Assert.That(wallCollision, Is.False,
                $"The release pose must remain clear of the wall. pose={player.transform.position}, wall={wallCollider.bounds}.");
            Assert.That(Vector3.Distance(player.transform.position, weed.transform.position), Is.GreaterThan(0.7f));
#endif
        }

        [UnityTest]
        public IEnumerator PassengerIsExcludedAndDriverIsReleasedBeforeCaptureCommits()
        {
#if !UNITY_EDITOR
            Assert.Ignore("Loads authored wheelbarrow and Tumbleweed prefabs through the Editor AssetDatabase.");
            yield break;
#else
            AssertNoActiveSession();
            CreateGround();
            GameObject weed = CreateWeed(Vector3.up * 0.65f, 0.6f, 10);
            GameObject target = CreatePlayerFixture("Wheelbarrow role capture target", Vector3.zero);
            Behaviour playerInput = (Behaviour)target.AddComponent(TypeByName("PlayerInputNew"));
            playerInput.enabled = false;
            Component playerWheelbarrow = target.AddComponent(TypeByName("PlayerWheelbarrowController"));

            GameObject wheelbarrowPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/New/Wheelbarrow.prefab");
            Assert.That(wheelbarrowPrefab, Is.Not.Null);
            GameObject wheelbarrow = Object.Instantiate(wheelbarrowPrefab, new Vector3(10f, 0f, 0f), Quaternion.identity);
            scope.Own(wheelbarrow);
            SceneManager.MoveGameObjectToScene(wheelbarrow, scope.OwnedScene);
            Component wheelbarrowController = wheelbarrow.GetComponent(TypeByName("WheelbarrowController"));
            NetworkObject playerNetworkObject = target.GetComponent<NetworkObject>();
            Set(playerNetworkObject, "OwnerClientId", 0UL);
            ulong clientId = 0UL;
            Set(wheelbarrowController, "localDriver", ulong.MaxValue);
            Set(wheelbarrowController, "localPassenger", clientId);
            Set(wheelbarrowController, "localState", Enum.Parse(TypeByName("WheelbarrowState"), "Free"));
            yield return Until(() => Get(playerWheelbarrow, "CurrentRole").ToString() == "Passenger",
                "offline wheelbarrow passenger role");
            yield return new WaitForFixedUpdate();
            Assert.That(Get<bool>(target.GetComponent(TypeByName("PlayerTemporaryStatusController")), "IsTumbleweedCaptured"),
                Is.False, "An occupied wheelbarrow passenger cannot be captured.");

            Set(wheelbarrowController, "localPassenger", ulong.MaxValue);
            Set(wheelbarrowController, "localDriver", clientId);
            Set(wheelbarrowController, "localState", Enum.Parse(TypeByName("WheelbarrowState"), "Driven"));
            Assert.That(Get(playerWheelbarrow, "CurrentRole").ToString(), Is.EqualTo("Driver"));
            Component status = target.GetComponent(TypeByName("PlayerTemporaryStatusController"));
            yield return Until(() => Get<bool>(status, "IsTumbleweedCaptured"), "capture after wheelbarrow physics reclaim");
            Assert.That(Get<ulong>(wheelbarrowController, "DriverClientId"), Is.EqualTo(ulong.MaxValue));
            Assert.That(Get(wheelbarrowController, "State").ToString(), Is.EqualTo("Free"));
            Assert.That(CallResult(wheelbarrowController, "GetRole", clientId).ToString(), Is.EqualTo("None"));
#endif
        }

        [UnityTest]
        public IEnumerator Stage02AuthoredSpawnersRespectWaitingDeadlinesAndRestartCleanly()
        {
#if !UNITY_EDITOR
            Assert.Ignore("Loads the authored Stage02 scene through the Editor test environment.");
            yield break;
#else
            AssertNoActiveSession();
            yield return LoadStage02Scene();
            yield return VerifyStage02SpawnDeadlinesAndReload();
            yield return UnloadStage02Scene();
            yield return LoadStage02Scene();
            yield return VerifyStage02SpawnDeadlinesAndReload();
            yield return UnloadStage02Scene();
#endif
        }

        [UnityTest]
        public IEnumerator Stage02WaterKeepsMidriverFloatAndTumbleweedClimbsBothBanks()
        {
#if !UNITY_EDITOR
            Assert.Ignore("Loads the authored Stage02 terrain and Tumbleweed prefab through the Editor.");
            yield break;
#else
            AssertNoActiveSession();
            yield return LoadStage02Scene();
            yield return null;

            Component water = FindSceneComponent(stage02Scene, TypeByName("WaterBody"));
            Assert.That(water, Is.Not.Null);
            Assert.That(LoadWeedPrefab(), Is.Not.Null);
            float surfaceHeight = Get<float>(water, "SurfaceHeight");

            GameObject middleBall = CreateStage02MovementFixture(
                new Vector3(5f, surfaceHeight + 0.26f, -24f), Vector3.forward);
            Rigidbody middleBody = middleBall.GetComponent<Rigidbody>();
            yield return Until(() => !middleBody.isKinematic, "offline midriver ball dynamics");
            Component middleMovement = middleBall.GetComponent(TypeByName("TumbleweedMovement"));
            Set(middleMovement, "windDirection", Vector3.forward);
            Set(middleMovement, "nextWindChangeTime", Time.fixedTime + 60f);
            middleBody.linearVelocity = Vector3.zero;
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(middleBody.position.y, Is.InRange(surfaceHeight + 0.18f, surfaceHeight + 0.38f),
                "Away from a bank the ball must keep its normal flotation height.");
            Object.Destroy(middleBall);
            yield return null;

            GameObject wallBall = CreateStage02MovementFixture(
                new Vector3(6.9f, surfaceHeight + 0.26f, -24f), Vector3.right);
            Rigidbody wallBody = wallBall.GetComponent<Rigidbody>();
            Component wallMovement = wallBall.GetComponent(TypeByName("TumbleweedMovement"));
            yield return Until(() => !wallBody.isKinematic, "offline wall-check ball dynamics");
            Set(wallMovement, "windDirection", Vector3.right);
            Set(wallMovement, "nextWindChangeTime", Time.fixedTime + 60f);
            wallBody.constraints |= RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
            GameObject wall = scope.Own(new GameObject("Tumbleweed test wall beside east bank"));
            SceneManager.MoveGameObjectToScene(wall, stage02Scene);
            Vector3 wallSample = wallBody.position + Vector3.right * (Get<float>(wallMovement, "movementRadius") * 2f);
            wall.transform.position = new Vector3(wallSample.x, surfaceHeight + 0.14f, wallSample.z);
            BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
            wallCollider.size = new Vector3(0.5f, 0.55f, 2f);
            Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(wallBody.position.y, Is.LessThan(surfaceHeight + 0.5f),
                "A wall in the forecast ray must not make the buoyancy target lift the ball.");
            Object.Destroy(wallBall);
            Object.Destroy(wall);
            yield return null;

            yield return RunStage02BankApproach(water, new Vector3(6.9f, surfaceHeight + 0.26f, -24f),
                Vector3.right, 8.5f, true, "east");
            yield return RunStage02BankApproach(water, new Vector3(3.1f, surfaceHeight + 0.26f, -24f),
                Vector3.left, 1.5f, false, "west");
            yield return RunStage02BankApproach(water, new Vector3(6.9f, surfaceHeight + 0.26f, 12f),
                Vector3.right, 8.5f, true, "east at z=12");
            yield return RunStage02BankApproach(water, new Vector3(3.1f, surfaceHeight + 0.26f, -12f),
                Vector3.left, 1.5f, false, "west at z=-12");
#endif
        }

        [UnityTest]
        public IEnumerator Stage02TumbleweedMaintainsWindAcrossTheCampAndBridgeDryBowls()
        {
#if !UNITY_EDITOR
            Assert.Ignore("Loads the authored Stage02 terrain and Tumbleweed prefab through the Editor.");
            yield break;
#else
            AssertNoActiveSession();
            yield return LoadStage02Scene();
            yield return null;

            Terrain terrain = (Terrain)FindSceneComponent(stage02Scene, typeof(Terrain));
            Assert.That(terrain, Is.Not.Null, "The test must use Stage02's authored terrain support.");
            yield return RunStage02DryBowlEscape(terrain, new Vector3(-2f, 0f, -32.5f), Vector3.left,
                3.5f, "camp south bowl");
            yield return RunStage02DryBowlEscape(terrain, new Vector3(21.5f, 0f, 9f), Vector3.back,
                4f, "bridge south bowl");
#endif
        }

        [UnityTest]
        public IEnumerator LoopbackHostRemoteOwnerAndLateObserverSeeCaptureReleaseAndDisconnectCleanup()
        {
#if !UNITY_EDITOR
            Assert.Ignore("Creates in-process loopback NGO peers, available only in the Editor.");
            yield break;
#else
            AssertNoActiveSession();
            CreateGround();
            GameObject weedPrefab = LoadWeedPrefab();
            playerPrefab = CreateNetworkPlayerPrefab();
            GameObject itemPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/New/EquippableItems/EquippableItem.prefab");
            Assert.That(itemPrefab, Is.Not.Null);
            ushort port = ReservePort();
            host = CreateManager("Tumbleweed loopback host", port, weedPrefab, itemPrefab);
            Assert.That(host.StartHost(), Is.True);
            host.SetSingleton();
            yield return Until(() => host.IsListening && host.LocalClient?.PlayerObject != null, "host player spawn");
            owner = CreateManager("Tumbleweed remote owner", port, weedPrefab, itemPrefab);
            Assert.That(owner.StartClient(), Is.True);
            owner.SetSingleton();
            yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null
                && host.ConnectedClientsIds.Count == 2, "remote owner connect");

            host.SetSingleton();
            NetworkObject remotePlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
            Assert.That(remotePlayer, Is.Not.Null);
            owner.LocalClient.PlayerObject.transform.position = new Vector3(5f, 0f, 0f);
            yield return Until(() => Vector3.Distance(remotePlayer.transform.position, new Vector3(5f, 0f, 0f)) < 0.3f,
                "remote owner position reaches the server");
            GameObject ball = Object.Instantiate(weedPrefab, new Vector3(5f, 0.65f, 0f), Quaternion.identity);
            scope.Own(ball);
            SetCaptureTiming(ball, 1.4f, 0.4f, 10);
            NetworkObject ballNetwork = ball.GetComponent<NetworkObject>();
            ballNetwork.Spawn();
            Rigidbody ballBody = ball.GetComponent<Rigidbody>();
            ballBody.isKinematic = true;
            ((Behaviour)ball.GetComponent(TypeByName("TumbleweedMovement"))).enabled = false;
            Assert.That(ballNetwork.DestroyWithScene, Is.True,
                "The spawned server ball must be destroyed when its Stage02 scene unloads.");
            Component hostStatus = remotePlayer.GetComponent(TypeByName("PlayerTemporaryStatusController"));
            yield return Until(() => Get<bool>(hostStatus, "IsTumbleweedCaptured"), "server captures remote-owned player");
            yield return Until(() => Get<bool>(owner.LocalClient.PlayerObject.GetComponent(TypeByName("PlayerTemporaryStatusController")),
                "IsTumbleweedCaptured"), "remote owner receives capture state");
            NetworkObject ownerBall = null;
            yield return Until(() =>
            {
                ownerBall = FindSpawnedNetworkObject(owner, ballNetwork.NetworkObjectId);
                return ownerBall != null;
            }, "remote owner receives the scene-owned network ball");
            Assert.That(ownerBall.DestroyWithScene, Is.True,
                "The owner's replica retains the scene-destruction lifecycle flag.");

            lateObserver = CreateManager("Tumbleweed late observer", port, weedPrefab, itemPrefab);
            Assert.That(lateObserver.StartClient(), Is.True);
            lateObserver.SetSingleton();
            yield return Until(() => lateObserver.IsConnectedClient && lateObserver.LocalClient?.PlayerObject != null
                && host.ConnectedClientsIds.Count == 3, "late observer connect");
            NetworkObject lateRemotePlayer = null;
            yield return Until(() =>
            {
                foreach (NetworkObject candidate in lateObserver.SpawnManager.SpawnedObjectsList)
                    if (candidate != null && candidate.IsPlayerObject && candidate.OwnerClientId == owner.LocalClientId)
                    { lateRemotePlayer = candidate; return true; }
                return false;
            }, "late observer receives remote player");
            NetworkObject observerBall = null;
            yield return Until(() =>
            {
                observerBall = FindSpawnedNetworkObject(lateObserver, ballNetwork.NetworkObjectId);
                return observerBall != null;
            }, "late observer receives the scene-owned network ball");
            Assert.That(observerBall.DestroyWithScene, Is.True,
                "The late observer replica retains the scene-destruction lifecycle flag.");
            yield return Until(() => Get<bool>(lateRemotePlayer.GetComponent(TypeByName("PlayerTemporaryStatusController")),
                "IsTumbleweedCaptured"), "late observer capture state");
            yield return Until(() => !Get<bool>(hostStatus, "IsTumbleweedCaptured"), "server timed release");
            yield return Until(() => !Get<bool>(owner.LocalClient.PlayerObject.GetComponent(TypeByName("PlayerTemporaryStatusController")),
                "IsTumbleweedCaptured"), "owner timed release");
            Assert.That(remotePlayer.transform.position, Is.Not.EqualTo(new Vector3(5f, 0f, 0f)));

            owner.LocalClient.PlayerObject.transform.position = ball.transform.position;
            yield return Until(() => Vector3.Distance(remotePlayer.transform.position, ball.transform.position) < 0.3f,
                "remote owner returns to ball");
            SetCaptureTiming(ball, 5f, 0.2f, 10);
            yield return Until(() => Get<bool>(hostStatus, "IsTumbleweedCaptured"), "second capture before disconnect");
            owner.Shutdown(true);
            yield return Until(() => host.ConnectedClientsIds.Count == 2, "remote owner disconnect");
            yield return Until(() => Get<int>(ball.GetComponent(TypeByName("TumbleweedCaptureController")), "CurrentCaptiveCount") == 0,
                "disconnect clears server capture reservation");
            Assert.That(Get<int>(ball.GetComponent(TypeByName("TumbleweedCaptureController")), "OccupiedSlotCount"), Is.Zero);
#endif
        }

        private GameObject CreateWeed(Vector3 position, float duration, int capacity)
        {
            GameObject weed = Object.Instantiate(LoadWeedPrefab(), position, Quaternion.identity);
            scope.Own(weed);
            SceneManager.MoveGameObjectToScene(weed, scope.OwnedScene);
            SetCaptureTiming(weed, duration, 5f, capacity);
            Behaviour movement = (Behaviour)weed.GetComponent(TypeByName("TumbleweedMovement"));
            movement.enabled = false;
            Rigidbody body = weed.GetComponent<Rigidbody>();
            body.isKinematic = true;
            return weed;
        }

        private GameObject CreatePlayerFixture(string name, Vector3 position)
        {
            GameObject player = new GameObject(name);
            scope.Own(player);
            SceneManager.MoveGameObjectToScene(player, scope.OwnedScene);
            player.transform.position = position;
            player.AddComponent<NetworkObject>();
            player.AddComponent(TypeByName("PlayerTemporaryStatusController"));
            player.AddComponent(TypeByName("PlayerHealth"));
            CharacterController capsule = player.AddComponent<CharacterController>();
            CopyAuthoredPlayerCapsuleGeometry(capsule);
            player.AddComponent(TypeByName("PlayerTransportCollisionController"));
            return player;
        }

        private static void CopyAuthoredPlayerCapsuleGeometry(CharacterController destination)
        {
#if UNITY_EDITOR
            GameObject authoredPlayer = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
            Assert.That(authoredPlayer, Is.Not.Null, "The capture fixture must use the production PlayerNew prefab geometry.");
            CharacterController authoredCapsule = authoredPlayer.GetComponent<CharacterController>();
            Assert.That(authoredCapsule, Is.Not.Null);
            destination.center = authoredCapsule.center;
            destination.height = authoredCapsule.height;
            destination.radius = authoredCapsule.radius;
#else
            destination.center = new Vector3(0f, 0.93f, 0f);
            destination.height = 2f;
            destination.radius = 0.5f;
#endif
        }

        private static void AssertCapsuleBottomClearsGround(CharacterController capsule)
        {
            Vector3 scale = capsule.transform.lossyScale;
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(radius * 2f, capsule.height * Mathf.Abs(scale.y));
            Vector3 center = capsule.transform.position + capsule.transform.rotation * Vector3.Scale(capsule.center, scale);
            float bottom = center.y - height * 0.5f;
            Assert.That(bottom, Is.GreaterThanOrEqualTo(0.02f),
                $"The released production capsule must sit above the test ground. bottom={bottom:F3}, root={capsule.transform.position}, center={capsule.center}, height={capsule.height}, scale={scale}.");
        }

        private GameObject CreateNetworkPlayerPrefab()
        {
            GameObject staging = scope.Own(new GameObject("Inactive Tumbleweed test player prefab root"));
            staging.SetActive(false);
            GameObject prefab = new GameObject("Tumbleweed loopback player prefab");
            prefab.transform.SetParent(staging.transform, false);
            NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
            typeof(NetworkObject).GetField("GlobalObjectIdHash", Instance).SetValue(networkObject, 0x54574C44u);
            networkObject.SetSceneObjectStatus(false);
            prefab.AddComponent(TypeByName("PlayerTemporaryStatusController"));
            prefab.AddComponent(TypeByName("PlayerHealth"));
            CharacterController capsule = prefab.AddComponent<CharacterController>();
            CopyAuthoredPlayerCapsuleGeometry(capsule);
            prefab.AddComponent(TypeByName("PlayerTransportCollisionController"));
            prefab.AddComponent(TypeByName("ClientNetworkTransform"));
            scope.Own(prefab);
            return prefab;
        }

        private NetworkManager CreateManager(string label, ushort port, GameObject weedPrefab, GameObject itemPrefab)
        {
            GameObject root = scope.Own(new GameObject(label));
            UnityTransport transport = root.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
            NetworkManager manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = playerPrefab,
                EnableSceneManagement = false
            };
            manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = weedPrefab });
            manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = itemPrefab });
            scope.OwnManager(manager);
            return manager;
        }

        private void CreateGround()
        {
            GameObject ground = scope.Own(new GameObject("Tumbleweed test ground"));
            SceneManager.MoveGameObjectToScene(ground, scope.OwnedScene);
            ground.transform.position = Vector3.down * 0.5f;
            BoxCollider collider = ground.AddComponent<BoxCollider>();
            collider.size = new Vector3(100f, 1f, 100f);

            GameObject cameraObject = scope.Own(new GameObject("Tumbleweed fixture main camera"));
            SceneManager.MoveGameObjectToScene(cameraObject, scope.OwnedScene);
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
        }

        private IEnumerator LoadStage02Scene()
        {
            const string scenePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
            Assert.That(!stage02Scene.IsValid() || !stage02Scene.isLoaded, Is.True,
                "The previous Stage02 instance must be unloaded before the next load.");
            bool configuredTimerForWaiting = false;
            void ConfigureWaitingTimer(Scene loadedScene, LoadSceneMode mode)
            {
                if (loadedScene.path != scenePath) return;
                stage02Scene = loadedScene;
                Component timer = FindSceneComponent(loadedScene, TypeByName("GameTimerManager"));
                Assert.That(timer, Is.Not.Null);
                if (!Get<bool>(timer, "waitForStartSignal"))
                    Set(timer, "waitForStartSignal", true);
                configuredTimerForWaiting = true;
            }
            SceneManager.sceneLoaded += ConfigureWaitingTimer;
            try
            {
                AsyncOperation load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                    scenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                Assert.That(load, Is.Not.Null, "The authored Stage02 scene must be loadable through the Editor test environment.");
                yield return load;
            }
            finally
            {
                SceneManager.sceneLoaded -= ConfigureWaitingTimer;
            }
            if (!stage02Scene.IsValid()) stage02Scene = SceneManager.GetSceneByPath(scenePath);
            Assert.That(configuredTimerForWaiting, Is.True,
                "Stage02's timer is set to wait before any authored Start or Update can run.");
            Assert.That(stage02Scene.IsValid() && stage02Scene.isLoaded, Is.True);
            Assert.That(SceneManager.SetActiveScene(stage02Scene), Is.True);
        }

        private IEnumerator UnloadStage02Scene()
        {
            if (!stage02Scene.IsValid() || !stage02Scene.isLoaded) yield break;
            if (scope != null && scope.OwnedScene.IsValid() && scope.OwnedScene.isLoaded)
                Assert.That(SceneManager.SetActiveScene(scope.OwnedScene), Is.True,
                    "Restore the test-owned scene before unloading Stage02's spawned contents.");
            AsyncOperation unload = SceneManager.UnloadSceneAsync(stage02Scene);
            if (unload != null) yield return unload;
            stage02Scene = default;
            Assert.That(FindSceneComponent(scope.OwnedScene, TypeByName("TumbleweedMovement")), Is.Null,
                "Scene-spawned Tumbleweeds must not leak into the test-owned scene across unload.");
        }

        private IEnumerator VerifyStage02SpawnDeadlinesAndReload()
        {
            Type timerType = TypeByName("GameTimerManager");
            Component timer = FindSceneComponent(stage02Scene, timerType);
            Component firstSpawner = FindSceneComponent(stage02Scene, TypeByName("NPCSpawner"), "TumbleweedSpawner_First");
            Component secondSpawner = FindSceneComponent(stage02Scene, TypeByName("NPCSpawner"), "TumbleweedSpawner_Second");
            Assert.That(timer, Is.Not.Null, "Stage02 must retain its authored GameTimerManager.");
            Assert.That(firstSpawner, Is.Not.Null);
            Assert.That(secondSpawner, Is.Not.Null);

            // Let authored Start methods run with the timer held in Waiting, then exercise
            // the timer's normal Waiting -> Running transition without saving the scene.
            yield return null;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(Get(timer, "State").ToString(), Is.EqualTo("Waiting"));
            Assert.That(Get<int>(firstSpawner, "TotalSpawnedNPCCount"), Is.Zero,
                "The first authored spawner stays idle while the round timer waits.");
            Assert.That(Get<int>(secondSpawner, "TotalSpawnedNPCCount"), Is.Zero,
                "The delayed authored spawner stays idle while the round timer waits.");

            Assert.That((bool)CallResult(timer, "TryStartTimer"), Is.True);
            yield return Until(() => Get<int>(firstSpawner, "TotalSpawnedNPCCount") == 1,
                "the zero-second authored spawn group");
            Assert.That(Get<int>(secondSpawner, "TotalSpawnedNPCCount"), Is.Zero,
                "The second spawner remains locked before its one-second timer condition.");
            yield return Until(() => Get<int>(secondSpawner, "TotalSpawnedNPCCount") == 1,
                "the one-second authored spawn group");
            Assert.That(Get<int>(firstSpawner, "TotalSpawnedNPCCount"), Is.EqualTo(1));
            Assert.That(Get<int>(secondSpawner, "TotalSpawnedNPCCount"), Is.EqualTo(1));
            Assert.That(Get<int>(firstSpawner, "ActiveNPCCount"), Is.EqualTo(1));
            Assert.That(Get<int>(secondSpawner, "ActiveNPCCount"), Is.EqualTo(1));
            Assert.That(FindSceneComponents(stage02Scene, TypeByName("TumbleweedMovement")).Length, Is.EqualTo(2),
                "Both scene-owned balls must belong to Stage02 so unload destroys them.");
        }

        private GameObject CreateStage02MovementFixture(Vector3 position, Vector3 direction)
        {
            GameObject ball = Object.Instantiate(LoadWeedPrefab(), position, Quaternion.LookRotation(direction, Vector3.up));
            scope.Own(ball);
            if (ball.scene != stage02Scene) SceneManager.MoveGameObjectToScene(ball, stage02Scene);
            return ball;
        }

        private IEnumerator RunStage02BankApproach(Component water, Vector3 start, Vector3 direction,
            float targetX, bool eastward, string bankName)
        {
            GameObject ball = CreateStage02MovementFixture(start, direction);
            NetworkObject networkObject = ball.GetComponent<NetworkObject>();
            Rigidbody body = ball.GetComponent<Rigidbody>();
            Component movement = ball.GetComponent(TypeByName("TumbleweedMovement"));
            yield return Until(() => !body.isKinematic, bankName + " bank ball dynamics");

            Set(movement, "windDirection", direction);
            Set(movement, "nextWindChangeTime", Time.fixedTime + 60f);

            body.linearVelocity = direction * 1.6f;
            body.angularVelocity = Vector3.zero;
            float maximumY = body.position.y;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline
                   && (eastward ? body.position.x < targetX : body.position.x > targetX))
            {
                maximumY = Mathf.Max(maximumY, body.position.y);
                yield return null;
            }

            maximumY = Mathf.Max(maximumY, body.position.y);
            Assert.That(eastward ? body.position.x : -body.position.x,
                Is.GreaterThanOrEqualTo(eastward ? targetX : -targetX),
                $"The same ball should reach the {bankName} Stage02 bank; final={body.position}, maximumY={maximumY:F2}.");
            Assert.That(ball.GetComponent<NetworkObject>(), Is.SameAs(networkObject),
                "Terrain bank assistance must move the existing ball rather than replace it.");
            float movementRadius = Get<float>(movement, "movementRadius");
            float surfaceHeight = Get<float>(water, "SurfaceHeight");
            Assert.That(maximumY, Is.LessThanOrEqualTo(surfaceHeight + movementRadius * 2f + 0.2f),
                $"Bank assistance must remain bounded by nearby terrain. maximumY={maximumY:F2}, surface={surfaceHeight:F2}.");
        }

        private IEnumerator RunStage02DryBowlEscape(Terrain terrain, Vector3 startXZ, Vector3 direction,
            float requiredProgress, string bowlName)
        {
            Vector3 terrainSample = new(startXZ.x, terrain.transform.position.y, startXZ.z);
            float groundHeight = terrain.SampleHeight(terrainSample) + terrain.transform.position.y;
            Vector3 start = new(startXZ.x, groundHeight + 0.70f, startXZ.z);
            GameObject ball = CreateStage02MovementFixture(start, direction);
            NetworkObject networkObject = ball.GetComponent<NetworkObject>();
            Rigidbody body = ball.GetComponent<Rigidbody>();
            Component movement = ball.GetComponent(TypeByName("TumbleweedMovement"));
            yield return Until(() => !body.isKinematic, bowlName + " ball dynamics");

            Set(movement, "windDirection", direction);
            Set(movement, "nextWindChangeTime", Time.fixedTime + 120f);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            float maximumProgress = 0f;
            float deadline = Time.realtimeSinceStartup + 5.5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                float progress = Vector3.Dot(body.position - start, direction);
                maximumProgress = Mathf.Max(maximumProgress, progress);
                if (maximumProgress >= requiredProgress) break;
                yield return null;
            }

            Assert.That(maximumProgress, Is.GreaterThanOrEqualTo(requiredProgress),
                $"The ball should keep its directed movement across the {bowlName} rim. start={start}, end={body.position}, furthestProgress={maximumProgress:F2}m.");
            Assert.That(ball.GetComponent<NetworkObject>(), Is.SameAs(networkObject),
                "Slope compensation must keep the same Tumbleweed object instance.");
        }

        private static Component FindSceneComponent(Scene scene, Type componentType, string gameObjectName = null)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in root.GetComponentsInChildren(componentType, true))
                {
                    if (component != null && (gameObjectName == null || component.gameObject.name == gameObjectName))
                        return component;
                }
            }
            return null;
        }

        private static Component[] FindSceneComponents(Scene scene, Type componentType)
        {
            List<Component> components = new();
            foreach (GameObject root in scene.GetRootGameObjects())
                components.AddRange(root.GetComponentsInChildren(componentType, true));
            return components.ToArray();
        }

        private static NetworkObject FindSpawnedNetworkObject(NetworkManager manager, ulong networkObjectId)
        {
            if (manager == null || manager.SpawnManager == null) return null;
            foreach (NetworkObject candidate in manager.SpawnManager.SpawnedObjectsList)
                if (candidate != null && candidate.NetworkObjectId == networkObjectId) return candidate;
            return null;
        }

        private static GameObject LoadWeedPrefab()
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Experiments/TutorialTerrainStage02/Tumbleweed/Tumbleweed.prefab");
            Assert.That(prefab, Is.Not.Null, "The registered Stage02 Tumbleweed prefab must be available.");
            return prefab;
#else
            Assert.Ignore("Loads the authored Stage02 Tumbleweed prefab through the Editor AssetDatabase.");
            return null;
#endif
        }

        private static void SetCaptureTiming(GameObject weed, float duration, float immunity, int capacity)
        {
            Component capture = weed.GetComponent(TypeByName("TumbleweedCaptureController"));
            Assert.That(capture, Is.Not.Null);
            Set(capture, "captureDuration", duration);
            Set(capture, "releaseImmunity", immunity);
            Set(capture, "capacity", capacity);
            Call(capture, "EnsureSlotCapacity");
        }

        private static void AssertNoActiveSession()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                Assert.Ignore("The isolated capture fixture requires no pre-existing NGO session.");
        }

        private static IEnumerator Until(Func<bool> condition, string label)
        {
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for " + label + ".");
        }

        private static ushort ReservePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            ushort port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static Type TypeByName(string name) => Type.GetType(name + ", Assembly-CSharp");
        private static object Get(object target, string name) => target.GetType().GetProperty(name, Instance)?.GetValue(target)
            ?? target.GetType().GetField(name, Instance)?.GetValue(target);
        private static T Get<T>(object target, string name) => (T)Get(target, name);
        private static void Set(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, Instance);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }
            PropertyInfo property = target.GetType().GetProperty(name, Instance);
            Assert.That(property, Is.Not.Null, "Could not resolve reflected member " + name + ".");
            property.SetValue(target, value);
        }
        private static void Call(object target, string method, params object[] arguments)
        {
            CallResult(target, method, arguments);
        }

        private static object CallResult(object target, string method, params object[] arguments)
        {
            MethodInfo selected = null;
            foreach (MethodInfo candidate in target.GetType().GetMethods(Instance))
            {
                if (candidate.Name != method) continue;
                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length != arguments.Length) continue;
                bool matches = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (arguments[i] == null)
                    {
                        if (parameters[i].ParameterType.IsValueType) matches = false;
                    }
                    else if (!parameters[i].ParameterType.IsInstanceOfType(arguments[i])) matches = false;
                }
                if (!matches) continue;
                selected = candidate;
                break;
            }
            Assert.That(selected, Is.Not.Null, "Could not resolve reflected method " + method + ".");
            return selected.Invoke(target, arguments);
        }
    }
}
