using UnityEngine;
using System.Collections.Generic;
using System;

[CreateAssetMenu(fileName = "BridgeElementsDatabaseSO", menuName = "Scriptable Objects/BridgeElementsDatabaseSO")]
public class BridgeElementsDatabaseSO : ScriptableObject
{
    public List<ObjectData> objectsData;
}

[Serializable]
public class ObjectData
{
    [field: SerializeField]
    public string Name { get; private set; }

    [field: SerializeField]
    public int ID { get; private set; }

    [field: SerializeField]
    public Vector2Int Size { get; private set; } = Vector2Int.one;

    [field: SerializeField]
    public GameObject Prefab { get; private set; }

    [field: SerializeField]
    public BuildingElementSO buildingElementSO { get; private set; }

}