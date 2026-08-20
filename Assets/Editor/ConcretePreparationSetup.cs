using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ConcretePreparationSetup
{
    private const string SetupLabel = "ConcretePreparationV1Complete";
    private const string TutorialScenePath = "Assets/Scenes/Tutorial_scene.unity";
    private const string GeneratedRoot = "Assets/GeneratedAssets/ConcretePreparation";
    private const string ResourceAssetRoot = "Assets/ScriptableObjectAssets/New/BaseResourceSO";
    private const string ResourcePrefabRoot = "Assets/Prefabs/New/Resources/ConcretePreparation";
    private const string SubstanceRoot = "Assets/ScriptableObjectAssets/New/Substances";
    private const string MixerRoot = "Assets/Prefabs/New/Concrete";
    private const string FurnaceRecipeRoot = "Assets/ScriptableObjectAssets/New/ProductionRecipes/Furnace";
    private const string MixerProfilePath = GeneratedRoot + "/ConcreteMixerProfile.asset";
    private const string SetupAnchorPath = GeneratedRoot + "/ConcretePreparationSetupAnchor.asset";

    private const string IronVeinTemplate = "Assets/Prefabs/New/Resources/IronVeinResource_prefab.prefab";
    private const string CarryableTemplate = "Assets/Prefabs/New/Resources/IronNuggetResource_prefab.prefab";
    private const string BucketPrefabPath = "Assets/Prefabs/New/Substances/PortableBucket.prefab";
    private const string FurnacePrefabPath = "Assets/Prefabs/New/Resources/Factories/BlastFurnace_prefab.prefab";

    static ConcretePreparationSetup()
    {
        EditorApplication.delayCall += TryRunOnce;
    }

    [MenuItem("Tools/RageQuitting/Setup Concrete Preparation V1")]
    public static void RunFromMenu()
    {
        RunSetup(true);
    }

    public static void RunBatch()
    {
        RunSetup(true);
    }

    private static void TryRunOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryRunOnce;
            return;
        }

        ConcreteMixerProfileSO anchor = AssetDatabase.LoadAssetAtPath<ConcreteMixerProfileSO>(SetupAnchorPath);
        if (anchor != null && AssetDatabase.GetLabels(anchor).Contains(SetupLabel))
        {
            return;
        }

        RunSetup(false);
    }

    private static void RunSetup(bool force)
    {
        try
        {
            EnsureFolders();

            ContainerSubstanceSO soil = AssetDatabase.LoadAssetAtPath<ContainerSubstanceSO>(SubstanceRoot + "/Soil.asset");
            ContainerSubstanceSO water = CreateSubstance(SubstanceRoot + "/Water.asset", "Water", ContainerSubstanceKind.Water, new Color(0.18f, 0.52f, 0.82f));
            ContainerSubstanceSO gravel = CreateSubstance(SubstanceRoot + "/Gravel.asset", "Gravel", ContainerSubstanceKind.Gravel, new Color(0.38f, 0.36f, 0.32f));
            ContainerSubstanceSO concrete = CreateSubstance(SubstanceRoot + "/Concrete.asset", "Concrete", ContainerSubstanceKind.Concrete, new Color(0.42f, 0.44f, 0.43f));
            if (soil != null)
            {
                SetEnum(soil, "substanceKind", (int)ContainerSubstanceKind.Soil);
            }

            ConfigureBucket(soil, water, gravel, concrete);

            Material limestoneMaterial = CreateMaterial(GeneratedRoot + "/Limestone.mat", new Color(0.72f, 0.69f, 0.56f));
            Material clayMaterial = CreateMaterial(GeneratedRoot + "/Clay.mat", new Color(0.48f, 0.25f, 0.16f));
            Material cementMaterial = CreateMaterial(GeneratedRoot + "/CementBag.mat", new Color(0.72f, 0.68f, 0.57f));
            Material mixerMetal = CreateMaterial(GeneratedRoot + "/MixerMetal.mat", new Color(0.17f, 0.43f, 0.48f));
            Material mixerDark = CreateMaterial(GeneratedRoot + "/MixerDark.mat", new Color(0.11f, 0.13f, 0.14f));
            Material concreteMaterial = CreateMaterial(GeneratedRoot + "/ConcreteWet.mat", new Color(0.42f, 0.44f, 0.43f));
            Material gravelMaterial = CreateMaterial(GeneratedRoot + "/GravelPatch.mat", new Color(0.38f, 0.36f, 0.32f));

            BaseResourceSO limestoneStone = CreateCarryableResource(
                ResourceAssetRoot + "/LimestoneStone.asset",
                ResourcePrefabRoot + "/LimestoneStoneResource.prefab",
                "Limestone Stone",
                limestoneMaterial,
                false);
            BaseResourceSO clayLump = CreateCarryableResource(
                ResourceAssetRoot + "/ClayLump.asset",
                ResourcePrefabRoot + "/ClayLumpResource.prefab",
                "Clay Lump",
                clayMaterial,
                false);
            BaseResourceSO cementBag = CreateCarryableResource(
                ResourceAssetRoot + "/CementBag.asset",
                ResourcePrefabRoot + "/CementBagResource.prefab",
                "Cement Bag",
                cementMaterial,
                true);

            BaseResourceSO limestoneVein = CreateDepositResource(
                ResourceAssetRoot + "/LimestoneVein.asset",
                ResourcePrefabRoot + "/LimestoneVeinResource.prefab",
                "Limestone Vein",
                limestoneMaterial,
                limestoneStone,
                EquippableItemType.Pickaxe);
            BaseResourceSO clayDeposit = CreateDepositResource(
                ResourceAssetRoot + "/ClayDeposit.asset",
                ResourcePrefabRoot + "/ClayDepositResource.prefab",
                "Clay Deposit",
                clayMaterial,
                clayLump,
                EquippableItemType.Shovel);

            ProductionRecipeSO cementRecipe = CreateCementRecipe(limestoneStone, clayLump, cementBag);
            AddRecipeToFurnacePrefab(cementRecipe);

            ConcreteMixerProfileSO mixerProfile = GetOrCreateAsset<ConcreteMixerProfileSO>(MixerProfilePath);
            GameObject mixerPrefab = CreateMixerPrefab(mixerProfile, water, gravel, cementBag, mixerMetal, mixerDark, concreteMaterial);

            RegisterNetworkPrefabs(new[]
            {
                limestoneStone.resourcePrefab,
                clayLump.resourcePrefab,
                cementBag.resourcePrefab,
                limestoneVein.resourcePrefab,
                clayDeposit.resourcePrefab,
                mixerPrefab
            });

            ConfigureTutorialScene(
                mixerPrefab,
                water,
                gravel,
                limestoneVein,
                clayDeposit,
                limestoneMaterial,
                clayMaterial,
                gravelMaterial,
                cementRecipe);

            ConcreteMixerProfileSO anchor = GetOrCreateAsset<ConcreteMixerProfileSO>(SetupAnchorPath);
            AssetDatabase.SetLabels(anchor, new[] { SetupLabel });
            EditorUtility.SetDirty(anchor);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Concrete preparation V1 setup completed.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (force)
            {
                throw;
            }
        }
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "GeneratedAssets");
        EnsureFolder("Assets/GeneratedAssets", "ConcretePreparation");
        EnsureFolder("Assets/Prefabs/New/Resources", "ConcretePreparation");
        EnsureFolder("Assets/Prefabs/New", "Concrete");
        EnsureFolder("Assets/ScriptableObjectAssets/New", "Substances");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static T GetOrCreateAsset<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
        {
            return asset;
        }

        asset = ScriptableObject.CreateInstance<T>();
        asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static ContainerSubstanceSO CreateSubstance(string path, string displayName, ContainerSubstanceKind kind, Color displayColor)
    {
        ContainerSubstanceSO asset = GetOrCreateAsset<ContainerSubstanceSO>(path);
        SerializedObject serialized = new SerializedObject(asset);
        serialized.FindProperty("displayName").stringValue = displayName;
        serialized.FindProperty("substanceKind").enumValueIndex = (int)kind;
        serialized.FindProperty("displayColor").colorValue = displayColor;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static void SetEnum(UnityEngine.Object target, string propertyName, int value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.enumValueIndex = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }

    private static void ConfigureBucket(params ContainerSubstanceSO[] substances)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BucketPrefabPath);
        try
        {
            PortableSubstanceContainer bucket = root.GetComponent<PortableSubstanceContainer>();
            if (bucket == null) return;

            SerializedObject serialized = new SerializedObject(bucket);
            SerializedProperty supported = serialized.FindProperty("supportedSubstances");
            List<ContainerSubstanceSO> valid = substances.Where(item => item != null).Distinct().ToList();
            supported.arraySize = valid.Count;
            for (int i = 0; i < valid.Count; i++)
            {
                supported.GetArrayElementAtIndex(i).objectReferenceValue = valid[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, BucketPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Material CreateMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static BaseResourceSO CreateCarryableResource(
        string assetPath,
        string prefabPath,
        string displayName,
        Material material,
        bool makeBag)
    {
        BaseResourceSO resource = GetOrCreateAsset<BaseResourceSO>(assetPath);
        resource.resourceName = displayName;
        resource.canBeCarried = true;
        resource.minAmountOfPlayersNeeded = 1;
        resource.recommendedCarriers = 1;
        resource.maxCarriers = 1;
        resource.allowMultipleCarriers = false;
        resource.resourceDurability = 0f;
        resource.baseResourceDestructionRecipeArray = Array.Empty<BaseResourceDestructionRecipe>();

        CopyPrefabIfMissing(CarryableTemplate, prefabPath);
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            root.name = displayName.Replace(" ", string.Empty) + "Resource";
            BaseResourceNew component = root.GetComponent<BaseResourceNew>();
            SetObjectReference(component, "baseResourceSO", resource);
            ApplyMaterial(root, material);
            if (makeBag)
            {
                BuildCementBagVisual(root.transform, material);
            }
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        resource.resourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        EditorUtility.SetDirty(resource);
        return resource;
    }

    private static BaseResourceSO CreateDepositResource(
        string assetPath,
        string prefabPath,
        string displayName,
        Material material,
        BaseResourceSO output,
        EquippableItemType tool)
    {
        BaseResourceSO resource = GetOrCreateAsset<BaseResourceSO>(assetPath);
        resource.resourceName = displayName;
        resource.canBeCarried = false;
        resource.resourceDurability = tool == EquippableItemType.Pickaxe ? 50f : 15f;
        resource.minAmountOfPlayersNeeded = 0;
        resource.allowMultipleCarriers = false;
        resource.baseResourceDestructionRecipeArray = new[]
        {
            new BaseResourceDestructionRecipe
            {
                finalProductBaseResourceSO = output,
                neededEquippableItemType = tool,
                products = new[] { new BaseResourceDestructionProduct { resourceSO = output, amount = 1 } },
                spawnOffsets = Array.Empty<Vector3>(),
                fallbackScatterRadius = 0.35f
            }
        };

        CopyPrefabIfMissing(IronVeinTemplate, prefabPath);
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            root.name = displayName.Replace(" ", string.Empty) + "Resource";
            BaseResourceNew component = root.GetComponent<BaseResourceNew>();
            SetObjectReference(component, "baseResourceSO", resource);
            ApplyMaterial(root, material);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        resource.resourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        EditorUtility.SetDirty(resource);
        return resource;
    }

    private static void CopyPrefabIfMissing(string source, string destination)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(destination) == null && !AssetDatabase.CopyAsset(source, destination))
        {
            throw new InvalidOperationException("Could not create prefab " + destination);
        }
    }

    private static void ApplyMaterial(GameObject root, Material material)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
        }
    }

    private static void BuildCementBagVisual(Transform root, Material material)
    {
        Transform previous = root.Find("ConcretePreparationBagVisual");
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        GameObject visual = new GameObject("ConcretePreparationBagVisual");
        visual.transform.SetParent(root, false);
        CreatePrimitive(visual.transform, PrimitiveType.Cube, "Bag", new Vector3(0f, 0.35f, 0f), new Vector3(0.62f, 0.72f, 0.3f), Vector3.zero, material, false);
        CreatePrimitive(visual.transform, PrimitiveType.Cube, "Fold", new Vector3(0f, 0.72f, 0f), new Vector3(0.46f, 0.08f, 0.25f), new Vector3(0f, 0f, 4f), material, false);
    }

    private static ProductionRecipeSO CreateCementRecipe(BaseResourceSO limestone, BaseResourceSO clay, BaseResourceSO cement)
    {
        string path = FurnaceRecipeRoot + "/Furnace_CementBag.asset";
        ProductionRecipeSO recipe = GetOrCreateAsset<ProductionRecipeSO>(path);
        SerializedObject serialized = new SerializedObject(recipe);
        serialized.FindProperty("recipeName").stringValue = "Cement Bag";
        SerializedProperty ingredients = serialized.FindProperty("requiredResources");
        ingredients.arraySize = 2;
        SetRequiredResource(ingredients.GetArrayElementAtIndex(0), limestone, 2);
        SetRequiredResource(ingredients.GetArrayElementAtIndex(1), clay, 2);
        serialized.FindProperty("productType").enumValueIndex = (int)FactoryProductType.BaseResource;
        serialized.FindProperty("baseResourceOutput").objectReferenceValue = cement;
        serialized.FindProperty("outputAmount").intValue = 1;
        serialized.FindProperty("meltingPoint").floatValue = 700f;
        serialized.FindProperty("combustionTemperature").floatValue = 900f;
        serialized.FindProperty("neededProgress").floatValue = 450f;
        serialized.FindProperty("neededCombustionProgress").floatValue = 900f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(recipe);
        return recipe;
    }

    private static void SetRequiredResource(SerializedProperty property, BaseResourceSO resource, int amount)
    {
        property.FindPropertyRelative("resourceType").objectReferenceValue = resource;
        property.FindPropertyRelative("amount").intValue = amount;
    }

    private static void AddRecipeToFurnacePrefab(ProductionRecipeSO recipe)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(FurnacePrefabPath);
        try
        {
            BlastFurnaceFactory furnace = root.GetComponentInChildren<BlastFurnaceFactory>(true);
            if (furnace != null)
            {
                AddObjectToArray(furnace, "productionRecipeSOArray", recipe);
                PrefabUtility.SaveAsPrefabAsset(root, FurnacePrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static GameObject CreateMixerPrefab(
        ConcreteMixerProfileSO profile,
        ContainerSubstanceSO water,
        ContainerSubstanceSO gravel,
        BaseResourceSO cement,
        Material metal,
        Material dark,
        Material wetConcrete)
    {
        string path = MixerRoot + "/ConcreteMixer.prefab";
        GameObject root = new GameObject("ConcreteMixer");
        try
        {
            root.AddComponent<NetworkObject>();
            ConcreteMixerController mixer = root.AddComponent<ConcreteMixerController>();
            ConcreteMixerCrankUI ui = root.AddComponent<ConcreteMixerCrankUI>();

            CreatePrimitive(root.transform, PrimitiveType.Cube, "Base", new Vector3(0f, 0.12f, 0f), new Vector3(2.7f, 0.24f, 1.65f), Vector3.zero, dark, true);
            CreatePrimitive(root.transform, PrimitiveType.Cube, "LeftFrame", new Vector3(-0.95f, 0.9f, 0f), new Vector3(0.16f, 1.65f, 0.16f), new Vector3(0f, 0f, -12f), metal, true);
            CreatePrimitive(root.transform, PrimitiveType.Cube, "RightFrame", new Vector3(0.95f, 0.9f, 0f), new Vector3(0.16f, 1.65f, 0.16f), new Vector3(0f, 0f, 12f), metal, true);

            GameObject pivotObject = new GameObject("DrumPivot");
            pivotObject.transform.SetParent(root.transform, false);
            pivotObject.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            pivotObject.transform.localEulerAngles = new Vector3(0f, 0f, 55f);
            Transform drumPivot = pivotObject.transform;
            GameObject drumSpinObject = new GameObject("DrumSpinVisual");
            drumSpinObject.transform.SetParent(drumPivot, false);
            Transform drumSpinVisual = drumSpinObject.transform;
            CreatePrimitive(drumSpinVisual, PrimitiveType.Cylinder, "Drum", Vector3.zero, new Vector3(0.92f, 0.9f, 0.92f), new Vector3(0f, 0f, 90f), metal, false);
            CreatePrimitive(drumSpinVisual, PrimitiveType.Cylinder, "Opening", new Vector3(0.83f, 0f, 0f), new Vector3(0.62f, 0.08f, 0.62f), new Vector3(0f, 0f, 90f), dark, false);

            GameObject loadingPoint = new GameObject("LoadingPoint");
            loadingPoint.transform.SetParent(root.transform, false);
            loadingPoint.transform.localPosition = new Vector3(1.35f, 1.4f, 0f);

            GameObject crankObject = CreatePrimitive(root.transform, PrimitiveType.Cylinder, "Crank", new Vector3(0f, 1.25f, -1.05f), new Vector3(0.12f, 0.42f, 0.12f), new Vector3(90f, 0f, 0f), dark, true);
            ConcreteMixerCrank crank = crankObject.AddComponent<ConcreteMixerCrank>();

            GameObject leverObject = CreatePrimitive(root.transform, PrimitiveType.Cube, "ModeLever", new Vector3(-1.25f, 1.05f, 0f), new Vector3(0.14f, 0.65f, 0.14f), new Vector3(0f, 0f, -35f), dark, true);
            ConcreteMixerModeLever lever = leverObject.AddComponent<ConcreteMixerModeLever>();

            GameObject spill = CreatePrimitive(root.transform, PrimitiveType.Cylinder, "SpillVisual", new Vector3(1.4f, 0.05f, 0f), new Vector3(0.9f, 0.03f, 0.72f), Vector3.zero, wetConcrete, false);
            spill.SetActive(false);

            SetObjectReference(mixer, "profile", profile);
            SetObjectReference(mixer, "waterSubstance", water);
            SetObjectReference(mixer, "gravelSubstance", gravel);
            SetObjectReference(mixer, "cementBagResource", cement);
            SetObjectReference(mixer, "crankInteractionPoint", crankObject.transform);
            SetObjectReference(mixer, "loadingInteractionPoint", loadingPoint.transform);
            SetObjectReference(mixer, "drumPivot", drumPivot);
            SetObjectReference(mixer, "drumSpinVisual", drumSpinVisual);
            SetObjectReference(mixer, "spillVisual", spill);
            SetObjectReference(crank, "mixer", mixer);
            SetObjectReference(lever, "mixer", mixer);
            SetObjectReference(lever, "leverVisual", leverObject.transform);
            SetObjectReference(ui, "mixer", mixer);

            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static GameObject CreatePrimitive(
        Transform parent,
        PrimitiveType type,
        string name,
        Vector3 localPosition,
        Vector3 localScale,
        Vector3 localEuler,
        Material material,
        bool keepCollider)
    {
        GameObject child = GameObject.CreatePrimitive(type);
        child.name = name;
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        child.transform.localScale = localScale;
        child.transform.localEulerAngles = localEuler;
        Renderer renderer = child.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = material;
        if (!keepCollider)
        {
            Collider collider = child.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        }
        return child;
    }

    private static void RegisterNetworkPrefabs(IEnumerable<GameObject> prefabs)
    {
        string[] listPaths =
        {
            "Assets/DefaultNetworkPrefabs.asset",
            "Assets/NGO_Minimal_Setup/NetworkPrefabsList.asset"
        };

        foreach (string listPath in listPaths)
        {
            UnityEngine.Object listAsset = AssetDatabase.LoadMainAssetAtPath(listPath);
            if (listAsset == null) continue;
            SerializedObject serialized = new SerializedObject(listAsset);
            SerializedProperty list = serialized.FindProperty("List");
            foreach (GameObject prefab in prefabs.Where(item => item != null))
            {
                bool exists = false;
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (list.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == prefab)
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists) continue;
                int index = list.arraySize;
                list.InsertArrayElementAtIndex(index);
                SerializedProperty entry = list.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("Override").enumValueIndex = 0;
                entry.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
                entry.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null;
                entry.FindPropertyRelative("SourceHashToOverride").ulongValue = 0;
                entry.FindPropertyRelative("OverridingTargetPrefab").objectReferenceValue = null;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(listAsset);
        }
    }

    private static void ConfigureTutorialScene(
        GameObject mixerPrefab,
        ContainerSubstanceSO water,
        ContainerSubstanceSO gravel,
        BaseResourceSO limestoneVein,
        BaseResourceSO clayDeposit,
        Material limestoneMaterial,
        Material clayMaterial,
        Material gravelMaterial,
        ProductionRecipeSO cementRecipe)
    {
        Scene scene = SceneManager.GetSceneByPath(TutorialScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Additive);

        try
        {
            foreach (GameObject root in scene.GetRootGameObjects().Where(item => item.name.StartsWith("ConcretePreparationV1_", StringComparison.Ordinal)).ToArray())
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            GameObject setupRoot = new GameObject("ConcretePreparationV1_Setup");
            SceneManager.MoveGameObjectToScene(setupRoot, scene);

            BlastFurnaceFactory furnace = FindInScene<BlastFurnaceFactory>(scene);
            if (furnace != null)
            {
                AddObjectToArray(furnace, "productionRecipeSOArray", cementRecipe);
                GameObject mixer = (GameObject)PrefabUtility.InstantiatePrefab(mixerPrefab, scene);
                mixer.name = "ConcretePreparationV1_ConcreteMixer";
                mixer.transform.SetPositionAndRotation(
                    furnace.transform.position + furnace.transform.right * 4.25f,
                    furnace.transform.rotation);
            }

            WaterBody waterBody = FindInScene<WaterBody>(scene);
            Bounds waterBounds = ResolveWaterBounds(waterBody);
            float surfaceY = waterBody != null ? waterBody.SurfaceHeight : waterBounds.max.y;
            float centerZ = waterBounds.center.z;
            float sourceLength = Mathf.Max(12f, waterBounds.size.z * 0.75f);

            CreateExtractionZone(setupRoot.transform, "WaterWest", 4101, water, "Collect", new Vector3(waterBounds.min.x + 0.7f, surfaceY + 0.04f, centerZ), new Vector3(1.4f, 0.18f, sourceLength), null);
            CreateExtractionZone(setupRoot.transform, "WaterEast", 4102, water, "Collect", new Vector3(waterBounds.max.x - 0.7f, surfaceY + 0.04f, centerZ), new Vector3(1.4f, 0.18f, sourceLength), null);
            CreateExtractionZone(setupRoot.transform, "GravelWest", 4201, gravel, "Scoop", Grounded(new Vector3(waterBounds.min.x - 2.2f, surfaceY, centerZ - 12f)), new Vector3(3f, 0.16f, 9f), gravelMaterial);
            CreateExtractionZone(setupRoot.transform, "GravelEast", 4202, gravel, "Scoop", Grounded(new Vector3(waterBounds.max.x + 2.2f, surfaceY, centerZ + 12f)), new Vector3(3f, 0.16f, 9f), gravelMaterial);

            ResourcePopulationZone ironZone = FindInScene<ResourcePopulationZone>(scene, zone => zone.ResourceType != null && zone.ResourceType.resourceName.Contains("Iron"));
            Vector3 limestoneCenter = ironZone != null ? ironZone.transform.position + ironZone.transform.right * 4f : (furnace != null ? furnace.transform.position + Vector3.forward * 8f : Vector3.zero);
            CreatePopulationZone(setupRoot.transform, "LimestoneCave", limestoneVein, limestoneCenter, new Vector3(7f, 4f, 7f), 2);

            Vector3 clayWest = Grounded(new Vector3(waterBounds.min.x - 4.5f, surfaceY, centerZ + 15f));
            Vector3 clayEast = Grounded(new Vector3(waterBounds.max.x + 4.5f, surfaceY, centerZ - 15f));
            CreatePopulationZone(setupRoot.transform, "ClayWest", clayDeposit, clayWest, new Vector3(7f, 4f, 10f), 2);
            CreatePopulationZone(setupRoot.transform, "ClayEast", clayDeposit, clayEast, new Vector3(7f, 4f, 10f), 2);

            SpawnInitialResources(scene, limestoneVein, limestoneCenter, "Limestone", 2, false);
            SpawnInitialResources(scene, clayDeposit, clayWest, "ClayWest", 2, true);
            SpawnInitialResources(scene, clayDeposit, clayEast, "ClayEast", 2, true);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedHere && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void CreateExtractionZone(
        Transform parent,
        string suffix,
        int sourceId,
        ContainerSubstanceSO substance,
        string label,
        Vector3 position,
        Vector3 size,
        Material visualMaterial)
    {
        GameObject zone = new GameObject("ConcretePreparationV1_" + suffix);
        zone.transform.SetParent(parent, true);
        zone.transform.position = position;
        BoxCollider collider = zone.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = size;
        SubstanceExtractionZone source = zone.AddComponent<SubstanceExtractionZone>();
        SerializedObject serialized = new SerializedObject(source);
        serialized.FindProperty("sourceId").intValue = sourceId;
        serialized.FindProperty("substance").objectReferenceValue = substance;
        serialized.FindProperty("interactionLabel").stringValue = label;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (visualMaterial != null)
        {
            CreatePrimitive(zone.transform, PrimitiveType.Cube, "Surface", Vector3.zero, new Vector3(size.x, 0.06f, size.z), Vector3.zero, visualMaterial, false);
        }
    }

    private static void CreatePopulationZone(Transform parent, string suffix, BaseResourceSO resource, Vector3 position, Vector3 size, int minimum)
    {
        GameObject zoneObject = new GameObject("ConcretePreparationV1_" + suffix + "PopulationZone");
        zoneObject.transform.SetParent(parent, true);
        zoneObject.transform.position = position;
        ResourcePopulationZone zone = zoneObject.AddComponent<ResourcePopulationZone>();
        SerializedObject serialized = new SerializedObject(zone);
        serialized.FindProperty("resourceType").objectReferenceValue = resource;
        serialized.FindProperty("minimumAvailableCount").intValue = minimum;
        serialized.FindProperty("replenishmentCooldown").floatValue = 15f;
        serialized.FindProperty("zoneSize").vector3Value = size;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SpawnInitialResources(Scene scene, BaseResourceSO resource, Vector3 center, string suffix, int count, bool snapToGround)
    {
        if (resource == null || resource.resourcePrefab == null) return;
        for (int i = 0; i < count; i++)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(resource.resourcePrefab, scene);
            instance.name = "ConcretePreparationV1_" + suffix + "_" + (i + 1);
            Vector3 offset = new Vector3((i == 0 ? -1f : 1f) * 1.3f, 0f, (i == 0 ? -0.6f : 0.6f));
            Vector3 position = center + offset;
            instance.transform.position = snapToGround ? Grounded(position) : position + Vector3.up * 0.02f;
            instance.transform.rotation = Quaternion.Euler(0f, i * 97f, 0f);
        }
    }

    private static Bounds ResolveWaterBounds(WaterBody waterBody)
    {
        if (waterBody != null)
        {
            SerializedObject serialized = new SerializedObject(waterBody);
            BoxCollider gameplay = serialized.FindProperty("gameplayVolume")?.objectReferenceValue as BoxCollider;
            if (gameplay != null) return gameplay.bounds;
        }
        return new Bounds(new Vector3(8f, -1.5f, 0f), new Vector3(12f, 3f, 88f));
    }

    private static Vector3 Grounded(Vector3 position)
    {
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 20f, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits.OrderBy(item => item.distance))
        {
            if (hit.collider.GetComponentInParent<WaterBody>() == null)
            {
                return hit.point + Vector3.up * 0.02f;
            }
        }
        return position;
    }

    private static T FindInScene<T>(Scene scene, Func<T, bool> predicate = null) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (T component in root.GetComponentsInChildren<T>(true))
            {
                if (predicate == null || predicate(component)) return component;
            }
        }
        return null;
    }

    private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        if (target == null) return;
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null) return;
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void AddObjectToArray(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        if (target == null || value == null) return;
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty array = serialized.FindProperty(propertyName);
        if (array == null || !array.isArray) return;
        for (int i = 0; i < array.arraySize; i++)
        {
            if (array.GetArrayElementAtIndex(i).objectReferenceValue == value) return;
        }
        int index = array.arraySize;
        array.InsertArrayElementAtIndex(index);
        array.GetArrayElementAtIndex(index).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
