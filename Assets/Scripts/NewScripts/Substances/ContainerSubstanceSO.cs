using UnityEngine;

public enum ContainerSubstanceKind
{
    Soil,
    Water,
    Gravel,
    Concrete
}

[CreateAssetMenu(fileName = "ContainerSubstanceSO", menuName = "Scriptable Objects/Container Substance")]
public class ContainerSubstanceSO : ScriptableObject
{
    [SerializeField] private string displayName = "Substance";
    [SerializeField] private Color displayColor = new Color(0.35f, 0.2f, 0.1f, 1f);
    [SerializeField] private ContainerSubstanceKind substanceKind = ContainerSubstanceKind.Soil;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Color DisplayColor => displayColor;
    public ContainerSubstanceKind SubstanceKind => substanceKind;
    public bool IsSoil => substanceKind == ContainerSubstanceKind.Soil;
}
