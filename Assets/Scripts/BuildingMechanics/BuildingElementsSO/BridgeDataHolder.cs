using System;
using System.Collections.Generic;
using UnityEngine;

public class BridgeGraphDataHolder : MonoBehaviour
{
    public List<BridgeNodeDataHolder> bridgeNodeList = new List<BridgeNodeDataHolder>();
    public Dictionary<string, BridgeNodeDataHolder> bridgeNodeDictionary = new Dictionary<string, BridgeNodeDataHolder>();
    public GameObject bridgeParent;
    public BridgeGraph bridgeNodeToCopy;

    public void TransferGraphToDataHolder(BridgeGraph bridgeGraphToTransfer)
    {
        foreach (BridgeNode node in bridgeGraphToTransfer.bridgeNodeList)
        {
            BridgeNodeDataHolder newNodeDataHolder = new BridgeNodeDataHolder(node.guid, node.nodeID, node.buildingElementSO, node.connectedBridgeElementsList, node.connectedSpans, node.connectedConnectors, node.connectedSupports, node.connectedGroundMounts, node.connectedSideMounts);
            Debug.Log("?");
            AddNode(newNodeDataHolder);
        }
    }


    private void Start()
    {
        Debug.Log(bridgeNodeList.Count);
        TransferGraphToDataHolder(bridgeNodeToCopy);
        Debug.Log(bridgeNodeList.Count);
    }

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
                Debug.Log("Node " + nodeToCheck.Value.guid + " is not connected properly.");
                isCorrectlyConnected = false;
            }
        }

        if (isCorrectlyConnected)
            Debug.Log("Every node in bridge is connected properly.");

        return isCorrectlyConnected;
    }


}

[Serializable]
public class BridgeNodeDataHolder
{
    public Guid guid;
    public string nodeID;
    public BuildingElementSO buildingElementSO;
    public List<string> connectedBridgeElementsList = new List<string>();
    public int connectedSpans;
    public int connectedConnectors;
    public int connectedSupports;
    public int connectedGroundMounts;
    public int connectedSideMounts;
    public List<Vector3Int> occupiedCellVectorsList = new List<Vector3Int>();

    public BridgeNodeDataHolder()
    {
        this.guid = Guid.NewGuid();
        this.nodeID = guid.ToString();
        this.connectedConnectors = 0;
        this.connectedSpans = 0;
        this.connectedSupports = 0;
        this.connectedGroundMounts = 0;
        this.connectedSideMounts = 0;
    }

    public BridgeNodeDataHolder(Guid guid, string nodeID, BuildingElementSO buildingElementSO, List<string> connectedBridgeElementsList, int connectedSpans, int connectedConnectors, int connectedSupports, int connectedGroundMounts, int connectedSideMounts)
    {
        this.guid = guid;
        this.nodeID = nodeID ?? throw new ArgumentNullException(nameof(nodeID));
        this.buildingElementSO = buildingElementSO ?? throw new ArgumentNullException(nameof(buildingElementSO));
        this.connectedBridgeElementsList = connectedBridgeElementsList ?? throw new ArgumentNullException(nameof(connectedBridgeElementsList));
        this.connectedSpans = connectedSpans;
        this.connectedConnectors = connectedConnectors;
        this.connectedSupports = connectedSupports;
        this.connectedGroundMounts = connectedGroundMounts;
        this.connectedSideMounts = connectedSideMounts;
    }

    /// <summary>
    /// Remove Connection between this and given nodeID in connectedBridgeElementsList
    /// </summary>
    public bool DisconnectNode(BridgeNodeDataHolder bridgeNodeToDisconnect)
    {
        bool isDisconnectingPossible = true;

        #region CheckingConnectionBetweenNodes

        // Check if given node is in this node's connectedBridgeElementsList
        isDisconnectingPossible = !CheckAlreadyConnectedNodes(bridgeNodeToDisconnect.nodeID);
        if (!isDisconnectingPossible)
            return isDisconnectingPossible;

        //Check if this node is in given node's connectedBridgeElementsList
        isDisconnectingPossible = !bridgeNodeToDisconnect.CheckAlreadyConnectedNodes(nodeID);
        if (!isDisconnectingPossible)
            return isDisconnectingPossible;

        #endregion

        #region RemoveNodesFromLists

        // Delete given Node from this node's connectedBridgeElementsList
        DeleteNodeFromConnectedElementsList(bridgeNodeToDisconnect.nodeID);

        // Delete this Node from given node's connectedBridgeElementsList
        bridgeNodeToDisconnect.DeleteNodeFromConnectedElementsList(nodeID);

        //Decrease this node's BridgeElementTypeCounter
        DecreaseConnectedElementsCounter(bridgeNodeToDisconnect.buildingElementSO.elementType);

        //Decrease given node's BridgeElementTypeCounter
        bridgeNodeToDisconnect.DecreaseConnectedElementsCounter(buildingElementSO.elementType);

        #endregion

        return isDisconnectingPossible;
    }

