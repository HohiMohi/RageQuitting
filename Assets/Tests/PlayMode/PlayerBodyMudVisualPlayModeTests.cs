using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class PlayerBodyMudVisualPlayModeTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/PlayerNew.prefab";
    private const string GoblinPrefabPath = "Assets/Meshes/Goblin_AI2.prefab";
    private const string SourceMaterialPath = "Assets/Meshes/Goblin_AI2_Assets/Materials/Color_12459a02-4210-4f83-b8f5-7f56e09bb7c8.mat";
    private const string MudMaterialPath = "Assets/Meshes/Goblin_AI2_Assets/Materials/GoblinPlayerMud.mat";

    private static readonly Type StatusControllerType = Type.GetType("PlayerTemporaryStatusController, Assembly-CSharp");
    private static readonly Type MudVisualType = Type.GetType("PlayerBodyMudVisual, Assembly-CSharp");
    private PlayModeTestScope scope;

    [UnityTearDown]
    public IEnumerator CleanupNetworkFixture()
    {
        if (scope == null) yield break;
        PlayModeTestScope cleanup = scope;
        yield return cleanup.Cleanup();
        scope = null;
        cleanup.AssertCleanupSucceeded();
    }

    [Test]
    public void PlayerNewUsesExactGoblinMeshMaterialSlotAndMudTemplate()
    {
        GameObject playerPrefab = LoadAsset<GameObject>(PlayerPrefabPath);
        GameObject goblinPrefab = LoadAsset<GameObject>(GoblinPrefabPath);
        Material sourceMaterial = LoadAsset<Material>(SourceMaterialPath);
        Material mudTemplate = LoadAsset<Material>(MudMaterialPath);
        Mesh sourceMesh = FindSourceRenderer(goblinPrefab, sourceMaterial).sharedMesh;

        Component visualSetup = playerPrefab.GetComponent(Type.GetType("PlayerGoblinVisualSetup, Assembly-CSharp"));
        Component mudVisual = playerPrefab.GetComponent(MudVisualType);
        Assert.That(visualSetup, Is.Not.Null);
        Assert.That(mudVisual, Is.Not.Null);
        Assert.That(GetField<GameObject>(visualSetup, "goblinVisualPrefab"), Is.SameAs(goblinPrefab));
        Assert.That(GetField<Mesh>(mudVisual, "sourceMesh"), Is.SameAs(sourceMesh));
        Assert.That(GetField<Material>(mudVisual, "sourceMaterial"), Is.SameAs(sourceMaterial));
        Assert.That(GetField<Material>(mudVisual, "mudMaterialTemplate"), Is.SameAs(mudTemplate));
        Assert.That(GetField<int>(mudVisual, "sourceMaterialSlot"), Is.Zero);
        Assert.That(sourceMesh, Is.SameAs(FindSourceRenderer(goblinPrefab, sourceMaterial).sharedMesh));
        Assert.That(mudTemplate.shader.name, Is.EqualTo("RageQuitting/Player Body Mud Lit"));
        Assert.That(mudTemplate.GetFloat("_MudAmount"), Is.Zero);
        Assert.That(mudTemplate.GetTexture("_BaseMap"), Is.SameAs(sourceMaterial.GetTexture("_BaseMap")));
    }

    [UnityTest]
    public IEnumerator RegisteredGoblinGetsPrivateMaterialAndOwnedSlotIsRestoredOnDisableAndRebind()
    {
        GameObject goblinPrefab = LoadAsset<GameObject>(GoblinPrefabPath);
        Material sourceMaterial = LoadAsset<Material>(SourceMaterialPath);
        Material template = LoadAsset<Material>(MudMaterialPath);
        Mesh sourceMesh = FindSourceRenderer(goblinPrefab, sourceMaterial).sharedMesh;
        GameObject first = CreateRegisteredVisualPlayer(goblinPrefab, sourceMesh, sourceMaterial, template);
        GameObject second = CreateRegisteredVisualPlayer(goblinPrefab, sourceMesh, sourceMaterial, template);
        try
        {
            yield return null;
            Type visualSetupType = Type.GetType("PlayerGoblinVisualSetup, Assembly-CSharp");
            GameObject firstSpawnedVisual = (GameObject)visualSetupType.GetProperty("SpawnedVisual").GetValue(first.GetComponent(visualSetupType));
            GameObject secondSpawnedVisual = (GameObject)visualSetupType.GetProperty("SpawnedVisual").GetValue(second.GetComponent(visualSetupType));
            SkinnedMeshRenderer firstRenderer = FindSourceRenderer(firstSpawnedVisual, null);
            SkinnedMeshRenderer secondRenderer = FindSourceRenderer(secondSpawnedVisual, null);
            Material firstMaterial = firstRenderer.sharedMaterials[0];
            Material secondMaterial = secondRenderer.sharedMaterials[0];
            Assert.That(firstMaterial, Is.Not.SameAs(template));
            Assert.That(secondMaterial, Is.Not.SameAs(template));
            Assert.That(firstMaterial, Is.Not.SameAs(secondMaterial), "Each bound goblin owns a distinct instance.");
            Assert.That(firstMaterial.shader.name, Is.EqualTo(template.shader.name));
            Assert.That(firstMaterial.GetTexture("_BaseMap"), Is.SameAs(sourceMaterial.GetTexture("_BaseMap")));
            Assert.That(firstMaterial.GetTexture("_BumpMap"), Is.SameAs(sourceMaterial.GetTexture("_BumpMap")));
            Assert.That(firstMaterial.GetFloat("_Metallic"), Is.Zero);
            Assert.That(firstMaterial.GetFloat("_Smoothness"), Is.EqualTo(0.5f).Within(0.0001f));

            firstMaterial.SetColor("_BaseColor", new Color(0.73f, 0.61f, 0.48f, 1f));
            SetStatusMud(first.GetComponent(StatusControllerType), 0.25f);
            Assert.That(firstMaterial.GetFloat("_MudAmount"), Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(firstMaterial.GetColor("_BaseColor"), Is.EqualTo(new Color(0.73f, 0.61f, 0.48f, 1f)),
                "Status changes update only the mud amount property.");
            Assert.That(secondMaterial.GetFloat("_MudAmount"), Is.Zero, "A second player remains visually independent.");

            Behaviour firstVisual = (Behaviour)first.GetComponent(MudVisualType);
            firstVisual.enabled = false;
            Assert.That(firstRenderer.sharedMaterials[0], Is.SameAs(sourceMaterial),
                "Disabling the visual controller restores its material only while that slot is still owned.");
            firstVisual.enabled = true;
            Material rebound = firstRenderer.sharedMaterials[0];
            Assert.That(rebound, Is.Not.SameAs(firstMaterial));
            Assert.That(rebound.GetFloat("_MudAmount"), Is.EqualTo(0.25f).Within(0.0001f),
                "Rebinding applies the current snapshot immediately.");
            StatusControllerType.GetMethod("ResetTemporaryStatuses").Invoke(first.GetComponent(StatusControllerType), null);
            Assert.That(rebound.GetFloat("_MudAmount"), Is.Zero, "Clearing or respawning clears the material on its live renderer.");

            Material externalReplacement = new Material(sourceMaterial);
            try
            {
                Material[] materials = firstRenderer.sharedMaterials;
                materials[0] = externalReplacement;
                firstRenderer.sharedMaterials = materials;
                MudVisualType.GetMethod("BindVisual").Invoke(firstVisual, new object[] { null });
                Assert.That(firstRenderer.sharedMaterials[0], Is.SameAs(externalReplacement),
                    "Cleanup does not overwrite a material installed by another system.");
            }
            finally { UnityEngine.Object.Destroy(externalReplacement); }
        }
        finally
        {
            UnityEngine.Object.Destroy(first);
            UnityEngine.Object.Destroy(second);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator ReplicatedMudAmountUpdatesHostOwnerAndLateObserverBodyMaterials()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("The mud visual network fixture requires no pre-existing network session.");

        scope = new PlayModeTestScope("PlayerBodyMudVisualNetwork");
        GameObject playerPrefab = CreateNetworkPlayerPrefab();
        scope.Own(playerPrefab.transform.parent.gameObject);
        ushort port = ReservePort();
        NetworkManager host = CreateOwnedManager("Mud visual host", playerPrefab, port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.LocalClient?.PlayerObject != null, "host player spawn");

        NetworkManager owner = CreateOwnedManager("Mud visual remote owner", playerPrefab, port);
        Assert.That(owner.StartClient(), Is.True);
        owner.SetSingleton();
        yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null &&
            host.ConnectedClientsIds.Count == 2, "remote owner player spawn");

        NetworkObject hostPlayer = host.LocalClient.PlayerObject;
        NetworkObject ownerOnHost = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        NetworkObject hostOnOwner = FindSpawned(owner, hostPlayer.NetworkObjectId);
        Assert.That(hostOnOwner, Is.Not.Null);
        BindVisual(hostPlayer);
        BindVisual(ownerOnHost);
        BindVisual(hostOnOwner);
        BindVisual(owner.LocalClient.PlayerObject);

        SetStatusMud(hostPlayer.GetComponent(StatusControllerType), 0.28f);
        SetStatusMud(ownerOnHost.GetComponent(StatusControllerType), 0.74f);
        ulong hostPlayerId = hostPlayer.NetworkObjectId;
        ulong ownerPlayerId = ownerOnHost.NetworkObjectId;
        yield return Until(() =>
        {
            NetworkObject remoteHost = FindSpawned(owner, hostPlayerId);
            NetworkObject remoteOwner = FindSpawned(owner, ownerPlayerId);
            return remoteHost != null && remoteOwner != null &&
                Mathf.Abs(GetMudAmount(remoteHost) - 0.28f) < 0.0001f &&
                Mathf.Abs(GetMudAmount(remoteOwner) - 0.74f) < 0.0001f &&
                Mathf.Abs(GetMudVisualAmount(remoteHost) - 0.28f) < 0.0001f &&
                Mathf.Abs(GetMudVisualAmount(remoteOwner) - 0.74f) < 0.0001f;
        }, "owner snapshots and matching shader values");

        NetworkManager lateObserver = CreateOwnedManager("Mud visual late observer", playerPrefab, port);
        Assert.That(lateObserver.StartClient(), Is.True);
        lateObserver.SetSingleton();
        yield return Until(() => lateObserver.IsConnectedClient && lateObserver.LocalClient?.PlayerObject != null &&
            host.ConnectedClientsIds.Count == 3 && FindSpawned(lateObserver, hostPlayerId) != null &&
            FindSpawned(lateObserver, ownerPlayerId) != null, "late observer receives existing players");

        NetworkObject lateHost = FindSpawned(lateObserver, hostPlayerId);
        NetworkObject lateOwner = FindSpawned(lateObserver, ownerPlayerId);
        BindVisual(lateHost);
        BindVisual(lateOwner);
        yield return Until(() =>
            Mathf.Abs(GetMudAmount(lateHost) - 0.28f) < 0.0001f &&
            Mathf.Abs(GetMudAmount(lateOwner) - 0.74f) < 0.0001f &&
            Mathf.Abs(GetMudVisualAmount(lateHost) - 0.28f) < 0.0001f &&
            Mathf.Abs(GetMudVisualAmount(lateOwner) - 0.74f) < 0.0001f,
            "late observer snapshot drives both body materials");

        Assert.That(GetMudVisualAmount(hostPlayer), Is.EqualTo(0.28f).Within(0.0001f));
        Assert.That(GetMudVisualAmount(ownerOnHost), Is.EqualTo(0.74f).Within(0.0001f));
        Assert.That(GetMudVisualAmount(hostOnOwner), Is.EqualTo(0.28f).Within(0.0001f));
        Assert.That(GetMudVisualAmount(owner.LocalClient.PlayerObject), Is.EqualTo(0.74f).Within(0.0001f));
    }

    private static GameObject CreateRegisteredVisualPlayer(GameObject goblinPrefab, Mesh mesh, Material source, Material template)
    {
        GameObject player = new GameObject("Mud visual registered player");
        player.SetActive(false);
        player.AddComponent<NetworkObject>();
        player.AddComponent(StatusControllerType);
        player.AddComponent(Type.GetType("PlayerNetworkSetup, Assembly-CSharp"));
        Component bodyMud = player.AddComponent(MudVisualType);
        SetField(bodyMud, "sourceMesh", mesh);
        SetField(bodyMud, "sourceMaterial", source);
        SetField(bodyMud, "mudMaterialTemplate", template);
        SetField(bodyMud, "sourceMaterialSlot", 0);
        Component visualSetup = player.AddComponent(Type.GetType("PlayerGoblinVisualSetup, Assembly-CSharp"));
        SetField(visualSetup, "goblinVisualPrefab", goblinPrefab);
        player.SetActive(true);
        return player;
    }

    private static GameObject CreateNetworkPlayerPrefab()
    {
        GameObject staging = new GameObject("Mud visual player prefab staging");
        staging.SetActive(false);
        GameObject prefab = new GameObject("Mud visual network player prefab");
        prefab.transform.SetParent(staging.transform, false);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .SetValue(networkObject, 0x4D554432u);
        networkObject.SetSceneObjectStatus(false);
        prefab.AddComponent(StatusControllerType);
        Component mudVisual = prefab.AddComponent(MudVisualType);
        Mesh sourceMesh = FindSourceRenderer(LoadAsset<GameObject>(GoblinPrefabPath), LoadAsset<Material>(SourceMaterialPath)).sharedMesh;
        SetField(mudVisual, "sourceMesh", sourceMesh);
        SetField(mudVisual, "sourceMaterial", LoadAsset<Material>(SourceMaterialPath));
        SetField(mudVisual, "mudMaterialTemplate", LoadAsset<Material>(MudMaterialPath));
        SetField(mudVisual, "sourceMaterialSlot", 0);
        GameObject visual = new GameObject("Networked goblin visual");
        visual.transform.SetParent(prefab.transform, false);
        SkinnedMeshRenderer renderer = visual.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = sourceMesh;
        renderer.sharedMaterial = LoadAsset<Material>(SourceMaterialPath);
        return prefab;
    }

    private static void BindVisual(NetworkObject player)
    {
        Component mudVisual = player.GetComponent(MudVisualType);
        GameObject visual = player.transform.Find("Networked goblin visual").gameObject;
        MudVisualType.GetMethod("BindVisual").Invoke(mudVisual, new object[] { visual });
    }

    private NetworkManager CreateOwnedManager(string objectName, GameObject playerPrefab, ushort port)
    {
        GameObject root = new GameObject(objectName);
        scope.Own(root);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = playerPrefab,
            EnableSceneManagement = false
        };
        scope.OwnManager(manager);
        return manager;
    }

    private static SkinnedMeshRenderer FindSourceRenderer(GameObject root, Material expectedMaterial)
    {
        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (expectedMaterial == null || Array.IndexOf(renderer.sharedMaterials, expectedMaterial) >= 0)
            {
                return renderer;
            }
        }

        Assert.Fail("The expected Goblin skinned renderer was not found.");
        return null;
    }

    private static NetworkObject FindSpawned(NetworkManager manager, ulong networkObjectId) =>
        manager != null && manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject instance)
            ? instance : null;

    private static void SetStatusMud(Component statuses, float amount)
    {
        float multiplier = 1f - 0.3f * Mathf.Clamp01(amount);
        MethodInfo set = StatusControllerType.GetMethod("SetMudAmount", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(set, Is.Not.Null);
        Assert.That((bool)set.Invoke(statuses, new object[] { amount, multiplier }), Is.True);
    }

    private static float GetMudAmount(NetworkObject player) =>
        (float)StatusControllerType.GetProperty("MudAmount").GetValue(player.GetComponent(StatusControllerType));

    private static float GetMudVisualAmount(NetworkObject player)
    {
        SkinnedMeshRenderer renderer = player.GetComponentInChildren<SkinnedMeshRenderer>(true);
        return renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_MudAmount")
            ? renderer.sharedMaterial.GetFloat("_MudAmount") : -1f;
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

    private static T LoadAsset<T>(string path) where T : UnityEngine.Object
    {
        Type assetDatabase = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
        Assert.That(assetDatabase, Is.Not.Null, "This authored asset test runs in the Unity Editor.");
        MethodInfo loadAsset = Array.Find(assetDatabase.GetMethods(BindingFlags.Static | BindingFlags.Public), method =>
            method.Name == "LoadAssetAtPath" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1);
        return (T)loadAsset.MakeGenericMethod(typeof(T)).Invoke(null, new object[] { path });
    }

    private static T GetField<T>(Component component, string fieldName) =>
        (T)component.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);

    private static void SetField(Component component, string fieldName, object value) =>
        component.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, value);
}
