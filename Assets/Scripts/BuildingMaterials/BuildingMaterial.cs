using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]

[DisallowMultipleComponent]
public class BuildingMaterial : MonoBehaviour, IPickable
{
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;


    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
    }

    public GameObject GetGameObject()
    {
        return gameObject;
    }

    public void InitialiseBuildingMaterial(MeshFilter meshFilter, Material material)
    {
        this.meshFilter = meshFilter;
        this.meshRenderer.material = material;

        gameObject.SetActive(true);
    }



    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
