#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ConstructionBookSetup
{
    private const string Root = "Assets/ScriptableObjectAssets/New/ConstructionBook";
    private const string CatalogPath = Root + "/ConstructionBookCatalog.asset";
    private const string PrefabPath = "Assets/Prefabs/New/ConstructionBook.prefab";
    private const string MaterialRoot = "Assets/GeneratedAssets/ConstructionBook";
    private const float PageAngle = 35f;
    private static readonly Vector3 PageCenter = new Vector3(0f, .70f, 0f);
    private static readonly Vector3 PageScale = new Vector3(.56f, .04f, .72f);

    [MenuItem("Tools/Construction Book/Build V1")]
    public static void BuildV1()
    {
        EnsureFolder(Root); EnsureFolder(MaterialRoot); EnsureFolder("Assets/Prefabs/New");
        ConstructionBookCatalogSO catalog = AssetDatabase.LoadAssetAtPath<ConstructionBookCatalogSO>(CatalogPath);
        if (catalog == null) { catalog = ScriptableObject.CreateInstance<ConstructionBookCatalogSO>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
        PopulateCatalog(catalog);
        GameObject prefab = BuildPrefab(catalog);
        AttachPlayerUi();
        PlaceInTutorial(prefab);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        ValidateV1();
        Debug.Log("ConstructionBookSetup: V1 assets, PlayerNew UI, and Tutorial scene instance are ready.");
    }

    [MenuItem("Tools/Construction Book/Validate V1")]
    public static void ValidateV1()
    {
        ConstructionBookCatalogSO catalog = AssetDatabase.LoadAssetAtPath<ConstructionBookCatalogSO>(CatalogPath);
        List<string> issues = catalog == null ? new List<string> { "Catalog asset is missing." } : catalog.ValidateCatalog().ToList();
        ProductionRecipeSO[] recipes = LoadCarpenterRecipes();
        if (catalog != null)
        {
            foreach (ConstructionBookCatalogSO.Entry entry in catalog.Entries)
            {
                if (entry != null && entry.component != null && !ConstructionBookData.TryResolveRecipe(entry.component, 1, recipes, out _, out string issue)) issues.Add(issue);
            }
        }
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        if (player == null || player.GetComponent<PlayerConstructionBookUI>() == null) issues.Add("PlayerNew.prefab is missing PlayerConstructionBookUI.");
        GameObject book = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (book == null || book.GetComponent<ConstructionBookController>() == null || book.GetComponent<NetworkObject>() == null) issues.Add("ConstructionBook.prefab wiring is incomplete.");
        else ValidatePrefabGeometry(book, issues);
        if (issues.Count == 0) Debug.Log($"ConstructionBook validation passed: catalog, {recipes.Length} Carpenter recipes, prefab, and player UI wiring are valid.");
        else foreach (string issue in issues) Debug.LogError("ConstructionBook validation: " + issue);
    }

    private static readonly string[] ComponentPaths =
    {
        "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenFoundation.asset",
        "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenAbutment.asset",
        "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenMainGirder.asset",
        "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenCrossBeam.asset",
        "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenDiagonalBracing.asset",
        "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenDeckPanel.asset"
    };
    private static readonly string[] Titles = { "Wooden Foundation", "Wooden Abutment", "Wooden Main Girder", "Wooden Cross Beam", "Wooden Diagonal Bracing", "Wooden Deck Panel" };
    private static readonly (string text, string[] reqs)[][] Steps =
    {
        new[] { ("Clear the marked site", new[]{"Axe"}), ("Loosen all three soil layers", new[]{"Shovel"}), ("Remove 6 soil portions from each layer", new[]{"Bucket"}), ("Deliver and pour one prepared concrete batch", new[]{"Loaded Concrete Wheelbarrow"}), ("Deliver and mount the foundation", new[]{"Wooden Foundation"}), ("Hammer it into place", new[]{"Industrial Hammer"}) },
        new[] { ("Deliver and mount the abutment", Array.Empty<string>()), ("Measure and level both axes", new[]{"Industrial Hammer","Spirit Level"}), ("Secure all four anchors", new[]{"Industrial Hammer"}), ("Backfill the site", new[]{"Shovel"}) },
        new[] { ("Deliver and mount the girder", Array.Empty<string>()), ("Measure and level both axes", new[]{"Industrial Hammer","Spirit Level"}), ("Fasten all four points in paired order", new[]{"Industrial Hammer"}) },
        new[] { ("Deliver and mount the beam", Array.Empty<string>()), ("Align its position", new[]{"Industrial Hammer"}), ("Tighten both clamps alternately", new[]{"Wrench"}), ("Secure all four fasteners", new[]{"Wrench"}) },
        new[] { ("Deliver and mount the brace", Array.Empty<string>()), ("Align its angle", new[]{"Industrial Hammer"}), ("Temporarily secure both ends", new[]{"Industrial Hammer"}), ("Fasten the marked points in order", new[]{"Wrench"}) },
        new[] { ("Deliver and mount the next panel", Array.Empty<string>()), ("Align the panel", new[]{"Industrial Hammer"}), ("Set an even gap", new[]{"Industrial Hammer"}), ("Secure all marked fasteners", new[]{"Wrench"}) }
    };

    private static void PopulateCatalog(ConstructionBookCatalogSO catalog)
    {
        BridgeComponentSO[] components = ComponentPaths.Select(AssetDatabase.LoadAssetAtPath<BridgeComponentSO>).ToArray();
        Sprite placeholder = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (placeholder == null) throw new InvalidOperationException("Unity's shared UI placeholder sprite could not be loaded.");
        SerializedObject so = new SerializedObject(catalog); SerializedProperty entries = so.FindProperty("entries"); entries.arraySize = 6;
        for (int i = 0; i < 6; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("component").objectReferenceValue = components[i];
            entry.FindPropertyRelative("title").stringValue = Titles[i];
            entry.FindPropertyRelative("partSprite").objectReferenceValue = placeholder;
            SerializedProperty steps = entry.FindPropertyRelative("steps"); steps.arraySize = Steps[i].Length;
            for (int s = 0; s < Steps[i].Length; s++)
            {
                SerializedProperty step = steps.GetArrayElementAtIndex(s); step.FindPropertyRelative("instruction").stringValue = Steps[i][s].text;
                SerializedProperty reqs = step.FindPropertyRelative("requirements"); reqs.arraySize = Steps[i][s].reqs.Length;
                for (int r = 0; r < Steps[i][s].reqs.Length; r++)
                {
                    SerializedProperty requirement = reqs.GetArrayElementAtIndex(r);
                    requirement.FindPropertyRelative("displayName").stringValue = Steps[i][s].reqs[r];
                    requirement.FindPropertyRelative("sprite").objectReferenceValue = placeholder;
                }
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(catalog);
    }

    private static GameObject BuildPrefab(ConstructionBookCatalogSO catalog)
    {
        Material wood = GetMaterial(MaterialRoot + "/Wood.mat", new Color(0.28f, 0.13f, 0.055f));
        Material parchment = GetMaterial(MaterialRoot + "/Parchment.mat", new Color(0.86f, 0.72f, 0.48f));
        GameObject root = new GameObject("ConstructionBook", typeof(BoxCollider), typeof(NetworkObject), typeof(ConstructionBookController));
        BoxCollider collider = root.GetComponent<BoxCollider>(); collider.center = new Vector3(0f, .46f, 0f); collider.size = new Vector3(1.35f, .92f, .75f);
        AddPrimitive(root.transform, "StandPost", PrimitiveType.Cube, new Vector3(0, .415f, 0), new Vector3(.16f, .55f, .16f), Vector3.zero, wood);
        AddPrimitive(root.transform, "StandBase", PrimitiveType.Cube, new Vector3(0, .08f, 0), new Vector3(.85f, .12f, .55f), Vector3.zero, wood);
        AddPrimitive(root.transform, "BookLeft", PrimitiveType.Cube, PageCenter + Vector3.left * .28f, PageScale, new Vector3(PageAngle, 0, 0), parchment);
        AddPrimitive(root.transform, "BookRight", PrimitiveType.Cube, PageCenter + Vector3.right * .28f, PageScale, new Vector3(PageAngle, 0, 0), parchment);
        Transform pageSurfaceAnchor = new GameObject("PageSurfaceAnchor").transform;
        pageSurfaceAnchor.SetParent(root.transform, false);
        Quaternion pageRotation = Quaternion.Euler(PageAngle, 0f, 0f);
        Vector3 physicalNormal = pageRotation * Vector3.up;
        Vector3 pageNormal = physicalNormal;
        Vector3 pageTopDirection = pageRotation * Vector3.forward;
        pageSurfaceAnchor.localPosition = PageCenter + physicalNormal * (PageScale.y * .5f + .024f);
        pageSurfaceAnchor.localRotation = Quaternion.LookRotation(pageNormal, pageTopDirection);
        ConstructionBookController controller = root.GetComponent<ConstructionBookController>(); SerializedObject so = new SerializedObject(controller);
        ProductionRecipeSO[] carpenterRecipes = LoadCarpenterRecipes();
        so.FindProperty("catalog").objectReferenceValue = catalog;
        so.FindProperty("pageSurfaceAnchor").objectReferenceValue = pageSurfaceAnchor;
        SerializedProperty recipes = so.FindProperty("carpenterRecipes"); recipes.arraySize = carpenterRecipes.Length;
        for (int i = 0; i < carpenterRecipes.Length; i++) recipes.GetArrayElementAtIndex(i).objectReferenceValue = carpenterRecipes[i]; so.ApplyModifiedPropertiesWithoutUndo();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); UnityEngine.Object.DestroyImmediate(root); return prefab;
    }

    private static void AttachPlayerUi()
    {
        using PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope("Assets/Prefabs/PlayerNew.prefab");
        if (scope.prefabContentsRoot.GetComponent<PlayerConstructionBookUI>() == null) scope.prefabContentsRoot.AddComponent<PlayerConstructionBookUI>();
    }
    private static void PlaceInTutorial(GameObject prefab)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Tutorial_scene.unity") throw new InvalidOperationException("Open Tutorial_scene before running Construction Book setup.");
        GameObject existing = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).FirstOrDefault(g => g.name == "ConstructionBook");
        if (existing == null)
        {
            Transform marker = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name.Contains("ConstructionSite_Marker"));
            existing = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); existing.name = "ConstructionBook";
            Vector3 origin = marker != null ? marker.position : Vector3.zero; existing.transform.position = origin + new Vector3(-3f, 0f, -2f); existing.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
    private static void AddPrimitive(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 rot, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localEulerAngles = rot;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial = material;
    }
    private static void ValidatePrefabGeometry(GameObject book, List<string> issues)
    {
        ValidateTransform(book.transform.Find("StandPost"), new Vector3(0f, .415f, 0f), new Vector3(.16f, .55f, .16f), Vector3.zero, "StandPost", issues);
        ValidateTransform(book.transform.Find("BookLeft"), PageCenter + Vector3.left * .28f, PageScale, new Vector3(PageAngle, 0f, 0f), "BookLeft", issues);
        ValidateTransform(book.transform.Find("BookRight"), PageCenter + Vector3.right * .28f, PageScale, new Vector3(PageAngle, 0f, 0f), "BookRight", issues);
        BoxCollider collider = book.GetComponent<BoxCollider>();
        if (collider == null || !Approximately(collider.center, new Vector3(0f, .46f, 0f)) || !Approximately(collider.size, new Vector3(1.35f, .92f, .75f))) issues.Add("ConstructionBook collider geometry is incorrect.");
        Transform anchor = book.transform.Find("PageSurfaceAnchor");
        if (anchor == null) issues.Add("PageSurfaceAnchor is missing.");
        else
        {
            Quaternion pageRotation = Quaternion.Euler(PageAngle, 0f, 0f);
            Vector3 physicalNormal = pageRotation * Vector3.up;
            Vector3 expectedPosition = PageCenter + physicalNormal * (PageScale.y * .5f + .024f);
            Vector3 worldPhysicalNormal = book.transform.TransformDirection(physicalNormal);
            Vector3 worldPageTop = book.transform.TransformDirection(pageRotation * Vector3.forward);
            if (!Approximately(anchor.localPosition, expectedPosition) || Vector3.Dot(anchor.forward, worldPhysicalNormal) < .999f || Vector3.Dot(anchor.up, worldPageTop) < .999f) issues.Add("PageSurfaceAnchor axis or 0.024m page-surface offset is incorrect.");
            SerializedObject controller = new SerializedObject(book.GetComponent<ConstructionBookController>());
            if (controller.FindProperty("pageSurfaceAnchor").objectReferenceValue != anchor) issues.Add("ConstructionBookController PageSurfaceAnchor reference is missing.");
        }
    }
    private static void ValidateTransform(Transform value, Vector3 position, Vector3 scale, Vector3 euler, string name, List<string> issues)
    {
        if (value == null || !Approximately(value.localPosition, position) || !Approximately(value.localScale, scale) || Quaternion.Angle(value.localRotation, Quaternion.Euler(euler)) > .01f) issues.Add(name + " geometry is incorrect.");
    }
    private static bool Approximately(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .000001f;
    private static Material GetMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path); if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        material = new Material(shader) { color = color }; AssetDatabase.CreateAsset(material, path); return material;
    }
    private static ProductionRecipeSO[] LoadCarpenterRecipes()
    {
        const string carpenterRoot = "Assets/ScriptableObjectAssets/New/ProductionRecipes/Carpenter";
        return AssetDatabase.FindAssets("t:ProductionRecipeSO", new[] { carpenterRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<ProductionRecipeSO>)
            .Where(recipe => recipe != null)
            .OrderBy(recipe => AssetDatabase.GetAssetPath(recipe), StringComparer.Ordinal)
            .ToArray();
    }
    private static void EnsureFolder(string path)
    {
        string current = "Assets"; foreach (string part in path.Substring("Assets/".Length).Split('/')) { string next = current + "/" + part; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part); current = next; }
    }
}
#endif
