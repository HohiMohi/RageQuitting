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

[PrebuildSetup(typeof(ExcavationTestSceneScope))]
[PostBuildCleanup(typeof(ExcavationTestSceneScope))]
public sealed class FoundationSoilShovelImpactNetworkPlayModeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const string Stage02Path = "Assets/Tests/Fixtures/TutorialTerrainStage02.unity";
    private readonly List<GameObject> owned = new();
    private readonly List<NetworkManager> managers = new();
    private Scene stageScene;
    private AsyncOperation load;
    private NetworkManager host;
    private NetworkManager owner;
    private Component gameplay;
    private Component priorGameplay;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
#if UNITY_EDITOR
        Assert.That(SceneUtility.GetBuildIndexByScenePath(Stage02Path), Is.GreaterThanOrEqualTo(0));
        Type gameplayType = TypeByName("GameplayManager");
        FieldInfo singleton = gameplayType.GetField("<Instance>k__BackingField", Static);
        priorGameplay = singleton.GetValue(null) as Component;
        load = SceneManager.LoadSceneAsync(Stage02Path, LoadSceneMode.Additive);
        while (!load.isDone) yield return null;
        stageScene = SceneManager.GetSceneByPath(Stage02Path);
        Assert.That(stageScene.IsValid() && stageScene.isLoaded, Is.True);
        // The fixture deliberately contains authored NetworkObjects for other tests. This
        // loopback test uses only the player prefab, so keep those scene objects local-only.
        foreach (NetworkObject sceneNetworkObject in Resources.FindObjectsOfTypeAll<NetworkObject>())
            if (sceneNetworkObject != null && sceneNetworkObject.gameObject.scene == stageScene)
                sceneNetworkObject.SetSceneObjectStatus(false);
#else
        Assert.Ignore("Uses the authored Stage02 network fixture.");
        yield break;
#endif
    }

    [UnityTest]
    public IEnumerator HostAndRemoteOwnerUseRayValidatedSoilRpcAndLegacyRequestCannotAdvancePit()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Requires an in-process UTP host and remote client.");
        yield break;
