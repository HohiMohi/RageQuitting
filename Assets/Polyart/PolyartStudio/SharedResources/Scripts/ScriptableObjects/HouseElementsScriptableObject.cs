using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName ="HouseElement_ScriptableObject", menuName ="ScriptableObjects/House Elements")]
public class HouseElementsScriptableObject : ScriptableObject
{
    public HouseElementArray[] houseElements;
 
}

[System.Serializable]
public class HouseElementArray
{
    public string elementId;
    public GameObject elementPrefab;
}