    /// <summary>
    /// Connect given BridgeNode - add it to the connectedBridgeElementsList. Returns true, if connecting is possible and done, otherwise returns false.
    /// </summary>
    public bool ConnectNode(BridgeNodeDataHolder bridgeNodeToConnect)
    {
        bool isConnectingPossible = true;

        #region ConnectionPossibilityCheck
        // Check if given Node's ID is the same as this node's ID - self Connection attempt
        isConnectingPossible = !CheckSelfConnection(bridgeNodeToConnect.nodeID);
        if (!isConnectingPossible)
            return isConnectingPossible;

        // Check if there is a free space(connectedBuildingElementType < BuildingElementSO.maxBuildingElementType) in this Node to connect given Node
        isConnectingPossible = CheckConnectionPossibility(bridgeNodeToConnect.buildingElementSO.elementType);
        if (!isConnectingPossible)
            return isConnectingPossible;

        // Check if there is a free space in given Node to connect this node
        isConnectingPossible = bridgeNodeToConnect.CheckConnectionPossibility(buildingElementSO.elementType);
        if (!isConnectingPossible)
            return isConnectingPossible;

        // Check if given Node is already connected to this node
        isConnectingPossible = CheckAlreadyConnectedNodes(bridgeNodeToConnect.nodeID);
        if (!isConnectingPossible)
            return isConnectingPossible;

        // Check if this node is already connected to this node - double check if nothing went wrong with connecting before
        isConnectingPossible = bridgeNodeToConnect.CheckAlreadyConnectedNodes(nodeID);
        if (!isConnectingPossible)
            return isConnectingPossible;
        #endregion

        #region AddNodesToList

        // Add given Node to this node ConnectedElementsList
        AddNodeToConnectedElementsList(bridgeNodeToConnect.nodeID);

        // Add this node to given Node ConnectedElementsList
        bridgeNodeToConnect.AddNodeToConnectedElementsList(nodeID);

        // Increase this node's BridgeElementTypeCounter
        IncreaseConnectedElementsCounter(bridgeNodeToConnect.buildingElementSO.elementType);

        // Increase give node's BridgeElementTypeCounter
        bridgeNodeToConnect.IncreaseConnectedElementsCounter(buildingElementSO.elementType);

        #endregion

        return isConnectingPossible;
    }

    /// <summary>
    /// Add given nodeID to connectedBridgeElementsList
    /// </summary>
    public void AddNodeToConnectedElementsList(string nodeIDToConnect)
    {
        connectedBridgeElementsList.Add(nodeIDToConnect);
    }

    /// <summary>
    /// Remove given nodeID from connectedBridgeElementsList
    /// </summary>
    public void DeleteNodeFromConnectedElementsList(string nodeIDToRemove)
    {
        connectedBridgeElementsList.Remove(nodeIDToRemove);
    }

    /// <summary>
    /// Decrease node's BuildingElementTypeCounter by 1
    /// </summary>
    public void DecreaseConnectedElementsCounter(BridgeElementType bridgeElementTypeToDecrease)
    {
        switch (bridgeElementTypeToDecrease)
        {
            case BridgeElementType.Connector:
                {
                    if (this.connectedConnectors > 0)
                        this.connectedConnectors--;
                    break;
                }
            case BridgeElementType.GroundMount:
                {
                    if (this.connectedGroundMounts > 0)
                        this.connectedGroundMounts--;
                    break;
                }
            case BridgeElementType.SideMount:
                {
                    if (this.connectedSideMounts > 0)
                        this.connectedSideMounts--;
                    break;
                }
            case BridgeElementType.Span:
                {
                    if (this.connectedSpans > 0)
                        this.connectedSpans--;
                    break;
                }
            case BridgeElementType.Support:
                {
                    if (this.connectedSupports > 0)
                        this.connectedSupports--;
                    break;
                }
            default:
                break;
        }
    }

