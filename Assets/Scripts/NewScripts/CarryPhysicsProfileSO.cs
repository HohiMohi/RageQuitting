using UnityEngine;

[CreateAssetMenu(fileName = "CarryPhysicsProfile", menuName = "Scriptable Objects/Carry Physics Profile")]
public class CarryPhysicsProfileSO : ScriptableObject
{
    [Min(0.01f)] public float mass = 20f;
    [Min(0f)] public float linearDrag = 1.5f;
    [Min(0f)] public float angularDrag = 4f;
    [Min(0f)] public float gripSpring = 900f;
    [Min(0f)] public float gripDamper = 90f;
    [Min(0f)] public float maxGripForce = 1800f;
    [Min(0f)] public float maxGripTorque = 250f;
    [Min(0.01f)] public float maxGripDistance = 1.25f;
    [Min(0f)] public float maxVelocity = 6f;
    [Min(0f)] public float maxAngularVelocity = 3f;
    public bool useGravity = true;
    public bool allowYawRotation = true;
    [Min(0f)] public float movementForce = 450f;
    [Min(0f)] public float movementDamper = 65f;
}
