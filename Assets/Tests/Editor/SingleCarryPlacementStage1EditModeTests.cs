using NUnit.Framework;
using UnityEngine;

public class SingleCarryPlacementStage1EditModeTests
{
    private GameObject resourceObject;
    private ScriptableObject profile;

    [TearDown]
    public void TearDown()
    {
        if (resourceObject != null) Object.DestroyImmediate(resourceObject);
        if (profile != null) Object.DestroyImmediate(profile);
    }

    [Test]
    public void ResourceEligibility_RequiresCarryableSingleCarrierProfile()
    {
        System.Type profileType = System.Type.GetType("BaseResourceSO, Assembly-CSharp");
        System.Type resourceType = System.Type.GetType("BaseResourceNew, Assembly-CSharp");
        Assert.That(profileType, Is.Not.Null);
        Assert.That(resourceType, Is.Not.Null);
        profile = ScriptableObject.CreateInstance(profileType);
        resourceObject = new GameObject("Placement eligibility resource");
        Component resource = resourceObject.AddComponent(resourceType);
        resourceType.GetField("baseResourceSO", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(resource, profile);

        profileType.GetField("canBeCarried").SetValue(profile, true);
        profileType.GetField("allowMultipleCarriers").SetValue(profile, false);
        Assert.That((bool)resourceType.GetProperty("CanEnterSingleCarryPlacement").GetValue(resource), Is.True);

        profileType.GetField("allowMultipleCarriers").SetValue(profile, true);
        Assert.That((bool)resourceType.GetProperty("CanEnterSingleCarryPlacement").GetValue(resource), Is.False);

        profileType.GetField("allowMultipleCarriers").SetValue(profile, false);
        profileType.GetField("canBeCarried").SetValue(profile, false);
        Assert.That((bool)resourceType.GetProperty("CanEnterSingleCarryPlacement").GetValue(resource), Is.False);
    }

    [Test]
    public void PlacementToggleAction_UsesExistingGameMapAndFBinding()
    {
        System.Type wrapperType = System.Type.GetType("PlayerGameInputActions, Assembly-CSharp");
        Assert.That(wrapperType, Is.Not.Null);
        object wrapper = System.Activator.CreateInstance(wrapperType);
        try
        {
            object game = wrapperType.GetProperty("Game").GetValue(wrapper);
            object action = game.GetType().GetProperty("ToggleSingleCarryPlacement").GetValue(game);
            Assert.That(action, Is.Not.Null);
            var bindings = (System.Collections.IEnumerable)action.GetType().GetProperty("bindings").GetValue(action);
            bool hasFBinding = false;
            foreach (object binding in bindings)
                hasFBinding |= (string)binding.GetType().GetProperty("path").GetValue(binding) == "<Keyboard>/f";
            Assert.That(hasFBinding, Is.True);
        }
        finally
        {
            wrapperType.GetMethod("Disable").Invoke(wrapper, null);
            Object.DestroyImmediate((Object)wrapperType.GetProperty("asset").GetValue(wrapper));
        }
    }

    [Test]
    public void PlacementGhost_CopiesRendererOnlyAndDoesNotAddPhysicsToSource()
    {
        System.Type ghostType = System.Type.GetType("SingleCarryPlacementGhost, Assembly-CSharp");
        Assert.That(ghostType, Is.Not.Null);
        resourceObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Vector3 sourcePosition = resourceObject.transform.position;
        object ghost = ghostType.GetConstructor(new[] { typeof(GameObject) }).Invoke(new object[] { resourceObject });
        GameObject ghostRoot = (GameObject)ghostType.GetField("root", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(ghost);
        try
        {
            Assert.That(ghostRoot, Is.Not.Null);
            Assert.That(ghostRoot.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(resourceObject.GetComponent<Collider>(), Is.Not.Null);
            ghostType.GetMethod("SetPosition").Invoke(ghost, new object[] { Vector3.forward * 2f });
            Assert.That(resourceObject.transform.position, Is.EqualTo(sourcePosition));
        }
        finally
        {
            ghostType.GetMethod("Dispose").Invoke(ghost, null);
        }
    }
}
