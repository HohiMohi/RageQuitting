using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(FixedJoint))]
public class Player : MonoBehaviour
{
    [SerializeField] private FixedJoint fixedJoinedObject;
    private GameObject holdedObject;

    private void Awake()
    {
        fixedJoinedObject = GetComponent<FixedJoint>();
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void HoldObject(GameObject objectToHold)
    {
        if (holdedObject != null)
        {
            Debug.Log("Already holding something");
            return;
        }
        fixedJoinedObject.connectedBody = objectToHold.GetComponent<Rigidbody>();
        holdedObject = objectToHold;
        
    }

    public GameObject GetHoldedObject()
    {
        return holdedObject;
    }
}