#else
        Type volumeType = TypeByName("FoundationExcavationVolume");
        Type siteType = TypeByName("BridgeConstructionSite");
        Type bridgeComponentType = TypeByName("BridgeComponent");
        Component[] volumes = ComponentsInScene(volumeType);
        Assert.That(volumes, Has.Length.EqualTo(2));
        Component volume = volumes[0];
        Component site = Get(volume, "ConstructionSite") as Component;
        Component bridgeComponent = site.GetComponent(bridgeComponentType);
        Component otherSite = Get(volumes[1], "ConstructionSite") as Component;
        Component otherBridgeComponent = otherSite.GetComponent(bridgeComponentType);
        ScriptableObject shovel = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/Shovel.asset");
        Assert.That(shovel, Is.Not.Null);
        SetDigging(site, volume, "Loosening");
        SetDigging(otherSite, volumes[1], "Loosening");
        Physics.SyncTransforms();

        GameObject bridgeRoot = bridgeComponent.GetComponentInParent(TypeByName("Bridge")).gameObject;
        ConfigureGameplay(TypeByName("GameplayManager"), bridgeRoot, bridgeComponentType);
        GameObject playerPrefab = CreatePlayerPrefab(shovel);
        ushort port = ReservePort();
        host = CreateManager("Foundation soil RPC host", port, playerPrefab);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        SetGameplaySingleton(gameplay);
        Call(gameplay, "EnsureNetworkMessagingRegistered");
        Call(gameplay, "InitializeServerBridgeState");
        yield return Until(() => host.LocalClient?.PlayerObject != null, "host player");

        owner = CreateManager("Foundation soil RPC owner", port, playerPrefab);
        Assert.That(owner.StartClient(), Is.True);
        owner.SetSingleton();
        yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null &&
            host.ConnectedClientsIds.Count == 2, "remote owner connection");

        NetworkObject hostPlayer = host.LocalClient.PlayerObject;
        NetworkObject remotePlayerOnServer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        NetworkObject remoteOwnerPlayer = owner.LocalClient.PlayerObject;
        Component hostInventory = hostPlayer.GetComponent(TypeByName("PlayerInventory"));
        Component remoteInventory = remotePlayerOnServer.GetComponent(TypeByName("PlayerInventory"));
        Call(hostInventory, "EnsureInventoryStorage");
        Call(remoteInventory, "EnsureInventoryStorage");
        Assert.That((bool)hostInventory.GetType().GetMethod("AddItem").Invoke(hostInventory, new object[] { shovel }), Is.True);
        Assert.That((bool)remoteInventory.GetType().GetMethod("AddItem").Invoke(remoteInventory, new object[] { shovel }), Is.True,
            "The server equips the remote owner through the authoritative inventory API.");
        owner.SetSingleton();
        yield return Until(() => (int)Get(remoteOwnerPlayer.GetComponent(TypeByName("PlayerInventory")), "_currentInventoryOccupiedSlots") > 0,
            "remote shovel inventory replication");

        Collider support = (Collider)Get(volume, "SoilSupportCollider");
        Vector3 top = SupportTop(support);
        Vector3 rayOrigin = top + Vector3.up;
        host.SetSingleton();
        TeleportPlayer(hostPlayer, rayOrigin);
        owner.SetSingleton();
        TeleportPlayer(remoteOwnerPlayer, rayOrigin);
        ConfigurePlayerAim(hostPlayer, rayOrigin, top);
        ConfigurePlayerAim(remoteOwnerPlayer, rayOrigin, top);
        yield return Until(() => Vector3.Distance(remotePlayerOnServer.transform.position, rayOrigin) < 0.1f,
            "server-known owner-authoritative player anchor");
        host.SetSingleton();
        SetGameplaySingleton(gameplay);
        ConfigurePlayerAim(remotePlayerOnServer, rayOrigin, top);
        foreach (NetworkManager manager in managers)
            foreach (NetworkObject playerCopy in manager.SpawnManager.SpawnedObjectsList)
                if (playerCopy != null && playerCopy.IsPlayerObject)
                    foreach (Collider collider in playerCopy.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        Physics.SyncTransforms();
        yield return null;

        // The host owner takes the same production impact path and is validated by the server.
        host.SetSingleton();
        SetGameplaySingleton(gameplay);
        Component hostAction = hostPlayer.GetComponent(TypeByName("PlayerActionController"));
        Set(hostAction, "actionItem", shovel);
        Component hostInteraction = hostPlayer.GetComponent(TypeByName("PlayerInteractionNew"));
        object[] hostRayArgs = { default(Ray) };
        Assert.That((bool)hostInteraction.GetType().GetMethod("TryGetAimRay", new[] { typeof(Ray).MakeByRefType() })
            .Invoke(hostInteraction, hostRayArgs), Is.True, "The host player has a production camera aim ray.");
        Ray hostRay = (Ray)hostRayArgs[0];
        Assert.That(Get(site, "CurrentStage").ToString(), Is.EqualTo("Digging"));
        Assert.That(Get(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
        Assert.That((bool)Get(volume, "HasValidLooseSoilConfiguration"), Is.True);
        Assert.That(Vector3.Distance(((Transform)Get(hostAction, "actionTransformHolder")).position, top), Is.LessThan(0.01f));
        Assert.That(hostRay.direction.y, Is.LessThan(-0.999f), "The camera is aimed down at the pit support.");
        Assert.That(Physics.Raycast(hostRay, out RaycastHit hostHit, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True,
            "The production camera ray reaches solid geometry.");
        Assert.That(hostHit.collider, Is.SameAs(support), "The nearest solid hit is the pit's explicit support collider.");
        Assert.That(Vector3.Dot(hostHit.normal, support.transform.up), Is.GreaterThan(0.999f), "The camera hits the support top face.");
        Assert.That(Get(hostInventory.GetType().GetMethod("GetSelectedItemForServerValidation").Invoke(hostInventory, null), "itemType").ToString(),
            Is.EqualTo("Shovel"), "The server validates the host's selected shovel.");
        Assert.That((bool)hostAction.GetType().GetMethod("IsTrustedMountingNailAimOrigin", Instance)
            .Invoke(hostAction, new object[] { hostRay.origin, hostRay.direction }), Is.True,
            "The host ray origin is aligned with the server-known player anchor.");
        object[] siteArgs = { (int)Get(bridgeComponent, "ComponentID"), null };
        Assert.That((bool)gameplay.GetType().GetMethod("TryGetConstructionSite", Instance).Invoke(gameplay, siteArgs), Is.True,
            "The server gameplay manager resolves the target component ID.");
        Assert.That(siteArgs[1], Is.SameAs(site), "The server component index maps to the selected Stage02 site.");
        object[] stateIndexArgs = { (int)Get(bridgeComponent, "ComponentID"), -1 };
        Assert.That((bool)gameplay.GetType().GetMethod("TryGetStateIndex", Instance).Invoke(gameplay, stateIndexArgs), Is.True,
            "The server has initialized replication state for this bridge component.");
        object availability = hostAction.GetType().GetMethod("GetActionAvailability", Instance).Invoke(hostAction, new[] { site });
        Assert.That((bool)Get(availability, "IsInRange"), Is.True, "The action holder is at the support top.");
        Assert.That((bool)Get(availability, "HasCorrectTool"), Is.True, "The host has the shovel equipped.");
        float hostBefore = (float)Get(site, "CurrentWorkProgress");
        Invoke(hostAction, "ResolveProfiledImpact");
        Assert.That((float)Get(site, "CurrentWorkProgress"), Is.GreaterThan(hostBefore),
            "The host's exact support ray should apply authoritative shovel work.");

        // Exercise the legacy server handler after a real remote shovel is equipped.
        host.SetSingleton();
        SetGameplaySingleton(gameplay);
        float legacyBefore = (float)Get(site, "CurrentWorkProgress");
        MethodInfo legacyHandler = gameplay.GetType().GetMethod("TryApplyConstructionWorkServer", Instance);
        Type toolType = TypeByName("EquippableItemType");
        legacyHandler.Invoke(gameplay, new[] { (object)owner.LocalClientId, Get(bridgeComponent, "ComponentID"),
            Enum.Parse(toolType, "Shovel"), -1 });
        yield return null;
        Assert.That((float)Get(site, "CurrentWorkProgress"), Is.EqualTo(legacyBefore),
            "The generic construction request cannot bypass the dedicated shovel aim validation.");

        Component remoteAction = remoteOwnerPlayer.GetComponent(TypeByName("PlayerActionController"));
        Set(remoteAction, "actionItem", shovel);
        float remoteBefore = (float)Get(site, "CurrentWorkProgress");
        Invoke(remoteAction, "ResolveProfiledImpact");
        host.SetSingleton();
        SetGameplaySingleton(gameplay);
        yield return Until(() => (float)Get(site, "CurrentWorkProgress") > remoteBefore,
            "remote owner's ray-validated shovel RPC");
        float remoteAcceptedProgress = (float)Get(site, "CurrentWorkProgress");
        Component serverRemoteAction = remotePlayerOnServer.GetComponent(TypeByName("PlayerActionController"));
        double nextAllowedImpact = (double)Get(serverRemoteAction, "nextServerFoundationSoilImpactTime");
        yield return Until(() => host.ServerTime.Time >= nextAllowedImpact, "remote shovel server cadence window");

        // Mismatched component identity and a non-unit raw ray are rejected by the same RPC.
        owner.SetSingleton();
        Component interaction = remoteOwnerPlayer.GetComponent(TypeByName("PlayerInteractionNew"));
        Ray ray;
        object[] rayArgs = { default(Ray) };
        Assert.That((bool)interaction.GetType().GetMethod("TryGetAimRay", new[] { typeof(Ray).MakeByRefType() })
            .Invoke(interaction, rayArgs), Is.True);
        ray = (Ray)rayArgs[0];
        float wrongPitBefore = (float)Get(otherSite, "CurrentWorkProgress");
        InvokeServerRpc(remoteAction, Get(otherBridgeComponent, "ComponentID"), ray.origin, ray.direction);
        InvokeServerRpc(remoteAction, Get(bridgeComponent, "ComponentID"), ray.origin, Vector3.zero);
        host.SetSingleton();
        SetGameplaySingleton(gameplay);
        yield return null;
        Assert.That((float)Get(otherSite, "CurrentWorkProgress"), Is.EqualTo(wrongPitBefore));
        Assert.That((float)Get(site, "CurrentWorkProgress"), Is.EqualTo(remoteAcceptedProgress),
            "Invalid component IDs and malformed rays must not add server progress.");
#endif
    }

#if UNITY_EDITOR
    private Component[] ComponentsInScene(Type type)
    {
        var found = new List<Component>();
        foreach (Component component in Resources.FindObjectsOfTypeAll(type))
            if (component != null && component.gameObject.scene == stageScene) found.Add(component);
        return found.ToArray();
    }

    private GameObject CreatePlayerPrefab(ScriptableObject shovel)
    {
        GameObject prefab = new GameObject("Foundation soil RPC player prefab");
        prefab.SetActive(false);
        owned.Add(prefab);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", Instance).SetValue(networkObject, 0x46534F49u);
        networkObject.SetSceneObjectStatus(false);
        Type inventoryType = TypeByName("PlayerInventory");
        Component inventory = prefab.AddComponent(inventoryType);
        FieldInfo catalog = inventoryType.GetField("equippableItemCatalog", Instance);
        Array catalogItems = Array.CreateInstance(catalog.FieldType.GetElementType(), 1);
        catalogItems.SetValue(shovel, 0);
        catalog.SetValue(inventory, catalogItems);
        Component input = prefab.AddComponent(TypeByName("PlayerInputNew"));
        ((Behaviour)input).enabled = false;
        Component interaction = prefab.AddComponent(TypeByName("PlayerInteractionNew"));
        CharacterController capsule = prefab.AddComponent<CharacterController>();
        capsule.height = 1.8f;
        capsule.radius = 0.3f;
        prefab.AddComponent(TypeByName("PlayerHealth"));
        prefab.AddComponent(TypeByName("ClientNetworkTransform"));
        Transform anchor = prefab.transform;
        Set(interaction, "interactionOrigin", anchor);
        GameObject cameraObject = new GameObject("Foundation soil RPC aim camera");
        cameraObject.transform.SetParent(anchor, false);
        Camera aimCamera = cameraObject.AddComponent<Camera>();
        aimCamera.nearClipPlane = 0.01f;
        Set(interaction, "aimCamera", aimCamera);
        Component action = prefab.AddComponent(TypeByName("PlayerActionController"));
        Set(action, "actionTransformHolder", anchor);
        prefab.SetActive(true);
        return prefab;
    }

    private void ConfigureGameplay(Type gameplayType, GameObject bridgeRoot, Type bridgeComponentType)
    {
        GameObject root = new GameObject("Foundation soil RPC GameplayManager");
        root.SetActive(false);
        owned.Add(root);
        gameplay = root.AddComponent(gameplayType);
        Set(gameplay, "bridge", bridgeRoot.GetComponent(TypeByName("Bridge")));
        Component[] components = bridgeRoot.GetComponentsInChildren(bridgeComponentType, true);
        int maxId = 0;
        foreach (Component component in components) maxId = Mathf.Max(maxId, (int)Get(component, "ComponentID"));
        Type dataType = gameplayType.GetField("bridgeComponentDataArray", Instance).FieldType.GetElementType();
        Array data = Array.CreateInstance(dataType, maxId + 1);
        var stageIndexes = new List<int>();
        foreach (Component component in components)
        {
            int id = (int)Get(component, "ComponentID");
            object row = Activator.CreateInstance(dataType);
            Set(row, "bridgeComponentSO", Get(component, "bridgeComponentSO"));
            data.SetValue(row, id);
            stageIndexes.Add(id);
        }
        Set(gameplay, "bridgeComponentDataArray", data);
        Type stageType = gameplayType.GetField("bridgeBuildingStages", Instance).FieldType.GetElementType();
        object stage = Activator.CreateInstance(stageType);
        Set(stage, "bridgeComponentDataIndexes", stageIndexes.ToArray());
        Array stages = Array.CreateInstance(stageType, 1);
        stages.SetValue(stage, 0);
        Set(gameplay, "bridgeBuildingStages", stages);
        Set(gameplay, "bridgeProgressInitialized", true);
        Call(gameplay, "CacheBridgeComponents");
        TypeByName("GameplayManager").GetField("<Instance>k__BackingField", Static).SetValue(null, gameplay);
    }

    private NetworkManager CreateManager(string label, ushort port, GameObject playerPrefab)
    {
        GameObject root = new GameObject(label);
        owned.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, PlayerPrefab = playerPrefab,
            EnableSceneManagement = false };
        managers.Add(manager);
        return manager;
    }

    private static void TeleportPlayer(NetworkObject player, Vector3 position)
    {
        Component networkTransform = player.GetComponent(TypeByName("ClientNetworkTransform"));
        MethodInfo teleport = networkTransform.GetType().GetMethod("Teleport", Instance);
        teleport.Invoke(networkTransform, new object[] { position, player.transform.rotation, player.transform.localScale });
    }

    private static void ConfigurePlayerAim(NetworkObject player, Vector3 rayOrigin, Vector3 actionPosition)
    {
        Component interaction = player.GetComponent(TypeByName("PlayerInteractionNew"));
        Camera camera = player.GetComponentInChildren<Camera>(true);
        Assert.That(camera, Is.Not.Null, "The spawned player replica owns an aim camera.");
        camera.gameObject.SetActive(true);
        camera.enabled = true;
        interaction.GetType().GetMethod("SetAimCamera", Instance).Invoke(interaction, new object[] { camera });
        camera.transform.SetPositionAndRotation(rayOrigin, Quaternion.LookRotation(Vector3.down, Vector3.forward));
        GameObject holder = new GameObject("Foundation soil action holder");
        holder.transform.SetParent(player.transform, true);
        holder.transform.position = actionPosition;
        Set(player.GetComponent(TypeByName("PlayerActionController")), "actionTransformHolder", holder.transform);
    }

    private static Vector3 SupportTop(Collider collider) => collider.transform.TransformPoint(
        ((BoxCollider)collider).center + Vector3.up * ((BoxCollider)collider).size.y * 0.5f);
    private static void SetDigging(Component site, Component volume, string substage)
    {
        Type siteType = TypeByName("BridgeConstructionSite");
        Type stageType = TypeByName("BridgeConstructionStage");
        Type substageType = TypeByName("FoundationDiggingSubstage");
        Field(siteType, "currentStage").SetValue(site, Enum.Parse(stageType, "Digging"));
        Field(siteType, "diggingSubstage").SetValue(site, Enum.Parse(substageType, substage));
        object workflow = Field(siteType, "workflow").GetValue(site);
        volume.GetType().GetMethod("ApplyDiggingState", Instance).Invoke(volume, new[]
        { Enum.Parse(stageType, "Digging"), Enum.Parse(substageType, substage), (object)0f, 0f, workflow, 0 });
    }
    private static void InvokeServerRpc(Component action, object componentId, Vector3 origin, Vector3 direction)
    {
        MethodInfo rpc = action.GetType().GetMethod("RequestFoundationSoilImpactServerRpc", Instance);
        Type paramsType = Type.GetType("Unity.Netcode.ServerRpcParams, Unity.Netcode.Runtime");
        rpc.Invoke(action, new[] { componentId, (object)origin, direction, Activator.CreateInstance(paramsType) });
    }
    private static void SetGameplaySingleton(Component value) =>
        TypeByName("GameplayManager").GetField("<Instance>k__BackingField", Static).SetValue(null, value);
    private static Type TypeByName(string name) => Type.GetType(name + ", Assembly-CSharp");
    private static object Get(object value, string name) => value.GetType().GetProperty(name, Instance)?.GetValue(value)
        ?? value.GetType().GetField(name, Instance)?.GetValue(value);
    private static void Set(object value, string name, object data) => Field(value.GetType(), name).SetValue(value, data);
    private static FieldInfo Field(Type type, string name) => type.GetField(name, Instance);
    private static void Call(object value, string method) => value.GetType().GetMethod(method, Instance).Invoke(value, null);
    private static void Invoke(object value, string method) => value.GetType().GetMethod(method, Instance).Invoke(value, null);
    private static IEnumerator Until(Func<bool> condition, string label)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
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
#endif

    [UnityTearDown]
    public IEnumerator TearDown()
    {
#if UNITY_EDITOR
        foreach (NetworkManager manager in managers)
            if (manager != null && manager.IsListening) manager.Shutdown(true);
        float deadline = Time.realtimeSinceStartup + 12f;
        while (Time.realtimeSinceStartup < deadline)
        {
            bool pending = false;
            foreach (NetworkManager manager in managers) pending |= manager != null && manager.ShutdownInProgress;
            if (!pending) break;
            yield return null;
        }
        foreach (GameObject item in owned) if (item != null) Object.Destroy(item);
        if (stageScene.IsValid() && stageScene.isLoaded)
        {
            AsyncOperation unload = SceneManager.UnloadSceneAsync(stageScene);
            while (unload != null && !unload.isDone) yield return null;
        }
        TypeByName("GameplayManager").GetField("<Instance>k__BackingField", Static)?.SetValue(null, priorGameplay);
        owned.Clear();
        managers.Clear();
#else
        yield break;
#endif
    }
}
