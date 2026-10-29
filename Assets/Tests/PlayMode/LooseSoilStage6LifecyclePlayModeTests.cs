using System.Collections;
using System.Linq;
using System.Reflection;
using System;
using System.Net.Sockets;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Type = System.Type;

/// <summary>
/// Integration coverage for the authored TutorialTerrainStage02 excavation loop.
/// Reflection is used because the PlayMode test assembly intentionally does not
/// reference Assembly-CSharp directly.
/// </summary>
public sealed class LooseSoilStage6LifecyclePlayModeTests
{
    private const string Stage02Path = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private Scene originalActiveScene;
    private string originalActiveScenePath;
    private string[] originalLoadedScenePaths;
    private AsyncOperation stageLoad;
    private bool ownsStageScene;
    private Scene recoveryScene;
    private GameObject restartControllerRoot;
    private GameObject[] buckets;
    private GameObject testPlayerRoot;
    private Object pouringProfileClone;
    private NetworkManager priorNetworkSingleton;
    private NetworkManager restartHost;
    private GameObject restartHostRoot;
    private GameObject runtimeNetworkBucket;
    private GameObject playmodeRunnerRoot;
    private Scene playmodeRunnerOriginalScene;
    private HideFlags playmodeRunnerOriginalFlags;
    private bool playmodeRunnerOriginalSceneWasBootstrap;
    private bool movedPlaymodeRunnerToDontDestroy;
    private bool playmodeRunnerSurvivedReload;
    private int completedStage02NetworkLoads;
    private readonly List<ulong> lastCompletedNetworkClients = new();
    private readonly List<ulong> lastTimedOutNetworkClients = new();

    [UnitySetUp]
    public IEnumerator LoadAuthoredStage02()
    {
        stageLoad = null;
        ownsStageScene = false;
        recoveryScene = default;
        restartControllerRoot = null;
        buckets = null;
        testPlayerRoot = null;
        pouringProfileClone = null;
        priorNetworkSingleton = NetworkManager.Singleton;
        restartHost = null;
        restartHostRoot = null;
        runtimeNetworkBucket = null;
        playmodeRunnerRoot = null;
        playmodeRunnerOriginalScene = default;
        playmodeRunnerOriginalFlags = HideFlags.None;
        playmodeRunnerOriginalSceneWasBootstrap = false;
        movedPlaymodeRunnerToDontDestroy = false;
        playmodeRunnerSurvivedReload = false;
        completedStage02NetworkLoads = 0;
        lastCompletedNetworkClients.Clear();
        lastTimedOutNetworkClients.Clear();
        originalActiveScene = SceneManager.GetActiveScene();
        originalActiveScenePath = originalActiveScene.IsValid() ? originalActiveScene.path : null;
        playmodeRunnerRoot = GetActivePlaymodeRunnerRoot();
        Assert.That(playmodeRunnerRoot, Is.Not.Null,
            "The actual Unity Test Framework controller is available during fixture setup.");
        playmodeRunnerOriginalScene = playmodeRunnerRoot.scene;
        playmodeRunnerOriginalFlags = playmodeRunnerRoot.hideFlags;
        playmodeRunnerOriginalSceneWasBootstrap = IsTestBootstrapScenePath(playmodeRunnerOriginalScene.path) ||
                                                   playmodeRunnerOriginalScene.name.StartsWith("InitTestScene", StringComparison.Ordinal);
        if (playmodeRunnerRoot.scene.name != "DontDestroyOnLoad")
        {
            Object.DontDestroyOnLoad(playmodeRunnerRoot);
            movedPlaymodeRunnerToDontDestroy = true;
        }
        Assert.That(GetActivePlaymodeRunnerRoot(), Is.SameAs(playmodeRunnerRoot),
            "The active test controller remains tracked after fixture setup preservation.");
        AssertTrackedPlaymodeRunner("fixture setup");
        originalLoadedScenePaths = Enumerable.Range(0, SceneManager.sceneCount)
            .Select(index => SceneManager.GetSceneAt(index))
            .Where(scene => scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(scene.path) &&
                            !IsTestBootstrapScenePath(scene.path))
            .Select(scene => scene.path).Distinct().ToArray();

        Scene stage = SceneManager.GetSceneByPath(Stage02Path);
        Assert.That(!stage.IsValid() || !stage.isLoaded, Is.True,
            "The restart integration test owns its Stage02 load and cannot consume a pre-existing loaded copy.");
        if (!stage.IsValid() || !stage.isLoaded)
        {
            ownsStageScene = true;
            stageLoad = SceneManager.LoadSceneAsync(Stage02Path, LoadSceneMode.Additive);
            Assert.That(stageLoad, Is.Not.Null, "The authored Stage02 scene should load additively.");
            while (!stageLoad.isDone) yield return null;
        }

        stage = SceneManager.GetSceneByPath(Stage02Path);
        Assert.That(stage.IsValid() && stage.isLoaded, Is.True);
        Assert.That(SceneManager.SetActiveScene(stage), Is.True);
        yield return null;
        AssertTrackedPlaymodeRunner("fixture setup completion");
    }

