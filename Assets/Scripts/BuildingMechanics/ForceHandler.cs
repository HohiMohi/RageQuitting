using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ForceHandler : MonoBehaviour
{
    public Rigidbody rigidbody;
    public FixedJoint joint;
    //public void FixedUpdate()
    //{
    //    if (rigidbody.GetAccumulatedForce() != Vector3.zero)
    //    {
    //        Debug.Log(rigidbody.GetAccumulatedForce());
    //    }
    //}

    private void Awake()
    {
        joint = gameObject.GetComponent<FixedJoint>();
    }
    private void FixedUpdate()
    {
        Debug.Log(joint.currentForce.magnitude);
    }
    //private void OnCollisionEnter(Collision collision)
    //{
    //    Debug.Log(collision.gameObject.GetComponent<Rigidbody>().mass);
    //    Debug.Log(collision.gameObject.GetComponent<Rigidbody>().position);
    //}

    //private void OnCollisionStay(Collision collision)
    //{
    //    Debug.Log(collision.gameObject.transform.position);
    //}

    //private void OnCollisionExit(Collision collision)
    //{
    //    Debug.Log("Left");
    //}
    
}
