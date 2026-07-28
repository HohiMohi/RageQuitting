using System;
using UnityEngine;

public enum FactoryProductType
{
    MountableBridgeComponent,
    BaseResource
}

[CreateAssetMenu(fileName = "ProductionRecipe", menuName = "Scriptable Objects/Production Recipe")]
public class ProductionRecipeSO : ScriptableObject
{
    [Header("Display")]
    [SerializeField] private string recipeName;
    [SerializeField] private Sprite recipeIcon;

    [Header("Ingredients")]
    [SerializeField] private RequiredResource[] requiredResources;

    [Header("Output")]
    [SerializeField] private FactoryProductType productType;
    [SerializeField] private MountableBridgeComponentSO mountableBridgeComponentOutput;
    [SerializeField] private BaseResourceSO baseResourceOutput;
    [Min(1)]
    [SerializeField] private int outputAmount = 1;

    [Header("Blast Furnace Process")]
    [Min(0f)]
    [SerializeField] private float meltingPoint;
    [Min(0f)]
    [SerializeField] private float combustionTemperature;
    [Min(0f)]
    [SerializeField] private float neededProgress;
    [Min(0f)]
    [SerializeField] private float neededCombustionProgress;

    public string RecipeName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(recipeName))
            {
                return recipeName;
            }

            if (productType == FactoryProductType.MountableBridgeComponent
                && mountableBridgeComponentOutput != null)
            {
                return mountableBridgeComponentOutput.componentName;
            }

            return baseResourceOutput != null ? baseResourceOutput.resourceName : name;
        }
    }

    public Sprite RecipeIcon
    {
        get
        {
            if (recipeIcon != null)
            {
                return recipeIcon;
            }

            return productType == FactoryProductType.MountableBridgeComponent
                ? mountableBridgeComponentOutput != null ? mountableBridgeComponentOutput.componentSprite : null
                : baseResourceOutput != null ? baseResourceOutput.icon : null;
        }
    }

    public RequiredResource[] RequiredResources => requiredResources ?? Array.Empty<RequiredResource>();
    public FactoryProductType ProductType => productType;
    public MountableBridgeComponentSO MountableBridgeComponentOutput => mountableBridgeComponentOutput;
    public BaseResourceSO BaseResourceOutput => baseResourceOutput;
    public int OutputAmount => Mathf.Max(1, outputAmount);
    public float MeltingPoint => meltingPoint;
    public float CombustionTemperature => combustionTemperature;
    public float NeededProgress => neededProgress;
    public float NeededCombustionProgress => neededCombustionProgress;

    public bool HasValidOutput =>
        productType == FactoryProductType.MountableBridgeComponent
            ? mountableBridgeComponentOutput != null && mountableBridgeComponentOutput.inGameGameObjectPrefab != null
            : baseResourceOutput != null && baseResourceOutput.resourcePrefab != null;
}
