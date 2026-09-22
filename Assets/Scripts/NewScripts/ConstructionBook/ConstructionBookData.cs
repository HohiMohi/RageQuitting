using System;
using System.Collections.Generic;

public readonly struct ConstructionBookMaterialLine
{
    public readonly BaseResourceSO Resource;
    public readonly int PerUnit;
    public readonly int Total;
    public ConstructionBookMaterialLine(BaseResourceSO resource, int perUnit, int total)
    { Resource = resource; PerUnit = perUnit; Total = total; }
}

public static class ConstructionBookData
{
    public static bool TryResolveRecipe(
        BridgeComponentSO component,
        int requiredCount,
        IEnumerable<ProductionRecipeSO> recipes,
        out List<ConstructionBookMaterialLine> materials,
        out string issue)
    {
        materials = new List<ConstructionBookMaterialLine>();
        issue = null;
        ProductionRecipeSO match = null;
        int matches = 0;
        foreach (ProductionRecipeSO recipe in recipes)
        {
            MountableBridgeComponentSO output = recipe != null ? recipe.MountableBridgeComponentOutput : null;
            if (recipe != null && recipe.ProductType == FactoryProductType.MountableBridgeComponent &&
                output != null && output.bridgeComponentSO == component)
            { match = recipe; matches++; }
        }
        if (matches != 1)
        {
            string componentName = component != null ? component.name : "null";
            issue = matches == 0 ? $"missing direct Carpenter recipe for '{componentName}'"
                : $"{matches} direct Carpenter recipes found for '{componentName}'";
            return false;
        }
        foreach (RequiredResource resource in match.RequiredResources)
        {
            if (resource.resourceType != null)
                materials.Add(new ConstructionBookMaterialLine(resource.resourceType, resource.amount, resource.amount * requiredCount));
        }
        return true;
    }
}
