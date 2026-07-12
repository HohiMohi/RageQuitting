using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NPCInterestProfile", menuName = "Scriptable Objects/NPC/Interest Profile")]
public class NPCInterestProfileSO : ScriptableObject
{
    [SerializeField] private bool allowAnyBaseResource = true;
    [SerializeField] private bool allowAnyMountableBridgeComponent = true;
    [SerializeField] private BaseResourceSO[] allowedBaseResources;
    [SerializeField] private MountableBridgeComponentSO[] allowedMountableBridgeComponents;

    public IReadOnlyList<BaseResourceSO> AllowedBaseResources => allowedBaseResources;
    public bool AllowsAnyBaseResource => allowAnyBaseResource;

    public bool IsInterestedIn(BaseResourceNew resource)
    {
        return resource != null && IsInterestedIn(resource.GetBaseResourceSO());
    }

    public bool IsInterestedIn(MountableBridgeComponent component)
    {
        return component != null && IsInterestedIn(component.GetMountableBridgeComponentSO());
    }

    public bool IsInterestedIn(BaseResourceSO resourceSO)
    {
        if (resourceSO == null)
        {
            return false;
        }

        if (allowAnyBaseResource)
        {
            return true;
        }

        return Contains(allowedBaseResources, resourceSO);
    }

    public bool IsInterestedIn(MountableBridgeComponentSO componentSO)
    {
        if (componentSO == null)
        {
            return false;
        }

        if (allowAnyMountableBridgeComponent)
        {
            return true;
        }

        return Contains(allowedMountableBridgeComponents, componentSO);
    }

    private static bool Contains<T>(T[] values, T searchedValue) where T : Object
    {
        if (values == null)
        {
            return false;
        }

        foreach (T value in values)
        {
            if (value == searchedValue)
            {
                return true;
            }
        }

        return false;
    }
}
