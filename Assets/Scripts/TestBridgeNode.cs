using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestBridgeNode : MonoBehaviour
{
    public BridgeGraph graph;
    public BridgeNode groundMountNode;
    public BridgeNode sideMount1Node;
    public BridgeNode sideMount2Node;
    public BridgeNode supportNode;
    public BridgeNode span1Node;
    public BridgeNode span2Node;
    public BridgeNode connector1Node;
    public BridgeNode connector2Node;
    void Start()
    {
        //graph.AddNode(groundMountNode);
        //graph.AddNode(sideMount1Node);
        //graph.AddNode(sideMount2Node);
        //graph.AddNode(supportNode);
        //graph.AddNode(span1Node);
        //graph.AddNode(span2Node);
        //graph.AddNode(connector1Node);
        //graph.AddNode(connector2Node);
        
        
        graph.InitialiseDictionaryFromList();

        connector1Node.ConnectNode(supportNode);
        connector2Node.ConnectNode(supportNode);
        connector1Node.ConnectNode(span1Node);
        connector2Node.ConnectNode(span2Node);
        
        
        graph.ValidateBridge();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
