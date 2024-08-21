using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPickable
{
    void InitialiseBuildingMaterial(MeshFilter meshFilter, Material material);

    GameObject GetGameObject();
}
