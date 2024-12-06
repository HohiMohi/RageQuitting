using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestBridgeNode : MonoBehaviour
{
    public BridgeGraph graph;
    public BridgeNode node1;
    public BridgeNode node2;
    public BridgeNode node3;
    void Start()
    {
        node1.ConnectNode(node2);
        node1.ConnectNode(node3);
        Debug.Log(node1.ValidateNode());
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
