using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class SingleCarryPlacementStage3EditModeTests
{
    private GameObject source;
    private GameObject obstacle;
    private GameObject secondObstacle;
    private GameObject trigger;
    private GameObject overlapRoot;
    private GameObject bucketInstance;
    private object validator;
    private object heldVisual;
    private TerrainData terrainData;
    private Mesh testMesh;

    [TearDown]
    public void TearDown()
    {
        if (source != null) UnityEngine.Object.DestroyImmediate(source);
        if (obstacle != null) UnityEngine.Object.DestroyImmediate(obstacle);
        if (secondObstacle != null) UnityEngine.Object.DestroyImmediate(secondObstacle);
        if (trigger != null) UnityEngine.Object.DestroyImmediate(trigger);
        if (overlapRoot != null) UnityEngine.Object.DestroyImmediate(overlapRoot);
        if (heldVisual != null) heldVisual.GetType().GetMethod("Dispose").Invoke(heldVisual, null);
        if (bucketInstance != null) UnityEngine.Object.DestroyImmediate(bucketInstance);
        if (terrainData != null) UnityEngine.Object.DestroyImmediate(terrainData);
        if (testMesh != null) UnityEngine.Object.DestroyImmediate(testMesh);
        Physics.SyncTransforms();
    }

    [Test]
    public void AirAndSupportingSurfaceAreValidButSolidOverlapBlocks()
    {
        CreateSource();
        Physics.SyncTransforms();
        object air = Pose(new Vector3(80f, 0f, 0f));
        AssertStatus(Validate(air), "Valid");

        obstacle = new GameObject("support plane") { transform = { position = new Vector3(80f, -0.1f, 0f) } };
        BoxCollider floor = obstacle.AddComponent<BoxCollider>();
        floor.size = new Vector3(4f, 0.2f, 4f);
        Physics.SyncTransforms();
        object supported = Pose(new Vector3(80f, 0.5f, 0f), floor, "SurfaceSupported");
        AssertStatus(Validate(supported), "Valid");

        object shallowIntrusion = Pose(new Vector3(80f, 0.4995f, 0f), floor, "SurfaceSupported");
        AssertStatus(Validate(shallowIntrusion), "Occupied");

        obstacle.transform.position = new Vector3(80f, 0.35f, 0f);
        Physics.SyncTransforms();
        object overlapping = Pose(new Vector3(80f, 0f, 0f));
        object result = Validate(overlapping);
        AssertStatus(result, "Occupied");
        Assert.That(Get(result, "BlockingCollider"), Is.SameAs(floor));
    }

    [Test]
    public void ExactFloorWallAndCeilingContactsRemainValid()
    {
        CreateSource();
        obstacle = new GameObject("support surfaces");
        BoxCollider surface = obstacle.AddComponent<BoxCollider>();
        surface.size = new Vector3(0.2f, 8f, 8f);
        obstacle.transform.position = new Vector3(1.1f, 0f, 0f);
        Physics.SyncTransforms();
        AssertStatus(Validate(Pose(new Vector3(0.5f, 0f, 0f), surface, "SurfaceSupported")), "Valid");

        obstacle.transform.position = new Vector3(0f, 0.1f, 0f);
        surface.size = new Vector3(8f, 0.2f, 8f);
        Physics.SyncTransforms();
        AssertStatus(Validate(Pose(new Vector3(0f, -0.5f, 0f), surface, "SurfaceSupported")), "Valid");

        obstacle.transform.position = new Vector3(0f, -0.6f, 0f);
        Physics.SyncTransforms();
        AssertStatus(Validate(Pose(Vector3.zero, surface, "SurfaceSupported")), "Valid");
    }

    [Test]
    public void GeometrySolverSupportedScaledSphereStaysValidAcrossCoplanarSupportTiles()
    {
        source = new GameObject("scaled held sphere")
        {
            transform = { position = new Vector3(80f, 0f, 0f), localScale = new Vector3(1f, 2f, 3f) }
        };
        SphereCollider sphere = source.AddComponent<SphereCollider>();
        sphere.radius = 0.5f;
        obstacle = new GameObject("first support tile");
        BoxCollider first = obstacle.AddComponent<BoxCollider>();
        first.size = new Vector3(20f, 0.2f, 20f);
        obstacle.transform.position = new Vector3(0f, -0.1f, 0f);
        secondObstacle = new GameObject("coplanar support tile");
        BoxCollider second = secondObstacle.AddComponent<BoxCollider>();
        second.size = first.size;
        secondObstacle.transform.position = obstacle.transform.position;
        validator = Activator.CreateInstance(Type.GetType("SingleCarryPlacementValidator, Assembly-CSharp"), new object[] { source });
        object solver = Activator.CreateInstance(Type.GetType("SingleCarryPlacementGeometrySolver, Assembly-CSharp"), new object[] { source });
        Physics.SyncTransforms();

        object pose = solver.GetType().GetMethod("Calculate").Invoke(solver, new object[]
        {
            new Ray(new Vector3(0f, 2f, 0f), Vector3.down), 2f, Quaternion.identity, Quaternion.identity, null
        });
        Assert.That(Get(pose, "Status").ToString(), Is.EqualTo("SurfaceSupported"));
        Assert.That(((Vector3)Get(pose, "Position")).y, Is.EqualTo(1.5f).Within(0.002f));
        AssertStatus(Validate(pose), "Valid");
        Assert.That(source.transform.position, Is.EqualTo(new Vector3(80f, 0f, 0f)));
    }

    [Test]
    public void UnitySphereAndCapsuleScalingAreValidatedAgainstWorldSpaceBlockers()
    {
        CreateSource();
        BoxCollider original = source.GetComponent<BoxCollider>();
        original.enabled = false;
        source.transform.localScale = new Vector3(1f, 2f, 3f);
        SphereCollider sphere = source.AddComponent<SphereCollider>();
        sphere.radius = 0.5f;
        validator = Activator.CreateInstance(Type.GetType("SingleCarryPlacementValidator, Assembly-CSharp"), new object[] { source });
        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = new Vector3(1.4f, 0f, 0f);
        obstacle.transform.localScale = Vector3.one * 0.1f;
        Physics.SyncTransforms();
        AssertStatus(Validate(Pose(Vector3.zero)), "Occupied");

        UnityEngine.Object.DestroyImmediate(sphere);
        CapsuleCollider capsule = source.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.height = 0.6f;
        capsule.radius = 0.5f;
        validator = Activator.CreateInstance(Type.GetType("SingleCarryPlacementValidator, Assembly-CSharp"), new object[] { source });
        Physics.SyncTransforms();
        AssertStatus(Validate(Pose(Vector3.zero)), "Occupied");
    }

    [Test]
    public void CharacterControllerAndDynamicColliderRemainOccupancyBlockers()
    {
        CreateSource();
        obstacle = new GameObject("other player character controller");
        CharacterController characterController = obstacle.AddComponent<CharacterController>();
        characterController.height = 1f;
        characterController.radius = 0.4f;
        Physics.SyncTransforms();
        AssertStatus(Validate(Pose(Vector3.zero)), "Occupied");
        UnityEngine.Object.DestroyImmediate(obstacle);
        obstacle = new GameObject("dynamic rigidbody prop");
        obstacle.AddComponent<BoxCollider>().size = Vector3.one * 0.25f;
        Rigidbody body = obstacle.AddComponent<Rigidbody>();
        body.useGravity = false;
        Physics.SyncTransforms();
        AssertStatus(Validate(Pose(Vector3.zero)), "Occupied");
    }

    [Test]
    public void ThinWallTerrainAndStaticMeshObstaclesBlockPlacement()
    {
        CreateSource();
        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = "thin wall";
        obstacle.transform.position = new Vector3(0f, 0f, 0.499f);
        obstacle.transform.localScale = new Vector3(4f, 4f, 0.002f);
        Physics.SyncTransforms();
        AssertStatus(Validate(Pose(Vector3.zero)), "Occupied");

        UnityEngine.Object.DestroyImmediate(obstacle);
        obstacle = null;
        terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(4f, 1f, 4f) };
        terrainData.SetHeights(0, 0, new float[33, 33]);
        obstacle = Terrain.CreateTerrainGameObject(terrainData);
        obstacle.transform.position = new Vector3(-2f, 0f, -2f);
        Physics.SyncTransforms();
        TerrainCollider terrainCollider = obstacle.GetComponent<TerrainCollider>();
        object belowTerrain = Validate(Pose(new Vector3(0f, -0.4f, 0f)));
        AssertStatus(belowTerrain, "Occupied", "A candidate box straddling the flat terrain must not be treated as clear.");
        Assert.That(Get(belowTerrain, "BlockingCollider"), Is.SameAs(terrainCollider));
        object aboveTerrain = Validate(Pose(new Vector3(0f, 0.4f, 0f)));
        AssertStatus(aboveTerrain, "Occupied", "A candidate box with its center above terrain still penetrates by 0.1 m.");
        Assert.That(Get(aboveTerrain, "BlockingCollider"), Is.SameAs(terrainCollider));
        AssertStatus(Validate(Pose(new Vector3(0f, 1.5f, 0f))), "Valid", "Air above the terrain remains placeable.");
        AssertStatus(Validate(Pose(new Vector3(0f, 0.5f, 0f))), "Valid", "Exact flat-terrain contact is clear.");
        AssertStatus(Validate(Pose(new Vector3(0f, 0.5001f, 0f))), "Valid", "A small gap above terrain is clear.");
        AssertStatus(Validate(Pose(new Vector3(0f, 0.4995f, 0f))), "Occupied", "A 0.5 mm penetration remains blocked.");

        float[,] slopeHeights = new float[33, 33];
        for (int z = 0; z < 33; z++)
            for (int x = 0; x < 33; x++)
                slopeHeights[z, x] = x / 32f * 0.5f;
        terrainData.SetHeights(0, 0, slopeHeights);
        Physics.SyncTransforms();
        object slopedTerrainOverlap = Validate(Pose(new Vector3(0f, 0.4f, 0f)));
        AssertStatus(slopedTerrainOverlap, "Occupied", "A sloped terrain crossing under the candidate footprint must be blocked.");
        Assert.That(Get(slopedTerrainOverlap, "BlockingCollider"), Is.SameAs(terrainCollider));

        terrainData.SetHeights(0, 0, new float[33, 33]);
        Physics.SyncTransforms();
        object terrainEdgeOverlap = Validate(Pose(new Vector3(2.4f, -0.4f, 0f)));
        AssertStatus(terrainEdgeOverlap, "Occupied", "A candidate partially crossing the terrain edge must be blocked.");
        Assert.That(Get(terrainEdgeOverlap, "BlockingCollider"), Is.SameAs(terrainCollider));
        AssertStatus(Validate(Pose(new Vector3(2.6f, -0.4f, 0f))), "Valid",
            "A candidate wholly outside the terrain footprint is clear.");

        UnityEngine.Object.DestroyImmediate(obstacle);
        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = new Vector3(0.1f, 0f, 0f);
        MeshFilter filter = obstacle.GetComponent<MeshFilter>();
        MeshCollider meshCollider = obstacle.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = filter.sharedMesh;
        UnityEngine.Object.DestroyImmediate(obstacle.GetComponent<BoxCollider>());
        Physics.SyncTransforms();
        object staticMeshCrossing = Validate(Pose(Vector3.zero));
        AssertStatus(staticMeshCrossing, "Occupied", "A real primitive crossing through a concave mesh face must be blocked.");
        Assert.That(Get(staticMeshCrossing, "BlockingCollider"), Is.SameAs(meshCollider));
    }

    [Test]
    public void ConcaveMeshBoundsDoNotTreatClearEnclosuresAsSolidVolumes()
    {
        CreateSource();
        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = "large closed concave mesh enclosure";
        obstacle.transform.localScale = Vector3.one * 10f;
        MeshFilter filter = obstacle.GetComponent<MeshFilter>();
        MeshCollider closedMesh = obstacle.AddComponent<MeshCollider>();
        closedMesh.sharedMesh = filter.sharedMesh;
        UnityEngine.Object.DestroyImmediate(obstacle.GetComponent<BoxCollider>());
        Physics.SyncTransforms();
        Assert.That(closedMesh.bounds.Contains(Vector3.zero), Is.True);
        AssertStatus(Validate(Pose(Vector3.zero)), "Valid",
            "Triangle meshes are surfaces; open air enclosed by distant faces is not a filled collider volume.");

        UnityEngine.Object.DestroyImmediate(obstacle);
        obstacle = new GameObject("open room concave mesh shell");
        testMesh = new Mesh
        {
            vertices = new[]
            {
                new Vector3(-5f, -5f, -5f), new Vector3(5f, -5f, -5f),
                new Vector3(5f, 5f, -5f), new Vector3(-5f, 5f, -5f),
                new Vector3(-5f, -5f, 5f), new Vector3(5f, -5f, 5f),
                new Vector3(5f, 5f, 5f), new Vector3(-5f, 5f, 5f)
            },
            triangles = new[]
            {
                0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
                0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5
            }
        };
        obstacle.AddComponent<MeshFilter>().sharedMesh = testMesh;
        MeshCollider openRoom = obstacle.AddComponent<MeshCollider>();
        openRoom.sharedMesh = testMesh;
        Physics.SyncTransforms();
        Assert.That(openRoom.bounds.Contains(Vector3.zero), Is.True);
        AssertStatus(Validate(Pose(Vector3.zero)), "Valid",
            "A clear room interior remains valid even though the combined open mesh bounds enclose it.");
    }

    [Test]
    public void PrimitiveOverlapFallbackDetectsBackfaceStaticMeshCrossing()
    {
        CreateSource();
        obstacle = new GameObject("one-sided vertical mesh plane");
        testMesh = new Mesh
        {
            vertices = new[]
            {
                new Vector3(-2f, -2f, 0f), new Vector3(-2f, 2f, 0f),
                new Vector3(2f, 2f, 0f), new Vector3(2f, -2f, 0f)
            },
            triangles = new[] { 0, 1, 2, 0, 2, 3 }
        };
        obstacle.AddComponent<MeshFilter>().sharedMesh = testMesh;
        MeshCollider plane = obstacle.AddComponent<MeshCollider>();
        plane.sharedMesh = testMesh;
        Physics.SyncTransforms();

        object backfaceCrossing = Validate(Pose(new Vector3(0f, 0f, 0.1f)));
        AssertStatus(backfaceCrossing, "Occupied", "The primitive overlap fallback must catch a concave-mesh backface crossing.");
        Assert.That(Get(backfaceCrossing, "BlockingCollider"), Is.SameAs(plane));
    }

    [Test]
    public void PrimitiveOverlapFallbackUsesExactSphereAndCapsuleShapesAgainstTerrain()
    {
        CreateSource();
        terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(4f, 1f, 4f) };
        terrainData.SetHeights(0, 0, new float[33, 33]);
        obstacle = Terrain.CreateTerrainGameObject(terrainData);
        obstacle.transform.position = new Vector3(-2f, 0f, -2f);
        TerrainCollider terrainCollider = obstacle.GetComponent<TerrainCollider>();
        source.GetComponent<BoxCollider>().enabled = false;

        SphereCollider sphere = source.AddComponent<SphereCollider>();
        sphere.radius = 0.5f;
        validator = Activator.CreateInstance(Type.GetType("SingleCarryPlacementValidator, Assembly-CSharp"), new object[] { source });
        Physics.SyncTransforms();
        object sphereTerrainCrossing = Validate(Pose(new Vector3(0f, -0.4f, 0f)));
        AssertStatus(sphereTerrainCrossing, "Occupied", "The exact shrunken sphere query must find terrain crossing.");
        Assert.That(Get(sphereTerrainCrossing, "BlockingCollider"), Is.SameAs(terrainCollider));
        AssertStatus(Validate(Pose(new Vector3(0f, 0.5f, 0f))), "Valid", "The shrunken sphere query must allow exact contact.");

        UnityEngine.Object.DestroyImmediate(sphere);
        CapsuleCollider capsule = source.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.height = 1f;
        capsule.radius = 0.25f;
        validator = Activator.CreateInstance(Type.GetType("SingleCarryPlacementValidator, Assembly-CSharp"), new object[] { source });
        Physics.SyncTransforms();
        object capsuleTerrainCrossing = Validate(Pose(new Vector3(0f, -0.4f, 0f)));
        AssertStatus(capsuleTerrainCrossing, "Occupied", "The exact shrunken capsule query must find terrain crossing.");
        Assert.That(Get(capsuleTerrainCrossing, "BlockingCollider"), Is.SameAs(terrainCollider));
        AssertStatus(Validate(Pose(new Vector3(0f, 0.5f, 0f))), "Valid", "The shrunken capsule query must allow exact contact.");
    }

    [Test]
    public void ValidatorIgnoresOnlySourceHierarchyAndTriggersAndChecksIgnoreRaycastLayer()
    {
        CreateSource();
        source.transform.position = new Vector3(80f, 0f, 0f);
        GameObject sourceChild = new GameObject("source child");
        sourceChild.transform.SetParent(source.transform, false);
        sourceChild.AddComponent<BoxCollider>();
        trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trigger.transform.position = Vector3.zero;
        trigger.GetComponent<Collider>().isTrigger = true;
        Physics.SyncTransforms();
        object candidate = Pose(Vector3.zero);
        AssertStatus(Validate(candidate), "Valid");

        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.layer = 2; // Ignore Raycast still physically blocks placement.
        Physics.SyncTransforms();
        object result = Validate(candidate);
        AssertStatus(result, "Occupied");
        Assert.That(Get(result, "BlockingCollider"), Is.SameAs(obstacle.GetComponent<Collider>()));
    }

    [Test]
    public void CompoundRotatedOffCenterAndNonUniformlyScaledCollidersBlockAtProposedPose()
    {
        CreateSource();
        GameObject child = new GameObject("off-center child");
        child.transform.SetParent(source.transform, false);
        child.transform.localPosition = new Vector3(0.6f, 0f, 0f);
        child.transform.localScale = new Vector3(2f, 1f, 1f);
        BoxCollider box = child.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 0.2f, 0.2f);
        validator = Activator.CreateInstance(Type.GetType("SingleCarryPlacementValidator, Assembly-CSharp"), new object[] { source });

        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = new Vector3(0.7f, 0.7f, 0f);
        obstacle.transform.localScale = Vector3.one * 0.2f;
        Physics.SyncTransforms();

        object result = Validate(Pose(Vector3.zero, null, "NoObstacle", Quaternion.Euler(0f, 0f, 45f)));
        AssertStatus(result, "Occupied");
        Assert.That(Get(result, "BlockingCollider"), Is.Not.Null);
        Assert.That(source.transform.position, Is.EqualTo(Vector3.zero), "Validation must not reposition the actual held object.");
    }

    [Test]
    public void NonConvexMeshFailsClosedAndConvexMeshCanBeValidated()
    {
        CreateSource();
        Mesh mesh = new Mesh();
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
        mesh.triangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
        source.GetComponent<BoxCollider>().enabled = false;
        MeshCollider meshCollider = source.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
        validator = Activator.CreateInstance(Type.GetType("SingleCarryPlacementValidator, Assembly-CSharp"), new object[] { source });
        Physics.SyncTransforms();
        AssertStatus(Validate(Pose(Vector3.zero)), "UnsupportedGeometry");
        meshCollider.convex = true;
        Physics.SyncTransforms();
        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = new Vector3(0.1f, 0.1f, 0.1f);
        Physics.SyncTransforms();
        AssertStatus(Validate(Pose(Vector3.zero)), "Occupied");
        UnityEngine.Object.DestroyImmediate(mesh);
    }

    [Test]
    public void ShearedColliderHierarchyFailsClosedAndSourcePhysicsStateIsUntouched()
    {
        CreateSource();
        source.transform.position = new Vector3(80f, 0f, 0f);
        source.transform.localScale = new Vector3(2f, 1f, 1f);
        Rigidbody body = source.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        GameObject child = new GameObject("rotated child under nonuniform parent");
        child.transform.SetParent(source.transform, false);
        child.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        child.AddComponent<BoxCollider>();
        validator = Activator.CreateInstance(Type.GetType("SingleCarryPlacementValidator, Assembly-CSharp"), new object[] { source });
        Physics.SyncTransforms();

        object result = Validate(Pose(Vector3.zero));

        AssertStatus(result, "UnsupportedGeometry");
        Assert.That(source.transform.position, Is.EqualTo(new Vector3(80f, 0f, 0f)));
        Assert.That(body.isKinematic, Is.True);
        Assert.That(body.useGravity, Is.False);
        Assert.That(source.GetComponent<BoxCollider>().enabled, Is.True);
    }

    [Test]
    public void OverlapQueryGrowsPastInitialBufferWithoutReturningFalseValid()
    {
        CreateSource();
        overlapRoot = new GameObject("overlap query fixture");
        for (int i = 0; i < 40; i++)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = "overlap " + i;
            item.transform.position = new Vector3((i % 5) * 0.02f, (i / 5) * 0.02f, 0f);
            item.transform.localScale = Vector3.one * 0.1f;
            item.transform.SetParent(overlapRoot.transform, true);
            if (i == 0) obstacle = item;
        }
        Physics.SyncTransforms();
        object result = Validate(Pose(Vector3.zero));
        Assert.That(Get(result, "IsValid"), Is.EqualTo(false));
        Assert.That(Get(result, "Status").ToString(), Is.EqualTo("Occupied"));
    }

    [Test]
    public void ActualBucketContentsAndPreHiddenRenderersAreRestoredExactly()
    {
#if UNITY_EDITOR
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/PortableBucket.prefab");
        Assert.That(prefab, Is.Not.Null);
        bucketInstance = UnityEngine.Object.Instantiate(prefab);
        Component container = bucketInstance.GetComponent(Type.GetType("PortableSubstanceContainer, Assembly-CSharp"));
        Assert.That(container, Is.Not.Null);
        Transform content = (Transform)container.GetType().GetField("contentVisual", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(container);
        Renderer contentRenderer = (Renderer)container.GetType().GetField("contentRenderer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(container);
        content.gameObject.SetActive(true);
        contentRenderer.forceRenderingOff = true;
        Renderer[] renderers = bucketInstance.GetComponentsInChildren<Renderer>(true);
        bool[] forceStates = Array.ConvertAll(renderers, renderer => renderer.forceRenderingOff);
        bool[] enabledStates = Array.ConvertAll(renderers, renderer => renderer.enabled);
        heldVisual = Activator.CreateInstance(Type.GetType("SingleCarryPlacementHeldVisual, Assembly-CSharp"), new object[] { bucketInstance });

        Assert.That(contentRenderer.forceRenderingOff, Is.True, "The actual bucket contents renderer must be covered.");
        for (int i = 0; i < renderers.Length; i++)
        {
            Assert.That(renderers[i].forceRenderingOff, Is.True);
            Assert.That(renderers[i].enabled, Is.EqualTo(enabledStates[i]));
        }

        heldVisual.GetType().GetMethod("Dispose").Invoke(heldVisual, null);
        for (int i = 0; i < renderers.Length; i++)
        {
            Assert.That(renderers[i].forceRenderingOff, Is.EqualTo(forceStates[i]));
            Assert.That(renderers[i].enabled, Is.EqualTo(enabledStates[i]));
        }
#else
        Assert.Ignore("This fixture loads the established bucket prefab through the Unity Editor AssetDatabase.");
#endif
    }

    private void CreateSource()
    {
        source = new GameObject("held candidate");
        source.AddComponent<BoxCollider>();
        validator = Activator.CreateInstance(Type.GetType("SingleCarryPlacementValidator, Assembly-CSharp"), new object[] { source });
    }

    private static object Pose(Vector3 position, Collider surface = null, string status = "NoObstacle", Quaternion? rotation = null)
    {
        Type statusType = Type.GetType("SingleCarryPlacementGeometryStatus, Assembly-CSharp");
        Type poseType = Type.GetType("SingleCarryPlacementPose, Assembly-CSharp");
        return Activator.CreateInstance(poseType, new object[] { position, rotation ?? Quaternion.identity,
            Enum.Parse(statusType, status), surface, surface != null ? Vector3.up : Vector3.zero });
    }

    private object Validate(object pose)
    {
        MethodInfo method = validator.GetType().GetMethod("Validate");
        return method.Invoke(validator, new object[] { pose, null });
    }

    private static object Get(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    private static void AssertStatus(object result, string status, string context = null)
    {
        string detail = $"blocker={Get(result, "BlockingCollider") ?? "none"}, depth={Get(result, "PenetrationDepth")}";
        Assert.That(Get(result, "Status").ToString(), Is.EqualTo(status),
            string.IsNullOrEmpty(context) ? detail : $"{context} {detail}");
    }
}
