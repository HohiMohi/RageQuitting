using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Type = System.Type;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class BridgeDeckAreaV1StagePlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<GameObject> roots = new();
    private readonly List<ScriptableObject> profiles = new();
    private object previousManagerInstance;
    private FieldInfo managerInstanceField;
    private Scene loadedAuthoredAreaScene;
    private Scene previousActiveScene;
    private bool openedAuthoredAreaScene;

    [UnitySetUp]
    public IEnumerator CaptureActiveSceneForFixture()
    {
        previousActiveScene = SceneManager.GetActiveScene();
        yield return null;
    }

    [UnityTest]
    public IEnumerator AuthoredTutorialSceneUsesPhysicalGirderTopAndBindsRuntimeLineMaterial()
    {
#if !UNITY_EDITOR
        Assert.Fail("Requires the authored tutorial scene.");
        yield break;
#else
        const string scenePath = "Assets/Experiments/BridgeDeckAreaV1/Scenes/TutorialDeckAreaV1.unity";
        loadedAuthoredAreaScene = SceneManager.GetSceneByPath(scenePath);
        openedAuthoredAreaScene = !loadedAuthoredAreaScene.IsValid() || !loadedAuthoredAreaScene.isLoaded;
        if (openedAuthoredAreaScene)
        {
            var load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone) yield return null;
            loadedAuthoredAreaScene = SceneManager.GetSceneByPath(scenePath);
        }
        Assert.That(loadedAuthoredAreaScene.IsValid() && loadedAuthoredAreaScene.isLoaded, Is.True);
        Component requirement = null;
        Component section = null;
        foreach (GameObject root in loadedAuthoredAreaScene.GetRootGameObjects())
        {
            if (requirement == null) requirement = root.GetComponentInChildren(T("BridgeDeckAreaRequirement"), true);
            if (section == null) section = root.GetComponentInChildren(T("BridgeDeckSection"), true);
        }
        Assert.That(requirement, Is.Not.Null);
        Assert.That(section, Is.Not.Null);
        Transform frame = (Transform)requirement.GetType().GetProperty("DeckFrame", PublicInstance).GetValue(requirement);
        IEnumerable girderList = (IEnumerable)section.GetType().GetProperty("MainGirders", PublicInstance).GetValue(section);
        float physicalTop = float.NegativeInfinity;
        foreach (object item in girderList)
        {
            if (item is not Component girder || girder == null) continue;
            foreach (Collider collider in girder.GetComponents<Collider>())
                if (collider.enabled && !collider.isTrigger) physicalTop = Mathf.Max(physicalTop, collider.bounds.max.y);
        }
        Assert.That(physicalTop, Is.Not.EqualTo(float.NegativeInfinity));
        Assert.That(frame.position.y, Is.EqualTo(physicalTop).Within(.002f),
            "The authored deck area frame must rest on the two structural girder colliders, excluding child leveling triggers.");
        object areaProfile = requirement.GetType().GetProperty("Profile", PublicInstance).GetValue(requirement);
        Material shared = (Material)areaProfile.GetType().GetField("presentationLineMaterial", PublicInstance).GetValue(areaProfile);
        var lines = requirement.GetComponentsInChildren<LineRenderer>(true);
        Assert.That(lines.Length, Is.GreaterThanOrEqualTo(2), "PlayMode Awake/Bind must create the area and opening renderers.");
        foreach (LineRenderer line in lines)
            Assert.That(line.sharedMaterial, Is.SameAs(shared),
                "Runtime component creation must receive the authored shared material despite Awake order.");
        Assert.That(shared.shader.name, Is.EqualTo("Universal Render Pipeline/Particles/Unlit"));
        Assert.That(shared.GetFloat("_ColorMode"), Is.EqualTo(0f));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator AuthoredConstructionBookShowsPerStockCostAndHidesFutureDeckInstructionsInBothViews()
    {
#if !UNITY_EDITOR
        Assert.Fail("Requires the authored tutorial scene and copied book catalog.");
        yield break;
#else
        const string scenePath = "Assets/Experiments/BridgeDeckAreaV1/Scenes/TutorialDeckAreaV1.unity";
        loadedAuthoredAreaScene = SceneManager.GetSceneByPath(scenePath);
        openedAuthoredAreaScene = !loadedAuthoredAreaScene.IsValid() || !loadedAuthoredAreaScene.isLoaded;
        if (openedAuthoredAreaScene)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone) yield return null;
            loadedAuthoredAreaScene = SceneManager.GetSceneByPath(scenePath);
        }
        Component book = FindSingle(loadedAuthoredAreaScene, "ConstructionBookController");
        Component manager = FindSingle(loadedAuthoredAreaScene, "GameplayManager");
        Component requirement = FindSingle(loadedAuthoredAreaScene, "BridgeDeckAreaRequirement");
        Assert.That(book, Is.Not.Null);
        Assert.That(manager, Is.Not.Null);
        Assert.That(requirement, Is.Not.Null);
        Set(manager, "currentBridgeBuildingStageIndex", 0);
        Set(manager, "bridgeProgressInitialized", true);

        IList spreads = GetField<IList>(book, "spreads");
        int areaSpreadIndex = -1;
        UnityEngine.Object areaComponentSO = (UnityEngine.Object)requirement.GetComponent(T("BridgeComponent"))
            .GetType().GetMethod("GetBridgeComponentSO", PublicInstance).Invoke(requirement.GetComponent(T("BridgeComponent")), null);
        for (int i = 0; i < spreads.Count; i++)
        {
            object spread = spreads[i];
            UnityEngine.Object spreadComponentSO = (UnityEngine.Object)spread.GetType().GetField("Component").GetValue(spread);
            if (spreadComponentSO == areaComponentSO)
            { areaSpreadIndex = i; break; }
        }
        Assert.That(areaSpreadIndex, Is.GreaterThanOrEqualTo(0), "The runtime book must contain the copied area adapter spread.");
        book.GetType().GetMethod("SnapTo", PrivateInstance).Invoke(book, new object[] { areaSpreadIndex });
        string leftCache = (string)book.GetType().GetMethod("GetLeftPageText", PublicInstance).Invoke(book, null);
        string rightCache = (string)book.GetType().GetMethod("GetRightPageText", PublicInstance).Invoke(book, null);
        Assert.That(leftCache, Does.Contain("COST PER STOCK PLATE").And.Contain("each"));
        Assert.That(leftCache, Does.Not.Contain("total").And.Not.Contain("x1"));
        Assert.That(rightCache, Does.Contain("Mounting instructions unlock when this deck stage begins."));

        GameObject screenRoot = Track(new GameObject("Screen construction book view fixture", typeof(RectTransform), typeof(Canvas)));
        Component screenView = screenRoot.AddComponent(T("ConstructionBookView"));
        screenView.GetType().GetMethod("Build", PublicInstance).Invoke(screenView,
            new object[] { new Color(.86f, .72f, .48f, 1f), new Color(.13f, .09f, .05f) });
        book.GetType().GetMethod("PopulateView", PublicInstance, null, new[] { screenView.GetType() }, null)
            .Invoke(book, new[] { screenView });
        Component worldView = GetField<object>(book, "worldView") as Component;
        Assert.That(worldView, Is.Not.Null);
        foreach (Component view in new[] { screenView, worldView })
        {
            string titleText = (string)GetField<object>(view, "title").GetType().GetProperty("text", PublicInstance)
                .GetValue(GetField<object>(view, "title"));
            string materialsText = ReadViewText(((Component)GetField<object>(view, "materialsRoot")).gameObject);
            string stepsText = ReadViewText(((Component)GetField<object>(view, "stepsRoot")).gameObject);
            Assert.That(titleText, Does.Not.Contain("  x1"));
            Assert.That(materialsText, Does.Contain("COST PER STOCK PLATE")
                .And.Contain("Wooden Board: 3 each").And.Contain("Forged Nail Bundle: 1 each"));
            Assert.That(materialsText, Does.Not.Contain(" total"));
            Assert.That(stepsText, Does.Contain("Mounting instructions unlock when this deck stage begins."));
        }
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator AuthoredStockPlateAcceptsFourRealHammeredNailsIntoBothConfiguredGirders()
    {
#if !UNITY_EDITOR
        Assert.Fail("Requires authored Unity prefabs.");
        yield break;
#else
        const string scenePath = "Assets/Experiments/BridgeDeckAreaV1/Scenes/TutorialDeckAreaV1.unity";
        loadedAuthoredAreaScene = SceneManager.GetSceneByPath(scenePath);
        openedAuthoredAreaScene = !loadedAuthoredAreaScene.IsValid() || !loadedAuthoredAreaScene.isLoaded;
        if (openedAuthoredAreaScene)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone) yield return null;
            loadedAuthoredAreaScene = SceneManager.GetSceneByPath(scenePath);
        }
        Component requirement = null;
        Component section = null;
        foreach (GameObject root in loadedAuthoredAreaScene.GetRootGameObjects())
        {
            if (requirement == null) requirement = root.GetComponentInChildren(T("BridgeDeckAreaRequirement"), true);
            if (section == null) section = root.GetComponentInChildren(T("BridgeDeckSection"), true);
        }
        Assert.That(requirement, Is.Not.Null);
        Assert.That(section, Is.Not.Null);
        Transform frame = (Transform)requirement.GetType().GetProperty("DeckFrame", PublicInstance).GetValue(requirement);
        var girderList = (IEnumerable)section.GetType().GetProperty("MainGirders", PublicInstance).GetValue(section);
        var girders = new List<Component>();
        foreach (object item in girderList) if (item is Component girder && girder != null) girders.Add(girder);
        Assert.That(girders.Count, Is.EqualTo(2));

        GameObject stockPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Experiments/BridgeDeckAreaV1/Prefabs/WoodenDeckAreaStockPlate.prefab");
        GameObject nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(stockPrefab, Is.Not.Null);
        Assert.That(nailPrefab, Is.Not.Null);
        GameObject panelRoot = Track(Object.Instantiate(stockPrefab));
        SceneManager.MoveGameObjectToScene(panelRoot, loadedAuthoredAreaScene);
        panelRoot.transform.SetPositionAndRotation(frame.TransformPoint(new Vector3(-2.03f, 0f, 0f)), frame.rotation);
        Component panel = panelRoot.GetComponent(T("BridgeDeckAreaPanel"));
        Assert.That(panel, Is.Not.Null);
        Rigidbody panelBody = panelRoot.GetComponent<Rigidbody>();
        panelBody.isKinematic = true;
        panelBody.useGravity = false;
        Physics.SyncTransforms();

        float railSpacing = Vector3.Dot(girders[1].transform.position - girders[0].transform.position, frame.forward);
        Assert.That(Mathf.Abs(railSpacing), Is.GreaterThan(1f));
        Vector2[] quarters = { new(-.1f, -.75f), new(-.1f, .75f), new(.1f, -.75f), new(.1f, .75f) };
        for (int i = 0; i < quarters.Length; i++)
        {
            Vector3 probe = panelRoot.transform.TransformPoint(new Vector3(quarters[i].x, .25f, quarters[i].y));
            RaycastHit[] hits = Physics.RaycastAll(probe, -panelRoot.transform.up, .5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Component expected = Vector3.Dot(girders[0].transform.position, frame.forward)
                < Vector3.Dot(girders[1].transform.position, frame.forward)
                ? (quarters[i].y < 0f ? girders[0] : girders[1])
                : (quarters[i].y < 0f ? girders[1] : girders[0]);
            Assert.That(System.Array.Exists(hits, hit => hit.collider != null && hit.collider.transform == expected.transform),
                Is.True, $"The actual scene girder must support the plate quarter at {quarters[i]}.");
        }

        Component prefabNail = nailPrefab.GetComponent(T("MountingNail"));
        ScriptableObject nailProfile = Object.Instantiate((ScriptableObject)Read(prefabNail, "Profile"));
        profiles.Add(nailProfile);
        object hammer = System.Enum.Parse(T("EquippableItemType"), "IndustrialHammer");
        var nails = new List<Component>();
        foreach (Vector2 quarter in quarters)
        {
            GameObject nailRoot = Track(Object.Instantiate(nailPrefab));
            SceneManager.MoveGameObjectToScene(nailRoot, loadedAuthoredAreaScene);
            Component nail = nailRoot.GetComponent(T("MountingNail"));
            Set(nail, "profile", nailProfile);
            AimNailAtPanel(nail, panelRoot.transform, quarter);
            nail.GetType().GetMethod("OnSingleCarryPlacementCommitted", PublicInstance).Invoke(nail, new object[] { null });
            Assert.That(Read(nail, "State").ToString(), Is.EqualTo("Prepared"), "Production nail must prepare on the authored stock collider.");
            for (int hit = 0; hit < 3; hit++)
                Assert.That(nail.GetType().GetMethod("TryApplyHammerHit", PublicInstance).Invoke(nail, new[] { hammer }), Is.True,
                    $"Authored-scene nail at {quarter} should accept hammer hit {hit + 1}.");
            Assert.That(Read(nail, "State").ToString(), Is.EqualTo("EmbeddedPair"));
            object[] attachment = { null };
            Assert.That((bool)nail.GetType().GetMethod("TryGetAttachmentInfo", PublicInstance).Invoke(nail, attachment), Is.True);
            object attachmentInfo = attachment[0];
            Transform actualGirder = (Transform)attachmentInfo.GetType().GetField("SecondTarget").GetValue(attachmentInfo);
            Component expectedGirder = Vector3.Dot(girders[0].transform.position, frame.forward)
                < Vector3.Dot(girders[1].transform.position, frame.forward)
                ? (quarter.y < 0f ? girders[0] : girders[1])
                : (quarter.y < 0f ? girders[1] : girders[0]);
            Assert.That(actualGirder, Is.EqualTo(expectedGirder.transform),
                "The nail must fasten to one of the two actual registered main girders.");
            nails.Add(nail);
        }
        object assessment = System.Activator.CreateInstance(T("BridgeDeckAreaStageAssessment"));
        object result = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        Assert.That(GetField<float>(result, "SecuredLengthMeters"), Is.EqualTo(2.5f).Within(.01f));
        Assert.That((bool)T("BridgeDeckAreaPanel").GetProperty("IsSecuredForArea").GetValue(panel), Is.True);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator CopiedCarpenterRecipeProducesTwoUsableStockPlatesAndConsumesItsPerPlateCostTwice()
    {
#if !UNITY_EDITOR
        Assert.Fail("Requires authored production assets.");
        yield break;
#else
        const string scenePath = "Assets/Experiments/BridgeDeckAreaV1/Scenes/TutorialDeckAreaV1.unity";
        loadedAuthoredAreaScene = SceneManager.GetSceneByPath(scenePath);
        openedAuthoredAreaScene = !loadedAuthoredAreaScene.IsValid() || !loadedAuthoredAreaScene.isLoaded;
        if (openedAuthoredAreaScene)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone) yield return null;
            loadedAuthoredAreaScene = SceneManager.GetSceneByPath(scenePath);
        }
        Assert.That(loadedAuthoredAreaScene.IsValid() && loadedAuthoredAreaScene.isLoaded, Is.True);
        Assert.That(SceneManager.SetActiveScene(loadedAuthoredAreaScene), Is.True,
            "Production spawned by the live factory must belong to the active gameplay scene as it does in a real session.");

        ScriptableObject recipe = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/Experiments/BridgeDeckAreaV1/Recipes/Carpenter_WoodenDeckAreaStockPlate.asset");
        Assert.That(recipe, Is.Not.Null);
        Component factory = null;
        Component storage = null;
        foreach (GameObject root in loadedAuthoredAreaScene.GetRootGameObjects())
        {
            foreach (Component candidate in root.GetComponentsInChildren(T("BaseFactory"), true))
            {
                IEnumerable configuredRecipes = (IEnumerable)candidate.GetType().GetField("productionRecipeSOArray", AllInstance).GetValue(candidate);
                bool wired = false;
                if (configuredRecipes != null)
                    foreach (object configuredRecipe in configuredRecipes) if (configuredRecipe == recipe) wired = true;
                if (!wired) continue;
                factory = candidate;
                storage = (Component)candidate.GetType().GetField("baseStorageNew", AllInstance).GetValue(candidate);
                break;
            }
            if (factory != null) break;
        }
        Assert.That(factory, Is.Not.Null, "The copied scene must wire the copied Carpenter recipe into its real factory.");
        Assert.That(storage, Is.Not.Null);
        T("BaseFactory").GetField("productionDuration", AllInstance).SetValue(factory, 0f);

        IEnumerable requiredResources = (IEnumerable)recipe.GetType().GetProperty("RequiredResources", PublicInstance).GetValue(recipe);
        var costs = new List<(ScriptableObject resource, int amount)>();
        foreach (object required in requiredResources)
        {
            ScriptableObject resource = (ScriptableObject)required.GetType().GetField("resourceType", AllInstance).GetValue(required);
            int amount = (int)required.GetType().GetField("amount", AllInstance).GetValue(required);
            Assert.That((bool)storage.GetType().GetMethod("IsStorable", PublicInstance).Invoke(storage, new object[] { resource }),
                Is.True, $"Copied Carpenter storage must accept {resource.name}.");
            storage.GetType().GetMethod("StoreBaseResource", PublicInstance).Invoke(storage, new object[] { resource, amount * 2 });
            costs.Add((resource, amount));
        }

        List<Component> initialPanels = GetAreaPanels(loadedAuthoredAreaScene);
        var initialPanelIds = new HashSet<int>();
        foreach (Component panel in initialPanels) initialPanelIds.Add(panel.GetInstanceID());
        factory.GetType().GetMethod("RequestSelectRecipe", PublicInstance).Invoke(factory, new object[] { recipe });
        Vector2 requiredDialDimensions = (Vector2)factory.GetType().GetMethod("GetRequiredDimensionsForSelectedComponent", PublicInstance)
            .Invoke(factory, null);
        SetCarpenterDialThroughCrank(factory, System.Enum.Parse(T("ComponentDimension"), "Width"), requiredDialDimensions.x);
        SetCarpenterDialThroughCrank(factory, System.Enum.Parse(T("ComponentDimension"), "Length"), requiredDialDimensions.y);
        Assert.That((bool)factory.GetType().GetProperty("AreDimensionsMatchingSelectedComponent", PublicInstance).GetValue(factory),
            Is.True, "The real Carpenter dial adjustments must match the stock product before starting production.");
        for (int production = 1; production <= 2; production++)
        {
            factory.GetType().GetMethod("RequestStartProduction", PublicInstance).Invoke(factory, null);
            yield return null;
            List<Component> newOutputs = new();
            foreach (Component panel in GetAreaPanels(loadedAuthoredAreaScene))
                if (!initialPanelIds.Contains(panel.GetInstanceID())) newOutputs.Add(panel);
            Assert.That(newOutputs.Count, Is.EqualTo(production),
                $"Production pass {production} must instantiate exactly one independent stock plate. "
                + $"selectedRecipe={factory.GetType().GetProperty("SelectedRecipe", PublicInstance).GetValue(factory)}, "
                + $"failure={factory.GetType().GetProperty("LastFailureReason", PublicInstance).GetValue(factory)}, "
                + $"isProducing={factory.GetType().GetProperty("IsProducing", PublicInstance).GetValue(factory)}, "
                + $"panels={CountAreaPanels(loadedAuthoredAreaScene)}.");
            Component output = newOutputs[production - 1];
            Assert.That((int)Read(output, "LengthMillimeters"), Is.EqualTo(2500));
            Assert.That((int)Read(output, "WidthMillimeters"), Is.EqualTo(2400));
            Assert.That((int)Read(output, "ThicknessMillimeters"), Is.EqualTo(200));
            Assert.That((output.transform.lossyScale - Vector3.one).sqrMagnitude, Is.LessThan(0.00000001f));
            Assert.That(output.GetComponent<Rigidbody>().mass, Is.EqualTo(1f).Within(.001f));
            Component mountable = output.GetComponent(T("MountableBridgeComponent"));
            Assert.That(mountable, Is.Not.Null);
            UnityEngine.Object productSO = (UnityEngine.Object)mountable.GetType()
                .GetField("mountableBridgeComponentSO", AllInstance).GetValue(mountable);
            Assert.That(productSO, Is.SameAs(recipe.GetType().GetProperty("MountableBridgeComponentOutput", PublicInstance).GetValue(recipe)));
            GameObject configuredPrefab = (GameObject)productSO.GetType().GetField("inGameGameObjectPrefab", AllInstance).GetValue(productSO);
            Assert.That(UnityEditor.AssetDatabase.GetAssetPath(configuredPrefab),
                Is.EqualTo("Assets/Experiments/BridgeDeckAreaV1/Prefabs/WoodenDeckAreaStockPlate.prefab"));
            roots.Add(output.gameObject);
            foreach ((ScriptableObject resource, int amount) in costs)
                Assert.That((int)storage.GetType().GetMethod("CheckBaseResourceAmount", PublicInstance)
                    .Invoke(storage, new object[] { resource }), Is.EqualTo(amount * (2 - production)),
                    $"Each plate must consume the recipe's per-stock-plate cost on pass {production}.");
        }

        int fullStockOutputs = 0;
        foreach (GameObject root in loadedAuthoredAreaScene.GetRootGameObjects())
            foreach (Component panel in root.GetComponentsInChildren(T("BridgeDeckAreaPanel"), true))
                if ((int)Read(panel, "LengthMillimeters") == 2500) fullStockOutputs++;
        Assert.That(fullStockOutputs, Is.GreaterThanOrEqualTo(2));
        yield return null;
