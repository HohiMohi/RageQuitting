using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPickable
{
    void InitialiseBuildingMaterial(MeshFilter meshFilter, Material material);

    public void PickedUp(GameObject pickerPlayer);

    public void PuttedDown(GameObject puttingDownPlayer);

    GameObject GetGameObject();
}
