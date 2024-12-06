using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuidlingElementsEnums
{

}
public enum BridgeElementType
{
    Support, //Main load-bearing element, other bridge elements are mounted to it
    Span, //Road elements, units walk on it
    Connector, //Lines, this elements connect support with span to transfer the load
    GroundMount, //Point(element), where supports are mounted
    SideMount //Boundary point of bridge, spans are mounted in them
}