#endif
    }

    // The actual lobby/button/restart/shutdown/reentry flow is run through the live Editor CLI because NGO Single scene loading stalls this UnityTest coroutine. Its evidence is in Artifacts/DeckAreaV1/lobby-flow-validation.json.


    [UnityTest]
    public IEnumerator FourRealHammeredNailsOnMinimumFragmentSecureItsInstanceSizedQuarters()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses authored Unity prefabs.");
        yield break;
#else
        System.Type tPanel = T("BridgeDeckAreaPanel"), tProfile = T("BridgeDeckAreaProfile");
        System.Type tSection = T("BridgeDeckSection"), tRequirement = T("BridgeDeckAreaRequirement");
        System.Type tBridgeComponent = T("BridgeComponent"), tNail = T("MountingNail");
        GameObject sectionRoot = Track(new GameObject("B3 area section"));
        Component section = sectionRoot.AddComponent(tSection);
        GameObject mainRoot = Track(new GameObject("B3 main girder"));
        GameObject otherRoot = Track(new GameObject("B3 second main girder"));
        Component mainGirder = CreateGirder(mainRoot, tBridgeComponent, -1f);
        Component otherGirder = CreateGirder(otherRoot, tBridgeComponent, 1f);
        System.Array girders = System.Array.CreateInstance(tBridgeComponent, 2);
        girders.SetValue(mainGirder, 0); girders.SetValue(otherGirder, 1);
        Set(section, "mainGirders", girders);

        ScriptableObject areaProfile = ScriptableObject.CreateInstance(tProfile);
        profiles.Add(areaProfile);
        tProfile.GetField("fullStockMassKilograms").SetValue(areaProfile, 2f);
        GameObject frame = Track(new GameObject("B3 area frame"));
        GameObject requirementRoot = Track(new GameObject("B3 area requirement"));
        requirementRoot.transform.SetParent(sectionRoot.transform, false);
        Component requirement = requirementRoot.AddComponent(tRequirement);
        Set(requirement, "profile", areaProfile);
        Set(requirement, "deckFrame", frame.transform);
        Set(requirement, "deckSection", section);

        GameObject panelRoot = Track(new GameObject("B3 minimum deck fragment",
            typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider), typeof(Rigidbody)));
        panelRoot.transform.position = Vector3.zero;
        Component panel = panelRoot.AddComponent(tPanel);
        SetDimensions(panel, 400, 2400, 200, 2500, 2000);
        BoxCollider panelBox = panelRoot.GetComponent<BoxCollider>();
        panelBox.size = new Vector3(.4f, .2f, 2.4f);
        Rigidbody panelBody = panelRoot.GetComponent<Rigidbody>();
        panelBody.isKinematic = true; panelBody.useGravity = false;
        Component carryBody = panelRoot.AddComponent(T("SharedCarryPhysicsBody"));
        Assert.That(panelBody.mass, Is.EqualTo(.32f).Within(.001f));
        carryBody.GetType().GetMethod("BeginSharedCarry", PublicInstance).Invoke(carryBody, new object[] { true });
        Assert.That(panelBody.mass, Is.EqualTo(.32f).Within(.001f), "Carry profile application preserves source-density fragment mass.");
        carryBody.GetType().GetMethod("EndSharedCarry", PublicInstance).Invoke(carryBody, null);
        Assert.That(panelBody.mass, Is.EqualTo(.32f).Within(.001f), "Drop restores the fragment's own mass rather than stock prefab mass.");
        panelBody.isKinematic = true; panelBody.useGravity = false;
        Physics.SyncTransforms();

        GameObject nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(nailPrefab, Is.Not.Null);
        Component prefabNail = nailPrefab.GetComponent(tNail);
        ScriptableObject nailProfile = Object.Instantiate((ScriptableObject)tNail.GetProperty("Profile", PublicInstance).GetValue(prefabNail));
        profiles.Add(nailProfile);
        nailProfile.GetType().GetField("requiredHitsForTwoObjects", PrivateInstance).SetValue(nailProfile, 3);
        object hammer = System.Enum.Parse(T("EquippableItemType"), "IndustrialHammer");
        Vector2[] entries = { new(-.1f, -1f), new(-.1f, 1f), new(.1f, -1f), new(.1f, 1f) };
        for (int i = 0; i < entries.Length; i++)
        {
            GameObject expectedGirder = entries[i].y < 0f ? mainRoot : otherRoot;
            RaycastHit[] hits = Physics.RaycastAll(panelRoot.transform.TransformPoint(new Vector3(entries[i].x, .25f, entries[i].y)),
                -panelRoot.transform.up, .5f);
            Assert.That(System.Array.Exists(hits, hit => hit.collider != null && hit.collider.gameObject == expectedGirder),
                Is.True, "The registered rail collider must be physically reachable through the panel before preparing a fastener.");
        }
        for (int i = 0; i < entries.Length; i++)
        {
            GameObject nailRoot = Track(Object.Instantiate(nailPrefab));
            Component nail = nailRoot.GetComponent(tNail);
            Set(nail, "profile", nailProfile);
            AimNailAtPanel(nail, panelRoot.transform, entries[i]);
            nail.GetType().GetMethod("OnSingleCarryPlacementCommitted", PublicInstance).Invoke(nail, new object[] { null });
            Assert.That(Read(nail, "State").ToString(), Is.EqualTo("Prepared"), "Each real nail must first prepare against the panel collider.");
            for (int hit = 0; hit < 3; hit++)
            {
                Assert.That(nail.GetType().GetMethod("TryApplyHammerHit", PublicInstance).Invoke(nail, new[] { hammer }), Is.True);
                if (hit < 2) Assert.That(Read(nail, "State").ToString(), Is.EqualTo("Partial"));
            }
            Assert.That(Read(nail, "State").ToString(), Is.EqualTo("EmbeddedPair"), "Fasteners count only after the actual hammer transition.");
            object[] attachmentArgs = { null };
            Assert.That((bool)nail.GetType().GetMethod("TryGetAttachmentInfo", PublicInstance).Invoke(nail, attachmentArgs), Is.True);
            object attachment = attachmentArgs[0];
            Transform attachedGirder = (Transform)attachment.GetType().GetField("SecondTarget").GetValue(attachment);
            GameObject expected = entries[i].y < 0f ? mainRoot : otherRoot;
            Assert.That(attachedGirder, Is.EqualTo(expected.transform), "The second nail target must be one of the two configured main girders.");
        }

        object assessment = System.Activator.CreateInstance(T("BridgeDeckAreaStageAssessment"));
        object result = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        Assert.That(GetField<float>(result, "SecuredLengthMeters"), Is.EqualTo(.4f).Within(.0001f));
        Assert.That(GetField<bool>(result, "IsComplete"), Is.False, "This one fragment is securely fastened, but does not cover the full bridge.");
        Assert.That((bool)tPanel.GetProperty("IsSecuredForArea").GetValue(panel), Is.True);

        object snapshot = tPanel.GetProperty("State").GetValue(panel);
        Set(snapshot, "CutStarted", true);
        tPanel.GetMethod("ApplySnapshot", PrivateInstance).Invoke(panel, new[] { snapshot });
        result = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        Assert.That(GetField<float>(result, "SecuredLengthMeters"), Is.EqualTo(0f), "A fully nailed but unfinished cut cannot count as secured coverage.");
        Assert.That(GetField<float>(result, "CoveredLengthMeters"), Is.EqualTo(0f));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator FullAreaRequiresTwelveRealNailsBeforeContributorLocksAndLeavesExtraFree()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses authored Unity prefabs.");
        yield break;
