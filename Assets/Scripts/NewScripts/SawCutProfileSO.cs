using UnityEngine;

[CreateAssetMenu(fileName = "SawCutProfile", menuName = "Scriptable Objects/Saw Cut Profile")]
public sealed class SawCutProfileSO : ScriptableObject
{
    [Tooltip("Canonical deck geometry, kerf, minimum piece length and material density profile.")]
    public BridgeDeckAreaProfile geometryProfile;
    [Min(0.01f)] public float activeCutSeconds = 4f;
    [Min(0.01f)] public float maximumRangeMeters = 2.5f;
    [Min(0.01f)] public float stableSeconds = 0.35f;
    [Min(0f)] public float maximumLinearSpeed = 0.05f;
    [Min(0f)] public float maximumAngularSpeedDegrees = 3f;
    public Material previewLineMaterial;
    public GameObject fragmentNetworkPrefab;
}
