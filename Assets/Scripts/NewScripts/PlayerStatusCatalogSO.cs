using UnityEngine;

[CreateAssetMenu(menuName = "Rage Quitting/Player Status Catalog", fileName = "PlayerStatusCatalog")]
public sealed class PlayerStatusCatalogSO : ScriptableObject
{
    [System.Serializable]
    public sealed class MudProfile
    {
        [Min(0f)] public float soilPercentagePointsPerMetre = 5f;
        [Min(0f)] public float bucketPercentagePointsPerUnit = 15f;
        [Min(0f)] public float waterRemovalPercentagePointsPerSecond = 10f;
        [Range(0f, 1f)] public float maximumMovementPenalty = 0.3f;
        [Range(0f, 1f)] public float maximumAlpha = 0.6f;
    }

    [SerializeField] private MudProfile mud = new MudProfile();
    public MudProfile Mud => mud ?? (mud = new MudProfile());
}
