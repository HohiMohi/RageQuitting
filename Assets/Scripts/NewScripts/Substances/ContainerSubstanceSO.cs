using UnityEngine;

[CreateAssetMenu(fileName = "ContainerSubstanceSO", menuName = "Scriptable Objects/Container Substance")]
public class ContainerSubstanceSO : ScriptableObject
{
    [SerializeField] private string displayName = "Substance";
    [SerializeField] private Color displayColor = new Color(0.35f, 0.2f, 0.1f, 1f);
    [SerializeField] private bool isSoil;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Color DisplayColor => displayColor;
    public bool IsSoil => isSoil;
}
