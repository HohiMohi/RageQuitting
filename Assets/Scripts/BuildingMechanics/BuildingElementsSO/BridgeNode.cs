using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

[CreateAssetMenu(menuName = "Scriptable Objects/BuildingElements/BridgeNode")]

public class BridgeNode : ScriptableObject
{
    public Guid guid;
    public string nodeID;
    public BuildingElementSO buildingElementSO;
    public Vector2 leftDownPosition;
    public Vector2 rightTopPosition;
    public Vector3 instantiationPosition;
    public GameObject bridgeNodePrefab;
    public List<string> connectedBridgeElementsList = new List<string>();
    public int connectedSpans;
    public int connectedConnectors;
    public int connectedSupports;
    public int connectedGroundMounts;
    public int connectedSideMounts;

    public BridgeNode()
    {
        this.guid = Guid.NewGuid();
        this.nodeID = guid.ToString();
        this.connectedConnectors = 0;
        this.connectedSpans = 0;
        this.connectedSupports = 0;
        this.connectedGroundMounts = 0;
        this.connectedSideMounts = 0;
    }


    /// <summary>
    /// Remove Connection between this and given nodeID in connectedBridgeElementsList
    /// </summary>
    public bool DisconnectNode(BridgeNode bridgeNodeToDisconnect)
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
    public bool ConnectNode(BridgeNode bridgeNodeToConnect)
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
                    if(this.connectedConnectors > 0)
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
                    if(this.connectedSpans > 0)
                        this.connectedSpans--;
                    break;
                }
            case BridgeElementType.Support:
                {
                    if(this.connectedSupports > 0)
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

    /// <summary>
    /// Increase node BridgeElementTypeCounter by 1
    /// </summary>
    public void IncreaseConnectedElementsCounter(BridgeElementType bridgeElementTypeToIncrease)
    {
        switch(bridgeElementTypeToIncrease)
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
        Instantiate<GameObject>(bridgeNodePrefab, instantiationPosition, Quaternion.identity, bridgeParentTransform);
        Debug.Log("Test?");
    }

    /// <summary>
    /// Validate Node, if it is correctly connected to other ones - each node HAVE to has at least 2 connected nodes. Returns true, if node is connected properly.
    /// </summary>
    public bool ValidateNode()
    {
        bool isCorrectlyConnected = true;
        switch(buildingElementSO.elementType)
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
