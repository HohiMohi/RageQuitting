using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPickable
{
    void InitialiseBuildingMaterial(MeshFilter meshFilter, Material material);

    public void PickedUp();

    public void PuttedDown();

    GameObject GetGameObject();
}
