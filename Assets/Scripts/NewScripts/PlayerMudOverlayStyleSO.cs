using UnityEngine;

[CreateAssetMenu(fileName = "PlayerMudOverlayStyle", menuName = "RageQuitting/Player Mud Overlay Style")]
public sealed class PlayerMudOverlayStyleSO : ScriptableObject
{
    [Header("Full screen mud texture")]
    [SerializeField] private Sprite mudTextureSprite;
    [SerializeField] private Texture2D thresholdMap;
    [Header("Animated drip")]
    [SerializeField] private Sprite dripSprite;

    public Sprite MudTextureSprite => mudTextureSprite;
    public Texture2D ThresholdCoverageMap => thresholdMap;
    public Sprite DripSprite => dripSprite;
}
