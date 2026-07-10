using UnityEngine;

public class EquippableToolVisualBuilder : MonoBehaviour
{
    [System.Serializable]
    public struct ToolVisualMaterials
    {
        public Material axeMaterial;
        public Material pickaxeMaterial;
        public Material handleMaterial;
    }

    [SerializeField] private Transform visualRoot;
    [SerializeField] private Material axeMaterial;
    [SerializeField] private Material pickaxeMaterial;
    [SerializeField] private Material handleMaterial;
    [SerializeField] private bool rebuildOnAwake = true;

    public const string GeneratedRootName = "GeneratedToolModel";

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
                BuildAxe(generatedRoot.transform, materials);
                break;
            case EquippableItemType.Pickaxe:
                BuildPickaxe(generatedRoot.transform, materials);
                break;
            default:
                Destroy(generatedRoot);
                return null;
        }

        return generatedRoot;
    }

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

        Destroy(previousModel.gameObject);
    }

    private ToolVisualMaterials GetConfiguredMaterials()
    {
        return new ToolVisualMaterials
        {
            axeMaterial = axeMaterial,
            pickaxeMaterial = pickaxeMaterial,
            handleMaterial = handleMaterial
        };
    }

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

    private static void SetupPart(GameObject part, string objectName, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
    {
        part.name = objectName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = localScale;

        foreach (Collider collider in part.GetComponentsInChildren<Collider>())
        {
            Destroy(collider);
        }

        if (part.TryGetComponent(out Renderer renderer))
        {
            renderer.sharedMaterial = material;
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
