using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MountingNailStage3EditModeTests
{
    [Test]
    public void SceneBakingUsesDistinctGlobalIdsAndRejectsPrefabAssets()
    {
        string scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/Tests/MountingNailStage3IdentityBakeFixture.unity");
        string prefabPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Tests/MountingNailStage3IdentityPrefab.prefab");
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        try
        {
            GameObject firstRoot = new("first baked physical root");
            SceneManager.MoveGameObjectToScene(firstRoot, scene);
            firstRoot.AddComponent<Rigidbody>();
            Component first = firstRoot.AddComponent(System.Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp"));
            GameObject secondRoot = new("second baked physical root");
            SceneManager.MoveGameObjectToScene(secondRoot, scene);
            secondRoot.AddComponent<Rigidbody>();
            Component second = secondRoot.AddComponent(first.GetType());
            Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);

            System.Type authoringType = System.Type.GetType("MountingNailSceneTargetAuthoring, Assembly-CSharp-Editor");
            Assert.That(authoringType, Is.Not.Null);
            MethodInfo bake = authoringType.GetMethod("Bake", BindingFlags.Static | BindingFlags.NonPublic);
            System.Array identities = System.Array.CreateInstance(first.GetType(), 2);
            identities.SetValue(first, 0);
            identities.SetValue(second, 1);
            bake.Invoke(null, new object[] { identities });
            string firstId = (string)first.GetType().GetProperty("StableTargetId").GetValue(first);
            string secondId = (string)second.GetType().GetProperty("StableTargetId").GetValue(second);
            Assert.That(firstId, Is.Not.Empty);
            Assert.That(secondId, Is.Not.EqualTo(firstId), "Separate instances in one saved scene have distinct serialized IDs.");

            GameObject prefabSource = new("prefab physical root");
            prefabSource.AddComponent<Rigidbody>();
            prefabSource.AddComponent(first.GetType());
            PrefabUtility.SaveAsPrefabAsset(prefabSource, prefabPath);
            Object.DestroyImmediate(prefabSource);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Component prefabIdentity = prefab.GetComponent(first.GetType());
            MethodInfo tryGetId = authoringType.GetMethod("TryGetSceneObjectId", BindingFlags.Static | BindingFlags.NonPublic);
            object[] args = { prefabIdentity, null };
            Assert.That(tryGetId.Invoke(null, args), Is.EqualTo(false), "Prefab assets cannot receive scene-instance identities.");
        }
        finally
        {
            try
            {
                bool hasRestorableActiveScene = false;
                foreach (SceneSetup setup in previousSetup)
                    if (setup.isLoaded && setup.isActive && !string.IsNullOrWhiteSpace(setup.path))
                    {
                        hasRestorableActiveScene = true;
                        break;
                    }
                if (hasRestorableActiveScene) EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            finally
            {
                AssetDatabase.DeleteAsset(scenePath);
                AssetDatabase.DeleteAsset(prefabPath);
            }
        }
    }

    [Test]
    public void IdentityBelongsToPhysicalRigidbodyRootAndReferenceCarriesStableId()
    {
        GameObject physicalRoot = new("Stage 3 physical root");
        GameObject colliderChild = new("Collider child");
        try
        {
            Rigidbody body = physicalRoot.AddComponent<Rigidbody>();
            colliderChild.transform.SetParent(physicalRoot.transform, false);
            colliderChild.AddComponent<BoxCollider>();
            System.Type identityType = System.Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
            Assert.That(identityType, Is.Not.Null);
            Component identity = physicalRoot.AddComponent(identityType);
            identityType.GetField("stableTargetId", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(identity, "scene-target-physical-root-test");

            Assert.That(identityType.GetProperty("PhysicalBody").GetValue(identity), Is.SameAs(body));
            Assert.That(identityType.GetProperty("HasValidIdentity").GetValue(identity), Is.EqualTo(true));
            Assert.That(colliderChild.GetComponentInParent(identityType), Is.SameAs(identity));

            object reference = identityType.GetMethod("CreateSceneReference").Invoke(identity, null);
            Assert.That(reference.GetType().GetProperty("IsValid").GetValue(reference), Is.EqualTo(true));
            Assert.That(reference.GetType().GetField("StableTargetId").GetValue(reference).ToString(),
                Is.EqualTo("scene-target-physical-root-test"));
        }
        finally
        {
            Object.DestroyImmediate(physicalRoot);
        }
    }

    [Test]
    public void UnbakedIdentityCannotProduceReference()
    {
        GameObject physicalRoot = new("Unbaked Stage 3 target");
        try
        {
            System.Type identityType = System.Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
            Component identity = physicalRoot.AddComponent(identityType);
            object reference = identityType.GetMethod("CreateSceneReference").Invoke(identity, null);
            Assert.That(reference.GetType().GetProperty("IsValid").GetValue(reference), Is.EqualTo(false));
        }
        finally
        {
            Object.DestroyImmediate(physicalRoot);
        }
    }

    [Test]
    public void NetworkTargetPhysicalChildPathDistinguishesRootsWithoutPrefabNames()
    {
        GameObject networkRoot = new("Network target fixture root");
        try
        {
            Transform physicalRoot = new GameObject("same-name").transform;
            physicalRoot.SetParent(networkRoot.transform, false);
            physicalRoot.gameObject.AddComponent<Rigidbody>();
            System.Type identityType = System.Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
            System.Type networkObjectType = System.Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime");
            Assert.That(networkObjectType, Is.Not.Null);
            networkRoot.AddComponent(networkObjectType);
            object[] args = { networkRoot.transform, physicalRoot, null };
            bool success = (bool)identityType.GetMethod("TryBuildPhysicalRootPath", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, args);
            Assert.That(success, Is.True);
            Assert.That(args[2].ToString(), Is.EqualTo("0"));

            System.Type referenceType = System.Type.GetType("MountingNailTargetReference, Assembly-CSharp");
            object first = System.Activator.CreateInstance(referenceType);
            referenceType.GetField("IsNetworkObject").SetValue(first, true);
            referenceType.GetField("NetworkObjectId").SetValue(first, 91UL);
            referenceType.GetField("PhysicalRootPath").SetValue(first, args[2]);
            object replacement = System.Activator.CreateInstance(referenceType);
            referenceType.GetField("IsNetworkObject").SetValue(replacement, true);
            referenceType.GetField("NetworkObjectId").SetValue(replacement, 92UL);
            referenceType.GetField("PhysicalRootPath").SetValue(replacement, args[2]);
            Assert.That(first.Equals(replacement), Is.False,
                "A replacement spawned object receives a different NGO spawn ID.");
        }
        finally
        {
            Object.DestroyImmediate(networkRoot);
        }
    }

    [Test]
    public void DuplicateSceneIdsRemainFailClosedAfterThirdRegistration()
    {
        GameObject stateRoot = new("Stage 3 scoped state fixture");
        GameObject firstRoot = new("first scene target");
        GameObject secondRoot = new("second scene target");
        GameObject thirdRoot = new("third scene target");
        try
        {
            System.Type stateType = System.Type.GetType("MountingNailSceneState, Assembly-CSharp");
            System.Type identityType = System.Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
            stateRoot.AddComponent(System.Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime"));
            Component state = stateRoot.AddComponent(stateType);
            Component first = AddTargetIdentity(firstRoot, identityType, "duplicate-id");
            Component second = AddTargetIdentity(secondRoot, identityType, "duplicate-id");
            Component third = AddTargetIdentity(thirdRoot, identityType, "duplicate-id");
            MethodInfo register = stateType.GetMethod("RegisterLocalTarget", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(register.Invoke(state, new[] { first }), Is.EqualTo(true));
            Assert.That(register.Invoke(state, new[] { second }), Is.EqualTo(false));
            Assert.That(register.Invoke(state, new[] { third }), Is.EqualTo(false),
                "A later registration cannot resurrect an ID already marked duplicate.");
        }
        finally
        {
            Object.DestroyImmediate(stateRoot);
            Object.DestroyImmediate(firstRoot);
            Object.DestroyImmediate(secondRoot);
            Object.DestroyImmediate(thirdRoot);
        }
    }

    private static Component AddTargetIdentity(GameObject root, System.Type identityType, string id)
    {
        Component identity = root.AddComponent(identityType);
        identityType.GetField("stableTargetId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(identity, id);
        return identity;
    }
}
