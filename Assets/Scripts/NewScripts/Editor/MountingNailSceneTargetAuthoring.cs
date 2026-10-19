#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using Unity.Netcode;

public static class MountingNailSceneTargetAuthoring
{
    [MenuItem("Tools/RageQuitting/Mounting Nails/Bake Selected Scene Target IDs")]
    private static void BakeSelected()
    {
        MountingNailSceneTargetIdentity[] selected = Selection.gameObjects
            .SelectMany(gameObject => gameObject.GetComponentsInChildren<MountingNailSceneTargetIdentity>(true)
                .Concat(gameObject.GetComponent<MountingNailSceneTargetIdentity>() != null
                    ? new[] { gameObject.GetComponent<MountingNailSceneTargetIdentity>() } : System.Array.Empty<MountingNailSceneTargetIdentity>()))
            .Distinct().ToArray();
        Bake(selected);
    }

    [MenuItem("Tools/RageQuitting/Mounting Nails/Bake Selected Scene Target IDs", true)]
    private static bool CanBakeSelected() => Selection.gameObjects.Length > 0;

    [MenuItem("Tools/RageQuitting/Mounting Nails/Validate Loaded Scene Target IDs")]
    private static void ValidateLoadedScenes()
    {
        List<MountingNailSceneTargetIdentity> identities = new();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (GameObject root in scene.GetRootGameObjects())
                identities.AddRange(root.GetComponentsInChildren<MountingNailSceneTargetIdentity>(true));
        }
        Validate(identities, true);
    }

    private static void Bake(IEnumerable<MountingNailSceneTargetIdentity> identities)
    {
        foreach (MountingNailSceneTargetIdentity identity in identities)
        {
            if (identity == null) continue;
            if (!TryGetSceneObjectId(identity, out string baked))
            {
                Debug.LogError($"{identity.name}: mounting nail IDs can only be baked onto objects in a saved, loaded scene.");
                continue;
            }
            if (identity.StableTargetId == baked) continue;
            Undo.RecordObject(identity, "Bake mounting nail scene target identity");
            SerializedObject serialized = new(identity);
            serialized.FindProperty("stableTargetId").stringValue = baked;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(identity);
            EditorSceneManager.MarkSceneDirty(identity.gameObject.scene);
        }
        Validate(identities, true);
    }

    private static void Validate(IEnumerable<MountingNailSceneTargetIdentity> identities, bool log)
    {
        Dictionary<string, MountingNailSceneTargetIdentity> seen = new();
        List<string> errors = new();
        foreach (MountingNailSceneTargetIdentity identity in identities)
        {
            if (identity == null) continue;
            string id = identity.StableTargetId;
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"{identity.name}: ID is not baked. Select its physical root and run Bake Selected Scene Target IDs.");
                continue;
            }
            if (identity.GetComponentInParent<Rigidbody>() != identity.GetComponent<Rigidbody>())
                errors.Add($"{identity.name}: an ancestor Rigidbody makes this a collider child, not a physical root.");
            if (seen.TryGetValue(id, out MountingNailSceneTargetIdentity duplicate))
                errors.Add($"Duplicate ID {id}: {duplicate.name} and {identity.name}.");
            else seen.Add(id, identity);
        }
        if (!log) return;
        if (errors.Count == 0) Debug.Log($"Validated {seen.Count} mounting nail scene target ID(s).");
        else Debug.LogError("Mounting nail scene target identity validation failed:\n" + string.Join("\n", errors));
    }

    private static bool TryGetSceneObjectId(MountingNailSceneTargetIdentity identity, out string value)
    {
        value = null;
        if (identity == null || EditorUtility.IsPersistent(identity)
            || identity.GetComponentInParent<NetworkObject>() != null || !identity.gameObject.scene.IsValid()
            || !identity.gameObject.scene.isLoaded || string.IsNullOrWhiteSpace(identity.gameObject.scene.path)) return false;
        GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(identity.gameObject);
        // UnityEditor.GlobalObjectId uses 2 for scene-object identities (kSceneObject).
        if (globalId.identifierType != 2
            || globalId.assetGUID.ToString() == "00000000000000000000000000000000") return false;
        value = globalId.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }
}
#endif
