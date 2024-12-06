using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Scriptable Objects/BuildingElements/BridgeGraph")]

public class BridgeGraph:ScriptableObject
{
    public List<BridgeNode> bridgeNodeList = new List<BridgeNode>();
    public Dictionary<string, BridgeNode> bridgeNodeDictionary = new Dictionary<string, BridgeNode>();


    

    /// <summary>
    /// Add given node to dictionary and list.
    /// </summary>
    public void AddNode(BridgeNode bridgeNodeToAdd)
    {
        if (CheckNodeAddingPossibility(bridgeNodeToAdd))
        {
            AddNodeToList(bridgeNodeToAdd);
            AddNodeToDictionary(bridgeNodeToAdd);
        }
    }

    /// <summary>
    /// Remove given node from dictionary and list.
    /// </summary>
    public void DeleteNode(BridgeNode bridgeNodeToDelete)
    {
        if(CheckNodeRemovingPossibility(bridgeNodeToDelete))
        {
            RemoveNodeFromList(bridgeNodeToDelete);
            RemoveNodeFromGraph(bridgeNodeToDelete);
        }
    }

    /// <summary>
    ///  Add given Node to graph's Dictionary
    /// </summary>
    private void AddNodeToDictionary(BridgeNode bridgeNodeToAdd)
    {
        bridgeNodeDictionary.Add(bridgeNodeToAdd.nodeID, bridgeNodeToAdd);
    }

    /// <summary>
    /// Add given nodeID to graph's List
    /// </summary>
    private void AddNodeToList(BridgeNode bridgeNodeToAdd)
    {
        bridgeNodeList.Add(bridgeNodeToAdd);
    }

    /// <summary>
    /// Remove given Node from Graph's Dictionary
    /// </summary>
    private void RemoveNodeFromGraph(BridgeNode bridgeNodeToRemove)
    {
        bridgeNodeDictionary.Remove(bridgeNodeToRemove.nodeID);
    }

    /// <summary>
    /// Remove given NodeID from Graph's List
    /// </summary>
    private void RemoveNodeFromList(BridgeNode bridgeNodeToRemove)
    {
        bridgeNodeList.Remove(bridgeNodeToRemove);
    }

    /// <summary>
    /// Recreate Dictionary from bridgeNodeList.
    /// </summary>
    public void InitialiseDictionaryFromList()
    {
        foreach(BridgeNode bridgeNode in bridgeNodeList)
        {
            bridgeNodeDictionary.Add(bridgeNode.nodeID, bridgeNode);
        }
    }

    /// <summary>
    /// Check if adding node to graph is possible - if list and graph do not already contain this node. Returns true if adding is possible - list and graph do not contain this node
    /// </summary>
    public bool CheckNodeAddingPossibility(BridgeNode bridgeNodeToCheck)
    {
        bool isAddingPossible = true;

        if (!(CheckListContent(bridgeNodeToCheck) && CheckDictionaryContent(bridgeNodeToCheck)))
            isAddingPossible = false;

        return isAddingPossible;
    }

    /// <summary>
    /// Check if removing node from graph is possible - if list and graph contain this node. Returns true if removing is possible - list and graph contain this node
    /// </summary>
    public bool CheckNodeRemovingPossibility(BridgeNode bridgeNodeToCheck)
    {
        bool isRemovingPossible = true;

        if (CheckListContent(bridgeNodeToCheck) || CheckDictionaryContent(bridgeNodeToCheck))
            isRemovingPossible = false;

        return isRemovingPossible;
    }

    /// <summary>
    /// Check if graph's dictionary contains given bridgeNode. Return true if dictionary DOES NOT contain given node.
    /// </summary>
    public bool CheckDictionaryContent(BridgeNode bridgeNodeToCheck)
    {
        bool isNotInDictionary = true;

        // Compare every key from dictionary with given BridgeNode's ID
        foreach (KeyValuePair<string, BridgeNode> node in bridgeNodeDictionary)
        {
            if (node.Key == bridgeNodeToCheck.nodeID)
            {
                isNotInDictionary = false;
                break;
            }
        }

        return isNotInDictionary;
    }

    /// <summary>
    /// Check if graph's list contains given nodeID. Return true if list DOES NOT contain given nodeID.
    /// </summary>
    public bool CheckListContent(BridgeNode nodeToCheck)
    {
        bool isNotInList = true;

        // Compare every nodeID in bridgeNodeList with given nodeIDToCheck
        foreach (BridgeNode node in bridgeNodeList)
        {
            if(node.nodeID == nodeToCheck.nodeID)
            {
                isNotInList = false;
                break;
            }
        }

        return isNotInList;
    }
}
