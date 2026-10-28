using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Type = System.Type;

public sealed class LooseSoilStage2PlayModeTests
{
    private const string Stage02 = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private AsyncOperation sceneLoad;
    private GameObject characterRoot;
    private GameObject destroyedFieldRoot;

    [UnitySetUp]
    public IEnumerator LoadStage02()
    {
        sceneLoad = SceneManager.LoadSceneAsync(Stage02, LoadSceneMode.Additive);
        Assert.That(sceneLoad, Is.Not.Null);
        while (!sceneLoad.isDone) yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator FirstLooseLayerHasNoColliderRejectsLegacyTransfersAndSupportsCharacterControllerOnHardFloor()
    {
        Type volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        Type siteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        Component[] volumes = FindSceneComponents(volumeType, Stage02).OrderBy(volume => volume.transform.position.x).ToArray();
        Assert.That(volumes, Has.Length.EqualTo(2));
        Component volume = volumes[0];
        Component site = ReadField(volume, "constructionSite") as Component;
        Component field = ReadField(volume, "looseSoilField") as Component;
        BoxCollider support = ReadField(volume, "soilSupportCollider") as BoxCollider;
        Assert.That(site, Is.Not.Null);
        Assert.That(siteType.IsAssignableFrom(site.GetType()), Is.True);
        Assert.That(field, Is.Not.Null);
        Assert.That(support, Is.Not.Null);
        Assert.That((bool)Property(field, "HasHeightField"), Is.True);
        Assert.That(field.GetComponent<Collider>(), Is.Null);

        object workflow = ReadField(site, "workflow");
        Assert.That(workflow, Is.Not.Null);
        float layerDepth = (float)Property(workflow, "FinalExcavationDepth") / (int)Property(workflow, "DiggingCycleCount");
        Assert.That(layerDepth, Is.EqualTo(.4f).Within(.0001f));
        Assert.That((float)Property(Property(field, "HeightField"), "TotalUnits"), Is.EqualTo(6f).Within(.0001f));
        Assert.That((float)Property(Property(field, "HeightField"), "TotalVolume"), Is.EqualTo(10.4f).Within(.0001f));

        Vector3 initialSupportTop = support.transform.TransformPoint(support.center + Vector3.up * support.size.y * .5f);
        object state = CreateFirstLooseLayerNetworkState(site, workflow, .5f);
        site.GetType().GetMethod("ApplyNetworkState", Instance).Invoke(site, new[] { state });
        Assert.That((bool)Property(field, "IsSurfaceActive"), Is.True);
        field.gameObject.SetActive(false);
        Assert.That((bool)Property(field, "IsSurfaceActive"), Is.False,
            "A disabled field GameObject must not report an active loose surface.");
        object[] inactiveQuery = { field.transform.TransformPoint(new Vector3(-2f, 0f, 0f)), 0f };
        Assert.That((bool)volume.GetType().GetMethod("TryGetLooseSoilSurfaceHeight").Invoke(volume, inactiveQuery), Is.False);
        field.gameObject.SetActive(true);
        Assert.That((bool)Property(field, "IsSurfaceActive"), Is.True);
        Assert.That((bool)Property(site, "IsSoilHardeningActive"), Is.False);
        Assert.That((float)Property(site, "RemainingSoilHardeningTime"), Is.Zero);

        UnityEngine.Object soilSubstance = null;
#if UNITY_EDITOR
        soilSubstance = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/ScriptableObjectAssets/New/Substances/Soil.asset");
#endif
        Assert.That(soilSubstance, Is.Not.Null);
        MethodInfo extract = siteType.GetMethod("TryExtractSoil", Instance);
        Assert.That((float)extract.Invoke(site, new[] { (object)1f, soilSubstance }), Is.Zero);
        Assert.That((bool)siteType.GetMethod("CanExtract", Instance).Invoke(site, new[] { soilSubstance }), Is.False);
        float counterBeforeReturn = (float)Property(site, "RemovedSoilUnits");
        Assert.That((float)siteType.GetMethod("ReturnSoil", Instance).Invoke(site, new object[] { 1f }), Is.Zero);
        Assert.That((float)Property(site, "RemovedSoilUnits"), Is.EqualTo(counterBeforeReturn));

        object zeroCounterState = CreateFirstLooseLayerNetworkState(site, workflow, 0f);
        site.GetType().GetMethod("ApplyNetworkState", Instance).Invoke(site, new[] { zeroCounterState });

        object model = Property(field, "HeightField");
        float removedCellX = .05f, cellZ = .1f;
        Assert.That((float)model.GetType().GetMethod("Remove").Invoke(model, new object[] { removedCellX, cellZ, .001f, 1f }),
            Is.GreaterThan(0f));
        field.GetType().GetMethod("RebuildMesh").Invoke(field, null);
        Assert.That(field.GetComponent<Collider>(), Is.Null, "Removing loose cells must not introduce collision on the visual mesh.");

        Vector3 queryPoint = field.transform.TransformPoint(new Vector3(-2f, 0f, 0f));
        object[] queryArgs = { queryPoint, 0f };
        Assert.That((bool)volume.GetType().GetMethod("TryGetLooseSoilSurfaceHeight").Invoke(volume, queryArgs), Is.True);
        Assert.That((float)queryArgs[1], Is.EqualTo(.08f).Within(.02f));

        characterRoot = new GameObject("Loose soil hard-floor probe");
        Vector2 probeCell = FindClearProbeCell(field, model, removedCellX, cellZ, support);
        object[] untouchedHeightArgs = { probeCell.x, probeCell.y, 0f };
        Assert.That((bool)model.GetType().GetMethod("TryGetHeight").Invoke(model, untouchedHeightArgs), Is.True);
        Assert.That((float)untouchedHeightArgs[2], Is.EqualTo(.4f).Within(.0001f));
        characterRoot.transform.position = field.transform.TransformPoint(new Vector3(probeCell.x, 2f, probeCell.y));
        CharacterController controller = characterRoot.AddComponent<CharacterController>();
        LooseSoilCollisionRecorder collisionRecorder = characterRoot.AddComponent<LooseSoilCollisionRecorder>();
        controller.height = 1.8f;
        controller.radius = .2f;
        controller.skinWidth = .02f;
        Physics.SyncTransforms();
        for (int frame = 0; frame < 120; frame++)
        {
            controller.Move(Physics.gravity * Time.deltaTime);
            yield return null;
        }
        string contactedColliders = string.Join(", ", collisionRecorder.Colliders
            .Where(collider => collider != null)
            .Select(collider => collider.name + " (" + collider.GetType().Name + ")"));
        float expectedCharacterCenter = support.transform.TransformPoint(support.center + Vector3.up * support.size.y * .5f).y + controller.height * .5f;
        Assert.That(characterRoot.transform.position.y, Is.EqualTo(expectedCharacterCenter).Within(.12f),
            "The CharacterController should pass through the collider-free loose visual and rest on the hard support one layer below. " +
            "Probe local XZ: " + probeCell + ". Contacted colliders: " + contactedColliders);
        Assert.That(expectedCharacterCenter, Is.EqualTo(initialSupportTop.y - layerDepth + .9f).Within(.03f));

        object changedSnapshot = field.GetType().GetMethod("CaptureSnapshot").Invoke(field, null);
        object stateAgain = CreateFirstLooseLayerNetworkState(site, workflow, 0f);
        site.GetType().GetMethod("ApplyNetworkState", Instance).Invoke(site, new[] { stateAgain });
        Assert.That((bool)model.GetType().GetMethod("MatchesSnapshot").Invoke(model, new[] { changedSnapshot }), Is.True,
            "Repeated body-state packets must not recreate a uniform loose field.");

        site.GetType().GetMethod("ApplyInitialCompletedState", Instance).Invoke(site, null);
        Assert.That((bool)Property(field, "IsSurfaceActive"), Is.False);
        Assert.That((float)Property(Property(field, "HeightField"), "TotalUnits"), Is.Zero,
            "Completing the opt-in loose-soil field consumes the final material; completion must not recreate phantom soil.");
        object[] hiddenQuery = { queryPoint, 0f };
        Assert.That((bool)volume.GetType().GetMethod("TryGetLooseSoilSurfaceHeight").Invoke(volume, hiddenQuery), Is.False,
            "The hidden loose surface must not answer a query after reset/complete.");

        Mesh runtimeMesh = Property(field, "RuntimeMesh") as Mesh;
        Assert.That(runtimeMesh, Is.Not.Null);
        destroyedFieldRoot = field.gameObject;
        Object.Destroy(destroyedFieldRoot);
        yield return null;
        Assert.That(runtimeMesh == null, Is.True, "OnDestroy must release the runtime-owned mesh.");
    }

    private static Component[] FindSceneComponents(Type type, string scenePath)
    {
        var results = new System.Collections.Generic.List<Component>();
        foreach (Object item in Resources.FindObjectsOfTypeAll(type))
        {
            Component component = item as Component;
            if (component != null && component.gameObject.scene.path == scenePath) results.Add(component);
        }
        return results.ToArray();
    }

    private static object CreateFirstLooseLayerNetworkState(Component site, object workflow, float removedUnits)
    {
        Type stateType = Type.GetType("BridgeComponentNetworkState, Assembly-CSharp");
        object state = Activator.CreateInstance(stateType, new object[] { (int)Property(site.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp")), "ComponentID") });
        stateType.GetField("constructionStage").SetValue(state, Convert.ToInt32(Enum.Parse(Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "Digging")));
        stateType.GetField("constructionValueA").SetValue(state, 0);
        stateType.GetField("constructionValueB").SetValue(state, Convert.ToInt32(Enum.Parse(Type.GetType("FoundationDiggingSubstage, Assembly-CSharp"), "SoilRemoval")));
        stateType.GetField("constructionProgress").SetValue(state, (float)Property(workflow, "LooseningProgressPerCycle"));
        stateType.GetField("constructionAux0").SetValue(state, removedUnits);
        stateType.GetField("constructionAux1").SetValue(state, Time.time + 30f);
        return state;
    }

    private static Vector2 FindClearProbeCell(Component field, object model, float removedCellX, float removedCellZ,
        BoxCollider support)
    {
        const float controllerHeight = 1.8f;
        const float controllerRadius = .2f;
        const float startHeightAboveField = 2f;
        MethodInfo tryGetHeight = model.GetType().GetMethod("TryGetHeight");
        float width = (float)Property(model, "Width");
        float length = (float)Property(model, "Length");
        float cellSize = (float)Property(model, "CellSize");
        int columns = (int)Property(model, "Columns");
        int rows = (int)Property(model, "Rows");
        Vector3 supportTop = support.transform.TransformPoint(support.center + Vector3.up * support.size.y * .5f);
        Physics.SyncTransforms();

        for (int row = 0; row < rows; row++)
        {
            float zCellWidth = Mathf.Min(cellSize, length - row * cellSize);
            float z = -length * .5f + row * cellSize + zCellWidth * .5f;
            if (Mathf.Abs(z) > length * .5f - controllerRadius) continue;
            for (int column = 0; column < columns; column++)
            {
                float xCellWidth = Mathf.Min(cellSize, width - column * cellSize);
                float x = -width * .5f + column * cellSize + xCellWidth * .5f;
                if (Mathf.Abs(x) > width * .5f - controllerRadius) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(removedCellX, removedCellZ)) < .5f) continue;
                object[] heightArgs = { x, z, 0f };
                if (!(bool)tryGetHeight.Invoke(model, heightArgs) ||
                    Mathf.Abs((float)heightArgs[2] - .4f) > .0001f) continue;

                Vector3 start = field.transform.TransformPoint(new Vector3(x, startHeightAboveField, z));
                float distance = start.y - (supportTop.y + controllerHeight * .5f);
                if (distance <= 0f) continue;
                float halfSegment = Mathf.Max(0f, controllerHeight * .5f - controllerRadius);
                Vector3 top = start + Vector3.up * halfSegment;
                Vector3 bottom = start - Vector3.up * halfSegment;
                if (Physics.CapsuleCast(top, bottom, controllerRadius, Vector3.down, out RaycastHit hit,
                        distance + .1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.collider == support)
                    return new Vector2(x, z);
            }
        }

        Assert.Fail("No untouched loose-soil cell has a clear CharacterController capsule path to the hard support. " +
                    "The authored ramp and all other scene colliders remain enabled during this search.");
        return default;
    }

    private static object ReadField(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    private static object Property(object target, string name) => target.GetType().GetProperty(name, Instance).GetValue(target);

    [UnityTearDown]
    public IEnumerator UnloadStage02()
    {
        if (characterRoot != null) Object.Destroy(characterRoot);
        if (destroyedFieldRoot != null) Object.Destroy(destroyedFieldRoot);
        Scene scene = SceneManager.GetSceneByPath(Stage02);
        if (scene.IsValid() && scene.isLoaded)
        {
            AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
            if (unload != null) while (!unload.isDone) yield return null;
        }
        yield return null;
    }
}

public sealed class LooseSoilCollisionRecorder : MonoBehaviour
{
    public readonly List<Collider> Colliders = new();

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider != null && !Colliders.Contains(hit.collider)) Colliders.Add(hit.collider);
    }
}
