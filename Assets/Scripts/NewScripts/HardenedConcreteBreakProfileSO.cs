using UnityEngine;

[CreateAssetMenu(fileName = "HardenedConcreteBreakProfile", menuName = "Scriptable Objects/Hardened Concrete Break Profile")]
public sealed class HardenedConcreteBreakProfileSO : ScriptableObject
{
    [SerializeField, Min(1f)] private float workRequired = 100f;
    [SerializeField, Min(0.05f)] private float collapseDuration = 0.4f;
    [SerializeField] private Vector3 crackThresholds = new Vector3(1f, 34f, 67f);
    [SerializeField] private EquippableItemType requiredTool = EquippableItemType.Pickaxe;

    public float WorkRequired => Mathf.Max(1f, workRequired);
    public float CollapseDuration => Mathf.Max(0.05f, collapseDuration);
    public EquippableItemType RequiredTool => requiredTool;
    public Vector3 CrackThresholds
    {
        get
        {
            float first = Mathf.Max(0f, crackThresholds.x);
            float second = Mathf.Max(first, crackThresholds.y);
            float third = Mathf.Max(second, crackThresholds.z);
            return new Vector3(first, second, third);
        }
    }
}
