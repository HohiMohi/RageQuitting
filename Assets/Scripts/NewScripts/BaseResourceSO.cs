using UnityEngine;

[CreateAssetMenu(fileName = "BaseResourceSO", menuName = "Scriptable Objects/BaseResourceSO")]
public class BaseResourceSO : ScriptableObject
{
    public string resourceName;
    public GameObject resourcePrefab;

}
