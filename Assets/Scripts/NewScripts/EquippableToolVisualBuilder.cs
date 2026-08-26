using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class EquippableToolVisualBuilder : MonoBehaviour
{
    [System.Serializable]
    public struct ToolVisualMaterials
    {
        public Material axeMaterial;
        public Material pickaxeMaterial;
        public Material handleMaterial;
        public GameObject axeModelPrefab;
        public GameObject pickaxeModelPrefab;
    }

    [SerializeField] private Transform visualRoot;
    [SerializeField] private Material axeMaterial;
    [SerializeField] private Material pickaxeMaterial;
    [SerializeField] private Material handleMaterial;
    [SerializeField] private GameObject axeModelPrefab;
    [SerializeField] private GameObject pickaxeModelPrefab;
    [SerializeField] private bool rebuildOnAwake = true;

    public const string GeneratedRootName = "GeneratedToolModel";
    public const string SecondaryGripName = "SecondaryGrip";

#if UNITY_EDITOR
    private const string AxeModelAssetPath = "Assets/AI_assets/Models/Tools/Axe_LowPoly_Corrected.prefab";
    private const string PickaxeModelAssetPath = "Assets/AI_assets/Models/Tools/Pickaxe_LowPoly.fbx";
#endif
    private static readonly Quaternion ImportedModelLocalRotation = Quaternion.Euler(-90f, 0f, 0f);

    private void Awake()
    {
        if (rebuildOnAwake)
        {
            Rebuild();
        }
    }

    [ContextMenu("Rebuild Tool Visual")]
    public void Rebuild()
    {
        EquippableItemSO equippableItemSO = GetEquippableItemSO();
        if (equippableItemSO == null)
        {
            return;
        }

        Transform root = GetVisualRoot();
        if (root == null)
        {
            return;
        }

        HidePlaceholderRenderers(root);
        ClearGeneratedModel(root);

        BuildVisual(equippableItemSO.itemType, root, GetConfiguredMaterials());
    }

    public static GameObject BuildVisual(EquippableItemType itemType, Transform parent, ToolVisualMaterials materials)
    {
        if (parent == null)
        {
            return null;
        }

        GameObject generatedRoot = new GameObject(GeneratedRootName);
        generatedRoot.transform.SetParent(parent, false);
        generatedRoot.transform.localPosition = Vector3.zero;
        generatedRoot.transform.localRotation = Quaternion.identity;
        generatedRoot.transform.localScale = Vector3.one;

        switch (itemType)
        {
            case EquippableItemType.Axe:
                if (TryBuildPrefabVisual(generatedRoot.transform, GetModelPrefab(itemType, materials.axeModelPrefab)))
                {
                    break;
                }

                BuildAxe(generatedRoot.transform, materials);
                break;
            case EquippableItemType.Pickaxe:
                if (TryBuildPrefabVisual(generatedRoot.transform, GetModelPrefab(itemType, materials.pickaxeModelPrefab)))
                {
                    break;
                }

                BuildPickaxe(generatedRoot.transform, materials);
                break;
            case EquippableItemType.Shovel:
                BuildShovel(generatedRoot.transform, materials);
                break;
            case EquippableItemType.IndustrialHammer:
                BuildIndustrialHammer(generatedRoot.transform, materials);
                break;
            case EquippableItemType.Wrench:
                BuildWrench(generatedRoot.transform, materials);
                break;
            case EquippableItemType.Rope:
                BuildRope(generatedRoot.transform, materials);
                break;
            case EquippableItemType.SpiritLevel:
                BuildSpiritLevel(generatedRoot.transform, materials);
                break;
            default:
                DestroyVisualObject(generatedRoot);
                return null;
        }

        return generatedRoot;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (axeModelPrefab == null)
        {
            axeModelPrefab = LoadEditorDefaultModel(EquippableItemType.Axe);
        }

        if (pickaxeModelPrefab == null)
        {
            pickaxeModelPrefab = LoadEditorDefaultModel(EquippableItemType.Pickaxe);
        }
    }
#endif

    private EquippableItemSO GetEquippableItemSO()
    {
        return TryGetComponent(out EquippableItem equippableItem) ? equippableItem.GetEquippableItemSO() : null;
    }

    private Transform GetVisualRoot()
    {
        if (visualRoot != null)
        {
            return visualRoot;
        }

        Transform itemVisuals = transform.Find("Item_visuals");
        visualRoot = itemVisuals != null ? itemVisuals : transform;
        return visualRoot;
    }

    private void HidePlaceholderRenderers(Transform root)
    {
        if (root.TryGetComponent(out Renderer renderer))
        {
            renderer.enabled = false;
        }
    }

    private void ClearGeneratedModel(Transform root)
    {
        Transform previousModel = root.Find(GeneratedRootName);
        if (previousModel == null)
        {
            return;
        }

        DestroyVisualObject(previousModel.gameObject);
    }

    private ToolVisualMaterials GetConfiguredMaterials()
    {
        return new ToolVisualMaterials
        {
            axeMaterial = axeMaterial,
            pickaxeMaterial = pickaxeMaterial,
            handleMaterial = handleMaterial,
            axeModelPrefab = axeModelPrefab,
            pickaxeModelPrefab = pickaxeModelPrefab
        };
    }

    private static bool TryBuildPrefabVisual(Transform root, GameObject modelPrefab)
    {
        if (root == null || modelPrefab == null)
        {
            return false;
        }

        GameObject model = Instantiate(modelPrefab, root, false);
        model.name = modelPrefab.name;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = ImportedModelLocalRotation;
        model.transform.localScale = Vector3.one;

        foreach (Collider collider in model.GetComponentsInChildren<Collider>())
        {
            DestroyVisualObject(collider);
        }

        return true;
    }

    private static GameObject GetModelPrefab(EquippableItemType itemType, GameObject configuredModelPrefab)
    {
        if (configuredModelPrefab != null)
        {
            return configuredModelPrefab;
        }

#if UNITY_EDITOR
        return LoadEditorDefaultModel(itemType);
#else
        return null;
#endif
    }

#if UNITY_EDITOR
    private static GameObject LoadEditorDefaultModel(EquippableItemType itemType)
    {
        string assetPath = itemType switch
        {
            EquippableItemType.Axe => AxeModelAssetPath,
            EquippableItemType.Pickaxe => PickaxeModelAssetPath,
            _ => null
        };

        return string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
    }
#endif

    private static void BuildAxe(Transform root, ToolVisualMaterials materials)
    {
        Material metal = GetMaterial(materials.axeMaterial, new Color(0.86f, 0.75f, 0.28f, 1f));
        Material handle = GetMaterial(materials.handleMaterial, new Color(0.46f, 0.25f, 0.12f, 1f));

        CreateCapsule("Handle", root, new Vector3(0f, -0.02f, 0f), Quaternion.Euler(0f, 0f, 0f), new Vector3(0.08f, 0.58f, 0.08f), handle);
        CreateCube("Grip", root, new Vector3(0f, -0.5f, 0f), Quaternion.identity, new Vector3(0.14f, 0.16f, 0.14f), handle);
        CreateCube("Head", root, new Vector3(0f, 0.42f, 0f), Quaternion.identity, new Vector3(0.44f, 0.14f, 0.12f), metal);
        CreateCube("Blade", root, new Vector3(0.29f, 0.38f, 0f), Quaternion.Euler(0f, 0f, -12f), new Vector3(0.24f, 0.32f, 0.055f), metal);
        CreateCube("BackSpike", root, new Vector3(-0.24f, 0.42f, 0f), Quaternion.Euler(0f, 0f, 35f), new Vector3(0.2f, 0.07f, 0.055f), metal);
    }

    private static void BuildPickaxe(Transform root, ToolVisualMaterials materials)
    {
        Material metal = GetMaterial(materials.pickaxeMaterial, new Color(0.48f, 0.48f, 0.48f, 1f));
        Material handle = GetMaterial(materials.handleMaterial, new Color(0.42f, 0.23f, 0.12f, 1f));

        CreateCapsule("Handle", root, new Vector3(0f, -0.05f, 0f), Quaternion.identity, new Vector3(0.075f, 0.62f, 0.075f), handle);
        CreateCube("Grip", root, new Vector3(0f, -0.56f, 0f), Quaternion.identity, new Vector3(0.13f, 0.14f, 0.13f), handle);
        CreateCube("HeadCenter", root, new Vector3(0f, 0.42f, 0f), Quaternion.identity, new Vector3(0.2f, 0.12f, 0.1f), metal);
        CreateCube("LeftPick", root, new Vector3(-0.31f, 0.43f, 0f), Quaternion.Euler(0f, 0f, -18f), new Vector3(0.48f, 0.07f, 0.055f), metal);
        CreateCube("RightPick", root, new Vector3(0.31f, 0.43f, 0f), Quaternion.Euler(0f, 0f, 18f), new Vector3(0.48f, 0.07f, 0.055f), metal);
    }

    private static void BuildShovel(Transform root, ToolVisualMaterials materials)
    {
        Material metal = GetMaterial(materials.pickaxeMaterial, new Color(0.48f, 0.48f, 0.48f, 1f));
        Material handle = GetMaterial(materials.handleMaterial, new Color(0.42f, 0.23f, 0.12f, 1f));

        CreateCapsule("Handle", root, new Vector3(0f, -0.05f, 0f), Quaternion.identity, new Vector3(0.07f, 0.65f, 0.07f), handle);
        CreateCube("Grip", root, new Vector3(0f, 0.62f, 0f), Quaternion.identity, new Vector3(0.28f, 0.08f, 0.08f), handle);
        CreateCube("Blade", root, new Vector3(0f, -0.68f, 0f), Quaternion.Euler(10f, 0f, 0f), new Vector3(0.34f, 0.32f, 0.08f), metal);
        CreateSecondaryGrip(root, new Vector3(0f, 0.18f, 0f));
    }

    private static void BuildIndustrialHammer(Transform root, ToolVisualMaterials materials)
    {
        Material metal = GetMaterial(materials.pickaxeMaterial, new Color(0.38f, 0.4f, 0.42f, 1f));
        Material handle = GetMaterial(materials.handleMaterial, new Color(0.42f, 0.23f, 0.12f, 1f));

        CreateCapsule("Handle", root, new Vector3(0f, -0.08f, 0f), Quaternion.identity, new Vector3(0.09f, 0.62f, 0.09f), handle);
        CreateCube("HammerHead", root, new Vector3(0f, 0.5f, 0f), Quaternion.identity, new Vector3(0.62f, 0.24f, 0.24f), metal);
        CreateSecondaryGrip(root, new Vector3(0f, 0.12f, 0f));
    }

    private static void CreateSecondaryGrip(Transform parent, Vector3 localPosition)
    {
        GameObject grip = new GameObject(SecondaryGripName);
        grip.transform.SetParent(parent, false);
        grip.transform.localPosition = localPosition;
        grip.transform.localRotation = Quaternion.identity;
    }

    private static void BuildWrench(Transform root, ToolVisualMaterials materials)
    {
        Material metal = GetMaterial(materials.pickaxeMaterial, new Color(0.42f, 0.45f, 0.48f, 1f));

        CreateCube("Handle", root, new Vector3(0f, -0.08f, 0f), Quaternion.identity, new Vector3(0.13f, 0.85f, 0.08f), metal);
        CreateCube("LowerGrip", root, new Vector3(0f, -0.52f, 0f), Quaternion.identity, new Vector3(0.22f, 0.18f, 0.1f), metal);
        CreateCube("JawLeft", root, new Vector3(-0.16f, 0.43f, 0f), Quaternion.Euler(0f, 0f, -25f), new Vector3(0.12f, 0.34f, 0.1f), metal);
        CreateCube("JawRight", root, new Vector3(0.16f, 0.43f, 0f), Quaternion.Euler(0f, 0f, 25f), new Vector3(0.12f, 0.34f, 0.1f), metal);
        CreateCube("JawBase", root, new Vector3(0f, 0.31f, 0f), Quaternion.identity, new Vector3(0.32f, 0.16f, 0.1f), metal);
    }

    private static void BuildRope(Transform root, ToolVisualMaterials materials)
    {
        Material rope = GetMaterial(materials.handleMaterial, new Color(0.42f, 0.24f, 0.1f, 1f));
        Material spool = GetMaterial(materials.pickaxeMaterial, new Color(0.25f, 0.27f, 0.28f, 1f));

        CreateCylinder("RopeCoil", root, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new Vector3(0.42f, 0.18f, 0.42f), rope);
        CreateCylinder("SpoolLeft", root, new Vector3(0f, 0f, -0.13f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.5f, 0.035f, 0.5f), spool);
        CreateCylinder("SpoolRight", root, new Vector3(0f, 0f, 0.13f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.5f, 0.035f, 0.5f), spool);
        CreateCapsule("Handle", root, new Vector3(0f, 0.37f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.055f, 0.24f, 0.055f), spool);
        CreateSecondaryGrip(root, new Vector3(-0.22f, 0f, 0f));
    }

    private static void BuildSpiritLevel(Transform root, ToolVisualMaterials materials)
    {
        Material body = GetMaterial(materials.handleMaterial, new Color(0.92f, 0.72f, 0.08f, 1f));
        Material frame = GetMaterial(materials.pickaxeMaterial, new Color(0.12f, 0.14f, 0.15f, 1f));
        Material liquid = GetMaterial(null, new Color(0.3f, 0.78f, 0.38f, 0.72f));
        Material bubbleMaterial = GetMaterial(null, new Color(0.94f, 1f, 0.72f, 1f));
        Material greenMarkMaterial = GetMaterial(null, new Color(0.18f, 0.95f, 0.25f, 1f));
        Material yellowMarkMaterial = GetMaterial(null, new Color(1f, 0.72f, 0.05f, 1f));

        CreateCube("Body", root, Vector3.zero, Quaternion.identity, new Vector3(0.92f, 0.12f, 0.1f), body);
        CreateCube("VialCutout", root, new Vector3(0f, 0f, -0.055f), Quaternion.identity, new Vector3(0.38f, 0.075f, 0.025f), frame);

        GameObject vial = new GameObject("Vial");
        vial.transform.SetParent(root, false);
        vial.transform.localPosition = new Vector3(0f, 0f, -0.075f);
        CreateCapsule("Liquid", vial.transform, Vector3.zero, Quaternion.Euler(0f, 0f, 90f),
            new Vector3(0.035f, 0.18f, 0.035f), liquid);
        GameObject bubble = CreateSphere("Bubble", vial.transform, Vector3.zero, Quaternion.identity,
            new Vector3(0.055f, 0.045f, 0.045f), bubbleMaterial);
        vial.AddComponent<SpiritLevelVial>();

        CreateCube("GreenMarkLeft", root, new Vector3(-0.035f, 0f, -0.102f), Quaternion.identity,
            new Vector3(0.012f, 0.11f, 0.008f), greenMarkMaterial);
        CreateCube("GreenMarkRight", root, new Vector3(0.035f, 0f, -0.102f), Quaternion.identity,
            new Vector3(0.012f, 0.11f, 0.008f), greenMarkMaterial);
        CreateCube("YellowMarkLeft", root, new Vector3(-0.105f, 0f, -0.102f), Quaternion.identity,
            new Vector3(0.012f, 0.11f, 0.008f), yellowMarkMaterial);
        CreateCube("YellowMarkRight", root, new Vector3(0.105f, 0f, -0.102f), Quaternion.identity,
            new Vector3(0.012f, 0.11f, 0.008f), yellowMarkMaterial);
        CreateSecondaryGrip(root, new Vector3(-0.32f, 0f, 0f));
    }

    private static void CreateCapsule(string objectName, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
    {
        GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        SetupPart(capsule, objectName, parent, localPosition, localRotation, localScale, material);
    }

    private static void CreateCube(string objectName, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SetupPart(cube, objectName, parent, localPosition, localRotation, localScale, material);
    }

    private static void CreateCylinder(string objectName, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
    {
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        SetupPart(cylinder, objectName, parent, localPosition, localRotation, localScale, material);
    }

    private static GameObject CreateSphere(string objectName, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        SetupPart(sphere, objectName, parent, localPosition, localRotation, localScale, material);
        return sphere;
    }

    private static void SetupPart(GameObject part, string objectName, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
    {
        part.name = objectName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = localScale;

        foreach (Collider collider in part.GetComponentsInChildren<Collider>())
        {
            DestroyVisualObject(collider);
        }

        if (part.TryGetComponent(out Renderer renderer))
        {
            renderer.sharedMaterial = material;
        }
    }

    private static void DestroyVisualObject(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static Material GetMaterial(Material configuredMaterial, Color fallbackColor)
    {
        if (configuredMaterial != null)
        {
            return configuredMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material fallbackMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
        fallbackMaterial.color = fallbackColor;
        return fallbackMaterial;
    }
}
