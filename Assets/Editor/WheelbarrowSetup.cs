using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.AI.Navigation;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class WheelbarrowSetup
{
    private const string ScenePath = "Assets/Scenes/Tutorial_scene.unity";
    private const string Root = "Assets/GeneratedAssets/Wheelbarrow";
    private const string PrefabPath = "Assets/Prefabs/New/Wheelbarrow.prefab";
    private const string PlayerPrefabPath = "Assets/Prefabs/PlayerNew.prefab";
    private const string WheelbarrowProfilePath = Root + "/WheelbarrowProfile.asset";
    private const string PouringProfilePath = Root + "/ConcretePouringProfile.asset";
    private const string WheelContactMaterialPath = Root + "/WheelContact.physicMaterial";
    private const string RopeTowContactMaterialPath = Root + "/RopeTowContact.physicMaterial";
    private const string RopeToolProfilePath = "Assets/ScriptableObjectAssets/New/RopeToolProfile.asset";
    private const float FoundationDockWheelClearance = 0.15f;
    private const float FailedConcreteNavMeshBakeMargin = 0.75f;

    [MenuItem("Tools/RageQuitting/Setup Wheelbarrow And Concrete Pouring")]
    public static void RunFromMenu() => RunSetup();

    public static void RunBatch()
    {
        RunSetup();
        EditorApplication.Exit(0);
    }

    public static string ConfigureWheelbarrowNetworkAuthority()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            NetworkObject networkObject = root.GetComponent<NetworkObject>();
            if (networkObject == null) networkObject = root.AddComponent<NetworkObject>();
            networkObject.DontDestroyWithOwner = true;

            foreach (NetworkTransform networkTransform in root.GetComponents<NetworkTransform>())
            {
                if (networkTransform is WheelbarrowNetworkTransform) continue;
                UnityEngine.Object.DestroyImmediate(networkTransform);
            }

            WheelbarrowNetworkTransform ownerTransform = root.GetComponent<WheelbarrowNetworkTransform>();
            if (ownerTransform == null) ownerTransform = root.AddComponent<WheelbarrowNetworkTransform>();
            ownerTransform.Interpolate = true;
            ownerTransform.PositionInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
            ownerTransform.RotationInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
            ownerTransform.PositionLerpSmoothing = false;
            ownerTransform.RotationLerpSmoothing = false;
            ownerTransform.UseUnreliableDeltas = true;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        WheelbarrowProfileSO profile = AssetDatabase.LoadAssetAtPath<WheelbarrowProfileSO>(WheelbarrowProfilePath);
        if (profile == null) throw new InvalidOperationException($"Missing wheelbarrow profile at {WheelbarrowProfilePath}.");
        SerializedObject serializedProfile = new SerializedObject(profile);
        serializedProfile.FindProperty("motionSnapshotRate").floatValue = 50f;
        serializedProfile.FindProperty("observerPresentationDelay").floatValue = 0.04f;
        serializedProfile.FindProperty("motionMaximumLinearSpeed").floatValue = 14f;
        serializedProfile.FindProperty("motionMaximumAngularSpeedDegrees").floatValue = 240f;
        serializedProfile.FindProperty("motionPositionTolerance").floatValue = 0.2f;
        serializedProfile.FindProperty("motionRotationToleranceDegrees").floatValue = 8f;
        serializedProfile.FindProperty("motionCorrectionPositionThreshold").floatValue = 0.45f;
        serializedProfile.FindProperty("motionCorrectionRotationThresholdDegrees").floatValue = 15f;
        serializedProfile.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return "Wheelbarrow owner authority prefab and 50 Hz motion profile configured.";
    }

    public static string ConfigureWheelbarrowRopeTow()
    {
        EnsureFolder("Assets/GeneratedAssets");
        EnsureFolder(Root);
        WheelbarrowProfileSO wheelbarrowProfile = GetOrCreate<WheelbarrowProfileSO>(WheelbarrowProfilePath);
        PhysicsMaterial towMaterial = CreateRopeTowContactMaterial();
        ConfigureRopeTowProfile(wheelbarrowProfile, towMaterial);

        ConfigureRopeToolTowProfile();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return "Wheelbarrow rope towing material and profiles configured.";
    }

    private static void RunSetup()
    {
        EnsureFolder("Assets/GeneratedAssets");
        EnsureFolder(Root);
        WheelbarrowProfileSO wheelbarrowProfile = GetOrCreate<WheelbarrowProfileSO>(WheelbarrowProfilePath);
        ConcretePouringProfileSO pouringProfile = GetOrCreate<ConcretePouringProfileSO>(PouringProfilePath);
        ConfigurePouringProfile(pouringProfile);
        Material wood = CreateMaterial(Root + "/WheelbarrowWood.mat", new Color(0.38f, 0.19f, 0.08f));
        Material metal = CreateMaterial(Root + "/WheelbarrowMetal.mat", new Color(0.22f, 0.25f, 0.26f));
        Material rubber = CreateMaterial(Root + "/WheelbarrowRubber.mat", new Color(0.035f, 0.04f, 0.04f));
        Material wet = CreateMaterial(Root + "/ConcreteWet.mat", new Color(0.36f, 0.39f, 0.39f));
        Material dry = CreateMaterial(Root + "/ConcreteDry.mat", new Color(0.57f, 0.58f, 0.55f));
        PhysicsMaterial wheelContactMaterial = CreateWheelContactMaterial();
        PhysicsMaterial ropeTowContactMaterial = CreateRopeTowContactMaterial();
        ConfigureRopeTowProfile(wheelbarrowProfile, ropeTowContactMaterial);
        ConfigureRopeToolTowProfile();
        GameObject prefab = CreateWheelbarrowPrefab(wheelbarrowProfile, wood, metal, rubber, wet, wheelContactMaterial);
        RegisterNetworkPrefab(prefab);
        ConfigurePlayerPrefab();
        ConfigureScene(prefab, pouringProfile, wet, dry);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Wheelbarrow setup completed.");
    }

    private static GameObject CreateWheelbarrowPrefab(WheelbarrowProfileSO profile, Material wood, Material metal, Material rubber,
        Material concrete, PhysicsMaterial wheelContactMaterial)
    {
        GameObject root = new GameObject("Wheelbarrow");
        NetworkObject networkObject = root.AddComponent<NetworkObject>();
        networkObject.DontDestroyWithOwner = true;
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = profile.BaseMass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        WheelbarrowNetworkTransform networkTransform = root.AddComponent<WheelbarrowNetworkTransform>();
        networkTransform.PositionInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
        networkTransform.RotationInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
        networkTransform.PositionLerpSmoothing = false;
        networkTransform.RotationLerpSmoothing = false;
        networkTransform.UseUnreliableDeltas = true;
        WheelbarrowController controller = root.AddComponent<WheelbarrowController>();
        NavMeshObstacle navigationObstacle = root.AddComponent<NavMeshObstacle>();
        navigationObstacle.shape = NavMeshObstacleShape.Box;
        navigationObstacle.center = new Vector3(0f, 0.62f, -0.15f);
        navigationObstacle.size = new Vector3(1.45f, 1.2f, 2.3f);
        navigationObstacle.carving = false;
        navigationObstacle.carveOnlyStationary = false;
        navigationObstacle.enabled = false;

        AddBox(root, new Vector3(0f, 0.7f, -0.1475f), new Vector3(1.25f, 0.12f, 0.755f));
        AddBox(root, new Vector3(-0.375f, 0.7f, 0.5275f), new Vector3(0.5f, 0.12f, 0.595f));
        AddBox(root, new Vector3(0.375f, 0.7f, 0.5275f), new Vector3(0.5f, 0.12f, 0.595f));
        AddBox(root, new Vector3(-0.62f, 0.92f, 0.15f), new Vector3(0.1f, 0.48f, 1.45f));
        AddBox(root, new Vector3(0.62f, 0.92f, 0.15f), new Vector3(0.1f, 0.48f, 1.45f));
        AddBox(root, new Vector3(-0.375f, 0.88f, 0.84f), new Vector3(0.5f, 0.42f, 0.1f));
        AddBox(root, new Vector3(0.375f, 0.88f, 0.84f), new Vector3(0.5f, 0.42f, 0.1f));
        AddBox(root, new Vector3(-0.48f, 0.62f, -0.9f), new Vector3(0.12f, 0.12f, 1.55f));
        AddBox(root, new Vector3(0.48f, 0.62f, -0.9f), new Vector3(0.12f, 0.12f, 1.55f));

        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        CreatePrimitive(visual.transform, PrimitiveType.Cube, "TrayFloorRear", new Vector3(0f, 0.7f, -0.1475f), new Vector3(1.25f, 0.12f, 0.755f), Vector3.zero, metal);
        CreatePrimitive(visual.transform, PrimitiveType.Cube, "TrayFloorFrontLeft", new Vector3(-0.375f, 0.7f, 0.5275f), new Vector3(0.5f, 0.12f, 0.595f), Vector3.zero, metal);
        CreatePrimitive(visual.transform, PrimitiveType.Cube, "TrayFloorFrontRight", new Vector3(0.375f, 0.7f, 0.5275f), new Vector3(0.5f, 0.12f, 0.595f), Vector3.zero, metal);
        CreatePrimitive(visual.transform, PrimitiveType.Cube, "TrayLeft", new Vector3(-0.62f, 0.92f, 0.15f), new Vector3(0.1f, 0.48f, 1.45f), new Vector3(0f, 0f, -8f), metal);
        CreatePrimitive(visual.transform, PrimitiveType.Cube, "TrayRight", new Vector3(0.62f, 0.92f, 0.15f), new Vector3(0.1f, 0.48f, 1.45f), new Vector3(0f, 0f, 8f), metal);
        CreatePrimitive(visual.transform, PrimitiveType.Cube, "TrayFrontLeft", new Vector3(-0.375f, 0.88f, 0.84f), new Vector3(0.5f, 0.42f, 0.1f), new Vector3(8f, 0f, 0f), metal);
        CreatePrimitive(visual.transform, PrimitiveType.Cube, "TrayFrontRight", new Vector3(0.375f, 0.88f, 0.84f), new Vector3(0.5f, 0.42f, 0.1f), new Vector3(8f, 0f, 0f), metal);
        CreatePrimitive(visual.transform, PrimitiveType.Cube, "HandleLeft", new Vector3(-0.48f, 0.63f, -1.02f), new Vector3(0.11f, 0.11f, 1.7f), new Vector3(-6f, 0f, 0f), wood);
        CreatePrimitive(visual.transform, PrimitiveType.Cube, "HandleRight", new Vector3(0.48f, 0.63f, -1.02f), new Vector3(0.11f, 0.11f, 1.7f), new Vector3(-6f, 0f, 0f), wood);
        GameObject supportLeft = CreatePrimitive(visual.transform, PrimitiveType.Cube, "SupportLeft", new Vector3(-0.42f, 0.28f, -0.4f), new Vector3(0.1f, 0.55f, 0.1f), new Vector3(0f, 0f, -15f), metal);
        GameObject supportRight = CreatePrimitive(visual.transform, PrimitiveType.Cube, "SupportRight", new Vector3(0.42f, 0.28f, -0.4f), new Vector3(0.1f, 0.55f, 0.1f), new Vector3(0f, 0f, 15f), metal);
        BoxCollider supportLeftCollider = supportLeft.AddComponent<BoxCollider>();
        BoxCollider supportRightCollider = supportRight.AddComponent<BoxCollider>();
        GameObject wheel = CreatePrimitive(visual.transform, PrimitiveType.Cylinder, "Wheel", new Vector3(0f, 0.44f, 0.66f), new Vector3(0.88f, 0.17f, 0.88f), new Vector3(0f, 0f, 90f), rubber);
        CreatePrimitive(wheel.transform, PrimitiveType.Cylinder, "Hub", Vector3.zero, new Vector3(0.5f, 1.15f, 0.5f), Vector3.zero, metal);

        GameObject wheelContact = new GameObject("WheelContact");
        wheelContact.transform.SetParent(root.transform, false);
        wheelContact.transform.localPosition = wheel.transform.localPosition;
        wheelContact.transform.localRotation = wheel.transform.localRotation;
        wheelContact.transform.localScale = wheel.transform.localScale;
        MeshCollider wheelContactCollider = wheelContact.AddComponent<MeshCollider>();
        wheelContactCollider.sharedMesh = wheel.GetComponent<MeshFilter>().sharedMesh;
        wheelContactCollider.convex = true;
        wheelContactCollider.material = wheelContactMaterial;

        WheelCollider drivenWheelCollider = root.AddComponent<WheelCollider>();
        drivenWheelCollider.center = new Vector3(0f,
            0.44f + profile.WheelSuspensionDistance * (1f - profile.WheelSuspensionTargetPosition), 0.66f);
        drivenWheelCollider.radius = profile.WheelRadius;
        drivenWheelCollider.suspensionDistance = profile.WheelSuspensionDistance;
        drivenWheelCollider.mass = 3f;
        drivenWheelCollider.wheelDampingRate = 1.5f;
        drivenWheelCollider.enabled = false;

        Transform driverAnchor = Point(root.transform, "DriverAnchor", new Vector3(0f, 0.05f, -1.75f));
        Transform driverSupportPoint = Point(root.transform, "DriverSupportPoint", new Vector3(0f, 0.72f, -1.75f));
        Transform passengerAnchor = Point(root.transform, "PassengerAnchor", new Vector3(0f, 0.92f, 0.15f));
        Transform cargoRoot = Point(root.transform, "CargoRoot", new Vector3(0f, 0.92f, 0.15f));
        Transform[] slots =
        {
            Point(cargoRoot, "CargoSlot1", new Vector3(-0.35f, 0f, -0.25f)),
            Point(cargoRoot, "CargoSlot2", new Vector3(0.35f, 0f, -0.25f)),
            Point(cargoRoot, "CargoSlot3", new Vector3(0f, 0f, 0.35f))
        };
        GameObject concreteCargo = CreatePrimitive(cargoRoot, PrimitiveType.Cube, "ConcreteCargoVisual", new Vector3(0f, 0.03f, 0f),
            new Vector3(1.05f, 0.18f, 1.05f), Vector3.zero, concrete);
        concreteCargo.SetActive(false);
        GameObject spill = CreatePrimitive(root.transform, PrimitiveType.Cylinder, "ConcreteSpillVisual", new Vector3(0f, 0.04f, 0.8f),
            new Vector3(0.9f, 0.025f, 0.7f), Vector3.zero, concrete);
        spill.SetActive(false);
        Transform leftPour = Point(root.transform, "LeftPourAnchor", new Vector3(-0.48f, 1.02f, -1.75f));
        Transform rightPour = Point(root.transform, "RightPourAnchor", new Vector3(0.48f, 1.02f, -1.75f));
        Transform exitLeft = Point(root.transform, "ExitLeft", new Vector3(-1.35f, 0f, -0.4f));
        Transform exitRight = Point(root.transform, "ExitRight", new Vector3(1.35f, 0f, -0.4f));

        CreateInteraction(root.transform, "HandlesInteraction", new Vector3(0f, 0.95f, -1.55f), new Vector3(1.45f, 1.2f, 0.65f), controller, WheelbarrowInteractionKind.Handles);
        CreateInteraction(root.transform, "CargoInteraction", new Vector3(0f, 1.1f, 0.1f), new Vector3(1.25f, 0.65f, 1f), controller, WheelbarrowInteractionKind.Cargo);
        CreateInteraction(root.transform, "PassengerInteraction", new Vector3(0f, 1.1f, 0.78f), new Vector3(1.1f, 0.6f, 0.25f), controller, WheelbarrowInteractionKind.Passenger);
        BoxCollider rightingInteraction = CreateInteraction(root.transform, "RightingInteraction", new Vector3(0f, 0.8f, -0.15f),
            new Vector3(2.1f, 1.8f, 2.8f), controller, WheelbarrowInteractionKind.Righting);
        rightingInteraction.enabled = false;
        GameObject autoBoard = new GameObject("FrontBoardingTrigger");
        autoBoard.transform.SetParent(root.transform, false);
        float boardingLeadDistance = profile.AutomaticBoardingLeadDistance;
        autoBoard.transform.localPosition = new Vector3(0f, 0.9f, 0.9f + boardingLeadDistance * 0.5f);
        BoxCollider autoCollider = autoBoard.AddComponent<BoxCollider>();
        autoCollider.isTrigger = true;
        autoCollider.size = new Vector3(1.1f, 1.2f, boardingLeadDistance);
        WheelbarrowAutoBoardingTrigger boarding = autoBoard.AddComponent<WheelbarrowAutoBoardingTrigger>();
        SetReference(boarding, "wheelbarrow", controller);

        SerializedObject serialized = new SerializedObject(controller);
        Set(serialized, "profile", profile); Set(serialized, "physicsBody", body); Set(serialized, "wheelContactCollider", wheelContactCollider);
        Set(serialized, "drivenWheelCollider", drivenWheelCollider);
        Set(serialized, "navigationObstacle", navigationObstacle);
        Set(serialized, "wheelVisual", wheel.transform); Set(serialized, "driverAnchor", driverAnchor); Set(serialized, "driverSupportPoint", driverSupportPoint); Set(serialized, "passengerAnchor", passengerAnchor);
        serialized.FindProperty("wheelVisualRadius").floatValue = profile.WheelRadius;
        SetObjectArray(serialized, "restingSupportColliders", new UnityEngine.Object[] { supportLeftCollider, supportRightCollider });
        Set(serialized, "cargoRoot", cargoRoot); SetArray(serialized, "cargoSlots", slots);
        Set(serialized, "concreteCargoVisual", concreteCargo); Set(serialized, "spillVisual", spill);
        Set(serialized, "presentationVisualRoot", visual.transform); Set(serialized, "automaticBoardingTrigger", autoCollider);
        Set(serialized, "leftPourAnchor", leftPour); Set(serialized, "rightPourAnchor", rightPour); SetArray(serialized, "safeExitPoints", new[] { exitLeft, exitRight });
        Set(serialized, "rightingInteractionCollider", rightingInteraction);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    private static void ConfigurePlayerPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            PlayerWheelbarrowController controller = root.GetComponent<PlayerWheelbarrowController>();
            if (controller == null) controller = root.AddComponent<PlayerWheelbarrowController>();
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("inputSendInterval").floatValue = 1f / 30f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (root.GetComponent<PlayerWheelbarrowPouringUI>() == null) root.AddComponent<PlayerWheelbarrowPouringUI>();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void ConfigureScene(GameObject prefab, ConcretePouringProfileSO pouringProfile, Material wet, Material dry)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (GameObject old in scene.GetRootGameObjects().Where(item => item.name.StartsWith("WheelbarrowV1_", StringComparison.Ordinal)).ToArray())
            UnityEngine.Object.DestroyImmediate(old);
        GameObject setupRoot = new GameObject("WheelbarrowV1_Setup");
        SceneManager.MoveGameObjectToScene(setupRoot, scene);

        ConcreteMixerController mixer = FindInScene<ConcreteMixerController>(scene);
        Vector3 spawn = mixer != null ? mixer.transform.position + mixer.transform.forward * 3.2f : new Vector3(-8f, 1f, 0f);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "WheelbarrowV1_Wheelbarrow";
        instance.transform.SetPositionAndRotation(Grounded(spawn), mixer != null ? mixer.transform.rotation : Quaternion.identity);

        if (mixer != null)
        {
            Vector3 target = mixer.transform.TransformPoint(new Vector3(2.45f, 0.02f, 0f));
            WheelbarrowDockingStation mixerDock = CreateDock(setupRoot.transform, "WheelbarrowV1_MixerDock", WheelbarrowDockType.MixerLoading,
                target, mixer.transform.rotation * Quaternion.Euler(0f, -90f, 0f), null, pouringProfile);
            SetReference(mixer, "concreteOutputStation", mixerDock);
        }

        BridgeConstructionSite[] sites = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<BridgeConstructionSite>(true))
            .Where(item => item.name.Contains("WoodenFoundation", StringComparison.OrdinalIgnoreCase)).ToArray();
        FoundationExcavationVolume[] volumes = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<FoundationExcavationVolume>(true)).ToArray();
        int index = 0;
        foreach (FoundationExcavationVolume volume in volumes)
        {
            BridgeConstructionSite site = sites.OrderBy(item => (item.transform.position - volume.transform.position).sqrMagnitude).FirstOrDefault();
            if (site == null) continue;
            ConfigureWorkflow(site);
            ConfigureConcreteVisual(volume, site, pouringProfile, wet, dry);

            if (!TryResolveFoundationDockPose(volume, instance, out Vector3 dockPosition, out Quaternion dockRotation))
            {
                Debug.LogError($"Cannot create the wheelbarrow dock for {site.name}: {volume.name} has no usable ExitRamp geometry.", volume);
                continue;
            }

            CreateDock(setupRoot.transform, "WheelbarrowV1_FoundationDock_" + (++index), WheelbarrowDockType.FoundationPouring,
                dockPosition, dockRotation, site, pouringProfile);
        }
        RebuildSceneNavMesh(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static WheelbarrowDockingStation CreateDock(Transform parent, string name, WheelbarrowDockType type,
        Vector3 position, Quaternion rotation, BridgeConstructionSite site, ConcretePouringProfileSO pouringProfile)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, true);
        root.transform.SetPositionAndRotation(position, rotation);
        root.AddComponent<NetworkObject>();
        BoxCollider capture = root.AddComponent<BoxCollider>(); capture.isTrigger = true; capture.center = Vector3.up * 0.8f; capture.size = new Vector3(3.5f, 2f, 4.5f);
        WheelbarrowDockingStation station = root.AddComponent<WheelbarrowDockingStation>();
        root.AddComponent<WheelbarrowDockingVisualizer>();
        Transform target = Point(root.transform, "TargetPose", Vector3.zero);
        target.localRotation = Quaternion.identity;
        SerializedObject stationSerialized = new SerializedObject(station);
        stationSerialized.FindProperty("dockType").enumValueIndex = (int)type;
        Set(stationSerialized, "targetPose", target); Set(stationSerialized, "foundationSite", site);

        if (type == WheelbarrowDockType.FoundationPouring)
        {
            WheelbarrowPouringMinigame minigame = root.AddComponent<WheelbarrowPouringMinigame>();
            Transform leftAnchor = Point(root.transform, "LeftPourPlayerAnchor", new Vector3(-0.7f, 0.05f, -2.65f));
            Transform rightAnchor = Point(root.transform, "RightPourPlayerAnchor", new Vector3(0.7f, 0.05f, -2.65f));
            Transform soloAnchor = Point(root.transform, "SoloPourPlayerAnchor", new Vector3(0f, 0.05f, -2.65f));
            WheelbarrowPourGripInteraction leftGrip = CreatePourGrip(
                root.transform, "LeftPourGrip", new Vector3(-0.55f, 1.05f, -1.55f), minigame, true);
            WheelbarrowPourGripInteraction rightGrip = CreatePourGrip(
                root.transform, "RightPourGrip", new Vector3(0.55f, 1.05f, -1.55f), minigame, false);
            SerializedObject minigameSerialized = new SerializedObject(minigame);
            Set(minigameSerialized, "profile", pouringProfile);
            Set(minigameSerialized, "leftPlayerAnchor", leftAnchor);
            Set(minigameSerialized, "rightPlayerAnchor", rightAnchor);
            Set(minigameSerialized, "soloPlayerAnchor", soloAnchor);
            Set(minigameSerialized, "leftGrip", leftGrip);
            Set(minigameSerialized, "rightGrip", rightGrip);
            minigameSerialized.ApplyModifiedPropertiesWithoutUndo();
            Set(stationSerialized, "pouringMinigame", minigame);
        }
        stationSerialized.ApplyModifiedPropertiesWithoutUndo();
        return station;
    }

    private static WheelbarrowPourGripInteraction CreatePourGrip(
        Transform parent,
        string name,
        Vector3 position,
        WheelbarrowPouringMinigame minigame,
        bool left)
    {
        GameObject grip = new GameObject(name);
        grip.transform.SetParent(parent, false); grip.transform.localPosition = position;
        BoxCollider collider = grip.AddComponent<BoxCollider>(); collider.isTrigger = true; collider.size = new Vector3(0.45f, 0.8f, 0.45f);
        WheelbarrowPourGripInteraction interaction = grip.AddComponent<WheelbarrowPourGripInteraction>();
        grip.AddComponent<WheelbarrowPourStationVisualizer>();
        SerializedObject serialized = new SerializedObject(interaction);
        Set(serialized, "minigame", minigame); serialized.FindProperty("leftSide").boolValue = left; serialized.ApplyModifiedPropertiesWithoutUndo();
        return interaction;
    }

    private static void ConfigureWorkflow(BridgeConstructionSite site)
    {
        SerializedObject serialized = new SerializedObject(site);
        BridgeComponent component = serialized.FindProperty("bridgeComponent")?.objectReferenceValue as BridgeComponent;
        BridgeConstructionWorkflowSO workflow = component != null && component.GetBridgeComponentSO() != null
            ? component.GetBridgeComponentSO().constructionWorkflow : null;
        if (workflow == null) return;
        SerializedObject workflowSerialized = new SerializedObject(workflow);
        workflowSerialized.FindProperty("requiredConcreteLoads").intValue = 1;
        workflowSerialized.FindProperty("concreteDryingDuration").floatValue = 30f;
        workflowSerialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(workflow);
    }

    private static void ConfigurePouringProfile(ConcretePouringProfileSO profile)
    {
        SerializedObject serialized = new SerializedObject(profile);
        serialized.FindProperty("criticalFailureSequenceDuration").floatValue = 0.8f;
        serialized.FindProperty("failedConcreteWorkRequired").floatValue = 100f;
        serialized.FindProperty("failedConcreteCollapseDuration").floatValue = 0.4f;
        serialized.FindProperty("failedConcreteCrackThresholds").vector3Value = new Vector3(1f, 34f, 67f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
    }

    private static void ConfigureConcreteVisual(
        FoundationExcavationVolume volume,
        BridgeConstructionSite site,
        ConcretePouringProfileSO pouringProfile,
        Material wet,
        Material dry)
    {
        if (volume == null) return;
        SerializedObject serialized = new SerializedObject(volume);
        Transform soil = serialized.FindProperty("soilSurface")?.objectReferenceValue as Transform;
        Transform excavationRoot = volume.transform.parent != null ? volume.transform.parent : volume.transform;
        Transform pitBottom = excavationRoot.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == "PitBottom");
        Transform exitRamp = excavationRoot.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == "ExitRamp");

        float bottomLocalY = -1.2f;
        if (pitBottom != null && TryGetWorldBounds(pitBottom.gameObject, out Bounds bottomBounds))
        {
            bottomLocalY = excavationRoot.InverseTransformPoint(
                new Vector3(bottomBounds.center.x, bottomBounds.max.y, bottomBounds.center.z)).y;
        }

        float fullTopLocalY = 0.08f;
        Renderer soilRenderer = soil != null ? soil.GetComponent<Renderer>() : null;
        if (soilRenderer != null)
        {
            Bounds soilBounds = soilRenderer.bounds;
            fullTopLocalY = excavationRoot.InverseTransformPoint(
                new Vector3(soilBounds.center.x, soilBounds.max.y, soilBounds.center.z)).y;
        }

        Vector2 footprint = soil != null
            ? new Vector2(Mathf.Max(0.2f, soil.localScale.x), Mathf.Max(0.2f, soil.localScale.z))
            : new Vector2(6.5f, 4f);
        float height = Mathf.Max(0.001f, fullTopLocalY - bottomLocalY);
        Transform existingFill = excavationRoot.Find("ConcreteFill");
        GameObject fill = existingFill != null
            ? existingFill.gameObject
            : CreatePrimitive(excavationRoot, PrimitiveType.Cube, "ConcreteFill", Vector3.zero, Vector3.one, Vector3.zero, wet);
        fill.transform.SetParent(excavationRoot, false);
        fill.transform.localPosition = new Vector3(
            soil != null ? soil.localPosition.x : 0f,
            bottomLocalY + height * 0.5f,
            soil != null ? soil.localPosition.z : 0f);
        fill.transform.localRotation = Quaternion.identity;
        fill.transform.localScale = new Vector3(footprint.x, height, footprint.y);
        fill.GetComponent<Renderer>().sharedMaterial = wet;

        BoxCollider collider = fill.GetComponent<BoxCollider>();
        if (collider == null) collider = fill.AddComponent<BoxCollider>();
        collider.enabled = false;
        fill.SetActive(false);
        ConfigureCriticalFailureObjects(
            volume,
            site,
            excavationRoot,
            fill,
            footprint,
            bottomLocalY,
            fullTopLocalY,
            dry,
            out FoundationFailedConcreteTarget failedTarget,
            out GameObject[] crackVisuals,
            out Transform failedPose,
            out BoxCollider recoveryVolume,
            out NavMeshObstacle pitObstacle,
            out GameObject bakeProxy);
        Set(serialized, "concreteFillVisual", fill.transform); Set(serialized, "concreteRenderer", fill.GetComponent<Renderer>());
        Set(serialized, "driedConcreteCollider", collider); Set(serialized, "wetConcreteMaterial", wet); Set(serialized, "dryConcreteMaterial", dry);
        serialized.FindProperty("concreteFootprintSize").vector2Value = footprint;
        serialized.FindProperty("concreteBottomLocalY").floatValue = bottomLocalY;
        serialized.FindProperty("concreteFullTopLocalY").floatValue = fullTopLocalY;
        Set(serialized, "exitRampRenderer", exitRamp != null ? exitRamp.GetComponent<Renderer>() : null);
        Set(serialized, "exitRampCollider", exitRamp != null ? exitRamp.GetComponent<Collider>() : null);
        Set(serialized, "failedConcreteTarget", failedTarget);
        SetObjectArray(serialized, "failedConcreteCrackVisuals", crackVisuals);
        Set(serialized, "failedWheelbarrowPose", failedPose);
        Set(serialized, "recoveryVolume", recoveryVolume);
        Set(serialized, "pitNavMeshObstacle", pitObstacle);
        Set(serialized, "navMeshBakeProxy", bakeProxy);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject siteSerialized = new SerializedObject(site);
        Set(siteSerialized, "concretePouringProfile", pouringProfile);
        siteSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureCriticalFailureObjects(
        FoundationExcavationVolume volume,
        BridgeConstructionSite site,
        Transform excavationRoot,
        GameObject concreteFill,
        Vector2 footprint,
        float bottomLocalY,
        float fullTopLocalY,
        Material dry,
        out FoundationFailedConcreteTarget failedTarget,
        out GameObject[] crackVisuals,
        out Transform failedPose,
        out BoxCollider recoveryVolume,
        out NavMeshObstacle pitObstacle,
        out GameObject bakeProxy)
    {
        Transform old = excavationRoot.Find("CriticalConcreteFailure");
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        GameObject root = new GameObject("CriticalConcreteFailure");
        root.transform.SetParent(excavationRoot, false);

        int notWalkableArea = NavMesh.GetAreaFromName("Not Walkable");
        foreach (NavMeshModifierVolume legacyPitExclusion in excavationRoot.GetComponents<NavMeshModifierVolume>())
        {
            if (legacyPitExclusion.area == notWalkableArea)
                legacyPitExclusion.enabled = false;
        }

        GameObject target = new GameObject("FailedConcreteTarget");
        target.transform.SetParent(root.transform, false);
        target.transform.localPosition = new Vector3(concreteFill.transform.localPosition.x, fullTopLocalY + 0.08f,
            concreteFill.transform.localPosition.z);
        BoxCollider targetCollider = target.AddComponent<BoxCollider>();
        targetCollider.isTrigger = true;
        targetCollider.size = new Vector3(footprint.x, 0.16f, footprint.y);
        failedTarget = target.AddComponent<FoundationFailedConcreteTarget>();
        failedTarget.ConfigureEditor(site, targetCollider);
        targetCollider.enabled = false;
        failedTarget.enabled = false;

        Material crackMaterial = CreateMaterial(Root + "/FailedConcreteCrack.mat", new Color(0.12f, 0.13f, 0.13f));
        crackVisuals = new GameObject[3];
        for (int stage = 0; stage < crackVisuals.Length; stage++)
        {
            GameObject crackRoot = new GameObject($"FailedConcreteCracks_{stage + 1}");
            crackRoot.transform.SetParent(root.transform, false);
            crackRoot.transform.localPosition = new Vector3(concreteFill.transform.localPosition.x,
                fullTopLocalY + 0.012f + stage * 0.002f, concreteFill.transform.localPosition.z);
            CreateCrackSegments(crackRoot.transform, stage + 1, footprint, crackMaterial);
            crackRoot.SetActive(false);
            crackVisuals[stage] = crackRoot;
        }

        failedPose = Point(root.transform, "FailedWheelbarrowPose",
            new Vector3(concreteFill.transform.localPosition.x, fullTopLocalY - 0.48f, concreteFill.transform.localPosition.z));
        Vector3 rampDirection = ResolveRampDirection(excavationRoot, concreteFill.transform.position);
        failedPose.rotation = Quaternion.LookRotation(-rampDirection, Vector3.up) * Quaternion.Euler(-8f, 0f, 18f);

        GameObject recovery = new GameObject("WheelbarrowRecoveryVolume");
        recovery.transform.SetParent(root.transform, false);
        recovery.transform.localPosition = new Vector3(concreteFill.transform.localPosition.x,
            (bottomLocalY + fullTopLocalY) * 0.5f, concreteFill.transform.localPosition.z);
        recoveryVolume = recovery.AddComponent<BoxCollider>();
        recoveryVolume.isTrigger = true;
        recoveryVolume.size = new Vector3(footprint.x + 0.4f, Mathf.Max(3f, fullTopLocalY - bottomLocalY + 1.5f), footprint.y + 0.4f);

        GameObject obstacleObject = new GameObject("PitNavMeshObstacle");
        obstacleObject.transform.SetParent(root.transform, false);
        obstacleObject.transform.localPosition = new Vector3(concreteFill.transform.localPosition.x,
            fullTopLocalY, concreteFill.transform.localPosition.z);
        pitObstacle = obstacleObject.AddComponent<NavMeshObstacle>();
        pitObstacle.shape = NavMeshObstacleShape.Box;
        pitObstacle.center = Vector3.zero;
        pitObstacle.size = new Vector3(footprint.x, 1.2f, footprint.y);
        pitObstacle.carving = true;
        pitObstacle.carveOnlyStationary = false;

        bakeProxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bakeProxy.name = "FailedConcreteNavMeshBakeProxy";
        bakeProxy.transform.SetParent(root.transform, false);
        bakeProxy.transform.localPosition = new Vector3(concreteFill.transform.localPosition.x,
            fullTopLocalY - 0.04f, concreteFill.transform.localPosition.z);
        bakeProxy.transform.localScale = new Vector3(
            footprint.x + FailedConcreteNavMeshBakeMargin * 2f,
            0.08f,
            footprint.y + FailedConcreteNavMeshBakeMargin * 2f);
        NavMeshModifier modifier = bakeProxy.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = 0;
        NavMeshSurface bakeSurface = bakeProxy.AddComponent<NavMeshSurface>();
        bakeSurface.agentTypeID = 0;
        bakeSurface.collectObjects = CollectObjects.Children;
        bakeSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        bakeSurface.layerMask = 1 << bakeProxy.layer;
        bakeSurface.defaultArea = 0;
    }

    private static void CreateCrackSegments(Transform parent, int stage, Vector2 footprint, Material material)
    {
        int segments = stage * 4;
        for (int i = 0; i < segments; i++)
        {
            float t = (i + 1f) / (segments + 1f);
            float angle = (i * 47f + stage * 23f) * Mathf.Deg2Rad;
            Vector3 position = new Vector3(
                Mathf.Lerp(-footprint.x * 0.38f, footprint.x * 0.38f, t),
                0f,
                Mathf.Sin(i * 1.7f) * footprint.y * 0.28f);
            CreatePrimitive(parent, PrimitiveType.Cube, $"Crack_{i + 1}", position,
                new Vector3(0.55f + stage * 0.12f, 0.012f, 0.035f),
                new Vector3(0f, angle * Mathf.Rad2Deg, 0f), material);
        }
    }

    private static Vector3 ResolveRampDirection(Transform excavationRoot, Vector3 center)
    {
        Transform ramp = excavationRoot.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == "ExitRamp");
        if (ramp != null)
        {
            Vector3 direction = Vector3.ProjectOnPlane(ramp.position - center, Vector3.up);
            if (direction.sqrMagnitude > 0.001f) return direction.normalized;
        }
        return excavationRoot.forward;
    }

    private static void RebuildSceneNavMesh(Scene scene)
    {
        FoundationExcavationVolume[] volumes = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<FoundationExcavationVolume>(true))
            .ToArray();
        GameObject[] proxies = volumes.Select(volume =>
            {
                SerializedObject serialized = new SerializedObject(volume);
                return serialized.FindProperty("navMeshBakeProxy")?.objectReferenceValue as GameObject;
            })
            .Where(proxy => proxy != null)
            .ToArray();
        NavMeshObstacle[] obstacles = volumes
            .Select(volume => volume.PitNavMeshObstacle)
            .Where(obstacle => obstacle != null)
            .ToArray();
        bool[] obstacleEnabled = obstacles.Select(obstacle => obstacle.enabled).ToArray();
        bool[] obstacleCarving = obstacles.Select(obstacle => obstacle.carving).ToArray();

        NavMeshSurface[] surfaces = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<NavMeshSurface>(true)).ToArray();
        if (surfaces.Length == 0)
        {
            Debug.LogWarning("Wheelbarrow setup did not find a NavMeshSurface in Tutorial_scene; bake proxy was created but NavMesh was not rebuilt.");
            return;
        }

        try
        {
            foreach (NavMeshObstacle obstacle in obstacles)
                obstacle.enabled = false;
            foreach (GameObject proxy in proxies)
            {
                Renderer renderer = proxy.GetComponent<Renderer>();
                Collider collider = proxy.GetComponent<Collider>();
                if (renderer != null) renderer.enabled = true;
                if (collider != null) collider.enabled = true;
            }
            foreach (NavMeshSurface surface in surfaces)
                surface.BuildNavMesh();
        }
        finally
        {
            DisableBakeProxies(proxies);
            for (int i = 0; i < obstacles.Length; i++)
            {
                obstacles[i].carving = obstacleCarving[i];
                obstacles[i].enabled = obstacleEnabled[i];
            }
        }
    }

    private static void DisableBakeProxies(IEnumerable<GameObject> proxies)
    {
        foreach (GameObject proxy in proxies)
        {
            Renderer renderer = proxy.GetComponent<Renderer>();
            Collider collider = proxy.GetComponent<Collider>();
            if (renderer != null) renderer.enabled = false;
            if (collider != null) collider.enabled = false;
        }
    }

    private static bool TryResolveFoundationDockPose(FoundationExcavationVolume volume, GameObject wheelbarrow,
        out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (volume == null || wheelbarrow == null) return false;

        Transform excavationRoot = volume.transform.parent != null ? volume.transform.parent : volume.transform;
        Transform ramp = excavationRoot.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == "ExitRamp");
        if (ramp == null || !TryGetWorldBounds(ramp.gameObject, out Bounds rampBounds)) return false;

        SerializedObject volumeSerialized = new SerializedObject(volume);
        Transform soilSurface = volumeSerialized.FindProperty("soilSurface")?.objectReferenceValue as Transform;
        Vector3 excavationCenter = soilSurface != null ? soilSurface.position : volume.transform.position;
        Vector3 outward = Vector3.ProjectOnPlane(rampBounds.center - excavationCenter, Vector3.up);
        if (outward.sqrMagnitude < 0.0001f) return false;
        outward.Normalize();

        WheelCollider wheel = wheelbarrow.GetComponent<WheelCollider>();
        float wheelForwardOffset = wheel != null
            ? Mathf.Max(0f, Vector3.Dot(wheel.center, Vector3.forward))
            : 0.66f;
        float rampOutwardExtent = Mathf.Abs(outward.x) * rampBounds.extents.x +
                                  Mathf.Abs(outward.z) * rampBounds.extents.z;
        Vector3 rampOuterEdge = rampBounds.center + outward * rampOutwardExtent;
        position = Grounded(rampOuterEdge + outward * (wheelForwardOffset + FoundationDockWheelClearance));
        rotation = Quaternion.LookRotation(-outward, Vector3.up);
        return true;
    }

    private static bool TryGetWorldBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null) continue;
            if (!hasBounds) bounds = collider.bounds;
            else bounds.Encapsulate(collider.bounds);
            hasBounds = true;
        }
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            if (!hasBounds) bounds = renderer.bounds;
            else bounds.Encapsulate(renderer.bounds);
            hasBounds = true;
        }
        return hasBounds;
    }

    private static BoxCollider CreateInteraction(Transform parent, string name, Vector3 position, Vector3 size, WheelbarrowController controller, WheelbarrowInteractionKind kind)
    {
        GameObject item = new GameObject(name); item.transform.SetParent(parent, false); item.transform.localPosition = position;
        BoxCollider collider = item.AddComponent<BoxCollider>(); collider.isTrigger = true; collider.size = size;
        WheelbarrowInteractionPoint point = item.AddComponent<WheelbarrowInteractionPoint>();
        SerializedObject serialized = new SerializedObject(point); Set(serialized, "wheelbarrow", controller); serialized.FindProperty("interactionKind").enumValueIndex = (int)kind; serialized.ApplyModifiedPropertiesWithoutUndo();
        return collider;
    }

    private static GameObject CreatePrimitive(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Vector3 euler, Material material)
    {
        GameObject result = GameObject.CreatePrimitive(type); result.name = name; result.transform.SetParent(parent, false);
        result.transform.localPosition = position; result.transform.localScale = scale; result.transform.localEulerAngles = euler;
        result.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(result.GetComponent<Collider>());
        return result;
    }

    private static Transform Point(Transform parent, string name, Vector3 position)
    {
        GameObject point = new GameObject(name); point.transform.SetParent(parent, false); point.transform.localPosition = position; return point.transform;
    }

    private static void AddBox(GameObject root, Vector3 center, Vector3 size) { BoxCollider box = root.AddComponent<BoxCollider>(); box.center = center; box.size = size; }
    private static void Set(SerializedObject serialized, string name, UnityEngine.Object value) { SerializedProperty property = serialized.FindProperty(name); if (property != null) property.objectReferenceValue = value; }
    private static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value) { SerializedObject serialized = new SerializedObject(target); Set(serialized, name, value); serialized.ApplyModifiedPropertiesWithoutUndo(); }
    private static void SetArray(SerializedObject serialized, string name, IReadOnlyList<Transform> values)
    {
        SerializedProperty property = serialized.FindProperty(name); property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void SetObjectArray(SerializedObject serialized, string name, IReadOnlyList<UnityEngine.Object> values)
    {
        SerializedProperty property = serialized.FindProperty(name); property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static T GetOrCreate<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
    }

    private static Material CreateMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.color = color; EditorUtility.SetDirty(material); return material;
    }

    private static PhysicsMaterial CreateWheelContactMaterial()
    {
        PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(WheelContactMaterialPath);
        if (material == null)
        {
            material = new PhysicsMaterial("WheelContact");
            AssetDatabase.CreateAsset(material, WheelContactMaterialPath);
        }
        material.staticFriction = 0.55f;
        material.dynamicFriction = 0.45f;
        material.bounciness = 0f;
        material.frictionCombine = PhysicsMaterialCombine.Average;
        material.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static PhysicsMaterial CreateRopeTowContactMaterial()
    {
        PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(RopeTowContactMaterialPath);
        if (material == null)
        {
            material = new PhysicsMaterial("RopeTowContact");
            AssetDatabase.CreateAsset(material, RopeTowContactMaterialPath);
        }
        material.staticFriction = 0.05f;
        material.dynamicFriction = 0.03f;
        material.bounciness = 0f;
        material.frictionCombine = PhysicsMaterialCombine.Minimum;
        material.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigureRopeTowProfile(WheelbarrowProfileSO wheelbarrowProfile,
        PhysicsMaterial towMaterial)
    {
        SerializedObject serialized = new SerializedObject(wheelbarrowProfile);
        serialized.FindProperty("ropeTowContactMaterial").objectReferenceValue = towMaterial;
        serialized.FindProperty("ropeTowActivationTension").floatValue = 0.04f;
        serialized.FindProperty("ropeTowReleaseDelay").floatValue = 0.2f;
        serialized.FindProperty("maximumRopeTowVerticalRatio").floatValue = 0.3f;
        serialized.FindProperty("ropeTowGroundProbeDistance").floatValue = 1.5f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(wheelbarrowProfile);
    }

    private static void ConfigureRopeToolTowProfile()
    {
        RopeToolProfileSO ropeProfile = AssetDatabase.LoadAssetAtPath<RopeToolProfileSO>(RopeToolProfilePath);
        if (ropeProfile == null) throw new InvalidOperationException($"Missing rope profile at {RopeToolProfilePath}.");
        ropeProfile.maximumWheelbarrowPullSpeed = 2.5f;
        EditorUtility.SetDirty(ropeProfile);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }

    private static void RegisterNetworkPrefab(GameObject prefab)
    {
        foreach (string path in new[] { "Assets/DefaultNetworkPrefabs.asset", "Assets/NGO_Minimal_Setup/NetworkPrefabsList.asset" })
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path); if (asset == null) continue;
            SerializedObject serialized = new SerializedObject(asset); SerializedProperty list = serialized.FindProperty("List");
            bool exists = Enumerable.Range(0, list.arraySize).Any(i => list.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == prefab);
            if (!exists)
            {
                int index = list.arraySize; list.InsertArrayElementAtIndex(index); SerializedProperty entry = list.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("Override").enumValueIndex = 0; entry.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
                entry.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null; entry.FindPropertyRelative("SourceHashToOverride").ulongValue = 0;
                entry.FindPropertyRelative("OverridingTargetPrefab").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset);
            }
        }
    }

    private static T FindInScene<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).FirstOrDefault();
    private static Vector3 Grounded(Vector3 position)
    {
        foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up * 20f, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore).OrderBy(item => item.distance))
            if (hit.collider.GetComponentInParent<WaterBody>() == null && hit.collider.attachedRigidbody == null)
                return hit.point + Vector3.up * 0.05f;
        return position;
    }
}
