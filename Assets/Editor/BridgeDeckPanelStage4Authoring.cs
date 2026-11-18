using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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
        if (serializedManager.FindProperty("useBridgeGuidance").boolValue)
        {
            if (entries.arraySize > 0)
            {
                entries.arraySize = 0;
                serializedManager.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return;
        }
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

    /// <summary>Authors only the explicitly supplied production scene and its private short catalog.</summary>
    public static string AuthorTutorialGuidance(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.path != TutorialScenePath)
            throw new InvalidOperationException("Authoring requires the loaded production Stage02 scene.");
        if (scene.isDirty) throw new InvalidOperationException("Refusing to overwrite an already dirty Stage02 scene.");
        BridgeStageInfoManager manager = SceneComponents<BridgeStageInfoManager>(scene).Single();
        ConstructionBookController book = SceneComponents<ConstructionBookController>(scene).Single();
        ConstructionBookCatalogSO catalog = AssetDatabase.LoadAssetAtPath<ConstructionBookCatalogSO>(ShortCatalogPath);
        if (catalog == null || new SerializedObject(book).FindProperty("catalog").objectReferenceValue != catalog)
            throw new InvalidOperationException("The Stage02 book must already reference its own short catalog.");
        BridgeComponent[] components = GuidanceComponents(scene);
        ValidateGuidanceGeometry(scene, components);
        Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        foreach (ConstructionBookCatalogSO.Entry entry in catalog.Entries)
        {
            if (entry.steps == null) continue;
            foreach (ConstructionBookCatalogSO.Step step in entry.steps)
            {
                if (step == null || step.requirements == null) continue;
                foreach (ConstructionBookCatalogSO.Requirement requirement in step.requirements)
                    if (requirement != null && !string.IsNullOrEmpty(requirement.displayName) && requirement.sprite != null)
                        sprites[requirement.displayName] = requirement.sprite;
            }
        }
        EquippableItemSO crowbar = AssetDatabase.LoadAssetAtPath<EquippableItemSO>("Assets/ScriptableObjectAssets/New/Crowbar.asset");
        EquippableItemSO pickaxe = AssetDatabase.LoadAssetAtPath<EquippableItemSO>("Assets/ScriptableObjectAssets/New/Pickaxe.asset");
        BaseResourceSO nail = AssetDatabase.LoadAssetAtPath<BaseResourceSO>("Assets/ScriptableObjectAssets/New/BaseResourceSO/MetalProducts/SingleMountingNail.asset");
        if (crowbar != null && crowbar.uiSprite != null) sprites["Crowbar"] = crowbar.uiSprite;
        if (pickaxe != null && pickaxe.uiSprite != null) sprites["Pickaxe"] = pickaxe.uiSprite;
        if (nail != null && nail.icon != null) sprites["Mounting Nail"] = nail.icon;
        SerializedObject serialized = new SerializedObject(catalog);
        SerializedProperty entries = serialized.FindProperty("entries");
        for (int e = 0; e < entries.arraySize; e++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(e);
            BridgeComponentSO definition = entry.FindPropertyRelative("component").objectReferenceValue as BridgeComponentSO;
            BridgeComponent representative = components.FirstOrDefault(component => component.GetBridgeComponentSO() == definition);
            if (representative == null) throw new InvalidOperationException("Catalog entry is outside the Stage02 bridge.");
            BridgeConstructionSite site = representative.GetComponent<BridgeConstructionSite>();
            List<string> keys = AuthoringKeys(definition, site);
            SerializedProperty steps = entry.FindPropertyRelative("steps");
            steps.arraySize = keys.Count;
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                BridgeGuidanceSteps.DefaultText(key, out string shortText, out string help, out string tools, out _);
                if (key.StartsWith("foundation.soften.", StringComparison.Ordinal) || key.StartsWith("foundation.collect.", StringComparison.Ordinal))
                {
                    int cycle = int.Parse(key.Substring(key.LastIndexOf('.') + 1));
                    shortText += $" (cycle {cycle}/{definition.constructionWorkflow.DiggingCycleCount})";
                }
                if (key == "foundation.concrete")
                {
                    BridgeGuidanceSteps.DefaultText("foundation.recover", out _, out string recovery, out _, out _);
                    help += " " + recovery;
                    tools = "Loaded Concrete Wheelbarrow / Pickaxe";
                }
                if (key.EndsWith(".mount", StringComparison.Ordinal))
                    tools = definition.componentName;
                if (key == "panel.place") tools = definition.componentName + " / Crowbar";
                if (key == "panel.nail") tools += " / Crowbar";
                SerializedProperty step = steps.GetArrayElementAtIndex(i);
                step.FindPropertyRelative("guidanceKey").stringValue = key;
                step.FindPropertyRelative("instruction").stringValue = shortText;
                step.FindPropertyRelative("detailedHelp").stringValue = help;
                SerializedProperty requirements = step.FindPropertyRelative("requirements");
                string[] names = string.IsNullOrEmpty(tools) ? Array.Empty<string>() : tools.Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(name => name != "Mounting Nail" || sprites.ContainsKey(name)).ToArray();
                requirements.arraySize = names.Length;
                for (int r = 0; r < names.Length; r++)
                {
                    string name = names[r];
                    Sprite icon = sprites.TryGetValue(name, out Sprite existing) ? existing : name == definition.componentName
                        ? entry.FindPropertyRelative("partSprite").objectReferenceValue as Sprite : null;
                    if (icon == null) throw new InvalidOperationException($"No existing requirement icon for '{name}'.");
                    SerializedProperty requirement = requirements.GetArrayElementAtIndex(r);
                    requirement.FindPropertyRelative("displayName").stringValue = name;
                    requirement.FindPropertyRelative("sprite").objectReferenceValue = icon;
                }
            }
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        SerializedObject stageInfo = new SerializedObject(manager);
        stageInfo.FindProperty("useBridgeGuidance").boolValue = true;
        stageInfo.FindProperty("entries").arraySize = 0;
        if (stageInfo.ApplyModifiedPropertiesWithoutUndo()) EditorSceneManager.MarkSceneDirty(scene);
        return ValidateTutorialGuidance(scene);
    }

    public static string ValidateTutorialGuidance(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.path != TutorialScenePath)
            throw new InvalidOperationException("Validation requires the loaded production Stage02 scene.");
        BridgeComponent[] components = GuidanceComponents(scene);
        ValidateGuidanceGeometry(scene, components);
        BridgeStageInfoManager manager = SceneComponents<BridgeStageInfoManager>(scene).Single();
        SerializedObject stageInfo = new SerializedObject(manager);
        if (!stageInfo.FindProperty("useBridgeGuidance").boolValue || stageInfo.FindProperty("entries").arraySize != 0)
            throw new InvalidOperationException("Stage02 must use guidance with no completion popups.");
        ConstructionBookCatalogSO catalog = AssetDatabase.LoadAssetAtPath<ConstructionBookCatalogSO>(ShortCatalogPath);
        if (catalog == null || new SerializedObject(SceneComponents<ConstructionBookController>(scene).Single()).FindProperty("catalog").objectReferenceValue != catalog)
            throw new InvalidOperationException("Stage02 must retain the private short catalog reference.");
        if (catalog.Entries.Count != 6) throw new InvalidOperationException("Stage02 requires six book workflows.");
        foreach (ConstructionBookCatalogSO.Entry entry in catalog.Entries)
        {
            BridgeComponent component = components.First(item => item.GetBridgeComponentSO() == entry.component);
            List<string> expected = AuthoringKeys(entry.component, component.GetComponent<BridgeConstructionSite>());
            if (!entry.steps.Select(step => step.guidanceKey).SequenceEqual(expected))
                throw new InvalidOperationException("Catalog keys do not match the configured workflow: " + entry.title);
            foreach (ConstructionBookCatalogSO.Step step in entry.steps)
                if (string.IsNullOrWhiteSpace(step.instruction) || string.IsNullOrWhiteSpace(step.detailedHelp))
                    throw new InvalidOperationException("A tutorial step lacks English short/detailed help.");
        }
        string[] issues = catalog.ValidateCatalog().ToArray();
        if (issues.Length > 0) throw new InvalidOperationException(string.Join("; ", issues));
        return "Validated Stage02 guidance: 6 stages, 13 parts (2/2/2/2/2/3), 10 fixed-part sockets, private keyed catalog, no completion popups.";
    }

    private static List<string> AuthoringKeys(BridgeComponentSO definition, BridgeConstructionSite site)
    {
        List<string> keys = new List<string>();
        int cycles = definition.constructionWorkflow != null ? definition.constructionWorkflow.DiggingCycleCount : 3;
        BridgeGuidanceSteps.CopyAuthoringKeys(BridgeGuidanceSteps.Kind(site), cycles, keys);
        return keys;
    }

    private static BridgeComponent[] GuidanceComponents(Scene scene)
    {
        GameplayManager gameplay = SceneComponents<GameplayManager>(scene).Single();
        Bridge bridge = new SerializedObject(gameplay).FindProperty("bridge").objectReferenceValue as Bridge;
        if (bridge == null) throw new InvalidOperationException("Stage02 gameplay has no explicit bridge.");
        return bridge.GetComponentsInChildren<BridgeComponent>(true);
    }

    private static void ValidateGuidanceGeometry(Scene scene, BridgeComponent[] components)
    {
        GameplayManager gameplay = SceneComponents<GameplayManager>(scene).Single();
        SerializedObject serialized = new SerializedObject(gameplay);
        SerializedProperty stages = serialized.FindProperty("bridgeBuildingStages");
        if (components.Length != 13 || stages.arraySize != 6)
            throw new InvalidOperationException("Stage02 bridge must retain its 13 parts and 6 stages.");
        HashSet<int> seen = new HashSet<int>();
        for (int s = 0; s < stages.arraySize; s++)
        {
            SerializedProperty indexes = stages.GetArrayElementAtIndex(s).FindPropertyRelative("bridgeComponentDataIndexes");
            if (indexes.arraySize != (s == 5 ? 3 : 2)) throw new InvalidOperationException("Unexpected bridge stage count.");
            for (int i = 0; i < indexes.arraySize; i++)
                if (!seen.Add(indexes.GetArrayElementAtIndex(i).intValue)) throw new InvalidOperationException("Duplicate stage component.");
        }
        foreach (BridgeComponent component in components)
        {
            BridgeConstructionSite site = component.GetComponent<BridgeConstructionSite>();
            if (site == null || component.GetBridgeComponentSO() == null || !seen.Contains(component.ComponentID))
                throw new InvalidOperationException("Stage02 component is missing its workflow or stage identity.");
            if (site is not BridgeDeckPanelConstructionSite && component.GetComponent<BridgeMountSocket>() == null)
                throw new InvalidOperationException("A fixed bridge part lacks its existing mounting socket.");
        }
    }

    private static IEnumerable<T> SceneComponents<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true));

    public static string AuthorSavedTutorialGuidance()
    {
        Scene original = SceneManager.GetActiveScene();
        int originalCount = SceneManager.sceneCount;
        Dictionary<int, bool> dirtyStates = new Dictionary<int, bool>();
        for (int i = 0; i < originalCount; i++)
        {
            Scene open = SceneManager.GetSceneAt(i);
            dirtyStates.Add(open.handle, open.isDirty);
        }
        string originalDisk = string.IsNullOrEmpty(original.path) ? null : File.ReadAllText(original.path);
        string sharedDisk = File.ReadAllText(CatalogPath);
        Scene target = SceneManager.GetSceneByPath(TutorialScenePath);
        bool opened = !target.IsValid() || !target.isLoaded;
        try
        {
            if (opened) target = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Additive);
            if (target.isDirty) throw new InvalidOperationException("Refusing to author an already dirty Stage02 scene.");
            string summary = AuthorTutorialGuidance(target);
            ConstructionBookCatalogSO catalog = AssetDatabase.LoadAssetAtPath<ConstructionBookCatalogSO>(ShortCatalogPath);
            AssetDatabase.SaveAssetIfDirty(catalog);
            if (target.isDirty && !EditorSceneManager.SaveScene(target)) throw new InvalidOperationException("Stage02 scene save failed.");
            string sceneDisk = File.ReadAllText(TutorialScenePath);
            string catalogDisk = File.ReadAllText(ShortCatalogPath);
            AuthorTutorialGuidance(target);
            if (target.isDirty || EditorUtility.IsDirty(catalog) || sceneDisk != File.ReadAllText(TutorialScenePath) ||
                catalogDisk != File.ReadAllText(ShortCatalogPath)) throw new InvalidOperationException("Authoring rerun was not a no-op.");
            if (sharedDisk != File.ReadAllText(CatalogPath)) throw new InvalidOperationException("Shared catalog changed.");
            return summary + " " + ValidateOpenTutorialAuthoring() + " Idempotent rerun and shared-catalog preservation passed.";
        }
        finally
        {
            if (opened && target.IsValid() && target.isLoaded) EditorSceneManager.CloseScene(target, true);
            if (original.IsValid() && original.isLoaded && SceneManager.GetActiveScene() != original) SceneManager.SetActiveScene(original);
            foreach (KeyValuePair<int, bool> prior in dirtyStates)
            {
                Scene current = default;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).handle == prior.Key) current = SceneManager.GetSceneAt(i);
                if (!current.IsValid() || !current.isLoaded || current.isDirty != prior.Value)
                    throw new InvalidOperationException("An originally open scene or its unsaved state changed.");
            }
            if (SceneManager.sceneCount != originalCount || originalDisk != null && File.ReadAllText(original.path) != originalDisk)
                throw new InvalidOperationException("Original scene count or active-scene file changed.");
        }
    }

    private static ConstructionBookCatalogSO UpdateCatalog(string path, BridgeComponentSO panel)
    {
        ConstructionBookCatalogSO catalog = AssetDatabase.LoadAssetAtPath<ConstructionBookCatalogSO>(path);
        if (catalog == null) throw new InvalidOperationException($"Construction book catalog is missing: {path}");
        // The Stage02 keyed tutorial owns its own ordering; preserve legacy/shared book authoring.
        if (path == ShortCatalogPath && catalog.Entries.Any(entry => entry.steps != null &&
            entry.steps.Any(step => step != null && !string.IsNullOrEmpty(step.guidanceKey)))) return catalog;
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