    /// <summary>
    /// Checking if it is possible to connect given BridgeElementType to BridgeNode
    /// </summary>
    public bool CheckConnectionPossibility(BridgeElementType bridgeElementType)
    {
        bool isConnectionPossible = true;

        // Checking if it is possible to connect new BridgeElementType
        switch (bridgeElementType)
        {
            case BridgeElementType.Connector:
                {
                    if ((buildingElementSO.maxConnectorJoints <= connectedConnectors))
                        isConnectionPossible = false;
                    break;
                }
            case BridgeElementType.GroundMount:
                {
                    if (buildingElementSO.maxGroundMountJoints <= connectedGroundMounts)
                        isConnectionPossible = false;
                    break;
                }
            case BridgeElementType.SideMount:
                {
                    if ((buildingElementSO.maxSideMountJoints <= connectedSideMounts) || ((connectedSupports + connectedSideMounts) >= 2))
                        isConnectionPossible = false;
                    break;
                }
            case BridgeElementType.Span:
                {
                    if (buildingElementSO.maxSpanJoints <= connectedSpans)
                        isConnectionPossible = false;
                    break;
                }
            case BridgeElementType.Support:
                {
                    if ((buildingElementSO.maxSupportJoints <= connectedSupports) || ((connectedSupports + connectedSideMounts) >= 2))
                        isConnectionPossible = false;
                    break;
                }
            default:
                {
                    break;
                }
        }

        return isConnectionPossible;
    }

    /// <summary>
    /// Check if given Node is already connected to this Node.
    /// </summary>
    public bool CheckAlreadyConnectedNodes(string nodeIDToCheck)
    {
        bool isConnectionPossible = true;

        foreach (string connectedNodeID in connectedBridgeElementsList)
        {
            if (connectedNodeID == nodeIDToCheck)
                isConnectionPossible = false;
        }

        return isConnectionPossible;
    }

    /// <summary>
    /// Check if given ID is same as this node's ID - returns true if they are the same
    /// </summary>
    public bool CheckSelfConnection(string nodeIdToCheck)
    {
        if (this.nodeID == nodeIdToCheck)
            return true;
        else
            return false;
    }

    public bool CheckCellOccupancy(Vector3Int cellPositionToCheck)
    {
        foreach (Vector3Int cellPosition in occupiedCellVectorsList)
        {
            if (cellPositionToCheck == cellPosition)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Increase node BridgeElementTypeCounter by 1
    /// </summary>
    public void IncreaseConnectedElementsCounter(BridgeElementType bridgeElementTypeToIncrease)
    {
        switch (bridgeElementTypeToIncrease)
        {
            case BridgeElementType.Connector:
                {
                    this.connectedConnectors++;
                    break;
                }
            case BridgeElementType.GroundMount:
                {
                    this.connectedGroundMounts++;
                    break;
                }
            case BridgeElementType.SideMount:
                {
                    this.connectedSideMounts++;
                    break;
                }
            case BridgeElementType.Span:
                {
                    this.connectedSpans++;
                    break;
                }
            case BridgeElementType.Support:
                {
                    this.connectedSupports++;
                    break;
                }
            default:
                break;
        }
    }

    /// <summary>
    /// Instantiate BridgeNode object - do poprawy, nie dzia³a przypisywanie do parenta
    /// </summary>
    public void InstantiateBridgeNodeElement(Transform bridgeParentTransform)
    {
        // Instantiate<GameObject>(bridgeNodePrefab, instantiationPosition, Quaternion.identity, bridgeParentTransform);
        //Instantiate<GameObject>(buildingElementSO.buildingElementPrefab, instantiationPosition, Quaternion.identity, bridgeParentTransform);

        Debug.Log("Test?");
    }

    /// <summary>
    /// Validate Node, if it is correctly connected to other ones - each node HAVE to has at least 2 connected nodes. Returns true, if node is connected properly.
    /// </summary>
    public bool ValidateNode()
    {
        bool isCorrectlyConnected = true;
        switch (buildingElementSO.elementType)
        {
            // Connector HAS TO be connected to 1 support and 1 span element
            case BridgeElementType.Connector:
                {
                    if (connectedSupports < 1 || connectedSpans < 1)
                        isCorrectlyConnected = false;
                    break;
                }
            // Ground Mount HAS TO be connected to 1 support element
            case BridgeElementType.GroundMount:
                {
                    if (connectedSupports < 1)
                        isCorrectlyConnected = false;
                    break;
                }
            // Side Mount HAS TO be connected to 1 span element
            case BridgeElementType.SideMount:
                {
                    if (connectedSpans < 1)
                        isCorrectlyConnected = false;
                    break;
                }
            // Span HAS TO be connected from both sides - 2 support/side mount elements connected
            case BridgeElementType.Span:
                {
                    if ((connectedSideMounts + connectedSupports) < 2)
                        isCorrectlyConnected = false;
                    break;
                }
            // Support HAS TO be connected to at least 2 Span nodes and 1 Ground Mount
            case BridgeElementType.Support:
                {
                    if (connectedSpans < 2 || connectedGroundMounts < 1)
                        isCorrectlyConnected = false;
                    break;
                }
            default:
                break;
        }


        return isCorrectlyConnected;
    }
}