using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class SingleCarryPlacementStage4EditModeTests
{
    [Test]
    public void ResourcePlacementEligibilityTracksCarryAndSharedConfiguration()
    {
        GameObject item = new GameObject("single carry resource eligibility");
        item.AddComponent<Rigidbody>();
        Component resource = item.AddComponent(Type.GetType("BaseResourceNew, Assembly-CSharp"));
        ScriptableObject profile = ScriptableObject.CreateInstance(Type.GetType("BaseResourceSO, Assembly-CSharp"));
        try
        {
            Type profileType = profile.GetType();
            profileType.GetField("canBeCarried").SetValue(profile, true);
            profileType.GetField("allowMultipleCarriers").SetValue(profile, false);
            resource.GetType().GetField("baseResourceSO", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(resource, profile);
            Assert.That((bool)resource.GetType().GetProperty("CanEnterSingleCarryPlacement").GetValue(resource), Is.True);

            profileType.GetField("allowMultipleCarriers").SetValue(profile, true);
            Assert.That((bool)resource.GetType().GetProperty("CanEnterSingleCarryPlacement").GetValue(resource), Is.False,
                "Configured Shared carry remains ineligible even when it could have one holder.");
            profileType.GetField("allowMultipleCarriers").SetValue(profile, false);
            profileType.GetField("canBeCarried").SetValue(profile, false);
            Assert.That((bool)resource.GetType().GetProperty("CanEnterSingleCarryPlacement").GetValue(resource), Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(item);
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void MountablePlacementEligibilityRejectsSharedCarryProfile()
    {
        GameObject item = new GameObject("single carry mountable eligibility");
        item.AddComponent<Rigidbody>();
        Component mountable = item.AddComponent(Type.GetType("MountableBridgeComponent, Assembly-CSharp"));
        ScriptableObject profile = ScriptableObject.CreateInstance(Type.GetType("MountableBridgeComponentSO, Assembly-CSharp"));
        try
        {
            FieldInfo profileField = mountable.GetType().GetField("mountableBridgeComponentSO", BindingFlags.Instance | BindingFlags.NonPublic);
            profileField.SetValue(mountable, profile);
            profile.GetType().GetField("allowMultipleCarriers").SetValue(profile, false);
            Assert.That((bool)mountable.GetType().GetProperty("CanEnterSingleCarryPlacement").GetValue(mountable), Is.True);
            profile.GetType().GetField("allowMultipleCarriers").SetValue(profile, true);
            Assert.That((bool)mountable.GetType().GetProperty("CanEnterSingleCarryPlacement").GetValue(mountable), Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(item);
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }
}