    [UnityTest]
    public IEnumerator AuthoredStage02ExcavatesAllLayersCollectsEighteenUnitsAndRestartsCleanly()
    {
        AssertTrackedPlaymodeRunner("all-layer lifecycle test entry");
        Type volumeType = Resolve("FoundationExcavationVolume");
        Type siteType = Resolve("BridgeConstructionSite");
        Type bucketType = Resolve("PortableSubstanceContainer");
        Type restartType = Resolve("PlayerLevelRestartController");
        Component[] volumes = FindSceneComponents(volumeType, Stage02Path)
            .OrderBy(volume => volume.transform.position.x).ToArray();
        Assert.That(volumes, Has.Length.EqualTo(2), "The authored scene has two foundation excavations.");

        Component volume = volumes[0];
        Component site = GetProperty(volume, "ConstructionSite") as Component;
        Component field = GetProperty(volume, "LooseSoilField") as Component;
        Assert.That(site, Is.Not.Null);
        Assert.That(field, Is.Not.Null);
        Assert.That(site.GetType(), Is.EqualTo(siteType));

        object workflow = GetField(site, "workflow");
        int cycleCount = (int)GetProperty(workflow, "DiggingCycleCount");
        int unitsPerCycle = (int)GetProperty(workflow, "SoilUnitsPerCycle");
        Assert.That(cycleCount, Is.EqualTo(3));
        Assert.That(unitsPerCycle, Is.EqualTo(6));
        PrepareControlledDiggingState(site, volume, field);
        Assert.That((float)GetProperty(GetProperty(field, "HeightField"), "TotalUnits"),
            Is.EqualTo(unitsPerCycle).Within(.001f), "Only the first authored layer is present at a time.");

        Object bucketPrefab;
        Object soil;
#if UNITY_EDITOR
        bucketPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Substances/PortableBucket.prefab");
        soil = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(
            "Assets/ScriptableObjectAssets/New/Substances/Soil.asset");
#else
        Assert.Ignore("Authored Stage02 asset integration runs in the Unity Editor.");
        yield break;
#endif
        Assert.That(bucketPrefab, Is.Not.Null);
        Assert.That(soil, Is.Not.Null);
        float capacity = Convert.ToSingle(GetProperty(((GameObject)bucketPrefab).GetComponent(bucketType), "Capacity"));
        Assert.That(capacity, Is.EqualTo(3f).Within(.0001f));

        Collider rampCollider = GetField(volume, "exitRampCollider") as Collider;
        Renderer rampRenderer = GetField(volume, "exitRampRenderer") as Renderer;
        Assert.That(rampCollider, Is.Not.Null);
        Assert.That(rampRenderer, Is.Not.Null);
        Assert.That(rampCollider.enabled, Is.False, "The authored ramp is disabled throughout the digging stage.");
        Assert.That(rampRenderer.enabled, Is.False);

        MethodInfo applyToolWork = siteType.GetMethod("TryApplyToolWork", new[]
            { Resolve("EquippableItemType"), typeof(float), typeof(int) });
        MethodInfo collect = siteType.GetMethod("TryCollectLooseSoil", Instance);
        Assert.That(applyToolWork, Is.Not.Null);
        Assert.That(collect, Is.Not.Null);

        object diggingTool = GetProperty(workflow, "DiggingTool");
        float looseningWork = (float)GetProperty(workflow, "LooseningProgressPerCycle");
        float collectedTotal = 0f;
        buckets = new GameObject[cycleCount * unitsPerCycle / (int)capacity];
        int bucketIndex = 0;

        try
        {
            for (int cycle = 0; cycle < cycleCount; cycle++)
            {
                Assert.That((int)GetProperty(site, "DiggingCycleIndex"), Is.EqualTo(cycle));
                Assert.That(GetProperty(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
                Assert.That(rampCollider.enabled, Is.False);
                Assert.That(rampRenderer.enabled, Is.False);
                Assert.That((bool)applyToolWork.Invoke(site,
                    new[] { diggingTool, (object)looseningWork, -1 }), Is.True,
                    "A normal shovel-work call loosens each authored layer.");
                Assert.That(GetProperty(site, "DiggingSubstage").ToString(), Is.EqualTo("SoilRemoval"));
                Assert.That((float)GetProperty(GetProperty(field, "HeightField"), "TotalUnits"),
                    Is.EqualTo(unitsPerCycle).Within(.001f), "Loosening exposes one fresh six-unit layer.");
                Vector3 collectionPoint = field.transform.TransformPoint(Vector3.zero);

                for (int bucketFill = 0; bucketFill < unitsPerCycle / (int)capacity; bucketFill++)
                {
                    GameObject bucketObject = Object.Instantiate((GameObject)bucketPrefab);
                    buckets[bucketIndex++] = bucketObject;
                    Component bucket = bucketObject.GetComponent(bucketType);
                    Assert.That(bucket, Is.Not.Null);

                    float accepted = (float)collect.Invoke(site, new object[]
                    {
                        bucket,
                        soil,
                        collectionPoint,
                        100f,
                        capacity,
                        0f
                    });
                    Assert.That(accepted, Is.EqualTo(capacity).Within(.001f),
                        "Each real bucket receives exactly its configured three-unit capacity.");
                    Assert.That((float)GetProperty(bucket, "CurrentUnits"), Is.EqualTo(capacity).Within(.001f));
                    collectedTotal += accepted;

                    Rigidbody bucketBody = bucketObject.GetComponent<Rigidbody>();
                    Assert.That(bucketBody, Is.Not.Null, "Authored portable buckets have a Rigidbody.");
                    bucketBody.isKinematic = true;
                    bucketBody.position = new Vector3(1000f + bucketIndex * 3f, 100f, 1000f);

                    yield return null;
                }

                object heightField = GetProperty(field, "HeightField");
                float expectedFieldUnits = cycle < cycleCount - 1 ? unitsPerCycle : 0f;
                Assert.That((float)GetProperty(heightField, "TotalUnits"), Is.EqualTo(expectedFieldUnits).Within(.00001f),
                    "The next inactive layer is reset after a cycle; final excavation clears the field.");
                Assert.That((float)GetProperty(site, "RemovedSoilUnits"),
                    Is.EqualTo((cycle + 1) * unitsPerCycle).Within(.00001f));
                if (cycle < cycleCount - 1)
                {
                    Assert.That((int)GetProperty(site, "DiggingCycleIndex"), Is.EqualTo(cycle + 1));
                    Assert.That(GetProperty(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
                    Assert.That((float)GetProperty(heightField, "TotalUnits"), Is.EqualTo(unitsPerCycle).Within(.001f),
                        "The next layer is reset to six units but remains inactive until loosened.");
                    Assert.That((bool)GetProperty(field, "IsSurfaceActive"), Is.False);
                }
            }

            Assert.That(collectedTotal, Is.EqualTo(18f).Within(.00001f));
            Assert.That(buckets.Sum(bucketObject => Convert.ToSingle(GetProperty(bucketObject.GetComponent(bucketType), "CurrentUnits"))),
                Is.EqualTo(18f).Within(.00001f), "The six real buckets retain the collected eighteen units until restart.");
            Assert.That(buckets.All(bucketObject => Mathf.Abs(Convert.ToSingle(
                    GetProperty(bucketObject.GetComponent(bucketType), "CurrentUnits")) - capacity) <= .001f), Is.True,
                "All six buckets hold their three-unit capacity before restart.");
            Assert.That(GetProperty(site, "CurrentStage").ToString(), Is.EqualTo("ConcretePouring"));
            Assert.That((float)GetProperty(site, "RemovedSoilUnits"), Is.EqualTo(18f).Within(.00001f));
            Assert.That((float)GetProperty(GetProperty(field, "HeightField"), "TotalUnits"), Is.Zero);
            Assert.That(rampCollider.enabled, Is.True, "The exit ramp returns after excavation is complete.");
            Assert.That(rampRenderer.enabled, Is.True);

            // Continue through the authored concrete delivery path. This clone only enables
            // single-player pouring for this offline test; the shared scene profile is untouched.
            Component wheelbarrow = FindSceneComponents(Resolve("WheelbarrowController"), Stage02Path).Single();
            Component mixer = FindSceneComponents(Resolve("ConcreteMixerController"), Stage02Path).Single();
            Component[] stations = FindSceneComponents(Resolve("WheelbarrowDockingStation"), Stage02Path);
            Component mixerDock = stations.Single(candidate => GetProperty(candidate, "DockType").ToString() == "MixerLoading");
            Component foundationDock = stations.Single(candidate => ReferenceEquals(GetProperty(candidate, "FoundationSite"), site));
            Component minigame = foundationDock.GetComponent(Resolve("WheelbarrowPouringMinigame"));
            Assert.That(minigame, Is.Not.Null);
            Object authoredPouringProfile = GetProperty(minigame, "Profile") as Object;
            Assert.That(authoredPouringProfile, Is.Not.Null);
            Assert.That((bool)GetProperty(authoredPouringProfile, "AllowSinglePlayerTesting"), Is.False,
                "The authored profile remains cooperative-only.");
            pouringProfileClone = Object.Instantiate((Object)authoredPouringProfile);
            pouringProfileClone.name = "Stage6 offline pouring test clone";
            pouringProfileClone.hideFlags = HideFlags.DontSave;
            SetField(pouringProfileClone, "allowSinglePlayerTesting", true);
            SetField(minigame, "profile", pouringProfileClone);
            Assert.That((bool)GetProperty(pouringProfileClone, "AllowSinglePlayerTesting"), Is.True);
            Assert.That((bool)GetProperty(authoredPouringProfile, "AllowSinglePlayerTesting"), Is.False);

            Component player = (Component)Object.FindFirstObjectByType(Resolve("PlayerInteractionNew"));
            Assert.That(player, Is.Not.Null,
                "The authored Stage02 offline gameplay scene provides its real PlayerNew instance.");
            testPlayerRoot = player.gameObject;
            Assert.That(testPlayerRoot.GetComponent(Resolve("PlayerInteractionNew")), Is.SameAs(player));
            Type networkObjectType = Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime");
            Assert.That(networkObjectType, Is.Not.Null);
            Component playerNetworkObject = testPlayerRoot.GetComponent(networkObjectType);
            Component playerCharacterController = testPlayerRoot.GetComponent<CharacterController>();
            Assert.That(player, Is.Not.Null);
            Assert.That(playerNetworkObject, Is.Not.Null);
            Assert.That(playerCharacterController, Is.Not.Null);
            foreach (Behaviour behaviour in testPlayerRoot.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == player || behaviour == playerCharacterController) continue;
                behaviour.enabled = false;
            }
            Assert.That((Component)Object.FindFirstObjectByType(Resolve("PlayerInteractionNew")), Is.SameAs(player));

            Transform driverAnchor = GetField(wheelbarrow, "driverAnchor") as Transform;
            Assert.That(driverAnchor, Is.Not.Null);
            testPlayerRoot.transform.SetPositionAndRotation(driverAnchor.position, driverAnchor.rotation);
            Assert.That((bool)wheelbarrow.GetType().GetMethod("RequestEnterDriver", Instance)
                .Invoke(wheelbarrow, new object[] { testPlayerRoot.transform }), Is.True,
                "The real player boards the authored wheelbarrow through its public driver path.");
            PositionForDock(wheelbarrow, mixerDock);
            Assert.That((bool)mixerDock.GetType().GetMethod("IsCompatibleWith", Instance)
                .Invoke(mixerDock, new[] { wheelbarrow }), Is.True);
            Assert.That((bool)mixerDock.GetType().GetMethod("EvaluateDriverDockingReadiness", Instance)
                .Invoke(mixerDock, new[] { wheelbarrow }), Is.True,
                "The authored mixer dock accepts the real wheelbarrow pose and speed.");
            Assert.That((bool)wheelbarrow.GetType().GetMethod("RequestDriverExitAndDock", Instance)
                .Invoke(wheelbarrow, new object[] { Convert.ToUInt64(GetProperty(wheelbarrow, "DriverClientId")) }), Is.True);
            Assert.That(GetProperty(mixerDock, "DockedWheelbarrow"), Is.SameAs(wheelbarrow));
            Assert.That(GetProperty(wheelbarrow, "HasConcrete"), Is.False);

            Component resolvedMixerPlayer = (Component)Object.FindFirstObjectByType(Resolve("PlayerInteractionNew"));
            Assert.That(resolvedMixerPlayer, Is.SameAs(player));
            testPlayerRoot.transform.position = ((Component)mixer).transform.position;
            Assert.That(Vector3.Distance(testPlayerRoot.transform.position, ((Component)mixer).transform.position),
                Is.LessThanOrEqualTo(3.5f), "The actual mixer interaction-distance gate is satisfied.");
            Assert.That(GetProperty(mixer, "BatchState").ToString(), Is.EqualTo("ConcreteReady"),
                "The authored mixer profile supplies its configured test-ready batch.");
            mixer.GetType().GetMethod("RequestToggleMode", Instance).Invoke(mixer, new object[] { testPlayerRoot.transform });
            Assert.That(GetProperty(mixer, "Mode").ToString(), Is.EqualTo("Pouring"));
            float transferDeadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < transferDeadline && !(bool)GetProperty(wheelbarrow, "HasConcrete"))
                yield return null;
            Assert.That((bool)GetProperty(wheelbarrow, "HasConcrete"), Is.True,
                "The mixer transfers its actual batch after the authored 0.75-second delay.");
            Assert.That((int)GetProperty(wheelbarrow, "ConcreteLoads"), Is.EqualTo(1));

            driverAnchor = GetField(wheelbarrow, "driverAnchor") as Transform;
            Assert.That(driverAnchor, Is.Not.Null);
            testPlayerRoot.transform.SetPositionAndRotation(driverAnchor.position, driverAnchor.rotation);
            Assert.That((bool)wheelbarrow.GetType().GetMethod("RequestEnterDriver", Instance)
                .Invoke(wheelbarrow, new object[] { testPlayerRoot.transform }), Is.True);
            PositionForDock(wheelbarrow, foundationDock);
            Assert.That((bool)foundationDock.GetType().GetMethod("IsCompatibleWith", Instance)
                .Invoke(foundationDock, new[] { wheelbarrow }), Is.True);
            Assert.That((bool)foundationDock.GetType().GetMethod("EvaluateDriverDockingReadiness", Instance)
                .Invoke(foundationDock, new[] { wheelbarrow }), Is.True,
                "The authored foundation dock accepts the loaded wheelbarrow inside its grounded capture volume.");
            Assert.That((bool)wheelbarrow.GetType().GetMethod("RequestDriverExitAndDock", Instance)
                .Invoke(wheelbarrow, new object[] { Convert.ToUInt64(GetProperty(wheelbarrow, "DriverClientId")) }), Is.True);
            Assert.That(GetProperty(foundationDock, "DockedWheelbarrow"), Is.SameAs(wheelbarrow));
            Assert.That(GetProperty(minigame, "State").ToString(), Is.EqualTo("WaitingForPlayers"));
            Transform soloAnchor = GetProperty(minigame, "SoloPlayerAnchor") as Transform;
            Assert.That(soloAnchor, Is.Not.Null);
            testPlayerRoot.transform.SetPositionAndRotation(soloAnchor.position, soloAnchor.rotation);
            Assert.That((bool)minigame.GetType().GetMethod("CanOfferJoin", Instance)
                .Invoke(minigame, new object[] { testPlayerRoot.transform, true }), Is.True,
                "The player is offered the authored grip after real dock binding and profile validation.");
            Assert.That((bool)minigame.GetType().GetMethod("RequestJoin", Instance)
                .Invoke(minigame, new object[] { testPlayerRoot.transform, true }), Is.True,
                "Public join performs the authored anchor, ground, grip, and collision validation.");
            Assert.That(GetProperty(minigame, "State").ToString(), Is.EqualTo("Active"));
            float pourDeadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < pourDeadline && GetProperty(minigame, "State").ToString() != "Success")
            {
                minigame.GetType().GetMethod("SubmitCursorDelta", Instance)
                    .Invoke(minigame, new object[] { 1f, Convert.ToUInt64(GetProperty(playerNetworkObject, "OwnerClientId")) });
                yield return null;
            }
            Assert.That(GetProperty(minigame, "State").ToString(), Is.EqualTo("Success"),
                "Bounded public cursor samples complete the real pour without setting private state.");
            Assert.That((bool)GetProperty(wheelbarrow, "HasConcrete"), Is.False,
                "The successful pour consumes the wheelbarrow's physical concrete load.");
            Assert.That((int)GetProperty(site, "ConcreteLoadsPoured"), Is.EqualTo(1));
            Assert.That(GetProperty(site, "CurrentStage").ToString(), Is.EqualTo("ConcreteDrying"));

            yield return new WaitForSeconds(31f);
            Assert.That(GetProperty(site, "CurrentStage").ToString(), Is.EqualTo("ReadyForMount"),
                "The authored thirty-second concrete drying transition completes naturally.");
            Assert.That(rampCollider.enabled, Is.False,
                "Fully poured and dry concrete disables the ramp collider while the concrete collider is active.");
            Assert.That(rampRenderer.enabled, Is.False,
                "The authored ramp renderer is hidden after the foundation is fully poured and dry.");
            Assert.That(buckets.Sum(bucketObject => Convert.ToSingle(GetProperty(bucketObject.GetComponent(bucketType), "CurrentUnits"))),
                Is.EqualTo(18f).Within(.00001f), "All six staged buckets still account for eighteen units immediately before restart.");

            restartControllerRoot = new GameObject("Stage6 restart integration probe");
            SceneManager.MoveGameObjectToScene(restartControllerRoot, SceneManager.GetSceneByPath(Stage02Path));
            Component restartController = restartControllerRoot.AddComponent(restartType);
            Assert.That((bool)GetProperty(restartController, "CanRequestRestart"), Is.True,
                "The public local restart path is available in the offline gameplay scene.");
            int oldSceneHandle = SceneManager.GetSceneByPath(Stage02Path).handle;
            restartType.GetMethod("RequestRestartLevel", Instance).Invoke(restartController, null);

            float restartDeadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < restartDeadline)
            {
                Scene reloaded = SceneManager.GetSceneByPath(Stage02Path);
                Component[] reloadedVolumes = reloaded.IsValid() && reloaded.isLoaded && reloaded.handle != oldSceneHandle
                    ? FindSceneComponents(volumeType, Stage02Path)
                    : new Component[0];
                if (reloadedVolumes.Length == 2 &&
                    GetProperty(GetField(reloadedVolumes[0], "constructionSite"), "CurrentStage").ToString() == "Clearing" &&
                    (int)GetProperty(GetField(reloadedVolumes[0], "constructionSite"), "RemainingObstacleCount") == 3)
                    break;
                yield return null;
            }

            Component[] resetVolumes = FindSceneComponents(volumeType, Stage02Path)
                .OrderBy(candidate => candidate.transform.position.x).ToArray();
            Assert.That(resetVolumes, Has.Length.EqualTo(2), "The restart reloads the authored scene.");
            Component resetSite = GetField(resetVolumes[0], "constructionSite") as Component;
            Component resetField = GetField(resetVolumes[0], "looseSoilField") as Component;
            Assert.That(GetProperty(resetSite, "CurrentStage").ToString(), Is.EqualTo("Clearing"),
                "Offline restart restores the authored obstacle-clearing stage.");
            Assert.That((int)GetProperty(resetSite, "RemainingObstacleCount"), Is.EqualTo(3));
            Assert.That((int)GetProperty(resetSite, "DiggingCycleIndex"), Is.Zero);
            Assert.That(GetProperty(resetSite, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
            Assert.That((float)GetProperty(resetSite, "RemovedSoilUnits"), Is.Zero.Within(.001f));
            Assert.That((bool)GetProperty(resetField, "IsSurfaceActive"), Is.False,
                "A restarted field has its initial unloosened surface state.");
            Assert.That((float)GetProperty(GetProperty(resetField, "HeightField"), "TotalUnits"),
                Is.EqualTo(unitsPerCycle).Within(.001f), "Restart restores the first six-unit layer.");
            Collider resetRampCollider = GetField(resetVolumes[0], "exitRampCollider") as Collider;
            Renderer resetRampRenderer = GetField(resetVolumes[0], "exitRampRenderer") as Renderer;
            Assert.That(resetRampCollider.enabled, Is.True);
            Assert.That(resetRampRenderer.enabled, Is.True);

            foreach (GameObject bucketObject in buckets)
            {
                Assert.That(bucketObject == null, Is.True,
                    "A single-scene restart removes each collected bucket from the old gameplay scene.");
            }
            buckets = null;
        }
        finally
        {
            foreach (GameObject bucketObject in buckets ?? new GameObject[0])
                if (bucketObject != null) Object.Destroy(bucketObject);
            if (restartControllerRoot != null) Object.Destroy(restartControllerRoot);
            if (pouringProfileClone != null) Object.Destroy(pouringProfileClone);
        }

        yield return null;
        AssertTrackedPlaymodeRunner("all-layer lifecycle test exit");
    }

    [UnityTest]
    public IEnumerator ReturningSoilDuringAnActivePourCannotConsumeTheDockedConcreteLoad()
    {
        AssertTrackedPlaymodeRunner("active-pour return test entry");
        Type volumeType = Resolve("FoundationExcavationVolume");
        Type bucketType = Resolve("PortableSubstanceContainer");
        Type siteType = Resolve("BridgeConstructionSite");
        Component volume = FindSceneComponents(volumeType, Stage02Path)
            .OrderBy(candidate => candidate.transform.position.x).First();
        Component site = GetField(volume, "constructionSite") as Component;
        Component field = GetField(volume, "looseSoilField") as Component;
        PrepareControlledDiggingState(site, volume, field);
        object workflow = GetField(site, "workflow");
        int cycles = (int)GetProperty(workflow, "DiggingCycleCount");
        int unitsPerCycle = (int)GetProperty(workflow, "SoilUnitsPerCycle");
        Object bucketPrefab;
        Object soil;
#if UNITY_EDITOR
        bucketPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Substances/PortableBucket.prefab");
        soil = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(
            "Assets/ScriptableObjectAssets/New/Substances/Soil.asset");
#else
        Assert.Ignore("Authored Stage02 asset integration runs in the Unity Editor.");
        yield break;
#endif
        Assert.That(bucketPrefab, Is.Not.Null);
        Assert.That(soil, Is.Not.Null);
        float capacity = Convert.ToSingle(GetProperty(((GameObject)bucketPrefab).GetComponent(bucketType), "Capacity"));
        buckets = new GameObject[cycles * unitsPerCycle / (int)capacity];
        int bucketIndex = 0;

        try
        {
            MethodInfo applyToolWork = siteType.GetMethod("TryApplyToolWork", new[]
                { Resolve("EquippableItemType"), typeof(float), typeof(int) });
            MethodInfo collect = siteType.GetMethod("TryCollectLooseSoil", Instance);
            object diggingTool = GetProperty(workflow, "DiggingTool");
            float looseningWork = (float)GetProperty(workflow, "LooseningProgressPerCycle");
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                Assert.That((bool)applyToolWork.Invoke(site, new[] { diggingTool, (object)looseningWork, -1 }), Is.True);
                Vector3 collectionPoint = field.transform.TransformPoint(Vector3.zero);
                for (int fillIndex = 0; fillIndex < unitsPerCycle / (int)capacity; fillIndex++)
                {
                    GameObject bucketObject = Object.Instantiate((GameObject)bucketPrefab);
                    buckets[bucketIndex++] = bucketObject;
                    Component bucket = bucketObject.GetComponent(bucketType);
                    Assert.That((float)collect.Invoke(site, new object[]
                    {
                        bucket, soil, collectionPoint, 100f, capacity, 0f
                    }), Is.EqualTo(capacity).Within(.00001f));
                    Rigidbody bucketBody = bucketObject.GetComponent<Rigidbody>();
                    Assert.That(bucketBody, Is.Not.Null);
                    bucketBody.isKinematic = true;
                    bucketBody.position = new Vector3(1000f + bucketIndex * 3f, 100f, 1000f);
                    yield return null;
                }
            }
            Assert.That(GetProperty(site, "CurrentStage").ToString(), Is.EqualTo("ConcretePouring"));
            Assert.That(buckets.Sum(bucketObject => Convert.ToSingle(
                GetProperty(bucketObject.GetComponent(bucketType), "CurrentUnits"))), Is.EqualTo(18f).Within(.00001f));

            Component[] stations = FindSceneComponents(Resolve("WheelbarrowDockingStation"), Stage02Path);
            Component mixerDock = stations.Single(candidate => GetProperty(candidate, "DockType").ToString() == "MixerLoading");
            Component foundationDock = stations.Single(candidate => ReferenceEquals(GetProperty(candidate, "FoundationSite"), site));
            Component minigame = foundationDock.GetComponent(Resolve("WheelbarrowPouringMinigame"));
            Assert.That(minigame, Is.Not.Null);
            Object authoredPouringProfile = GetProperty(minigame, "Profile") as Object;
            Assert.That(authoredPouringProfile, Is.Not.Null);
            Assert.That((bool)GetProperty(authoredPouringProfile, "AllowSinglePlayerTesting"), Is.False);
            float authoredCursorSensitivity = Convert.ToSingle(GetProperty(authoredPouringProfile, "CursorSensitivity"));
            float authoredAutomaticPartnerSpeed = Convert.ToSingle(GetProperty(authoredPouringProfile, "AutomaticPartnerSpeed"));
            pouringProfileClone = Object.Instantiate(authoredPouringProfile);
            pouringProfileClone.name = "Stage6 soil-return regression profile clone";
            pouringProfileClone.hideFlags = HideFlags.DontSave;
            SetField(pouringProfileClone, "allowSinglePlayerTesting", true);
            SetField(minigame, "profile", pouringProfileClone);

            Component player = (Component)Object.FindFirstObjectByType(Resolve("PlayerInteractionNew"));
            Assert.That(player, Is.Not.Null);
            testPlayerRoot = player.gameObject;
            Component playerNetworkObject = player.GetComponent(Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime"));
            Assert.That(playerNetworkObject, Is.Not.Null);
            Component wheelbarrow = FindSceneComponents(Resolve("WheelbarrowController"), Stage02Path).Single();
            Component mixer = FindSceneComponents(Resolve("ConcreteMixerController"), Stage02Path).Single();

            Transform driverAnchor = GetField(wheelbarrow, "driverAnchor") as Transform;
            Assert.That(driverAnchor, Is.Not.Null);
            player.transform.SetPositionAndRotation(driverAnchor.position, driverAnchor.rotation);
            Assert.That((bool)wheelbarrow.GetType().GetMethod("RequestEnterDriver", Instance)
                .Invoke(wheelbarrow, new object[] { player.transform }), Is.True);
            PositionForDock(wheelbarrow, mixerDock);
            Assert.That((bool)mixerDock.GetType().GetMethod("EvaluateDriverDockingReadiness", Instance)
                .Invoke(mixerDock, new[] { wheelbarrow }), Is.True);
            Assert.That((bool)wheelbarrow.GetType().GetMethod("RequestDriverExitAndDock", Instance)
                .Invoke(wheelbarrow, new object[] { Convert.ToUInt64(GetProperty(wheelbarrow, "DriverClientId")) }), Is.True);
            player.transform.position = ((Component)mixer).transform.position;
            Assert.That(GetProperty(mixer, "BatchState").ToString(), Is.EqualTo("ConcreteReady"));
            mixer.GetType().GetMethod("RequestToggleMode", Instance).Invoke(mixer, new object[] { player.transform });
            float transferDeadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < transferDeadline && !(bool)GetProperty(wheelbarrow, "HasConcrete"))
                yield return null;
            Assert.That((bool)GetProperty(wheelbarrow, "HasConcrete"), Is.True);

            driverAnchor = GetField(wheelbarrow, "driverAnchor") as Transform;
            player.transform.SetPositionAndRotation(driverAnchor.position, driverAnchor.rotation);
            Assert.That((bool)wheelbarrow.GetType().GetMethod("RequestEnterDriver", Instance)
                .Invoke(wheelbarrow, new object[] { player.transform }), Is.True);
            PositionForDock(wheelbarrow, foundationDock);
            Assert.That((bool)foundationDock.GetType().GetMethod("EvaluateDriverDockingReadiness", Instance)
                .Invoke(foundationDock, new[] { wheelbarrow }), Is.True);
            Assert.That((bool)wheelbarrow.GetType().GetMethod("RequestDriverExitAndDock", Instance)
                .Invoke(wheelbarrow, new object[] { Convert.ToUInt64(GetProperty(wheelbarrow, "DriverClientId")) }), Is.True);
            Assert.That(GetProperty(minigame, "State").ToString(), Is.EqualTo("WaitingForPlayers"));
            minigame.GetType().GetMethod("CancelAndRelease", Instance).Invoke(minigame, new object[] { false });
            yield return null;
            Assert.That(GetProperty(minigame, "State").ToString(), Is.EqualTo("Inactive"),
                "An explicit cancellation remains inactive across updates while the foundation is ready.");
            Assert.That((bool)GetProperty(wheelbarrow, "HasConcrete"), Is.True,
                "Explicit non-loss cancellation preserves the concrete batch.");
            minigame.GetType().GetMethod("Bind", Instance).Invoke(minigame,
                new object[] { foundationDock, wheelbarrow, site });
            Assert.That(GetProperty(minigame, "State").ToString(), Is.EqualTo("WaitingForPlayers"));
            Transform soloAnchor = GetProperty(minigame, "SoloPlayerAnchor") as Transform;
            Assert.That(soloAnchor, Is.Not.Null);
            player.transform.SetPositionAndRotation(soloAnchor.position, soloAnchor.rotation);
            Assert.That((bool)minigame.GetType().GetMethod("RequestJoin", Instance)
                .Invoke(minigame, new object[] { player.transform, true }), Is.True);
            Assert.That(GetProperty(minigame, "State").ToString(), Is.EqualTo("Active"));
            Assert.That((bool)GetProperty(wheelbarrow, "HasConcrete"), Is.True);

            Component returnBucket = buckets[0].GetComponent(bucketType);
            Vector3 returnPoint = field.transform.TransformPoint(Vector3.zero);
            MethodInfo returnSoil = siteType.GetMethod("TryReturnLooseSoilFromBucket", Instance);
            Assert.That(returnSoil, Is.Not.Null);
            float returned = (float)returnSoil.Invoke(site,
                new object[] { returnBucket, soil, returnPoint, 1f });
            Assert.That(returned, Is.GreaterThan(0f),
                "The normal public return path accepts loose soil and debits its source bucket.");
            Assert.That((float)GetProperty(returnBucket, "CurrentUnits"),
                Is.EqualTo(capacity - returned).Within(.00001f), "The partial local return remains conserved in the source bucket.");
            Assert.That(GetProperty(site, "CurrentStage").ToString(), Is.EqualTo("Digging"));

            ulong clientId = Convert.ToUInt64(GetProperty(playerNetworkObject, "OwnerClientId"));
            yield return null;
            Assert.That(GetProperty(minigame, "State").ToString(), Is.EqualTo("WaitingForPlayers"),
                "The next authoritative update cancels a pour invalidated by soil return.");
            Assert.That(GetProperty(minigame, "LeftPlayer"), Is.EqualTo(ulong.MaxValue));
            Assert.That(GetProperty(minigame, "RightPlayer"), Is.EqualTo(ulong.MaxValue));
            Assert.That((float)GetProperty(minigame, "LeftCursor"), Is.Zero);
            Assert.That((float)GetProperty(minigame, "RightCursor"), Is.Zero);
            Assert.That((bool)GetProperty(wheelbarrow, "IsDockSecured"), Is.True);
            Assert.That((bool)GetProperty(wheelbarrow, "HasConcrete"), Is.True,
                "Cancellation restores the secured wheelbarrow without spilling its batch.");
            player.transform.SetPositionAndRotation(soloAnchor.position, soloAnchor.rotation);
            bool canJoinWhileDigging = (bool)minigame.GetType().GetMethod("CanOfferJoin", Instance)
                .Invoke(minigame, new object[] { player.transform, true });
            bool joinedWhileDigging = (bool)minigame.GetType().GetMethod("RequestJoin", Instance)
                .Invoke(minigame, new object[] { player.transform, true });
            Assert.That(canJoinWhileDigging, Is.False);
            Assert.That(joinedWhileDigging, Is.False);
            Assert.That((bool)GetProperty(wheelbarrow, "HasConcrete"), Is.True);

            float bucketBeforeRecollect = (float)GetProperty(returnBucket, "CurrentUnits");
            float recollected = (float)collect.Invoke(site, new object[]
            {
                returnBucket, soil, returnPoint, 100f, capacity, 0f
            });
            float bucketAfterRecollect = (float)GetProperty(returnBucket, "CurrentUnits");
            Assert.That(recollected, Is.EqualTo(returned).Within(.00001f),
                "Public shovel collection recovers the returned amount from the reopened excavation.");
            Assert.That(bucketAfterRecollect, Is.EqualTo(capacity).Within(.00001f),
                "The real bucket returns to its full, conserved three-unit content.");
            Assert.That(Mathf.Abs((bucketAfterRecollect - bucketBeforeRecollect) - recollected), Is.LessThanOrEqualTo(.00001f));
            Assert.That(GetProperty(site, "CurrentStage").ToString(), Is.EqualTo("ConcretePouring"));
            Assert.That((float)GetProperty(site, "RemovedSoilUnits"), Is.EqualTo(18f).Within(.00001f));
            Assert.That((float)GetProperty(GetProperty(field, "HeightField"), "TotalUnits"), Is.Zero);

            float waitingDeadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < waitingDeadline && GetProperty(minigame, "State").ToString() != "WaitingForPlayers")
                yield return null;
            Assert.That(GetProperty(minigame, "State").ToString(), Is.EqualTo("WaitingForPlayers"),
                "The same secured dock resumes its normal waiting session once the foundation is ready again.");
            player.transform.SetPositionAndRotation(soloAnchor.position, soloAnchor.rotation);
            Assert.That((bool)minigame.GetType().GetMethod("CanOfferJoin", Instance)
                .Invoke(minigame, new object[] { player.transform, true }), Is.True);
            Assert.That((bool)minigame.GetType().GetMethod("RequestJoin", Instance)
                .Invoke(minigame, new object[] { player.transform, true }), Is.True,
                "The same dock accepts a normal public join after the returned soil is recovered.");

            float cursorSensitivity = Convert.ToSingle(GetProperty(pouringProfileClone, "CursorSensitivity"));
            float cursorStep = Mathf.Min(
                Convert.ToSingle(GetProperty(pouringProfileClone, "MaximumCursorSpeed")) * 0.1f,
                Convert.ToSingle(GetProperty(pouringProfileClone, "AutomaticPartnerSpeed")) * 0.1f);
            Assert.That(cursorSensitivity, Is.GreaterThan(0f));
            Assert.That(cursorStep, Is.GreaterThan(0f));
            float inputDelta = cursorStep / cursorSensitivity;
            float pourDeadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < pourDeadline &&
                   GetProperty(minigame, "State").ToString() == "Active")
            {
                minigame.GetType().GetMethod("SubmitCursorDelta", Instance)
                    .Invoke(minigame, new object[] { inputDelta, clientId });
                yield return null;
            }
            string pourState = GetProperty(minigame, "State").ToString();
            bool concreteLoadConsumed = !(bool)GetProperty(wheelbarrow, "HasConcrete");
            int pouredLoads = (int)GetProperty(site, "ConcreteLoadsPoured");
            string postPourStage = GetProperty(site, "CurrentStage").ToString();
            Assert.That(pourState, Is.EqualTo("Success"));
            Assert.That(concreteLoadConsumed, Is.True);
            Assert.That(pouredLoads, Is.EqualTo(1));
            Assert.That(postPourStage, Is.EqualTo("ConcreteDrying"));
            Assert.That(GetProperty(minigame, "Profile"), Is.SameAs(pouringProfileClone),
                "The test-only clone remains assigned only to this runtime minigame instance.");
            Assert.That((bool)GetProperty(authoredPouringProfile, "AllowSinglePlayerTesting"), Is.False,
                "The shared authored pouring profile remains cooperative-only and unchanged.");
            Assert.That(Convert.ToSingle(GetProperty(authoredPouringProfile, "CursorSensitivity")),
                Is.EqualTo(authoredCursorSensitivity).Within(.000001f));
            Assert.That(Convert.ToSingle(GetProperty(authoredPouringProfile, "AutomaticPartnerSpeed")),
                Is.EqualTo(authoredAutomaticPartnerSpeed).Within(.000001f));
        }
        finally
        {
            foreach (GameObject bucketObject in buckets ?? new GameObject[0])
                if (bucketObject != null) Object.Destroy(bucketObject);
            buckets = null;
            if (pouringProfileClone != null) Object.Destroy(pouringProfileClone);
            pouringProfileClone = null;
        }
        yield return null;
        AssertTrackedPlaymodeRunner("active-pour return test exit");
    }

    [UnityTest]
    public IEnumerator AuthoredStage02HostPublicRestartReloadsNetworkSceneAndRestoresExcavation()
    {
        AssertTrackedPlaymodeRunner("host restart test entry");
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Stage02 assets available only in the Unity Editor.");
        yield break;
#else
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True,
            "The host restart fixture does not take over an existing NGO session.");
        Assert.That(SceneUtility.GetBuildIndexByScenePath(Stage02Path), Is.GreaterThanOrEqualTo(0));

        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        GameObject bucketPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Substances/PortableBucket.prefab");
        Object soil = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(
            "Assets/ScriptableObjectAssets/New/Substances/Soil.asset");
        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(bucketPrefab, Is.Not.Null);
        Assert.That(soil, Is.Not.Null);

        restartHostRoot = new GameObject("Stage6 authored Stage02 restart host");
        Object.DontDestroyOnLoad(restartHostRoot);
        UnityTransport transport = restartHostRoot.AddComponent<UnityTransport>();
        MethodInfo setConnectionData = typeof(UnityTransport).GetMethod("SetConnectionData",
            new[] { typeof(string), typeof(ushort), typeof(string) });
        Assert.That(setConnectionData, Is.Not.Null,
            "The installed UnityTransport exposes its string/port/listen-address configuration API.");
        setConnectionData.Invoke(transport, new object[] { "127.0.0.1", ReservePort(), "127.0.0.1" });
        restartHost = restartHostRoot.AddComponent<NetworkManager>();
        restartHost.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = playerPrefab,
            EnableSceneManagement = true
        };
        restartHost.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = bucketPrefab });

        restartHost.SetSingleton();
        Component playerSpawnManager = restartHostRoot.AddComponent(Resolve("PlayerSpawnManager"));
        Assert.That(playerSpawnManager, Is.Not.Null,
            "The owned host includes the same authored player-respawn service as MultiplayerStartScene.");
        SetField(playerSpawnManager, "playerPrefab", playerPrefab);
        Assert.That(GetField(playerSpawnManager, "playerPrefab"), Is.SameAs(playerPrefab));
        Assert.That(restartHost.StartHost(), Is.True, "The isolated host starts with NGO scene management enabled.");
        float startupDeadline = Time.realtimeSinceStartup + 20f;
        while (Time.realtimeSinceStartup < startupDeadline &&
               (!restartHost.IsListening || restartHost.LocalClient?.PlayerObject == null))
            yield return null;
        Assert.That(restartHost.IsListening && restartHost.LocalClient?.PlayerObject != null, Is.True,
            "NGO spawns the configured real PlayerNew host owner.");
        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Stage02Path));

        Type volumeType = Resolve("FoundationExcavationVolume");
        Type siteType = Resolve("BridgeConstructionSite");
        Type bucketType = Resolve("PortableSubstanceContainer");
        Component[] initialVolumes = FindSceneComponents(volumeType, Stage02Path)
            .OrderBy(volume => volume.transform.position.x).ToArray();
        Assert.That(initialVolumes, Has.Length.EqualTo(2));
        Component initialVolume = initialVolumes[0];
        Component initialSite = GetField(initialVolume, "constructionSite") as Component;
        Component initialField = GetField(initialVolume, "looseSoilField") as Component;
        object initialHeightField = GetProperty(initialField, "HeightField");
        Assert.That(initialSite.GetType(), Is.EqualTo(siteType));
        Assert.That(GetProperty(initialSite, "CurrentStage").ToString(), Is.EqualTo("Clearing"),
            "The authored Stage02 site starts in its saved obstacle-clearing stage.");
        Assert.That((int)GetProperty(initialSite, "RemainingObstacleCount"), Is.EqualTo(3),
            "The authored initial state retains its three active clearing obstacles.");
        Assert.That((bool)GetProperty(initialSite, "RequiresSiteClearing"), Is.True);
        Assert.That((int)GetProperty(initialSite, "DiggingCycleIndex"), Is.Zero);
        Assert.That((float)GetProperty(initialSite, "RemovedSoilUnits"), Is.Zero);
        Assert.That((bool)GetProperty(initialField, "IsSurfaceActive"), Is.False);
        Assert.That((float)GetProperty(initialHeightField, "TotalUnits"), Is.EqualTo(6f).Within(.00001f));
        object initialShape = initialHeightField.GetType().GetMethod("CaptureSnapshot", Instance).Invoke(initialHeightField, null);
        Assert.That(initialShape, Is.Not.Null);
        Collider initialRamp = GetField(initialVolume, "exitRampCollider") as Collider;
        Renderer initialRampRenderer = GetField(initialVolume, "exitRampRenderer") as Renderer;
        Assert.That(initialRamp, Is.Not.Null);
        Assert.That(initialRampRenderer, Is.Not.Null);
        Assert.That(initialRamp.enabled, Is.True, "The ramp remains available during authored obstacle clearing.");
        Assert.That(initialRampRenderer.enabled, Is.True);

        NetworkObject restartPlayerObject = restartHost.LocalClient.PlayerObject.GetComponent<NetworkObject>();
        Assert.That(restartPlayerObject, Is.Not.Null);
        Assert.That(restartPlayerObject.IsSpawned && restartPlayerObject.IsPlayerObject, Is.True);
        Assert.That(restartPlayerObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Type restartType = Resolve("PlayerLevelRestartController");
        Component restartController = restartPlayerObject.GetComponent(restartType);
        Assert.That(restartController, Is.Not.Null,
            "The real configured PlayerNew prefab owns the public restart controller.");
        Assert.That((bool)GetProperty(restartController, "CanRequestRestart"), Is.True);

        object workflow = GetField(initialSite, "workflow");
        object diggingTool = GetProperty(workflow, "DiggingTool");
        float looseningWork = Convert.ToSingle(GetProperty(workflow, "LooseningProgressPerCycle"));
        MethodInfo applyToolWork = siteType.GetMethod("TryApplyToolWork", new[]
            { Resolve("EquippableItemType"), typeof(float), typeof(int) });
        Assert.That(applyToolWork, Is.Not.Null);
        PrepareControlledDiggingState(initialSite, initialVolume, initialField);
        Assert.That((bool)applyToolWork.Invoke(initialSite,
            new[] { diggingTool, (object)looseningWork, -1 }), Is.True,
            "The host applies ordinary public shovel work to the actual authored foundation.");
        Assert.That(GetProperty(initialSite, "DiggingSubstage").ToString(), Is.EqualTo("SoilRemoval"));

        runtimeNetworkBucket = Object.Instantiate(bucketPrefab,
            new Vector3(900f, 30f, 900f), Quaternion.identity);
        runtimeNetworkBucket.name = "Stage6 host restart spawned soil bucket";
        SceneManager.MoveGameObjectToScene(runtimeNetworkBucket, SceneManager.GetSceneByPath(Stage02Path));
        Rigidbody bucketBody = runtimeNetworkBucket.GetComponent<Rigidbody>();
        Assert.That(bucketBody, Is.Not.Null);
        bucketBody.isKinematic = true;
        NetworkObject runtimeBucketNetworkObject = runtimeNetworkBucket.GetComponent<NetworkObject>();
        Assert.That(runtimeBucketNetworkObject, Is.Not.Null);
        runtimeBucketNetworkObject.Spawn();
        Component bucket = runtimeNetworkBucket.GetComponent(bucketType);
        Vector3 collectionPoint = initialField.transform.TransformPoint(Vector3.zero);
        float extracted = (float)siteType.GetMethod("TryCollectLooseSoil", Instance).Invoke(initialSite,
            new object[] { bucket, soil, collectionPoint, 3f, 1f, 0f });
        Assert.That(extracted, Is.InRange(.00001f, 1f),
            "The host collects a conserved portion through the public source-to-network-bucket path.");
        Assert.That((float)GetProperty(bucket, "CurrentUnits"), Is.EqualTo(extracted).Within(.00001f));
        Assert.That((float)GetProperty(GetProperty(initialField, "HeightField"), "TotalUnits"),
            Is.EqualTo(6f - extracted).Within(.00001f));
        Assert.That((float)GetProperty(bucket, "CurrentUnits") +
                    (float)GetProperty(GetProperty(initialField, "HeightField"), "TotalUnits"),
            Is.EqualTo(6f).Within(.00001f), "The carved field and spawned bucket conserve the layer ledger.");
        Assert.That((bool)GetProperty(initialField, "IsSurfaceActive"), Is.True);
        object carvedHeightField = GetProperty(initialField, "HeightField");
        Assert.That((bool)carvedHeightField.GetType().GetMethod("MatchesSnapshot", Instance)
            .Invoke(carvedHeightField, new[] { initialShape }), Is.False,
            "The nonuniform public shovel/collection work changes the initial field shape.");

        int initialSceneHandle = SceneManager.GetSceneByPath(Stage02Path).handle;
        restartHost.SceneManager.OnLoadEventCompleted += RecordStage02NetworkLoadCompletion;
        restartType.GetMethod("RequestRestartLevel", Instance).Invoke(restartController, null);
        float firstRestartDeadline = Time.realtimeSinceStartup + 30f;
        while (Time.realtimeSinceStartup < firstRestartDeadline &&
               (completedStage02NetworkLoads < 1 || SceneManager.GetSceneByPath(Stage02Path).handle == initialSceneHandle))
            yield return null;
        Assert.That(completedStage02NetworkLoads, Is.EqualTo(1),
            "The actual NGO Single scene load completes before restart state is inspected.");
        Assert.That(lastTimedOutNetworkClients, Is.Empty, "The host-only restart has no timed-out NGO clients.");
        Assert.That(lastCompletedNetworkClients, Does.Contain(NetworkManager.ServerClientId));
        Assert.That(playmodeRunnerSurvivedReload, Is.True,
            "The actual Unity Test Framework runner survived the NGO Single scene transition.");
        Assert.That(restartHost.IsListening && restartHost.IsHost, Is.True,
            "The isolated host remains listening across NGO scene management.");
        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Stage02Path));
        Assert.That(runtimeNetworkBucket == null, Is.True,
            "Restart despawns and destroys the runtime bucket spawned into the old gameplay scene.");

        float playerRespawnDeadline = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < playerRespawnDeadline &&
               restartHost.LocalClient?.PlayerObject == null)
            yield return null;
        NetworkObject restartedPlayerObject = restartHost.LocalClient.PlayerObject != null
            ? restartHost.LocalClient.PlayerObject.GetComponent<NetworkObject>() : null;
        Assert.That(restartedPlayerObject, Is.Not.Null);
        Assert.That(restartedPlayerObject.IsSpawned && restartedPlayerObject.IsPlayerObject, Is.True);
        Assert.That(restartedPlayerObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Component restartedController = restartedPlayerObject.GetComponent(restartType);
        Assert.That(restartedController, Is.Not.Null);
        Assert.That((bool)GetProperty(restartedController, "CanRequestRestart"), Is.True,
            "The persistent owner can request restart again after the first event completes.");

        Component[] resetVolumes = FindSceneComponents(volumeType, Stage02Path)
            .OrderBy(volume => volume.transform.position.x).ToArray();
        Assert.That(resetVolumes, Has.Length.EqualTo(2));
        Component resetVolume = resetVolumes[0];
        Component resetSite = GetField(resetVolume, "constructionSite") as Component;
        Component resetField = GetField(resetVolume, "looseSoilField") as Component;
        object resetHeightField = GetProperty(resetField, "HeightField");
        Assert.That(GetProperty(resetSite, "CurrentStage").ToString(), Is.EqualTo("Clearing"),
            "The first host restart restores the authored obstacle-clearing stage.");
        Assert.That((int)GetProperty(resetSite, "RemainingObstacleCount"), Is.EqualTo(3));
        Assert.That((int)GetProperty(resetSite, "DiggingCycleIndex"), Is.Zero);
        Assert.That(GetProperty(resetSite, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
        Assert.That((float)GetProperty(resetSite, "RemovedSoilUnits"), Is.Zero);
        Assert.That((bool)GetProperty(resetField, "IsSurfaceActive"), Is.False);
        Assert.That((float)GetProperty(resetHeightField, "TotalUnits"), Is.EqualTo(6f).Within(.00001f));
        Assert.That((bool)resetHeightField.GetType().GetMethod("MatchesSnapshot", Instance)
            .Invoke(resetHeightField, new[] { initialShape }), Is.True,
            "The restarted heightfield has the same initial geometry as the authored scene.");
        Collider resetRamp = GetField(resetVolume, "exitRampCollider") as Collider;
        Renderer resetRampRenderer = GetField(resetVolume, "exitRampRenderer") as Renderer;
        Assert.That(resetRamp.enabled, Is.True);
        Assert.That(resetRampRenderer.enabled, Is.True);

        int secondSceneHandle = SceneManager.GetSceneByPath(Stage02Path).handle;
        restartType.GetMethod("RequestRestartLevel", Instance).Invoke(restartedController, null);
        float secondRestartDeadline = Time.realtimeSinceStartup + 30f;
        while (Time.realtimeSinceStartup < secondRestartDeadline &&
               (completedStage02NetworkLoads < 2 || SceneManager.GetSceneByPath(Stage02Path).handle == secondSceneHandle))
            yield return null;
        Assert.That(completedStage02NetworkLoads, Is.EqualTo(2),
            "The same real owner can request a second normal NGO scene restart after completion.");
        Assert.That(lastTimedOutNetworkClients, Is.Empty);
        float secondPlayerRespawnDeadline = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < secondPlayerRespawnDeadline &&
               restartHost.LocalClient?.PlayerObject == null)
            yield return null;
        Assert.That(restartHost.IsListening && restartHost.LocalClient?.PlayerObject != null, Is.True);
        Component[] secondResetVolumes = FindSceneComponents(volumeType, Stage02Path)
            .OrderBy(volume => volume.transform.position.x).ToArray();
        Assert.That(secondResetVolumes, Has.Length.EqualTo(2));
        Component secondResetSite = GetField(secondResetVolumes[0], "constructionSite") as Component;
        Component secondResetField = GetField(secondResetVolumes[0], "looseSoilField") as Component;
        Assert.That(GetProperty(secondResetSite, "CurrentStage").ToString(), Is.EqualTo("Clearing"),
            "A second host restart again restores the authored obstacle-clearing stage.");
        Assert.That((int)GetProperty(secondResetSite, "RemainingObstacleCount"), Is.EqualTo(3));
        Assert.That((int)GetProperty(secondResetSite, "DiggingCycleIndex"), Is.Zero);
        Assert.That((float)GetProperty(GetProperty(secondResetField, "HeightField"), "TotalUnits"),
            Is.EqualTo(6f).Within(.00001f));
#endif
        AssertTrackedPlaymodeRunner("host restart test exit");
    }

    private void RecordStage02NetworkLoadCompletion(string sceneName, LoadSceneMode mode,
        List<ulong> completed, List<ulong> timedOut)
    {
        if (sceneName != "TutorialTerrainStage02" || mode != LoadSceneMode.Single) return;
        playmodeRunnerSurvivedReload = playmodeRunnerRoot != null;
        completedStage02NetworkLoads++;
        lastCompletedNetworkClients.Clear();
        lastCompletedNetworkClients.AddRange(completed);
        lastTimedOutNetworkClients.Clear();
        lastTimedOutNetworkClients.AddRange(timedOut);
    }

    [UnityTearDown]
    public IEnumerator RestoreLoadedScenes()
    {
        NetworkManager ownedHost = restartHost;
        bool hostShutdownCompleted = ownedHost == null;
        if (restartHost != null && restartHost.SceneManager != null)
            restartHost.SceneManager.OnLoadEventCompleted -= RecordStage02NetworkLoadCompletion;
        if (runtimeNetworkBucket != null)
        {
            NetworkObject bucketNetworkObject = runtimeNetworkBucket.GetComponent<NetworkObject>();
            if (bucketNetworkObject != null && bucketNetworkObject.IsSpawned &&
                restartHost != null && restartHost.IsServer)
                bucketNetworkObject.Despawn(true);
            if (runtimeNetworkBucket != null) Object.Destroy(runtimeNetworkBucket);
            runtimeNetworkBucket = null;
        }
        if (restartHost != null && restartHost.IsListening) restartHost.Shutdown(true);
        float hostShutdownDeadline = Time.realtimeSinceStartup + 12f;
        while (Time.realtimeSinceStartup < hostShutdownDeadline && ownedHost != null &&
               (ownedHost.IsListening || ownedHost.ShutdownInProgress))
            yield return null;
        hostShutdownCompleted = ownedHost == null || (!ownedHost.IsListening && !ownedHost.ShutdownInProgress);
        if (restartHostRoot != null) Object.Destroy(restartHostRoot);
        restartHostRoot = null;
        restartHost = null;
        yield return null;
        if (restartControllerRoot != null) Object.Destroy(restartControllerRoot);
        if (pouringProfileClone != null) Object.Destroy(pouringProfileClone);
        foreach (GameObject bucketObject in buckets ?? new GameObject[0])
            if (bucketObject != null) Object.Destroy(bucketObject);

        Scene stage = SceneManager.GetSceneByPath(Stage02Path);
        foreach (string originalPath in originalLoadedScenePaths ?? new string[0])
        {
            Scene original = SceneManager.GetSceneByPath(originalPath);
            if (!original.IsValid() || !original.isLoaded)
            {
                AsyncOperation restore = SceneManager.LoadSceneAsync(originalPath, LoadSceneMode.Additive);
                if (restore != null) while (!restore.isDone) yield return null;
                original = SceneManager.GetSceneByPath(originalPath);
            }
        }
        Scene originalActive = string.IsNullOrEmpty(originalActiveScenePath)
            ? originalActiveScene
            : SceneManager.GetSceneByPath(originalActiveScenePath);
        bool hasRestorableActiveScene = originalActive.IsValid() && originalActive.isLoaded &&
                                        originalActive != SceneManager.GetSceneByPath(Stage02Path);
        if (!hasRestorableActiveScene)
        {
            foreach (string originalPath in originalLoadedScenePaths ?? new string[0])
            {
                Scene candidate = SceneManager.GetSceneByPath(originalPath);
                if (candidate.IsValid() && candidate.isLoaded)
                {
                    originalActive = candidate;
                    hasRestorableActiveScene = true;
                    break;
                }
            }
            if (!hasRestorableActiveScene)
            {
                recoveryScene = SceneManager.CreateScene("LooseSoilStage6RestartRecovery");
                originalActive = recoveryScene;
            }
        }
        if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);

        RestorePlaymodeRunnerToRestoredScene(originalActive);

        stage = SceneManager.GetSceneByPath(Stage02Path);
        if (ownsStageScene && stage.IsValid() && stage.isLoaded && SceneManager.sceneCount > 1)
        {
            AsyncOperation unload = SceneManager.UnloadSceneAsync(stage);
            if (unload != null) while (!unload.isDone) yield return null;
        }

        if (recoveryScene.IsValid() && recoveryScene.isLoaded && SceneManager.sceneCount > 1 &&
            SceneManager.GetActiveScene() != recoveryScene)
        {
            AsyncOperation unloadRecovery = SceneManager.UnloadSceneAsync(recoveryScene);
            if (unloadRecovery != null) while (!unloadRecovery.isDone) yield return null;
        }

        NetworkManager currentSingleton = NetworkManager.Singleton;
        if (priorNetworkSingleton != null) priorNetworkSingleton.SetSingleton();
        else if (currentSingleton != null && currentSingleton != ownedHost) currentSingleton.SetSingleton();
        else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

        priorNetworkSingleton = null;
        completedStage02NetworkLoads = 0;
        lastCompletedNetworkClients.Clear();
        lastTimedOutNetworkClients.Clear();

        yield return null;
        AssertTrackedPlaymodeRunner("fixture teardown exit");
        Assert.That(hostShutdownCompleted, Is.True,
            "The owned NGO host completes shutdown after all fixture cleanup and scene restoration.");
    }

    private static Type Resolve(string name) => Type.GetType(name + ", Assembly-CSharp");

    private static void PrepareControlledDiggingState(Component site, Component volume, Component field)
    {
        Assert.That(GetProperty(site, "CurrentStage").ToString(), Is.EqualTo("Clearing"),
            "The fixture begins from Stage02's actual authored clearing state.");
        Assert.That((bool)GetProperty(site, "RequiresSiteClearing"), Is.True);
        Assert.That((int)GetProperty(site, "RemainingObstacleCount"), Is.EqualTo(3),
            "The fixture confirms all three authored obstacles are active before it clears them.");
        Assert.That((bool)GetProperty(field, "IsSurfaceActive"), Is.False);
        Assert.That((float)GetProperty(GetProperty(field, "HeightField"), "TotalUnits"), Is.EqualTo(6f).Within(.001f));

        Collider rampCollider = GetField(volume, "exitRampCollider") as Collider;
        Renderer rampRenderer = GetField(volume, "exitRampRenderer") as Renderer;
        Assert.That(rampCollider, Is.Not.Null);
        Assert.That(rampRenderer, Is.Not.Null);
        Assert.That(rampCollider.enabled, Is.True, "The authored ramp is available during obstacle clearing.");
        Assert.That(rampRenderer.enabled, Is.True);

        site.GetType().GetMethod("ApplyInitialCompletedState", Instance).Invoke(site, null);
        Type bridgeComponentType = Resolve("BridgeComponent");
        Component bridgeComponent = site.GetComponent(bridgeComponentType);
        Assert.That(bridgeComponent, Is.Not.Null);
        Type networkStateType = Resolve("BridgeComponentNetworkState");
        Type stageType = Resolve("BridgeConstructionStage");
        Type substageType = Resolve("FoundationDiggingSubstage");
        Assert.That(networkStateType, Is.Not.Null);
        Assert.That(stageType, Is.Not.Null);
        Assert.That(substageType, Is.Not.Null);
        int componentId = (int)GetProperty(bridgeComponent, "ComponentID");
        object diggingState = Activator.CreateInstance(networkStateType, new object[] { componentId });
        SetField(diggingState, "constructionStage", (int)Enum.Parse(stageType, "Digging"));
        SetField(diggingState, "constructionValueA", 0);
        SetField(diggingState, "constructionValueB", (int)Enum.Parse(substageType, "Loosening"));
        SetField(diggingState, "constructionAux0", 0f);
        MethodInfo applyNetworkState = bridgeComponentType.GetMethod("ApplyNetworkState", Instance);
        Assert.That(applyNetworkState, Is.Not.Null);
        applyNetworkState.Invoke(bridgeComponent, new[] { diggingState });

        Assert.That(GetProperty(site, "CurrentStage").ToString(), Is.EqualTo("Digging"),
            "The excavation lifecycle begins only after the test explicitly clears obstacles and applies a real Digging state.");
        Assert.That((int)GetProperty(site, "RemainingObstacleCount"), Is.Zero);
        Assert.That((int)GetProperty(site, "DiggingCycleIndex"), Is.Zero);
        Assert.That(GetProperty(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
        Assert.That((float)GetProperty(site, "RemovedSoilUnits"), Is.Zero);
        Assert.That((bool)GetProperty(field, "IsSurfaceActive"), Is.False);
        Assert.That((float)GetProperty(GetProperty(field, "HeightField"), "TotalUnits"), Is.EqualTo(6f).Within(.001f));
        Assert.That(rampCollider.enabled, Is.False, "Ramp assertions continue to hold throughout the controlled Digging stage.");
        Assert.That(rampRenderer.enabled, Is.False);
    }

    private static ushort ReservePort()
    {
        UdpClient listener = new UdpClient(0);
        try
        {
            return (ushort)((System.Net.IPEndPoint)listener.Client.LocalEndPoint).Port;
        }
        finally
        {
            listener.Dispose();
        }
    }

    private static GameObject GetActivePlaymodeRunnerRoot()
    {
        Type controllerType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(
                "UnityEngine.TestTools.TestRunner.PlaymodeTestsController", false))
            .FirstOrDefault(type => type != null);
        object controller = controllerType?.GetProperty("ActiveController",
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        Component component = controller as Component;
        return component != null ? component.transform.root.gameObject : null;
    }

    private void AssertTrackedPlaymodeRunner(string context)
    {
        GameObject activeRoot = GetActivePlaymodeRunnerRoot();
        Type controllerType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(
                "UnityEngine.TestTools.TestRunner.PlaymodeTestsController", false))
            .FirstOrDefault(type => type != null);
        int controllerCount = controllerType == null ? 0 :
            Resources.FindObjectsOfTypeAll(controllerType).OfType<Component>()
                .Count(component => component != null && component.gameObject.scene.IsValid() &&
                                    component.gameObject.scene.isLoaded);
        Assert.That(activeRoot, Is.SameAs(playmodeRunnerRoot),
            $"The active Test Framework controller remains the tracked controller at {context}.");
        Assert.That(controllerCount, Is.EqualTo(1),
            $"Restoring project scenes does not reload the serialized bootstrap controller at {context}.");
    }

    private void RestorePlaymodeRunnerToRestoredScene(Scene fallbackScene)
    {
        if (playmodeRunnerRoot == null || !movedPlaymodeRunnerToDontDestroy) return;
        if (playmodeRunnerOriginalSceneWasBootstrap)
        {
            return;
        }
        Scene destination = playmodeRunnerOriginalScene.IsValid() && playmodeRunnerOriginalScene.isLoaded
            ? playmodeRunnerOriginalScene : fallbackScene;
        if (destination.IsValid() && destination.isLoaded && playmodeRunnerRoot.scene != destination)
            SceneManager.MoveGameObjectToScene(playmodeRunnerRoot, destination);
        playmodeRunnerRoot.hideFlags = playmodeRunnerOriginalFlags;
    }

    private static bool IsTestBootstrapScenePath(string path) =>
        !string.IsNullOrEmpty(path) && path.Replace('\\', '/').StartsWith("Assets/InitTestScene", StringComparison.Ordinal);

    private static Component[] FindSceneComponents(Type type, string scenePath) =>
        Resources.FindObjectsOfTypeAll(type).OfType<Component>()
            .Where(component => component != null && component.gameObject.scene.path == scenePath).ToArray();

    private static object GetField(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    private static object GetProperty(object target, string name) => target.GetType().GetProperty(name, Instance).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);

    private static void PositionForDock(object wheelbarrow, object station)
    {
        Transform targetPose = GetProperty(station, "TargetPose") as Transform;
        Assert.That(targetPose, Is.Not.Null);
        Component bodyComponent = GetProperty(wheelbarrow, "PhysicsBody") as Component;
        Assert.That(bodyComponent, Is.Not.Null);
        Rigidbody body = bodyComponent as Rigidbody;
        Assert.That(body, Is.Not.Null);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        ((Component)wheelbarrow).transform.SetPositionAndRotation(targetPose.position, targetPose.rotation);
        Physics.SyncTransforms();
    }
}
