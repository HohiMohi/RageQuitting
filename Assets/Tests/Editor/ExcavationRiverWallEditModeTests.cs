using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ExcavationRiverWallEditModeTests
{
    private const string ScenePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";

    [Test]
    public void BothRiverWallsAreSolidOutsideTheSoilAndCloseTheFullPitEnd()
    {
        Scene stage = default;
        try
        {
            stage = EditorSceneManager.OpenPreviewScene(ScenePath);
            System.Type volumeType = System.Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
            Assert.That(volumeType, Is.Not.Null);
            var volumes = Resources.FindObjectsOfTypeAll(volumeType).Cast<Component>()
                .Where(v => v != null && v.gameObject.scene == stage).ToArray();
            Assert.That(volumes, Has.Length.EqualTo(2));
            foreach (Component volume in volumes)
            {
                Transform wall = volume.transform.Find("PitWallRiver");
                Assert.That(wall, Is.Not.Null, volume.name + " must have the authored river end wall.");
                BoxCollider collider = wall.GetComponent<BoxCollider>();
                Renderer renderer = wall.GetComponent<Renderer>();
                Assert.That(collider, Is.Not.Null);
                Assert.That(collider.enabled, Is.True);
                Assert.That(collider.isTrigger, Is.False);
                Assert.That(collider.attachedRigidbody, Is.Null, "River walls stay static world colliders.");
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.enabled, Is.True);
                Assert.That(renderer.sharedMaterial, Is.Not.Null);
                Assert.That(renderer.sharedMaterial.renderQueue, Is.LessThanOrEqualTo(2500));

                BoxCollider soil = FindCollider(stage, "PitBottom", volume.transform.position.x);
                BoxCollider north = FindCollider(stage, "PitWallNorth", volume.transform.position.x);
                BoxCollider south = FindCollider(stage, "PitWallSouth", volume.transform.position.x);
                float direction = volume.transform.position.x < 5f ? 1f : -1f;
                if (direction > 0f)
                    Assert.That(collider.bounds.min.x, Is.GreaterThanOrEqualTo(soil.bounds.max.x - .001f),
                        "Wall thickness must sit outside the soil footprint.");
                else
                    Assert.That(collider.bounds.max.x, Is.LessThanOrEqualTo(soil.bounds.min.x + .001f),
                        "Wall thickness must sit outside the soil footprint.");
                Assert.That(collider.bounds.size.y, Is.GreaterThanOrEqualTo(Mathf.Max(north.bounds.size.y, south.bounds.size.y) - .001f));
                Assert.That(collider.bounds.min.z, Is.LessThanOrEqualTo(south.bounds.min.z + .001f));
                Assert.That(collider.bounds.max.z, Is.GreaterThanOrEqualTo(north.bounds.max.z - .001f));

                // The authored ramp remains at the opposite end and the wall does not replace it.
                Transform ramp = Resources.FindObjectsOfTypeAll<Transform>()
                    .Where(t => t.gameObject.scene == stage && t.name == "ExitRamp")
                    .OrderBy(t => Mathf.Abs(t.position.x - volume.transform.position.x)).FirstOrDefault();
                Assert.That(ramp, Is.Not.Null);
                Assert.That(direction * (ramp.position.x - collider.bounds.center.x), Is.LessThan(0f),
                    "The existing ramp-side opening remains opposite the new river end wall.");
                Assert.That(wall.GetComponent("MountingNailSceneTargetIdentity"), Is.Not.Null);
                string stableId = new SerializedObject(wall.GetComponent("MountingNailSceneTargetIdentity"))
                    .FindProperty("stableTargetId").stringValue;
                Assert.That(stableId, Is.Not.Empty);
                Assert.That(stableId, Is.EqualTo(GlobalObjectId.GetGlobalObjectIdSlow(wall.gameObject).ToString()));
            }
            System.Type identityType = System.Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
            Assert.That(identityType, Is.Not.Null);
            string[] sceneIdentityIds = Resources.FindObjectsOfTypeAll(identityType).Cast<Component>()
                .Where(identity => identity != null && identity.gameObject.scene == stage)
                .Select(identity => new SerializedObject(identity).FindProperty("stableTargetId").stringValue).ToArray();
            Assert.That(sceneIdentityIds.All(id => !string.IsNullOrWhiteSpace(id)), Is.True);
            Assert.That(sceneIdentityIds.Distinct().Count(), Is.EqualTo(sceneIdentityIds.Length),
                "The added identities remain unique among every existing Stage02 physical target.");
        }
        finally
        {
            if (stage.IsValid() && stage.isLoaded) EditorSceneManager.ClosePreviewScene(stage);
        }
    }

    private static BoxCollider FindCollider(Scene scene, string name, float x) =>
        Resources.FindObjectsOfTypeAll<BoxCollider>().Where(c => c != null && c.gameObject.scene == scene && c.name == name)
            .OrderBy(c => Mathf.Abs(c.bounds.center.x - x)).FirstOrDefault();
}
