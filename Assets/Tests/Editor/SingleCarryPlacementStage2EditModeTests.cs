using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class SingleCarryPlacementStage2EditModeTests
{
    private GameObject source;
    private GameObject surface;
    private GameObject ghostSource;
    private object ghost;
    private Material testMaterial;

    [TearDown]
    public void TearDown()
    {
        if (ghost != null) ghost.GetType().GetMethod("Dispose").Invoke(ghost, null);
        if (ghostSource != null) UnityEngine.Object.DestroyImmediate(ghostSource);
        if (testMaterial != null) UnityEngine.Object.DestroyImmediate(testMaterial);
        if (surface != null) UnityEngine.Object.DestroyImmediate(surface);
        if (source != null) UnityEngine.Object.DestroyImmediate(source);
    }

    [Test]
    public void NoSurface_PlacesAimPointAtConfiguredDistance()
    {
        source = new GameObject("source");
        source.AddComponent<BoxCollider>();
        Physics.SyncTransforms();

        object pose = Calculate(new Ray(new Vector3(20f, 5f, 0f), Vector3.forward), 2f, Quaternion.identity, Quaternion.identity);

        Vector3 position = (Vector3)Property(pose, "Position");
        Assert.That(position.x, Is.EqualTo(20f).Within(0.0001f));
        Assert.That(position.y, Is.EqualTo(5f).Within(0.0001f));
        Assert.That(position.z, Is.EqualTo(2f).Within(0.0001f));
        AssertStatus(pose, "NoObstacle");
    }

    [Test]
    public void ObliqueRay_AnchorsTangentPositionToHitPointAndSupportsOffCenterBox()
    {
        CreateFloor();
        source = new GameObject("off-center source");
        BoxCollider box = source.AddComponent<BoxCollider>();
        box.center = new Vector3(0.35f, 0.2f, -0.4f);
        box.size = new Vector3(0.4f, 0.6f, 0.8f);
        Ray ray = new Ray(new Vector3(1f, 1.5f, 0f), new Vector3(-0.4f, -0.916515f, 0f));
        Physics.SyncTransforms();
        RaycastHit[] physicsHits = Physics.RaycastAll(ray, 2f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        RaycastHit expectedHit = default;
        bool foundExpectedSurface = false;
        float nearestExpectedDistance = float.PositiveInfinity;
        foreach (RaycastHit candidate in physicsHits)
        {
            if (candidate.collider == null || candidate.collider.transform == source.transform
                || candidate.collider.transform.IsChildOf(source.transform)
                || candidate.distance >= nearestExpectedDistance) continue;
            expectedHit = candidate;
            nearestExpectedDistance = candidate.distance;
            foundExpectedSurface = true;
        }
        Assert.That(foundExpectedSurface, Is.True);

        object pose = Calculate(ray, 2f, Quaternion.identity, Quaternion.identity);
        Vector3 position = (Vector3)Property(pose, "Position");

        Assert.That(position.x, Is.EqualTo(expectedHit.point.x).Within(0.002f));
        Assert.That(position.z, Is.EqualTo(expectedHit.point.z).Within(0.002f));
        Assert.That(position.y + box.center.y - box.size.y * 0.5f, Is.EqualTo(expectedHit.point.y).Within(0.002f));
        AssertStatus(pose, "SurfaceSupported");
        Assert.That(source.transform.position, Is.EqualTo(Vector3.zero), "The solver must not move the actual held object.");
    }

    [Test]
    public void NonUniformlyScaledSphere_UsesUnityMaxAxisRadius()
    {
        CreateFloor();
        source = new GameObject("scaled sphere") { transform = { position = new Vector3(80f, 0f, 0f), localScale = new Vector3(2f, 1f, 3f) } };
        SphereCollider sphere = source.AddComponent<SphereCollider>();
        sphere.radius = 0.4f;
        Physics.SyncTransforms();

        object pose = Calculate(DownwardRay(), 2f, Quaternion.identity, Quaternion.identity);

        Assert.That(((Vector3)Property(pose, "Position")).y, Is.EqualTo(1.2f).Within(0.002f));
        AssertStatus(pose, "SurfaceSupported");
    }

    [Test]
    public void NonUniformlyScaledCapsule_UsesScaledHeightAndMaximumCrossAxisRadius()
    {
        CreateFloor();
        source = new GameObject("scaled capsule") { transform = { position = new Vector3(80f, 0f, 0f), localScale = new Vector3(1f, 2f, 3f) } };
        CapsuleCollider capsule = source.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.radius = 0.5f;
        capsule.height = 2f;
        Physics.SyncTransforms();

        object pose = Calculate(DownwardRay(), 2f, Quaternion.identity, Quaternion.identity);

        // Unity scales radius by max(X,Z)=3 and height by Y=2: r=1.5, h=4, support=1.5+0.5.
        Assert.That(((Vector3)Property(pose, "Position")).y, Is.EqualTo(2f).Within(0.002f));
        AssertStatus(pose, "SurfaceSupported");
    }

    [Test]
    public void NonUniformlyScaledCapsule_WallSupportUsesMaxCrossAxisRadius()
    {
        surface = new GameObject("wall");
        BoxCollider wall = surface.AddComponent<BoxCollider>();
        wall.size = new Vector3(0.2f, 20f, 20f);
        surface.transform.position = new Vector3(0.1f, 0f, 0f);
        source = new GameObject("wide capsule") { transform = { position = new Vector3(80f, 0f, 0f), localScale = new Vector3(1f, 2f, 3f) } };
        CapsuleCollider capsule = source.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.radius = 0.5f;
        capsule.height = 2f;
        Physics.SyncTransforms();

        object pose = Calculate(new Ray(new Vector3(1f, 0f, 0f), Vector3.left), 2f, Quaternion.identity, Quaternion.identity);

        // PhysX radius uses max cross-axis scale (Z=3), so support along X is 1.5 m.
        Assert.That(((Vector3)Property(pose, "Position")).x, Is.EqualTo(1.7f).Within(0.002f));
        AssertStatus(pose, "SurfaceSupported");
    }

    [Test]
    public void RotatedElongatedChildBox_UsesFullRotationAndScaledOffset()
    {
        CreateFloor();
        source = new GameObject("scaled hierarchy");
        GameObject child = new GameObject("elongated child");
        child.transform.SetParent(source.transform, false);
        child.transform.localScale = new Vector3(2f, 1f, 1f);
        child.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        BoxCollider box = child.AddComponent<BoxCollider>();
        box.size = new Vector3(2f, 0.2f, 0.2f);
        Physics.SyncTransforms();

        object pose = Calculate(DownwardRay(), 2f, Quaternion.identity,
            Quaternion.AngleAxis(90f, Vector3.forward));

        Assert.That(((Vector3)Property(pose, "Position")).y, Is.EqualTo(2f).Within(0.003f));
        AssertStatus(pose, "SurfaceSupported");
    }

    [Test]
    public void ShearedColliderHierarchy_IsExplicitlyUnsupported()
    {
        CreateFloor();
        source = new GameObject("sheared root");
        GameObject parent = new GameObject("nonuniform parent");
        parent.transform.SetParent(source.transform, false);
        parent.transform.localScale = new Vector3(2f, 1f, 1f);
        GameObject child = new GameObject("rotated collider");
        child.transform.SetParent(parent.transform, false);
        child.transform.localRotation = Quaternion.AngleAxis(35f, Vector3.forward);
        child.AddComponent<BoxCollider>();
        Physics.SyncTransforms();

        object pose = Calculate(DownwardRay(), 2f, Quaternion.identity, Quaternion.identity);

        AssertStatus(pose, "UnsupportedCollider");
    }

    [Test]
    public void TriggerAndIgnoredOwnerAreSkippedAndNearestSolidSurfaceWins()
    {
        surface = new GameObject("near solid");
        BoxCollider near = surface.AddComponent<BoxCollider>();
        near.size = new Vector3(4f, 0.2f, 0.2f);
        near.transform.position = new Vector3(0f, 1f, 1f);
        GameObject farther = new GameObject("far solid");
        BoxCollider far = farther.AddComponent<BoxCollider>();
        far.size = new Vector3(4f, 0.2f, 0.2f);
        far.transform.position = new Vector3(0f, 1f, 2f);
        GameObject trigger = new GameObject("trigger");
        BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
        triggerCollider.isTrigger = true;
        trigger.transform.position = new Vector3(0f, 1f, 0.5f);
        GameObject owner = new GameObject("ignored owner");
        BoxCollider ownerCollider = owner.AddComponent<BoxCollider>();
        ownerCollider.size = new Vector3(2f, 2f, 0.2f);
        owner.transform.position = new Vector3(0f, 1f, 0f);
        source = new GameObject("held shape") { transform = { position = new Vector3(50f, 0f, 0f) } };
        source.AddComponent<BoxCollider>();
        Physics.SyncTransforms();

        object pose = Calculate(new Ray(new Vector3(0f, 1f, -1f), Vector3.forward), 2f,
            Quaternion.identity, Quaternion.identity, owner.transform);

        Assert.That(Property(pose, "SurfaceCollider"), Is.SameAs(near));
        Assert.That(((Vector3)Property(pose, "Position")).z, Is.EqualTo(near.transform.position.z - 0.1f - 0.5f).Within(0.002f));
        UnityEngine.Object.DestroyImmediate(farther);
        UnityEngine.Object.DestroyImmediate(trigger);
        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void SolverSupportsFloorWallAndCeilingFaces()
    {
        source = new GameObject("unit placement box") { transform = { position = new Vector3(80f, 0f, 0f) } };
        source.AddComponent<BoxCollider>();

        surface = new GameObject("floor");
        BoxCollider plane = surface.AddComponent<BoxCollider>();
        plane.size = new Vector3(20f, 0.2f, 20f);
        surface.transform.position = new Vector3(0f, -0.1f, 0f);
        Physics.SyncTransforms();
        object pose = Calculate(new Ray(new Vector3(0f, 1f, 0f), Vector3.down), 2f, Quaternion.identity, Quaternion.identity);
        Assert.That(((Vector3)Property(pose, "Position")).y, Is.EqualTo(0.5f).Within(0.002f));
        AssertStatus(pose, "SurfaceSupported");
        UnityEngine.Object.DestroyImmediate(surface);

        surface = new GameObject("wall");
        plane = surface.AddComponent<BoxCollider>();
        plane.size = new Vector3(20f, 20f, 0.2f);
        surface.transform.position = new Vector3(0f, 0f, 0.1f);
        Physics.SyncTransforms();
        pose = Calculate(new Ray(new Vector3(0f, 0f, -1f), Vector3.forward), 2f, Quaternion.identity, Quaternion.identity);
        Assert.That(((Vector3)Property(pose, "Position")).z, Is.EqualTo(-0.5f).Within(0.002f));
        AssertStatus(pose, "SurfaceSupported");
        UnityEngine.Object.DestroyImmediate(surface);

        surface = new GameObject("ceiling");
        plane = surface.AddComponent<BoxCollider>();
        plane.size = new Vector3(20f, 0.2f, 20f);
        surface.transform.position = new Vector3(0f, 0.1f, 0f);
        Physics.SyncTransforms();
        pose = Calculate(new Ray(new Vector3(0f, -1f, 0f), Vector3.up), 2f, Quaternion.identity, Quaternion.identity);
        Assert.That(((Vector3)Property(pose, "Position")).y, Is.EqualTo(-0.5f).Within(0.002f));
        AssertStatus(pose, "SurfaceSupported");
    }

    [Test]
    public void SurfaceBeyondAimDistance_DoesNotShortenAimDistanceOrBecomeSupport()
    {
        surface = new GameObject("far surface");
        BoxCollider farCollider = surface.AddComponent<BoxCollider>();
        farCollider.size = new Vector3(4f, 4f, 0.1f);
        surface.transform.position = new Vector3(0f, 0f, 3f);
        source = new GameObject("source") { transform = { position = new Vector3(80f, 0f, 0f) } };
        source.AddComponent<BoxCollider>();
        Physics.SyncTransforms();

        object pose = Calculate(new Ray(Vector3.zero, Vector3.forward), 2f, Quaternion.identity, Quaternion.identity);

        Assert.That(((Vector3)Property(pose, "Position")).z, Is.EqualTo(2f).Within(0.0001f));
        AssertStatus(pose, "NoObstacle");
    }

    [Test]
    public void PhysicalColliderOnIgnoreRaycastLayerStillProvidesSurface()
    {
        surface = new GameObject("layer two blocker") { layer = 2 };
        BoxCollider collider = surface.AddComponent<BoxCollider>();
        collider.size = new Vector3(4f, 4f, 0.2f);
        surface.transform.position = new Vector3(0f, 0f, 1f);
        source = new GameObject("source") { transform = { position = new Vector3(80f, 0f, 0f) } };
        source.AddComponent<BoxCollider>();
        Physics.SyncTransforms();

        object pose = Calculate(new Ray(new Vector3(0f, 0f, -1f), Vector3.forward), 2f, Quaternion.identity, Quaternion.identity);

        Assert.That(Property(pose, "SurfaceCollider"), Is.SameAs(collider));
        Assert.That(((Vector3)Property(pose, "Position")).z, Is.EqualTo(0.4f).Within(0.002f));
        AssertStatus(pose, "SurfaceSupported");
    }

    [Test]
    public void MoreThanInitialRaycastBufferHits_StillSelectsNearestSurface()
    {
        var layers = new System.Collections.Generic.List<GameObject>();
        try
        {
            for (int i = 0; i < 40; i++)
            {
                GameObject layer = new GameObject("ray layer " + i);
                BoxCollider collider = layer.AddComponent<BoxCollider>();
                collider.size = new Vector3(2f, 2f, 0.01f);
                layer.transform.position = new Vector3(0f, 0f, i * 0.025f);
                layers.Add(layer);
            }
            source = new GameObject("source") { transform = { position = new Vector3(80f, 0f, 0f) } };
            source.AddComponent<BoxCollider>();
            Physics.SyncTransforms();

            object pose = Calculate(new Ray(new Vector3(0f, 0f, -1f), Vector3.forward), 2f, Quaternion.identity, Quaternion.identity);
            Assert.That(Property(pose, "SurfaceCollider"), Is.SameAs(layers[0].GetComponent<Collider>()));
            AssertStatus(pose, "SurfaceSupported");
        }
        finally
        {
            foreach (GameObject layer in layers) if (layer != null) UnityEngine.Object.DestroyImmediate(layer);
        }
    }

    [Test]
    public void UnreadableMeshCollider_IsExplicitlyUnsupported()
    {
        CreateFloor();
        source = new GameObject("unreadable mesh") { transform = { position = new Vector3(80f, 0f, 0f) } };
        Mesh mesh = new Mesh { name = "unreadable" };
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2 };
        MeshCollider collider = source.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        mesh.UploadMeshData(true);
        Physics.SyncTransforms();

        object pose = Calculate(DownwardRay(), 2f, Quaternion.identity, Quaternion.identity);

        AssertStatus(pose, "UnsupportedCollider");
        UnityEngine.Object.DestroyImmediate(mesh);
    }

    [Test]
    public void ReadableMeshSupport_UsesReferencedGeometryNotUnusedVertices()
    {
        CreateFloor();
        source = new GameObject("readable mesh") { transform = { position = new Vector3(80f, 0f, 0f) } };
        Mesh mesh = new Mesh { name = "referenced-triangle" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f),
            new Vector3(0f, -100f, 0f)
        };
        mesh.triangles = new[] { 0, 1, 2 };
        source.AddComponent<MeshCollider>().sharedMesh = mesh;
        Physics.SyncTransforms();

        object pose = Calculate(DownwardRay(), 2f, Quaternion.identity, Quaternion.identity);

        Assert.That(((Vector3)Property(pose, "Position")).y, Is.EqualTo(0f).Within(0.002f));
        AssertStatus(pose, "SurfaceSupported");
        UnityEngine.Object.DestroyImmediate(mesh);
    }

    [Test]
    public void GhostCopiesHierarchyAndVisibleMeshWithoutScriptsOrCollidersAndPreservesSourceMaterial()
    {
        ghostSource = new GameObject("visual source");
        ghostSource.transform.localScale = new Vector3(1.5f, 2f, 0.75f);
        GameObject child = GameObject.CreatePrimitive(PrimitiveType.Cube);
        child.name = "contents";
        child.transform.SetParent(ghostSource.transform, false);
        child.transform.localPosition = new Vector3(0.2f, 0.3f, -0.4f);
        child.transform.localScale = new Vector3(0.25f, 0.5f, 0.75f);
        Collider originalCollider = child.GetComponent<Collider>();
        Material originalMaterial = child.GetComponent<Renderer>().sharedMaterial;
        Color originalColor = originalMaterial.color;
        Type ghostType = Type.GetType("SingleCarryPlacementGhost, Assembly-CSharp");
        ghost = Activator.CreateInstance(ghostType, new object[] { ghostSource });
        GameObject root = (GameObject)Property(ghost, "Root");

        Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
        Transform previewChild = root.transform.Find("contents Placement Preview");
        Assert.That(previewChild, Is.Not.Null);
        Assert.That(previewChild.localPosition, Is.EqualTo(child.transform.localPosition));
        Assert.That(previewChild.localScale, Is.EqualTo(child.transform.localScale));
        Assert.That(previewChild.GetComponent<MeshRenderer>(), Is.Not.Null);
        Assert.That(originalCollider, Is.Not.Null);
        Assert.That(originalMaterial.color, Is.EqualTo(originalColor));
        Assert.That(previewChild.GetComponent<Renderer>().sharedMaterial, Is.Not.SameAs(originalMaterial));
        Assert.That(previewChild.GetComponent<Renderer>().sharedMaterial.color.a, Is.LessThanOrEqualTo(0.52f));
    }

    [Test]
    public void GhostAlphaClipUsesSourceAlphaBeforeApplyingPreviewOpacity()
    {
        ghostSource = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Shader sourceShader = Shader.Find("Universal Render Pipeline/Lit");
        Assert.That(sourceShader, Is.Not.Null);
        testMaterial = new Material(sourceShader);
        testMaterial.SetColor("_BaseColor", new Color(0.5f, 0.6f, 0.7f, 0.9f));
        testMaterial.SetFloat("_AlphaClip", 1f);
        testMaterial.SetFloat("_Cutoff", 0.8f);
        ghostSource.GetComponent<Renderer>().sharedMaterial = testMaterial;
        Type ghostType = Type.GetType("SingleCarryPlacementGhost, Assembly-CSharp");
        ghost = Activator.CreateInstance(ghostType, new object[] { ghostSource });
        GameObject root = (GameObject)Property(ghost, "Root");
        Material preview = root.GetComponentInChildren<Renderer>().sharedMaterial;

        Assert.That(preview.GetFloat("_AlphaClip"), Is.EqualTo(1f));
        Assert.That(preview.GetFloat("_Cutoff"), Is.EqualTo(0.8f));
        Assert.That(preview.GetFloat("_SourceAlpha"), Is.EqualTo(0.9f).Within(0.01f));
        Assert.That(preview.GetFloat("_PreviewOpacity"), Is.EqualTo(0.52f).Within(0.001f));
        Assert.That(preview.GetFloat("_SourceAlpha"), Is.GreaterThan(preview.GetFloat("_Cutoff")),
            "Preview translucency must not feed back into the source alpha clipping threshold.");
    }

    [Test]
    public void PortableBucketGhostIncludesLiveContentsGeometryAndColor()
    {
        ghostSource = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/PortableBucket.prefab");
        Assert.That(ghostSource, Is.Not.Null);
        ghostSource = UnityEngine.Object.Instantiate(ghostSource);
        ghostSource.name = "Loaded bucket test instance";
        Type containerType = Type.GetType("PortableSubstanceContainer, Assembly-CSharp");
        Component container = ghostSource.GetComponent(containerType);
        Assert.That(container, Is.Not.Null);
        containerType.GetField("localUnits", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(container, 2);
        containerType.GetField("localSubstanceIndex", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(container, 0);
        Transform content = (Transform)containerType.GetField("contentVisual", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(container);
        Renderer contentRenderer = (Renderer)containerType.GetField("contentRenderer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(container);
        content.gameObject.SetActive(true);
        content.localScale = new Vector3(0.27f, 0.02f, 0.27f);
        content.localPosition = new Vector3(content.localPosition.x, Mathf.Lerp(0.45f, 0.55f, 2f / 3f), content.localPosition.z);
        Array supportedSubstances = (Array)containerType.GetField("supportedSubstances", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(container);
        object soil = supportedSubstances.GetValue(0);
        Color soilColor = (Color)soil.GetType().GetProperty("DisplayColor").GetValue(soil);
        testMaterial = new Material(contentRenderer.sharedMaterial);
        testMaterial.color = soilColor;
        contentRenderer.sharedMaterial = testMaterial;
        Assert.That(content.gameObject.activeInHierarchy, Is.True);
        Color expectedContentColor = contentRenderer.sharedMaterial.color;

        Type ghostType = Type.GetType("SingleCarryPlacementGhost, Assembly-CSharp");
        ghost = Activator.CreateInstance(ghostType, new object[] { ghostSource });
        GameObject root = (GameObject)Property(ghost, "Root");
        Transform previewContent = root.transform.Find("Contents Placement Preview");

        Assert.That(previewContent, Is.Not.Null, "The actual bucket contents transform must appear in the ghost hierarchy.");
        Renderer previewRenderer = previewContent.GetComponent<Renderer>();
        Assert.That(previewRenderer, Is.Not.Null);
        Color expectedPreviewColor = Color.Lerp(expectedContentColor, new Color(0.72f, 0.74f, 0.76f, 1f), 0.18f);
        Assert.That(previewRenderer.sharedMaterial.color.r, Is.EqualTo(expectedPreviewColor.r).Within(0.01f));
        Assert.That(previewRenderer.sharedMaterial.color.g, Is.EqualTo(expectedPreviewColor.g).Within(0.01f));
        Assert.That(previewRenderer.sharedMaterial.color.b, Is.EqualTo(expectedPreviewColor.b).Within(0.01f));
        Assert.That(previewRenderer.sharedMaterial.color.a, Is.LessThanOrEqualTo(0.52f));
        Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
        Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
    }

    [Test]
    public void PlacementRotationAction_UsesArrowCompositeInGeneratedWrapper()
    {
        Type wrapperType = Type.GetType("PlayerGameInputActions, Assembly-CSharp");
        object wrapper = Activator.CreateInstance(wrapperType);
        try
        {
            object game = wrapperType.GetProperty("Game").GetValue(wrapper);
            object action = game.GetType().GetProperty("SingleCarryPlacementRotation").GetValue(game);
            var bindings = (System.Collections.IEnumerable)action.GetType().GetProperty("bindings").GetValue(action);
            string[] expectedPaths = { "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow" };
            var actualPaths = new System.Collections.Generic.List<string>();
            string compositePath = null;
            foreach (object binding in bindings)
            {
                string path = (string)binding.GetType().GetProperty("path").GetValue(binding);
                bool isComposite = (bool)binding.GetType().GetProperty("isComposite").GetValue(binding);
                if (isComposite) compositePath = path;
                if (Array.IndexOf(expectedPaths, path) >= 0) actualPaths.Add(path);
            }
            CollectionAssert.AreEquivalent(expectedPaths, actualPaths);
            Assert.That(compositePath, Is.EqualTo("2DVector(mode=1)"), "Digital diagonals must not normalize away either rotation axis.");
        }
        finally
        {
            wrapperType.GetMethod("Disable").Invoke(wrapper, null);
            UnityEngine.Object.DestroyImmediate((UnityEngine.Object)wrapperType.GetProperty("asset").GetValue(wrapper));
        }
    }

    private object Calculate(Ray ray, float distance, Quaternion baseYaw, Quaternion localRotation, Transform ignoredOwner = null)
    {
        var solverType = Type.GetType("SingleCarryPlacementGeometrySolver, Assembly-CSharp");
        object solver = Activator.CreateInstance(solverType, new object[] { source });
        MethodInfo calculate = solverType.GetMethod("Calculate");
        return calculate.Invoke(solver, new object[] { ray, distance, baseYaw, localRotation, ignoredOwner });
    }

    private static object Property(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    private static void AssertStatus(object pose, string expected) => Assert.That(Property(pose, "Status").ToString(), Is.EqualTo(expected));
    private void CreateFloor()
    {
        surface = new GameObject("floor");
        BoxCollider floor = surface.AddComponent<BoxCollider>();
        floor.size = new Vector3(20f, 0.2f, 20f);
        surface.transform.position = new Vector3(0f, -0.1f, 0f);
    }
    private static Ray DownwardRay() => new Ray(new Vector3(0f, 1f, 0f), Vector3.down);
}