#else
        System.Type tPanel = T("BridgeDeckAreaPanel"), tProfile = T("BridgeDeckAreaProfile");
        System.Type tSection = T("BridgeDeckSection"), tRequirement = T("BridgeDeckAreaRequirement");
        System.Type tBridgeComponent = T("BridgeComponent"), tNail = T("MountingNail");
        GameObject sectionRoot = Track(new GameObject("B3 full area section"));
        Component section = sectionRoot.AddComponent(tSection);
        GameObject bridgeRoot = Track(new GameObject("B3 staged bridge"));
        bridgeRoot.SetActive(false);
        GameObject holder = Track(new GameObject("B3 staged bridge components"));
        holder.transform.SetParent(bridgeRoot.transform, false);
        GameObject mainRoot = Track(new GameObject("B3 full-area main girder"));
        GameObject otherRoot = Track(new GameObject("B3 full-area second main girder"));
        mainRoot.transform.SetParent(holder.transform, true); otherRoot.transform.SetParent(holder.transform, true);
        Component mainGirder = CreateGirder(mainRoot, tBridgeComponent, -1f);
        Component otherGirder = CreateGirder(otherRoot, tBridgeComponent, 1f);
        System.Array girders = System.Array.CreateInstance(tBridgeComponent, 2);
        girders.SetValue(mainGirder, 0); girders.SetValue(otherGirder, 1); Set(section, "mainGirders", girders);
        ScriptableObject areaProfile = ScriptableObject.CreateInstance(tProfile); profiles.Add(areaProfile);
        GameObject frame = Track(new GameObject("B3 full-area frame"));
        GameObject requirementRoot = Track(new GameObject("B3 full-area requirement"));
        requirementRoot.transform.SetParent(holder.transform, false);
        Component adapter = requirementRoot.AddComponent(tBridgeComponent);
        Set(adapter, "componentID", 1);
        Component requirement = requirementRoot.AddComponent(tRequirement);
        Set(requirement, "profile", areaProfile); Set(requirement, "deckFrame", frame.transform);
        Set(requirement, "deckSection", section);
        Component bridge = bridgeRoot.AddComponent(T("Bridge"));
        Set(bridge, "bridgeComponentHolder", holder);
        GameObject earlierRoot = Track(new GameObject("B3 prior stage component"));
        earlierRoot.transform.SetParent(holder.transform, false);
        Set(earlierRoot.AddComponent(tBridgeComponent), "componentID", 0);
        GameObject laterRoot = Track(new GameObject("B3 later stage component"));
        laterRoot.transform.SetParent(holder.transform, false);
        Set(laterRoot.AddComponent(tBridgeComponent), "componentID", 2);
        GameObject managerRoot = Track(new GameObject("B3 staged gameplay manager")); managerRoot.SetActive(false);
        System.Type managerType = T("GameplayManager");
        Component manager = managerRoot.AddComponent(managerType);
        Set(manager, "bridge", bridge);
        System.Array data = System.Array.CreateInstance(managerType.GetField("bridgeComponentDataArray", PrivateInstance).FieldType.GetElementType(), 3);
        Set(manager, "bridgeComponentDataArray", data);
        Set(manager, "bridgeBuildingStages", CreateStages(managerType, new[] { 0, 1, 2 }));
        Set(manager, "currentBridgeBuildingStageIndex", 0);
        Set(manager, "bridgeProgressInitialized", true);
        ((Behaviour)manager).enabled = false;
        managerInstanceField = managerType.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        previousManagerInstance = managerInstanceField.GetValue(null);
        managerInstanceField.SetValue(null, manager);
        managerRoot.SetActive(true);
        bridgeRoot.SetActive(true);

        GameObject nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(nailPrefab, Is.Not.Null);
        Component prefabNail = nailPrefab.GetComponent(tNail);
        ScriptableObject nailProfile = Object.Instantiate((ScriptableObject)tNail.GetProperty("Profile", PublicInstance).GetValue(prefabNail));
        profiles.Add(nailProfile);
        nailProfile.GetType().GetField("requiredHitsForTwoObjects", PrivateInstance).SetValue(nailProfile, 3);
        object hammer = System.Enum.Parse(T("EquippableItemType"), "IndustrialHammer");
        int[] lengths = { 2500, 2500, 1560 };
        float[] centers = { -2.03f, .47f, 2.50f };
        Component[] panels = new Component[3];
        List<Component> nails = new();
        Vector2[] railPairs = { new(-1f, -1f), new(-1f, 1f), new(1f, -1f), new(1f, 1f) };
        for (int p = 0; p < panels.Length; p++)
        {
            GameObject root = Track(new GameObject($"B3 full-area plate {p}",
                typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider), typeof(Rigidbody)));
            root.transform.position = new Vector3(centers[p], 0f, 0f);
            panels[p] = root.AddComponent(tPanel);
            Set(panels[p], "geometryProfile", areaProfile);
            tPanel.GetMethod("ApplyMaterialProfile", PublicInstance).Invoke(panels[p], new[] { areaProfile });
            SetDimensions(panels[p], lengths[p], 2400, 200, 2500, 1000);
            Rigidbody body = root.GetComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            root.GetComponent<BoxCollider>().size = new Vector3(lengths[p] * .001f, .2f, 2.4f);
            Physics.SyncTransforms();
            for (int q = 0; q < 4; q++)
            {
                Vector2 local = new(railPairs[q].x * lengths[p] * .00025f, railPairs[q].y);
                GameObject nailRoot = Track(Object.Instantiate(nailPrefab));
                Component nail = nailRoot.GetComponent(tNail); Set(nail, "profile", nailProfile);
                AimNailAtPanel(nail, root.transform, local);
                nail.GetType().GetMethod("OnSingleCarryPlacementCommitted", PublicInstance).Invoke(nail, new object[] { null });
                Assert.That(Read(nail, "State").ToString(), Is.EqualTo("Prepared"), $"Plate {p} quarter {q} prepares on its actual collider.");
                for (int hit = 0; hit < 3; hit++)
                    Assert.That(nail.GetType().GetMethod("TryApplyHammerHit", PublicInstance).Invoke(nail, new[] { hammer }), Is.True,
                        $"Plate {p} quarter {q} hammer hit {hit + 1} must embed through the production target checks.");
                Assert.That(Read(nail, "State").ToString(), Is.EqualTo("EmbeddedPair"));
                nails.Add(nail);
            }
        }
        GameObject extraRoot = Track(new GameObject("B3 unclaimed extra deck plate",
            typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider), typeof(Rigidbody)));
        extraRoot.transform.position = new Vector3(4.5f, 0f, 0f);
        Component extra = extraRoot.AddComponent(tPanel); Set(extra, "geometryProfile", areaProfile);
        tPanel.GetMethod("ApplyMaterialProfile", PublicInstance).Invoke(extra, new[] { areaProfile });
        Rigidbody extraBody = extraRoot.GetComponent<Rigidbody>(); extraBody.isKinematic = true; extraBody.useGravity = false;
        Physics.SyncTransforms();

        object assessment = System.Activator.CreateInstance(T("BridgeDeckAreaStageAssessment"));
        object result = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        Assert.That(GetField<float>(result, "SecuredLengthMeters"), Is.EqualTo(6.56f).Within(.002f));
        Assert.That(GetField<bool>(result, "IsComplete"), Is.True, "Coverage is complete only when every real area nail is embedded and every joint is closed.");
        MethodInfo tryExtract = tNail.GetMethod("TryExtractForCrowbar", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That((bool)tryExtract.Invoke(nails[0], null), Is.True,
            "Before the area stage advances, a secured contributor remains reversibly extractable.");
        result = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        Assert.That(GetField<float>(result, "SecuredLengthMeters"), Is.LessThan(6.56f));
        Assert.That(GetField<bool>(result, "IsComplete"), Is.False, "Removing one real nail removes that panel from secured coverage.");
        GameObject replacementRoot = Track(Object.Instantiate(nailPrefab));
        Component replacement = replacementRoot.GetComponent(tNail); Set(replacement, "profile", nailProfile);
        AimNailAtPanel(replacement, panels[0].transform, new Vector2(-.625f, -1f));
        replacement.GetType().GetMethod("OnSingleCarryPlacementCommitted", PublicInstance).Invoke(replacement, new object[] { null });
        Assert.That(Read(replacement, "State").ToString(), Is.EqualTo("Prepared"));
        for (int hit = 0; hit < 3; hit++)
            Assert.That(replacement.GetType().GetMethod("TryApplyHammerHit", PublicInstance).Invoke(replacement, new[] { hammer }), Is.True,
                $"The replacement fastener must pass the same real placement/hit checks (hit {hit + 1}).");
        nails[0] = replacement;
        result = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        Assert.That(GetField<bool>(result, "IsComplete"), Is.True, "Re-securing the removed quarter restores complete coverage.");

        Type interactionPromptType = T("InteractionPrompt");
        IList actualPanelPrompts = (IList)System.Activator.CreateInstance(typeof(List<>).MakeGenericType(interactionPromptType));
        Component legacyMarker = panels[1].gameObject.AddComponent(T("BridgeDeckPanelMounting"));
        tPanel.GetMethod("GetInteractionPrompts", PublicInstance).Invoke(panels[1], new object[] { null, actualPanelPrompts });
        Assert.That(CountPromptType(actualPanelPrompts, "Information"), Is.Zero,
            "A future area stage must hide every mounting diagnosis while preserving ordinary actions.");
        IList markerPrompts = (IList)System.Activator.CreateInstance(typeof(List<>).MakeGenericType(interactionPromptType));
        legacyMarker.GetType().GetMethod("GetInteractionPrompts", PublicInstance)
            .Invoke(legacyMarker, new object[] { null, markerPrompts });
        Assert.That(CountPromptType(markerPrompts, "Information"), Is.Zero,
            "The legacy marker entrypoint must delegate to the same future-stage gate.");
        legacyMarker.GetType().GetMethod("AddPlacementInformationPrompts", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(legacyMarker, new object[] { markerPrompts });
        Assert.That(markerPrompts.Count, Is.Zero,
            "The legacy placement-information entrypoint must not leak future-stage guidance or global status.");
        Set(manager, "currentBridgeBuildingStageIndex", 1);

        // Exercise the real F/E candidate diagnostic with a source between two neighbors.
        // The left opening is fillable; only the smaller right opening needs correction.
        SetDimensions(panels[0], 1500, 2400, 200, 2500, 1000);
        SetDimensions(panels[1], 1000, 2400, 200, 2500, 1000);
        SetDimensions(panels[2], 1560, 2400, 200, 2500, 1000);
        panels[0].transform.position = new Vector3(-1.95f, 0f, 0f);
        panels[1].transform.position = Vector3.zero;
        panels[2].transform.position = new Vector3(1.36f, 0f, 0f);
        int[] previewLengths = { 1500, 1000, 1560 };
        for (int i = 0; i < panels.Length; i++)
            panels[i].GetComponent<BoxCollider>().size = new Vector3(previewLengths[i] * .001f, .2f, 2.4f);
        Physics.SyncTransforms();
        MethodInfo placementDiagnostic = tRequirement.GetMethod("TryGetPlacementCandidateDiagnostic", BindingFlags.Static | BindingFlags.Public);
        object[] placementDiagnosticArgs =
        {
            panels[1], Vector3.zero, Quaternion.identity, null, false, false
        };
        Assert.That((bool)placementDiagnostic.Invoke(null, placementDiagnosticArgs), Is.True);
        object rightOpeningDiagnostic = placementDiagnosticArgs[3];
        Assert.That(GetField<object>(rightOpeningDiagnostic, "Kind").ToString(), Is.EqualTo("NarrowOpening"));
        Assert.That(GetField<float>(rightOpeningDiagnostic, "MinimumGapMeters"), Is.EqualTo(.08f).Within(.001f),
            "The F/E candidate feedback must use the right neighbor's own footprint, not the last reused scratch array.");
        Assert.That(GetField<float>(rightOpeningDiagnostic, "MaximumGapMeters"), Is.EqualTo(.08f).Within(.001f));
        Assert.That(GetField<float>(rightOpeningDiagnostic, "RequiredGapMeters"), Is.EqualTo(.4f).Within(.001f));

        object[] raisedCandidateArgs = { panels[1], new Vector3(0f, .041f, 0f), Quaternion.identity, null, false, false };
        Assert.That((bool)placementDiagnostic.Invoke(null, raisedCandidateArgs), Is.True);
        Assert.That(GetField<object>(raisedCandidateArgs[3], "Kind").ToString(), Is.EqualTo("NarrowOpening"),
            "F/E selects the measured narrow opening before a later height correction when the footprint spans the deck sweep.");
        panels[1].transform.position = new Vector3(0f, .041f, 0f);
        Physics.SyncTransforms();
        object raisedResult = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        object[] raisedPanelDiagnosticArgs = { panels[1], raisedResult, null, false };
        Assert.That((bool)tRequirement.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == "TryGetPanelPresentationDiagnostic" && method.GetParameters().Length == 4)
            .Invoke(requirement, raisedPanelDiagnosticArgs), Is.True);
        Assert.That(GetField<object>(raisedPanelDiagnosticArgs[2], "Kind").ToString(), Is.EqualTo("NarrowOpening"),
            "The laid-panel path must use the same visible priority as F/E.");
        Assert.That(GetField<float>(raisedCandidateArgs[3], "MinimumGapMeters"),
            Is.EqualTo(GetField<float>(raisedPanelDiagnosticArgs[2], "MinimumGapMeters")).Within(.0001f),
            "F/E and laid panels report the same measured opening minimum.");
        Assert.That(GetField<float>(raisedCandidateArgs[3], "MaximumGapMeters"),
            Is.EqualTo(GetField<float>(raisedPanelDiagnosticArgs[2], "MaximumGapMeters")).Within(.0001f),
            "F/E and laid panels report the same measured opening maximum.");
        Assert.That(GetField<IList>(raisedResult, "ContributingPanels").Count, Is.EqualTo(2));
        Assert.That(GetField<float>(raisedResult, "CoveredLengthMeters"), Is.EqualTo(3.06f).Within(.001f),
            "Choosing a higher visible priority does not admit invalid-height geometry into assessment coverage.");
        panels[1].transform.position = Vector3.zero;
        Physics.SyncTransforms();
        object restoredResult = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        Assert.That(GetField<float>(restoredResult, "CoveredLengthMeters"), Is.EqualTo(4.06f).Within(.001f));

        // An overlap between two other valid plates removes both from the laid contributor
        // set; candidate feedback must use that same set when choosing a neighboring opening.
        SetDimensions(panels[0], 1500, 2400, 200, 2500, 1000);
        SetDimensions(panels[1], 1000, 2400, 200, 2500, 1000);
        SetDimensions(panels[2], 1560, 2400, 200, 2500, 1000);
        panels[0].transform.position = new Vector3(-1.35f, 0f, 0f);
        panels[1].transform.position = Vector3.zero;
        panels[2].transform.position = new Vector3(1.8f, 0f, 0f);
        SetDimensions(extra, 1000, 2400, 200, 2500, 1000);
        extra.transform.position = new Vector3(-1.85f, 0f, 0f);
        for (int i = 0; i < panels.Length; i++)
            panels[i].GetComponent<BoxCollider>().size = new Vector3(panels[i].GetComponent<BoxCollider>().size.x, .2f, 2.4f);
        extra.GetComponent<BoxCollider>().size = new Vector3(1f, .2f, 2.4f);
        Physics.SyncTransforms();
        object[] filteredNeighborArgs = { panels[1], Vector3.zero, Quaternion.identity, null, false, false };
        Assert.That((bool)placementDiagnostic.Invoke(null, filteredNeighborArgs), Is.True);
        Assert.That(GetField<object>(filteredNeighborArgs[3], "Kind").ToString(), Is.EqualTo("None"),
            "An overlapping, non-contributing neighbor must not fabricate a narrow opening in the preview.");
        object filteredNeighborResult = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        Assert.That(GetField<IList>(filteredNeighborResult, "ContributingPanels").Count, Is.EqualTo(2));
        object[] filteredCandidateDiagnosticArgs = { panels[1], filteredNeighborResult, null, false };
        Assert.That((bool)tRequirement.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == "TryGetPanelPresentationDiagnostic" && method.GetParameters().Length == 4)
            .Invoke(requirement, filteredCandidateDiagnosticArgs), Is.True);
        Assert.That(GetField<object>(filteredCandidateDiagnosticArgs[2], "Kind").ToString(), Is.Not.EqualTo("NarrowOpening"),
            "Laid-panel guidance uses the same overlap-filtered neighbors as F/E.");
        object[] overlapLeftDiagnosticArgs = { panels[0], filteredNeighborResult, null, false };
        Assert.That((bool)tRequirement.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == "TryGetPanelPresentationDiagnostic" && method.GetParameters().Length == 4)
            .Invoke(requirement, overlapLeftDiagnosticArgs), Is.True);
        Assert.That(GetField<object>(overlapLeftDiagnosticArgs[2], "Kind").ToString(), Is.EqualTo("Overlap"));
        object[] overlapRightDiagnosticArgs = { extra, filteredNeighborResult, null, false };
        Assert.That((bool)tRequirement.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == "TryGetPanelPresentationDiagnostic" && method.GetParameters().Length == 4)
            .Invoke(requirement, overlapRightDiagnosticArgs), Is.True);
        Assert.That(GetField<object>(overlapRightDiagnosticArgs[2], "Kind").ToString(), Is.EqualTo("Overlap"));

        // Restore the established preview fixture before its outside/end/cut guards.
        SetDimensions(panels[0], 1500, 2400, 200, 2500, 1000);
        SetDimensions(panels[1], 1000, 2400, 200, 2500, 1000);
        SetDimensions(panels[2], 1560, 2400, 200, 2500, 1000);
        panels[0].transform.position = new Vector3(-1.95f, 0f, 0f);
        panels[1].transform.position = Vector3.zero;
        panels[2].transform.position = new Vector3(1.36f, 0f, 0f);
        extra.transform.position = new Vector3(4.5f, 0f, 0f);
        Physics.SyncTransforms();

        // Preserve factual fastening feedback when a genuinely four-nailed plate has
        // a higher-priority narrow opening plus a later underside-height fault.
        SetDimensions(panels[0], 2500, 2400, 200, 2500, 1000);
        SetDimensions(panels[1], 2500, 2400, 200, 2500, 1000);
        SetDimensions(panels[2], 1460, 2400, 200, 2500, 1000);
        panels[0].transform.position = new Vector3(-2.03f, 0f, 0f);
        panels[1].transform.position = new Vector3(.47f, .041f, 0f);
        panels[2].transform.position = new Vector3(2.55f, 0f, 0f);
        Physics.SyncTransforms();
        object factResult = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        object[] physicalFactArgs = { panels[1], factResult, null, false };
        Assert.That((bool)tRequirement.GetMethod("TryGetPanelPresentationDiagnostic", PublicInstance)
            .Invoke(requirement, physicalFactArgs), Is.True);
        Assert.That(GetField<object>(physicalFactArgs[2], "Kind").ToString(), Is.EqualTo("NarrowOpening"));
        Assert.That((bool)physicalFactArgs[3], Is.True,
            "Actual EmbeddedPair attachments retain their 4/4 fact even while invalid-height geometry is excluded from secured area.");
        string physicalFactText = (string)tRequirement.GetMethod("FormatDiagnostic", BindingFlags.Static | BindingFlags.Public)
            .Invoke(null, new[] { physicalFactArgs[2], physicalFactArgs[3], false });
        Assert.That(physicalFactText, Does.Contain("4/4 nails").And.Contain("too narrow"));
        object[] raisedNarrowCandidateArgs =
            { panels[1], new Vector3(.47f, .041f, 0f), Quaternion.identity, null, false, false };
        Assert.That((bool)placementDiagnostic.Invoke(null, raisedNarrowCandidateArgs), Is.True);
        Assert.That(GetField<object>(raisedNarrowCandidateArgs[3], "Kind").ToString(), Is.EqualTo("NarrowOpening"),
            "The same real collider pose selects the same first issue before it is committed.");

        object[] outsideCandidateArgs = { panels[1], new Vector3(10f, 0f, 0f), Quaternion.identity, null, false, false };
        Assert.That((bool)placementDiagnostic.Invoke(null, outsideCandidateArgs), Is.True);
        Assert.That(GetField<object>(outsideCandidateArgs[3], "Kind").ToString(), Is.EqualTo("OutsideArea"),
            "An F/E candidate wholly outside the deck receives only the outside-area correction.");
        object[] partialCandidateArgs = { panels[1], new Vector3(3.4f, 0f, 0f), Quaternion.identity, null, false, false };
        Assert.That((bool)placementDiagnostic.Invoke(null, partialCandidateArgs), Is.True);
        Assert.That(GetField<object>(partialCandidateArgs[3], "Kind").ToString(), Is.EqualTo("LongitudinalBounds"),
            "A candidate whose real collider footprint partly crosses the deck end receives the end correction, not an outside-area message.");

        object cutSnapshot = tPanel.GetProperty("State", PublicInstance).GetValue(panels[1]);
        Set(cutSnapshot, "CutStarted", true);
        tPanel.GetMethod("ApplySnapshot", PrivateInstance).Invoke(panels[1], new[] { cutSnapshot });
        object[] unfinishedCandidateArgs = { panels[1], Vector3.zero, Quaternion.identity, null, false, false };
        Assert.That((bool)placementDiagnostic.Invoke(null, unfinishedCandidateArgs), Is.True);
        Assert.That(GetField<object>(unfinishedCandidateArgs[3], "Kind").ToString(), Is.EqualTo("UnfinishedCut"),
            "A started cut is diagnosed before otherwise valid placement geometry.");
        Set(cutSnapshot, "CutStarted", false);
        tPanel.GetMethod("ApplySnapshot", PrivateInstance).Invoke(panels[1], new[] { cutSnapshot });
        Physics.SyncTransforms();

        SetDimensions(panels[0], 2500, 2400, 200, 2500, 1000);
        SetDimensions(panels[1], 2500, 2400, 200, 2500, 1000);
        SetDimensions(panels[2], 1560, 2400, 200, 2500, 1000);
        panels[0].transform.position = new Vector3(-2.03f, 0f, 0f);
        panels[1].transform.position = new Vector3(.47f, 0f, 0f);
        panels[2].transform.position = new Vector3(2.50f, 0f, 0f);
        int[] originalLengths = { 2500, 2500, 1560 };
        for (int i = 0; i < panels.Length; i++)
            panels[i].GetComponent<BoxCollider>().size = new Vector3(originalLengths[i] * .001f, .2f, 2.4f);
        Physics.SyncTransforms();
        SetDimensions(panels[1], 2400, 2400, 200, 2500, 1000);
        panels[1].transform.position = new Vector3(.42f, 0f, 0f);
        Physics.SyncTransforms();
        result = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        Assert.That(GetField<bool>(result, "IsComplete"), Is.False, "A 100mm opening remains open during the active area stage.");
        GameObject interactor = Track(new GameObject("Aimed at real deck opening"));
        Camera aimedCamera = interactor.AddComponent<Camera>();
        aimedCamera.transform.position = frame.transform.position + new Vector3(1.67f, 2f, 0f);
        aimedCamera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        actualPanelPrompts.Clear();
        tPanel.GetMethod("GetInteractionPrompts", PublicInstance).Invoke(panels[1],
            new object[] { interactor.transform, actualPanelPrompts });
        string areaPrompt = FindPromptContaining(actualPanelPrompts, "too narrow");
        Assert.That(areaPrompt, Does.Contain("4/4 nails").And.Contain("too narrow"),
            "A narrow opening blocks the whole stage but preserves this secured plate's four-fastener status.");
        Assert.That(areaPrompt, Does.Not.Contain("Covered:").And.Not.Contain("Secured:"),
            "Per-area totals belong to the empty-opening hover, not a plate status.");
        markerPrompts.Clear();
        legacyMarker.GetType().GetMethod("GetInteractionPrompts", PublicInstance)
            .Invoke(legacyMarker, new object[] { interactor.transform, markerPrompts });
        Assert.That(FindPromptContaining(markerPrompts, "too narrow"), Is.EqualTo(areaPrompt),
            "The opted-in legacy marker entrypoint delegates to the area's shared per-panel diagnosis.");
        Assert.That(FindPromptContaining(markerPrompts, "Covered:"), Is.Empty);
        Assert.That(FindPromptContaining(markerPrompts, "Secured:"), Is.Empty);
        int delegatedActions = CountPromptType(markerPrompts, "Interact");
        Assert.That(delegatedActions, Is.LessThanOrEqualTo(1), "Delegation must not duplicate actionable prompts.");
        markerPrompts.Clear();
        legacyMarker.GetType().GetMethod("AddPlacementInformationPrompts", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(legacyMarker, new object[] { markerPrompts });
        Assert.That(FindPromptContaining(markerPrompts, "too narrow"), Is.EqualTo(areaPrompt));
        Assert.That(CountPromptType(markerPrompts, "Information"), Is.EqualTo(1));
        Assert.That(CountPromptType(markerPrompts, "Interact"), Is.Zero,
            "Placement information is informational only; it must not inject a pickup into a list that already contains Drop.");
        GameObject hudRoot = Track(new GameObject("Real area prompt HUD formatter"));
        Component hud = hudRoot.AddComponent(T("LookingAtComponentUI"));
        IList hudPrompts = (IList)hud.GetType().GetField("prompts", AllInstance).GetValue(hud);
        foreach (object prompt in actualPanelPrompts) hudPrompts.Add(prompt);
        string formattedAreaPrompt = (string)hud.GetType().GetMethod("FormatPrompts", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(hud, null);
        Assert.That(formattedAreaPrompt.Split('\n').Length, Is.LessThanOrEqualTo(3));
        Assert.That(formattedAreaPrompt, Does.Contain("4/4 nails").And.Contain("too narrow"),
            "The real provider output must survive the default HUD line budget.");
        interactor.AddComponent(T("PlayerInputNew"));
        Component playerInteraction = interactor.AddComponent(T("PlayerInteractionNew"));
        GameObject uiRoot = Track(new GameObject("Actual empty-target area HUD"));
        uiRoot.transform.SetParent(interactor.transform, false);
        Component actualHud = uiRoot.AddComponent(T("LookingAtComponentUI"));
        GameObject hudCanvas = Track(new GameObject("Actual empty-target HUD canvas", typeof(RectTransform), typeof(Canvas)));
        hudCanvas.transform.SetParent(uiRoot.transform, false);
        Component hudText = hudCanvas.AddComponent(Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro", true));
        Set(actualHud, "componentInfoText", hudText);
        yield return null;
        actualHud.GetType().GetMethod("Update", PrivateInstance).Invoke(actualHud, null);
        string emptyTargetHudText = (string)hudText.GetType().GetProperty("text", PublicInstance).GetValue(hudText);
        Assert.That(playerInteraction.GetType().GetProperty("CurrentTarget", PublicInstance).GetValue(playerInteraction), Is.Null);
        Assert.That(emptyTargetHudText, Does.Contain("Covered:").And.Contain("Secured:").And.Contain("100 mm"),
            "The active-stage HUD must show separate covered/secured totals and the selected opening while the actual look target is empty.");
        Set(manager, "currentBridgeBuildingStageIndex", 0);
        actualHud.GetType().GetMethod("Update", PrivateInstance).Invoke(actualHud, null);
        Assert.That((string)hudText.GetType().GetProperty("text", PublicInstance).GetValue(hudText), Is.Empty,
            "The same empty-target HUD must hide area guidance before the stage activates.");
        SetDimensions(panels[1], 2500, 2400, 200, 2500, 1000);
        panels[1].transform.position = new Vector3(.47f, 0f, 0f);
        Physics.SyncTransforms();
        Set(manager, "currentBridgeBuildingStageIndex", 0);
        result = T("BridgeDeckAreaStageAssessment").GetMethod("Evaluate").Invoke(assessment, new[] { requirement });
        Assert.That(GetField<bool>(result, "IsComplete"), Is.True, "Restore the secured full-area fixture before stage lifecycle assertions.");
        Assert.That((bool)managerType.GetMethod("IsAreaRequirementPresented", PublicInstance).Invoke(manager, new[] { requirement }),
            Is.False, "A future deck stage must not expose mounting guidance or assessment to players.");
        object[] placementArgs = { panels[0], panels[0].transform.position, panels[0].transform.rotation, false, false };
        string placementMessage = (string)tRequirement.GetMethod("GetPlacementCandidateMessage", BindingFlags.Static | BindingFlags.Public)
            .Invoke(null, placementArgs);
        Assert.That(placementMessage, Is.Empty);
        Assert.That((bool)placementArgs[3], Is.True);
        Assert.That((bool)placementArgs[4], Is.False);
        Assert.That((int)managerType.GetField("currentBridgeBuildingStageIndex", PrivateInstance).GetValue(manager), Is.EqualTo(0));
        object componentData0 = data.GetValue(0);
        Set(componentData0, "isMounted", true); Set(componentData0, "isAssembled", true); data.SetValue(componentData0, 0);
        Set(manager, "currentBridgeBuildingStageIndex", 1);
        Assert.That((bool)managerType.GetMethod("IsAreaRequirementActive", PublicInstance).Invoke(manager, new[] { requirement }), Is.True);
        extra.transform.position = new Vector3(8f, 0f, 0f);
        markerPrompts.Clear();
        tPanel.GetMethod("GetInteractionPrompts", PublicInstance).Invoke(extra, new object[] { null, markerPrompts });
        Assert.That(CountPromptType(markerPrompts, "Information"), Is.Zero,
            "A stock plate wholly outside the deck must not receive mounting diagnosis from its real provider.");
        extra.transform.position = new Vector3(4.5f, 0f, 0f);
        Physics.SyncTransforms();
        Assert.That((bool)tryExtract.Invoke(nails[1], null), Is.False,
            "The last-nail removal path must freshly assess and finalize a complete active stage before extraction.");
        Assert.That((int)managerType.GetField("currentBridgeBuildingStageIndex", PrivateInstance).GetValue(manager), Is.EqualTo(2),
            "Activating an already-complete area advances exactly one stage and never skips the unfinished later component.");
        Assert.That((bool)tBridgeComponent.GetProperty("IsMounted", PublicInstance).GetValue(adapter), Is.True);
        Assert.That((bool)tBridgeComponent.GetProperty("IsAssembled", PublicInstance).GetValue(adapter), Is.True,
            "Offline adapter completion mirrors authoritative GameplayManager progress without legacy events.");
        Assert.That((bool)tPanel.GetProperty("IsLockedForCompletedArea").GetValue(panels[0]), Is.True,
            "The area locks contributors only when global stage progress advances.");
        foreach (Component panel in panels)
        {
            Assert.That((bool)tPanel.GetProperty("IsLockedForCompletedArea").GetValue(panel), Is.True);
            Assert.That((bool)tPanel.GetMethod("SetAreaLock", PublicInstance).Invoke(panel, new object[] { false }), Is.False,
                "A finalized contributing plate cannot be unlocked by reset paths.");
        }
        Assert.That(Read(nails[1], "State").ToString(), Is.EqualTo("EmbeddedPair"),
            "The nail that raced stage finalization remains attached to both registered targets.");
        Assert.That(tNail.GetMethod("RemoveForPanelReset", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(nails[1], new object[] { null }), Is.Null,
            "Reset-driven embedded nail removal is also refused after the contributors lock.");
        Assert.That((bool)tryExtract.Invoke(nails[1], null), Is.False,
            "Crowbar extraction remains refused after contributor finalization.");
        Assert.That((bool)tPanel.GetProperty("IsLockedForCompletedArea").GetValue(extra), Is.False,
            "An extra plate outside the completed contributor set stays free.");
        actualPanelPrompts.Clear();
        tPanel.GetMethod("GetInteractionPrompts", PublicInstance).Invoke(panels[0], new object[] { null, actualPanelPrompts });
        Assert.That(FindPromptDescription(actualPanelPrompts, "Locked after completion"), Is.Not.Empty,
            "A contributor remains visibly locked after the area stage advances into the future.");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator AuthoredPlayerHudExpandsForAreaRowsAndRestoresLegacySize()
    {
#if !UNITY_EDITOR
        Assert.Fail("Inspects the authored PlayerNew HUD prefab.");
        yield break;
#else
        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        Assert.That(playerPrefab, Is.Not.Null);
        GameObject inactiveHolder = Track(new GameObject("Inactive authored HUD holder"));
        inactiveHolder.SetActive(false);
        GameObject player = Track(Object.Instantiate(playerPrefab, inactiveHolder.transform));
        Component ui = player.GetComponentInChildren(T("LookingAtComponentUI"), true);
        Assert.That(ui, Is.Not.Null);
        Component text = (Component)ui.GetType().GetField("componentInfoText", PrivateInstance).GetValue(ui);
        GameObject visual = (GameObject)ui.GetType().GetField("visualRoot", PrivateInstance).GetValue(ui);
        RectTransform uiRect = ui.GetComponent<RectTransform>();
        RectTransform textRect = text.GetComponent<RectTransform>();
        RectTransform visualRect = visual != null ? visual.GetComponent<RectTransform>() : null;
        Vector2 originalUi = uiRect.sizeDelta, originalText = textRect.sizeDelta;
        Vector2 originalVisual = visualRect != null ? visualRect.sizeDelta : Vector2.zero;
        float textWidth = textRect.rect.width;
        float originalTextHeight = textRect.rect.height;
        Vector2 preferred = (Vector2)text.GetType().GetMethod("GetPreferredValues", new[] { typeof(string), typeof(float), typeof(float) })
            .Invoke(text, new object[] { "Covered: 2.50 m\nSecured: 0.00 m\nOpening: 80–80 mm too narrow (min 400 mm). Change spacing.", textWidth, 0f });
        float preferredHeight = preferred.y;

        string areaRows = "Covered: 2.50 m\nSecured: 0.00 m\nOpening: 80–80 mm too narrow (min 400 mm). Change spacing.";
        ui.GetType().GetMethod("SetPromptText", PrivateInstance).Invoke(ui, new object[] { areaRows, true });
        float expectedGrowth = Mathf.Max(0f, preferredHeight + 8f - originalTextHeight);
        Assert.That(textRect.sizeDelta, Is.EqualTo(originalText),
            "The authored stretched TMP text keeps its original -75×-10 inset offsets.");
        Assert.That(uiRect.sizeDelta.y, Is.EqualTo(originalUi.y + expectedGrowth).Within(2f),
            "Only the authored 500×50 layout owner grows, by the preferred text height plus its padding.");
        if (visualRect != null && visualRect != uiRect)
            Assert.That(visualRect.sizeDelta, Is.EqualTo(originalVisual), "The stretched HUD background follows the root without a second size increase.");
        bool isOverflowing = (bool)text.GetType().GetProperty("isTextOverflowing").GetValue(text);
        Assert.That(isOverflowing, Is.False, "All area feedback must fit in the resized authored HUD.");

        ui.GetType().GetMethod("SetPromptText", PrivateInstance).Invoke(ui, new object[] { "E - Pick up", false });
        Assert.That(textRect.sizeDelta, Is.EqualTo(originalText));
        Assert.That(uiRect.sizeDelta, Is.EqualTo(originalUi));
        if (visualRect != null) Assert.That(visualRect.sizeDelta, Is.EqualTo(originalVisual));
        yield return null;
#endif
    }

    private static int CountAreaPanels(Scene scene)
    {
        return GetAreaPanels(scene).Count;
    }

    private static string FindPromptDescription(IList prompts, string prefix)
    {
        foreach (object prompt in prompts)
        {
            string description = (string)prompt.GetType().GetField("Description").GetValue(prompt);
            if (description.StartsWith(prefix, System.StringComparison.Ordinal)) return description;
        }
        return string.Empty;
    }

    private static string FindPromptContaining(IList prompts, string text)
    {
        foreach (object prompt in prompts)
        {
            string description = (string)prompt.GetType().GetField("Description").GetValue(prompt);
            if (description.Contains(text)) return description;
        }
        return string.Empty;
    }

    private static int CountPromptType(IList prompts, string actionKind)
    {
        int count = 0;
        foreach (object prompt in prompts)
            if (((object)prompt.GetType().GetField("ActionKind").GetValue(prompt)).ToString() == actionKind) count++;
        return count;
    }

    private static IEnumerator Until(System.Func<bool> condition, string description, float timeoutSeconds = 20f)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, $"Timed out waiting for {description}.");
    }

    private static Component FindSingle(Scene scene, string typeName)
    {
        System.Type componentType = typeName.Contains(",") ? System.Type.GetType(typeName) : T(typeName);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Component[] components = root.GetComponentsInChildren(componentType, true);
            if (components.Length > 0) return components[0];
        }
        return null;
    }

    private static Component GetLocalPlayerObject(Type managerType, Component manager)
    {
        object localClient = managerType.GetProperty("LocalClient", PublicInstance).GetValue(manager);
        return localClient != null
            ? (Component)localClient.GetType().GetProperty("PlayerObject", PublicInstance).GetValue(localClient)
            : null;
    }

    private static List<Component> GetAreaPanels(Scene scene)
    {
        List<Component> panels = new();
        foreach (GameObject root in scene.GetRootGameObjects())
            panels.AddRange(root.GetComponentsInChildren(T("BridgeDeckAreaPanel"), true));
        return panels;
    }

    private static void SetCarpenterDialThroughCrank(Component factory, object dimension, float requestedValue)
    {
        MethodInfo countMethod = factory.GetType().GetMethod("GetDimensionStepCount", PublicInstance);
        MethodInfo valueMethod = factory.GetType().GetMethod("GetDimensionValueForStep", PublicInstance);
        int stepCount = (int)countMethod.Invoke(factory, new[] { dimension });
        int matchingStep = -1;
        for (int step = 0; step < stepCount; step++)
            if (Mathf.Approximately((float)valueMethod.Invoke(factory, new object[] { dimension, step }), requestedValue))
            { matchingStep = step; break; }
        Assert.That(matchingStep, Is.GreaterThanOrEqualTo(0), $"The copied Carpenter dial must reach required value {requestedValue}.");
        factory.GetType().GetMethod("RequestBeginDimensionAdjustment", PublicInstance)
            .Invoke(factory, new[] { dimension, null });
        factory.GetType().GetMethod("RequestSetDimensionStep", PublicInstance)
            .Invoke(factory, new object[] { dimension, matchingStep });
        factory.GetType().GetMethod("RequestEndDimensionAdjustment", PublicInstance)
            .Invoke(factory, new[] { dimension });
    }

    private Component CreateGirder(GameObject root, System.Type bridgeComponentType, float z)
    {
        root.SetActive(false);
        root.transform.position = new Vector3(0f, -.05f, z);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(6.56f, .1f, .2f);
        Rigidbody body = root.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
        Component component = root.AddComponent(bridgeComponentType);
        Set(component, "startFullyCompleted", true);
        root.SetActive(true);
        component.GetType().GetMethod("ApplyInitialCompletedState", AllInstance).Invoke(component, null);
        ((Behaviour)component).enabled = false;
        box.enabled = true;
        return component;
    }

    private static void AimNailAtPanel(Component nail, Transform panel, Vector2 xz)
    {
        Transform tip = (Transform)nail.GetType().GetProperty("Tip", PublicInstance).GetValue(nail);
        nail.transform.rotation = Quaternion.FromToRotation(nail.transform.forward, -panel.up) * nail.transform.rotation;
        Vector3 entry = panel.TransformPoint(new Vector3(xz.x, .2f, xz.y));
        nail.transform.position += entry + panel.up * .005f - tip.position;
        Physics.SyncTransforms();
    }

    private static void SetDimensions(Component panel, int length, int width, int thickness, int sourceLength, int sourceMass)
    {
        System.Type snapshotType = T("BridgeDeckAreaPanelSnapshot");
        object snapshot = System.Activator.CreateInstance(snapshotType);
        Set(snapshot, "LengthMillimeters", length); Set(snapshot, "WidthMillimeters", width);
        Set(snapshot, "ThicknessMillimeters", thickness); Set(snapshot, "SourceLengthMillimeters", sourceLength);
        Set(snapshot, "SourceMassGrams", sourceMass); Set(snapshot, "OperatorClientId", ulong.MaxValue);
        T("BridgeDeckAreaPanel").GetMethod("ApplySnapshot", PrivateInstance).Invoke(panel, new[] { snapshot });
    }

    private static System.Array CreateStages(System.Type managerType, int[] componentIndexes)
    {
        System.Type stageType = managerType.GetField("bridgeBuildingStages", PrivateInstance).FieldType.GetElementType();
        System.Array stages = System.Array.CreateInstance(stageType, componentIndexes.Length);
        for (int i = 0; i < componentIndexes.Length; i++)
        {
            object stage = System.Activator.CreateInstance(stageType);
            Set(stage, "bridgeComponentDataIndexes", new[] { componentIndexes[i] });
            stages.SetValue(stage, i);
        }
        return stages;
    }

    private static T GetField<T>(object target, string name) => (T)target.GetType().GetField(name, AllInstance).GetValue(target);
    private static string ReadViewText(GameObject root)
    {
        Type textType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", true);
        System.Text.StringBuilder text = new();
        foreach (Component component in root.GetComponentsInChildren(textType, true))
        {
            if (!component.gameObject.activeInHierarchy) continue;
            string value = (string)textType.GetProperty("text", PublicInstance).GetValue(component);
            if (text.Length > 0) text.Append('\n');
            text.Append(value);
        }
        return text.ToString();
    }
    private static object Read(Component target, string property) => target.GetType().GetProperty(property, PublicInstance).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, AllInstance).SetValue(target, value);
    private GameObject Track(GameObject value) { roots.Add(value); return value; }
    private static System.Type T(string name) => System.Type.GetType(name + ", Assembly-CSharp", true);

    [TearDown]
    public void TearDown()
    {
        for (int i = roots.Count - 1; i >= 0; i--) if (roots[i] != null) Object.Destroy(roots[i]);
        for (int i = profiles.Count - 1; i >= 0; i--) if (profiles[i] != null) Object.Destroy(profiles[i]);
        roots.Clear(); profiles.Clear();
        if (managerInstanceField != null) managerInstanceField.SetValue(null, previousManagerInstance);
    }

    [UnityTearDown]
    public IEnumerator UnloadAuthoredAreaSceneAfterSmoke()
    {


        if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            SceneManager.SetActiveScene(previousActiveScene);
        if (openedAuthoredAreaScene && loadedAuthoredAreaScene.IsValid() && loadedAuthoredAreaScene.isLoaded)
        {
            AsyncOperation unload = SceneManager.UnloadSceneAsync(loadedAuthoredAreaScene);
            while (unload != null && !unload.isDone) yield return null;
        }
        openedAuthoredAreaScene = false;
        loadedAuthoredAreaScene = default;
        previousActiveScene = default;
    }
}
