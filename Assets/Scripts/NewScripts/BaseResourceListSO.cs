using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BaseResourceListSO", menuName = "Scriptable Objects/BaseResourceListSO")]
public class BaseResourceListSO : ScriptableObject
{
    public List<BaseResourceSO> baseResourceSOList; 
}
