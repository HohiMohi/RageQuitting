using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BuildingMaterialDetails_", menuName = "Scriptable Objects/Pickables/BuildingMaterial")]
public class BuildingMaterialDetailsSO : ScriptableObject
{
    public GameObject buildingMaterialPrefab;
    public MeshFilter meshFilter;
    public Material material;
}
