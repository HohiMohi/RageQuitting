using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Small, repeatable authoring operation for the physical deck-panel tutorial flow.</summary>
public static class BridgeDeckPanelStage4Authoring
{
    public const string TutorialScenePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
    private const string CatalogPath = "Assets/ScriptableObjectAssets/New/ConstructionBook/ConstructionBookCatalog.asset";
    private const string ShortCatalogPath = "Assets/Experiments/TutorialTerrainStage02/BridgeWidthRevision/ConstructionBookCatalogShort.asset";
    private const string DeckPanelComponentPath = "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenDeckPanel.asset";

    public static void UpdateCatalogInstructions()
    {
        BridgeComponentSO panel = AssetDatabase.LoadAssetAtPath<BridgeComponentSO>(DeckPanelComponentPath);
        if (panel == null) throw new InvalidOperationException("Wooden deck panel component asset is missing.");
        ConstructionBookCatalogSO fullCatalog = UpdateCatalog(CatalogPath, panel);
        ConstructionBookCatalogSO shortCatalog = UpdateCatalog(ShortCatalogPath, panel);
        AssetDatabase.SaveAssetIfDirty(fullCatalog);
        AssetDatabase.SaveAssetIfDirty(shortCatalog);
    }

    public static void UpdateOpenTutorialStageInfo()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != TutorialScenePath)
            throw new InvalidOperationException("Make TutorialTerrainStage02 the active scene before updating its stage information.");

        GameObject managerObject = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(transform => transform.name == "BridgeStageInfoManager")
            .Select(transform => transform.gameObject)
            .FirstOrDefault();
        BridgeStageInfoManager manager = managerObject != null ? managerObject.GetComponent<BridgeStageInfoManager>() : null;
        BridgeComponentSO panel = AssetDatabase.LoadAssetAtPath<BridgeComponentSO>(DeckPanelComponentPath);
        if (manager == null || panel == null) throw new InvalidOperationException("Stage info manager or deck panel component asset is missing.");

        SerializedObject serializedManager = new SerializedObject(manager);
        SerializedProperty entries = serializedManager.FindProperty("entries");
        SerializedProperty panelCompletion = null;
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty candidate = entries.GetArrayElementAtIndex(i);
            if (candidate.FindPropertyRelative("componentType").objectReferenceValue == panel
                && candidate.FindPropertyRelative("stage").enumValueIndex == (int)BridgeConstructionStage.Complete)
            {
                panelCompletion = candidate;
                break;
            }
        }
        if (panelCompletion == null)
        {
            int index = entries.arraySize++;
            panelCompletion = entries.GetArrayElementAtIndex(index);
        }
        panelCompletion.FindPropertyRelative("componentType").objectReferenceValue = panel;
        panelCompletion.FindPropertyRelative("stage").enumValueIndex = (int)BridgeConstructionStage.Complete;
        panelCompletion.FindPropertyRelative("title").stringValue = "Deck panel secured";
        panelCompletion.FindPropertyRelative("message").stringValue =
            "Use F / E to position the panel by hand on the marked main-girder slot. Place a separate Mounting Nail through the panel into a main girder in each of the four quadrants, then strike each nail three times with the Industrial Hammer.";
        serializedManager.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static string ValidateOpenTutorialAuthoring()
    {
        Scene scene = SceneManager.GetSceneByPath(TutorialScenePath);
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("TutorialTerrainStage02 must be open in the Editor.");
        BridgeDeckSection[] sections = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<BridgeDeckSection>(true)).ToArray();
        BridgeDeckSection main = sections.SingleOrDefault(section => section.name == "BridgeDeckSection"
            && section.transform.IsChildOf(FindTransform(scene, "TutorialGameplay/Bridge")));
        if (main == null)
            throw new InvalidOperationException("The main tutorial bridge section was not found.");
        if (main.PanelSlots == null || main.PanelSlots.Count != 3 || main.MainGirders == null || main.MainGirders.Count != 2)
            throw new InvalidOperationException("The main section must have three slots and exactly two explicit main girders.");
        if (main.PhysicalPanelState == null)
            throw new InvalidOperationException("The main bridge section requires its own physical panel state.");
        for (int i = 0; i < main.PanelSlots.Count; i++)
        {
            BridgeDeckPanelConstructionSite slot = main.PanelSlots[i];
            if (slot == null || slot.DeckSection != main || slot.SlotIndex != i || !slot.UsesPhysicalPanelPlacement)
                throw new InvalidOperationException($"Main deck slot {i} is not connected to the physical main-section workflow.");
        }
        foreach (BridgeComponent girder in main.MainGirders)
        {
            if (girder == null || girder.name.IndexOf("WoodenMainGirder", StringComparison.Ordinal) < 0
                || !girder.transform.IsChildOf(FindTransform(scene, "TutorialGameplay/Bridge")))
                throw new InvalidOperationException("A main girder reference points outside the real TutorialTerrainStage02 bridge girders.");
        }
        MountingNailSceneTargetIdentity[] mainGirderIdentities = main.MainGirders
            .Select(girder => girder.GetComponent<MountingNailSceneTargetIdentity>()).ToArray();
        if (mainGirderIdentities.Any(identity => identity == null || !identity.HasValidIdentity)
            || mainGirderIdentities.Select(identity => identity.StableTargetId).Distinct().Count() != mainGirderIdentities.Length)
            throw new InvalidOperationException("Both main girders require distinct baked scene target IDs for networked nail resolution.");

        BridgeComponentSO panelAsset = AssetDatabase.LoadAssetAtPath<BridgeComponentSO>(DeckPanelComponentPath);
        MountableBridgeComponentSO product = AssetDatabase.LoadAssetAtPath<MountableBridgeComponentSO>(
            "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenDeckPanelMountable.asset");
        GameObject productPrefab = product != null ? product.inGameGameObjectPrefab : null;
        BridgeDeckPanelMounting marker = productPrefab != null ? productPrefab.GetComponent<BridgeDeckPanelMounting>() : null;
        MountableBridgeComponent mountable = productPrefab != null ? productPrefab.GetComponent<MountableBridgeComponent>() : null;
        UnityEditor.SerializedObject serializedMountable = mountable != null ? new UnityEditor.SerializedObject(mountable) : null;
        UnityEngine.Object linkedProduct = serializedMountable != null
            ? serializedMountable.FindProperty("mountableBridgeComponentSO").objectReferenceValue : null;
        if (panelAsset == null || panelAsset.deckPanelConstructionWorkflow == null
            || !panelAsset.deckPanelConstructionWorkflow.UsesPhysicalPanelPlacement
            || product == null || product.bridgeComponentSO != panelAsset || productPrefab == null
            || marker == null || !marker.IsPhysicalPanel || mountable == null || linkedProduct != product)
            throw new InvalidOperationException("The authored Wooden Deck Panel product prefab is missing its physical-panel identity.");
        return $"Validated 3 main slots, 2 uniquely identified main girders, main-section panel state, physical Wooden Deck Panel product ({productPrefab.name}).";
    }

    private static ConstructionBookCatalogSO UpdateCatalog(string path, BridgeComponentSO panel)
    {
        ConstructionBookCatalogSO catalog = AssetDatabase.LoadAssetAtPath<ConstructionBookCatalogSO>(path);
        if (catalog == null) throw new InvalidOperationException($"Construction book catalog is missing: {path}");
        SerializedObject serializedCatalog = new SerializedObject(catalog);
        SerializedProperty entries = serializedCatalog.FindProperty("entries");
        SerializedProperty panelEntry = null;
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty candidate = entries.GetArrayElementAtIndex(i);
            if (candidate.FindPropertyRelative("component").objectReferenceValue == panel)
            {
                panelEntry = candidate;
                break;
            }
        }
        if (panelEntry == null) throw new InvalidOperationException($"Deck panel entry is missing in {path}");

        Sprite hammerSprite = FindRequirementSprite(entries, "Industrial Hammer");
        if (hammerSprite == null) hammerSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        SerializedProperty steps = panelEntry.FindPropertyRelative("steps");
        string[] instructions =
        {
            "Deliver and mount the deck panel.",
            "Use F / E to position the panel by hand over its marked slot on the main girders.",
            "Keep the panel level and correctly oriented within the marked mounting area.",
            "Place a separate Mounting Nail through the panel into a main girder in each of the four quadrants, then strike each nail three times with the Industrial Hammer."
        };
        steps.arraySize = instructions.Length;
        for (int i = 0; i < instructions.Length; i++)
        {
            SerializedProperty step = steps.GetArrayElementAtIndex(i);
            step.FindPropertyRelative("instruction").stringValue = instructions[i];
            SerializedProperty requirements = step.FindPropertyRelative("requirements");
            bool needsHammer = i == instructions.Length - 1;
            requirements.arraySize = needsHammer ? 1 : 0;
            if (needsHammer)
            {
                SerializedProperty requirement = requirements.GetArrayElementAtIndex(0);
                requirement.FindPropertyRelative("displayName").stringValue = "Industrial Hammer";
                requirement.FindPropertyRelative("sprite").objectReferenceValue = hammerSprite;
            }
        }
        serializedCatalog.ApplyModifiedProperties();
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    private static Sprite FindRequirementSprite(SerializedProperty entries, string displayName)
    {
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty steps = entries.GetArrayElementAtIndex(i).FindPropertyRelative("steps");
            for (int s = 0; s < steps.arraySize; s++)
            {
                SerializedProperty requirements = steps.GetArrayElementAtIndex(s).FindPropertyRelative("requirements");
                for (int r = 0; r < requirements.arraySize; r++)
                {
                    SerializedProperty requirement = requirements.GetArrayElementAtIndex(r);
                    if (requirement.FindPropertyRelative("displayName").stringValue == displayName)
                        return requirement.FindPropertyRelative("sprite").objectReferenceValue as Sprite;
                }
            }
        }
        return null;
    }

    private static Transform FindTransform(Scene scene, string relativePath)
    {
        string[] parts = relativePath.Split('/');
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform current = root.transform;
            for (int i = 1; i < parts.Length && current != null; i++) current = current.Find(parts[i]);
            if (current != null) return current;
        }
        throw new InvalidOperationException($"Could not find scene hierarchy path {relativePath}.");
    }
}
