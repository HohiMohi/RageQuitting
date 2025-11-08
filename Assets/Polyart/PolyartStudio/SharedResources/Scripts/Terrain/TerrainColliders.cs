using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TerrainColliders : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<TerrainCollider>().enabled = false;
        GetComponent<TerrainCollider>().enabled = true;
    }
}
