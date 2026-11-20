using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class SingleCarryPlacementStage5EditModeTests
{
    [Test]
    public void PlayerPrefabHasExactlyOneEnabledNetworkPreviewBehaviour()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        Assert.That(prefab, Is.Not.Null);
        System.Type previewType = System.Type.GetType("PlayerSingleCarryPlacementNetworkPreview, Assembly-CSharp");
        Assert.That(previewType, Is.Not.Null);
        Component[] previews = prefab.GetComponents(previewType);
        Assert.That(previews, Has.Length.EqualTo(1));
        Assert.That(((Behaviour)previews[0]).enabled, Is.True);
        System.Type setupType = System.Type.GetType("PlayerNetworkSetup, Assembly-CSharp");
        Component setup = prefab.GetComponent(setupType);
        Component[] components = prefab.GetComponents<Component>();
        Assert.That(setup, Is.Not.Null);
        Assert.That(System.Array.IndexOf(components, previews[0]), Is.LessThan(System.Array.IndexOf(components, setup)),
            "The preview must receive OnNetworkSpawn before PlayerNetworkSetup deactivates scene placeholders.");
        Assert.That(prefab.GetComponent(System.Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime")), Is.Not.Null);
    }

    [Test]
    public void PreviewBehaviourIsNotDisabledByRemoteOwnerOnlySetup()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            System.Type setupType = System.Type.GetType("PlayerNetworkSetup, Assembly-CSharp");
            System.Type previewType = System.Type.GetType("PlayerSingleCarryPlacementNetworkPreview, Assembly-CSharp");
            Component setup = instance.GetComponent(setupType);
            Behaviour preview = (Behaviour)instance.GetComponent(previewType);
            Assert.That(setup, Is.Not.Null);
            Assert.That(preview, Is.Not.Null);

            setupType.GetMethod("SetupRemotePlayer", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic).Invoke(setup, null);
            Assert.That(preview.enabled, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }
}
