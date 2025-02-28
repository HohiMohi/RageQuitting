using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Scriptable Objects/BuildingElements/BridgeGraph")]

public class BridgeGraph:ScriptableObject
{
    public List<BridgeNode> bridgeNodeList = new List<BridgeNode>();
    public Dictionary<string, BridgeNode> bridgeNodeDictionary = new Dictionary<string, BridgeNode>();
    public GameObject bridgeParent;

    

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

    /// <summary>
    /// Instantiate bridge - create objects for every BridgeNode
    /// </summary>
    public void InstantiateBridge()
    {
        Debug.Log("?");
        foreach (KeyValuePair<string, BridgeNode> bridgeNodeObject in bridgeNodeDictionary)
        {
            bridgeNodeObject.Value.InstantiateBridgeNodeElement(bridgeParent.transform);

        }
    }

    /// <summary>
    /// Check if every node added to graph is correctly connected - returns false, if any node IS NOT connected correctly.
    /// </summary>
    public bool ValidateBridge()
    {
        bool isCorrectlyConnected = true;

        // Check if every node in bridgeNodeDictionary is connected properly
        foreach (KeyValuePair<string, BridgeNode> nodeToCheck in bridgeNodeDictionary)
        {
            if(!nodeToCheck.Value.ValidateNode())
            {
                Debug.Log("Node " + nodeToCheck.Value.name + " is not connected properly.");
                isCorrectlyConnected = false;
            }
        }

        if (isCorrectlyConnected)
            Debug.Log("Every node in bridge is connected properly.");

        return isCorrectlyConnected;
    }


}

[CreateAssetMenu(menuName = "Scriptable Objects/BuildingElements/BridgeGraph2")]

public class BridgeGraph2 : ScriptableObject
{
    public List<BridgeNodeDataHolder> bridgeNodeList = new List<BridgeNodeDataHolder>();
    public Dictionary<string, BridgeNodeDataHolder> bridgeNodeDictionary = new Dictionary<string, BridgeNodeDataHolder>();
    public GameObject bridgeParent;



    /// <summary>
    /// Add given node to dictionary and list.
    /// </summary>
    public void AddNode(BridgeNodeDataHolder bridgeNodeToAdd)
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
    public void DeleteNode(BridgeNodeDataHolder bridgeNodeToDelete)
    {
        if (CheckNodeRemovingPossibility(bridgeNodeToDelete))
        {
            int connectedElementCount = bridgeNodeToDelete.connectedBridgeElementsList.Count;
            string[] connectedElementIDsTable = new string[connectedElementCount];
            int counter = 0;
            foreach (string nodeID in bridgeNodeToDelete.connectedBridgeElementsList)
            {
                connectedElementIDsTable[counter] = nodeID;
                counter++;
            }
            for (int i = 0; i < connectedElementCount; i++)
            {
                bridgeNodeDictionary[connectedElementIDsTable[i]].DisconnectNode(bridgeNodeToDelete);
            }
            RemoveNodeFromList(bridgeNodeToDelete);
            RemoveNodeFromGraph(bridgeNodeToDelete);
        }
    }

    /// <summary>
    ///  Add given Node to graph's Dictionary
    /// </summary>
    private void AddNodeToDictionary(BridgeNodeDataHolder bridgeNodeToAdd)
    {
        bridgeNodeDictionary.Add(bridgeNodeToAdd.nodeID, bridgeNodeToAdd);
    }

    /// <summary>
    /// Add given nodeID to graph's List
    /// </summary>
    private void AddNodeToList(BridgeNodeDataHolder bridgeNodeToAdd)
    {
        bridgeNodeList.Add(bridgeNodeToAdd);
    }

    /// <summary>
    /// Remove given Node from Graph's Dictionary
    /// </summary>
    private void RemoveNodeFromGraph(BridgeNodeDataHolder bridgeNodeToRemove)
    {
        bridgeNodeDictionary.Remove(bridgeNodeToRemove.nodeID);
    }

