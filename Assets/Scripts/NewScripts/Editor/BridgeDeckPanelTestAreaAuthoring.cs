#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BridgeDeckPanelTestAreaAuthoring
{
    private const string ScenePath = "Assets/Tests/Fixtures/MountingNailTestScene.unity";
    private const string SectionPrefabPath = "Assets/Prefabs/New/BridgeComponents/BridgeDeckSection.prefab";
    private const string GirderPrefabPath = "Assets/Prefabs/New/BridgeComponents/WoodenMainGirderBridgeComponent.prefab";
    private const string PanelPrefabPath = "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab";
    private const string NailPrefabPath = "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab";
    private const string GirderDefinitionPath = "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenMainGirder.asset";
    private const string BakeMenu = "Tools/RageQuitting/Mounting Nails/Bake Selected Scene Target IDs";
    private const int AddedNailCount = 36;

    [MenuItem("Tools/RageQuitting/Bridge Deck Panels/Create Stage 1 Test Fixture")]
    public static void CreateOrUpdate()
    {
        Scene previousActive = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Could not open the mounting nail test fixture scene.");

        try
        {
            GameObject area = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "NailTestArea");
            if (area == null) throw new InvalidOperationException("NailTestArea root was not found in the mounting nail test fixture scene.");
            BridgeDeckSection section = area.GetComponentsInChildren<BridgeDeckSection>(true)
                .FirstOrDefault(x => x.TryGetComponent(out BridgeDeckPanelMounting fixtureMarker)
                                     && fixtureMarker.IsStage1TestFixture);
            int oldChildCount = area.transform.childCount;
            int oldNailCount = area.GetComponentsInChildren<MountingNail>(true).Length;
            Dictionary<MountingNailSceneTargetIdentity, string> oldIds = scene.GetRootGameObjects()
                .SelectMany(x => x.GetComponentsInChildren<MountingNailSceneTargetIdentity>(true))
                .ToDictionary(x => x, x => x.StableTargetId);

            GameObject fixtureRoot;
            if (section == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SectionPrefabPath);
                if (prefab == null) throw new InvalidOperationException("BridgeDeckSection prefab was not found.");
                fixtureRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                fixtureRoot.transform.SetParent(area.transform, false);
                fixtureRoot.transform.localPosition = new Vector3(0f, 0f, 5.2f);
                section = fixtureRoot.GetComponent<BridgeDeckSection>();
                BridgeDeckPanelMounting marker = fixtureRoot.AddComponent<BridgeDeckPanelMounting>();
                SetMarkerRole(marker, 2);
            }
            else fixtureRoot = section.gameObject;

            if (section == null) throw new InvalidOperationException("Fixture section component is missing.");
            fixtureRoot.transform.localPosition = new Vector3(0f, 0f, 5.2f);
            SerializedObject sectionData = new(section);
            sectionData.FindProperty("sectionLength").floatValue = 6.56f;
            sectionData.FindProperty("panelGap").floatValue = 0.04f;
            sectionData.FindProperty("nominalPanelLength").floatValue = 2.16f;

            BridgeDeckPanelConstructionSite[] sites = fixtureRoot.GetComponentsInChildren<BridgeDeckPanelConstructionSite>(true);
            if (sites.Length < 3) throw new InvalidOperationException("Section prefab has fewer than three deck slots.");
            BridgeDeckPanelConstructionSite[] fixtureSites = sites.Take(3).ToArray();
            SerializedProperty slots = sectionData.FindProperty("panelSlots");
            slots.arraySize = fixtureSites.Length;
            for (int i = 0; i < fixtureSites.Length; i++)
            {
                slots.GetArrayElementAtIndex(i).objectReferenceValue = fixtureSites[i];
                Transform layoutRoot = (Transform)new SerializedObject(fixtureSites[i])
                    .FindProperty("layoutRoot").objectReferenceValue;
                if (layoutRoot != null) layoutRoot.localScale = Vector3.one;
                BridgeDeckPanelMounting marker = fixtureSites[i].GetComponent<BridgeDeckPanelMounting>();
                if (marker == null) marker = fixtureSites[i].gameObject.AddComponent<BridgeDeckPanelMounting>();
                SetMarkerRole(marker, 1);
                fixtureSites[i].transform.localPosition = new Vector3((i - 1) * 2.2f, 0.275f, 0f);
                BridgeComponent slotComponent = fixtureSites[i].GetComponent<BridgeComponent>();
                SetBridgeComponent(slotComponent, -200 - i, false, false, false);
                BridgeComponent slotBridge = fixtureSites[i].GetComponent<BridgeComponent>();
                GameObject ghostObject = slotBridge != null
                    ? new SerializedObject(slotBridge).FindProperty("readyForMountingVisualsGameObject").objectReferenceValue as GameObject
                    : null;
                if (ghostObject != null)
                {
                    ghostObject.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                    ghostObject.transform.localScale = new Vector3(2.16f, 0.2f, 2.4f);
                    foreach (Collider collider in ghostObject.GetComponentsInChildren<Collider>(true))
                    {
                        collider.enabled = false;
                        collider.isTrigger = true;
                    }
                }
            }

            foreach (BridgeDeckPanelConstructionSite unused in sites.Skip(3))
                if (unused != null) Undo.DestroyObjectImmediate(unused.gameObject);

            GameObject girderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GirderPrefabPath);
            GameObject panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            GameObject nailPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NailPrefabPath);
            BridgeComponentSO girderDefinition = AssetDatabase.LoadAssetAtPath<BridgeComponentSO>(GirderDefinitionPath);
            if (girderPrefab == null || panelPrefab == null || nailPrefab == null || girderDefinition == null)
                throw new InvalidOperationException("A girder, panel, nail prefab, or girder definition is missing.");

            BridgeComponent[] girders = new BridgeComponent[2];
            for (int i = 0; i < girders.Length; i++)
            {
                Transform existing = fixtureRoot.transform.Find("Girder_" + i);
                GameObject girder = existing != null ? existing.gameObject
                    : (GameObject)PrefabUtility.InstantiatePrefab(girderPrefab, scene);
                girder.name = "Girder_" + i;
                girder.transform.SetParent(fixtureRoot.transform, false);
                girder.transform.localPosition = new Vector3(0f, 0f, i == 0 ? -0.975f : 0.975f);
                girder.transform.localRotation = Quaternion.identity;
                girder.transform.localScale = new Vector3(8f / 14f, 1f, 1f);
                BridgeComponent component = girder.GetComponent<BridgeComponent>();
                SetBridgeComponent(component, -100 - i, true, true, true);
                SerializedObject girderData = new(component);
                girderData.FindProperty("bridgeComponentSO").objectReferenceValue = girderDefinition;
                girderData.ApplyModifiedPropertiesWithoutUndo();
                if (girder.GetComponent<MountingNailSceneTargetIdentity>() == null)
                    girder.AddComponent<MountingNailSceneTargetIdentity>();
                girders[i] = component;
            }
            sectionData.FindProperty("mainGirders").arraySize = girders.Length;
            for (int i = 0; i < girders.Length; i++)
                sectionData.FindProperty("mainGirders").GetArrayElementAtIndex(i).objectReferenceValue = girders[i];
            sectionData.ApplyModifiedPropertiesWithoutUndo();

            BridgeDeckPanelMounting[] existingPanels = fixtureRoot.GetComponentsInChildren<BridgeDeckPanelMounting>(true)
                .Where(x => x.IsPhysicalPanel && x.transform.parent == fixtureRoot.transform).ToArray();
            for (int i = 3; i < existingPanels.Length; i++)
                Undo.DestroyObjectImmediate(existingPanels[i].gameObject);
            existingPanels = existingPanels.Take(3).ToArray();
            for (int i = 0; i < existingPanels.Length; i++)
            {
                existingPanels[i].transform.localPosition = new Vector3((i - 1) * 2.9f, 0f, 3.6f);
                existingPanels[i].transform.localRotation = Quaternion.identity;
            }
            for (int i = existingPanels.Length; i < 3; i++)
            {
                GameObject panel = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, scene);
                panel.transform.SetParent(fixtureRoot.transform, false);
                panel.transform.localPosition = new Vector3((i - 1) * 2.9f, 0f, 3.6f);
                panel.transform.localRotation = Quaternion.identity;
            }

            int currentNails = fixtureRoot.GetComponentsInChildren<MountingNail>(true).Length;
            for (int i = currentNails; i < AddedNailCount; i++)
            {
                GameObject nail = (GameObject)PrefabUtility.InstantiatePrefab(nailPrefab, scene);
                nail.name = "FixtureNail_" + i.ToString("00");
                nail.transform.SetParent(fixtureRoot.transform, false);
                int row = i / 12;
                int column = i % 12;
                nail.transform.localPosition = new Vector3(-3.3f + column * 0.6f, 0.04f, 6.6f + row * 0.16f);
                nail.transform.localRotation = Quaternion.identity;
            }

            if (fixtureRoot.GetComponentsInChildren<MountingNail>(true).Length != AddedNailCount)
                throw new InvalidOperationException("Fixture must contain exactly 36 added SingleMountingNail instances.");
            int expectedTotalNails = oldNailCount + (section.transform.parent == area.transform && oldChildCount == area.transform.childCount ? 0 : AddedNailCount);
            if (area.GetComponentsInChildren<MountingNail>(true).Length != expectedTotalNails)
                throw new InvalidOperationException("Existing nail count changed while creating the fixture.");
            if (area.transform.childCount != oldChildCount + (oldChildCount == area.transform.childCount ? 0 : 1))
                throw new InvalidOperationException("Existing NailTestArea children changed outside the fixture root.");

            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.objects = girders.Select(x => x.gameObject).ToArray();
            if (!EditorApplication.ExecuteMenuItem(BakeMenu))
                throw new InvalidOperationException("Could not invoke the existing selected-target ID baker.");
            EditorSceneManager.SaveScene(scene);
            foreach (KeyValuePair<MountingNailSceneTargetIdentity, string> old in oldIds)
                if (old.Key != null && old.Key.StableTargetId != old.Value)
                    throw new InvalidOperationException("An existing Stage02 mounting target ID changed during fixture baking.");

            ValidateSavedPreview(oldIds, expectedTotalNails);

            SceneManager.SetActiveScene(previousActive);
            Debug.Log($"Stage1 physical deck fixture ready: {fixtureSites.Length} passive slots, 2 girder refs, 3 loose panels, 36 added nails; existing NailTestArea children={oldChildCount}, nails={oldNailCount} preserved. Section center={fixtureRoot.transform.position}; clear east approach={fixtureRoot.transform.position + Vector3.right * 5.2f}; supply panels center Y={fixtureRoot.transform.position.y}; girder top / panel bottom Y={fixtureRoot.transform.position.y + 0.275f:F3}.");
        }
        finally
        {
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            // Keep the authored scene loaded. Closing it with removeScene=true could discard
            // unrelated editor changes if this menu was invoked while another tool dirtied it.
        }
    }

    private static void ValidateSavedPreview(Dictionary<MountingNailSceneTargetIdentity, string> oldIds, int expectedAreaNailCount)
    {
        Scene preview = EditorSceneManager.OpenPreviewScene(ScenePath);
        try
        {
            GameObject area = preview.GetRootGameObjects().FirstOrDefault(x => x.name == "NailTestArea");
            if (area == null) throw new InvalidOperationException("Saved Stage02 preview is missing NailTestArea.");
            BridgeDeckPanelMounting fixture = preview.GetRootGameObjects()
                .SelectMany(x => x.GetComponentsInChildren<BridgeDeckPanelMounting>(true))
                .FirstOrDefault(x => x.IsStage1TestFixture);
            if (fixture == null) throw new InvalidOperationException("Saved Stage02 preview is missing the Stage1 fixture marker.");
            BridgeDeckSection section = fixture.GetComponent<BridgeDeckSection>();
            if (section == null || section.PanelSlots.Count != 3 || section.MainGirders.Count != 2)
                throw new InvalidOperationException("Saved Stage02 preview does not retain exactly 3 slots and 2 explicit girder references.");
            BridgeDeckPanelMounting[] panels = fixture.GetComponentsInChildren<BridgeDeckPanelMounting>(true)
                .Where(x => x.IsPhysicalPanel && x.transform.parent == fixture.transform).ToArray();
            if (panels.Length != 3) throw new InvalidOperationException($"Saved Stage02 preview contains {panels.Length} physical panels instead of 3.");
            if (fixture.GetComponentsInChildren<MountingNail>(true).Length != AddedNailCount)
                throw new InvalidOperationException("Saved Stage02 preview does not contain exactly 36 fixture nails.");
            int areaNails = area.GetComponentsInChildren<MountingNail>(true).Length;
            if (areaNails != expectedAreaNailCount)
                throw new InvalidOperationException($"Saved Stage02 preview NailTestArea has {areaNails} nails; expected {expectedAreaNailCount}.");
            foreach (KeyValuePair<MountingNailSceneTargetIdentity, string> old in oldIds)
            {
                MountingNailSceneTargetIdentity persisted = preview.GetRootGameObjects()
                    .SelectMany(x => x.GetComponentsInChildren<MountingNailSceneTargetIdentity>(true))
                    .FirstOrDefault(x => x.StableTargetId == old.Value);
                if (persisted == null) throw new InvalidOperationException("A pre-existing Stage02 static target ID is absent from the saved preview: " + old.Value);
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void SetMarkerRole(BridgeDeckPanelMounting marker, int roleIndex)
    {
        SerializedObject data = new(marker);
        data.FindProperty("role").enumValueIndex = roleIndex;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBridgeComponent(BridgeComponent component, int componentId,
        bool mounted, bool assembled, bool fullyCompleted)
    {
        SerializedObject data = new(component);
        data.FindProperty("componentID").intValue = componentId;
        data.FindProperty("isMounted").boolValue = mounted;
        data.FindProperty("isAssembled").boolValue = assembled;
        data.FindProperty("startFullyCompleted").boolValue = fullyCompleted;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