    /// <summary>
    /// Remove given NodeID from Graph's List
    /// </summary>
    private void RemoveNodeFromList(BridgeNodeDataHolder bridgeNodeToRemove)
    {
        bridgeNodeList.Remove(bridgeNodeToRemove);
    }

    /// <summary>
    /// Recreate Dictionary from bridgeNodeList.
    /// </summary>
    public void InitialiseDictionaryFromList()
    {
        foreach (BridgeNodeDataHolder bridgeNode in bridgeNodeList)
        {
            bridgeNodeDictionary.Add(bridgeNode.nodeID, bridgeNode);
        }
    }

    /// <summary>
    /// Check if adding node to graph is possible - if list and graph do not already contain this node. Returns true if adding is possible - list and graph do not contain this node
    /// </summary>
    public bool CheckNodeAddingPossibility(BridgeNodeDataHolder bridgeNodeToCheck)
    {
        bool isAddingPossible = true;

        if (!(CheckListContent(bridgeNodeToCheck) && CheckDictionaryContent(bridgeNodeToCheck)))
            isAddingPossible = false;

        return isAddingPossible;
    }

    /// <summary>
    /// Check if removing node from graph is possible - if list and graph contain this node. Returns true if removing is possible - list and graph contain this node
    /// </summary>
    public bool CheckNodeRemovingPossibility(BridgeNodeDataHolder bridgeNodeToCheck)
    {
        bool isRemovingPossible = true;

        if (CheckListContent(bridgeNodeToCheck) || CheckDictionaryContent(bridgeNodeToCheck))
            isRemovingPossible = false;

        return isRemovingPossible;
    }

    /// <summary>
    /// Check if graph's dictionary contains given bridgeNode. Return true if dictionary DOES NOT contain given node.
    /// </summary>
    public bool CheckDictionaryContent(BridgeNodeDataHolder bridgeNodeToCheck)
    {
        bool isNotInDictionary = true;

        // Compare every key from dictionary with given BridgeNode's ID
        foreach (KeyValuePair<string, BridgeNodeDataHolder> node in bridgeNodeDictionary)
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
    public bool CheckListContent(BridgeNodeDataHolder nodeToCheck)
    {
        bool isNotInList = true;

        // Compare every nodeID in bridgeNodeList with given nodeIDToCheck
        foreach (BridgeNodeDataHolder node in bridgeNodeList)
        {
            if (node.nodeID == nodeToCheck.nodeID)
            {
                isNotInList = false;
                break;
            }
        }

        return isNotInList;
    }

    /// <summary>
    /// Instantiate bridge - create objects for every BridgeNode
    /// </summary>
    public void InstantiateBridge()
    {
        Debug.Log("?");
        foreach (KeyValuePair<string, BridgeNodeDataHolder> bridgeNodeObject in bridgeNodeDictionary)
        {
            bridgeNodeObject.Value.InstantiateBridgeNodeElement(bridgeParent.transform);

        }
    }

    public BridgeNodeDataHolder FindBridgeNode(BuildingElementSO buildingElementTypeToFind, Vector3Int occupiedCell)
    {
        foreach(BridgeNodeDataHolder nodeToCheck in bridgeNodeList)
        {
            if(nodeToCheck.buildingElementSO == buildingElementTypeToFind)
            {
                if (nodeToCheck.CheckCellOccupancy(occupiedCell))
                    return nodeToCheck;
            }
        }

        return null;
    }

    /// <summary>
    /// Check if every node added to graph is correctly connected - returns false, if any node IS NOT connected correctly.
    /// </summary>
    public bool ValidateBridge()
    {
        bool isCorrectlyConnected = true;

        // Check if every node in bridgeNodeDictionary is connected properly
        foreach (KeyValuePair<string, BridgeNodeDataHolder> nodeToCheck in bridgeNodeDictionary)
        {
            if (!nodeToCheck.Value.ValidateNode())
            {
                Debug.Log("Node " + nodeToCheck.Value.nodeID + " is not connected properly.");
                isCorrectlyConnected = false;
            }
        }

        if (isCorrectlyConnected)
            Debug.Log("Every node in bridge is connected properly.");

        return isCorrectlyConnected;
    }


}
