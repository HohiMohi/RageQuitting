using System;
using UnityEngine;

public enum EquippableColliderShapeType
{
    Box,
    Capsule
}

[Serializable]
public struct EquippableColliderShape
{
    public EquippableColliderShapeType shapeType;
    public Vector3 center;
    public Vector3 rotationEuler;
    [Tooltip("Box size, or capsule diameter (X/Z) and height (Y).")]
    public Vector3 size;
}

[CreateAssetMenu(fileName = "EquippableWorldPhysicsProfile", menuName = "Scriptable Objects/Equippable World Physics Profile")]
public sealed class EquippableWorldPhysicsProfileSO : ScriptableObject
{
    [Header("Rigidbody")]
    [SerializeField, Min(0.01f)] private float mass = 3f;
    [SerializeField, Min(0f)] private float linearDamping = 0.15f;
    [SerializeField, Min(0f)] private float angularDamping = 0.6f;
    [SerializeField, Min(0.1f)] private float maximumAngularVelocity = 20f;
    [SerializeField] private PhysicsMaterial physicsMaterial;

    [Header("Drop")]
    [SerializeField, Min(0f)] private float dropForwardVelocity = 2.2f;
    [SerializeField, Min(0f)] private float dropUpwardVelocity = 0.8f;
    [SerializeField, Min(0f)] private float dropAngularVelocity = 1.5f;
    [SerializeField, Min(0f)] private float dropClearance = 0.2f;
    [SerializeField, Min(0f)] private float collisionGraceMinimumDuration = 0.2f;
    [SerializeField, Min(0.1f)] private float collisionGraceMaximumDuration = 1.5f;

    [Header("Push")]
    [SerializeField, Min(0f)] private float playerPushVelocityChange = 0.7f;
    [SerializeField, Min(0f)] private float npcPushVelocityChange = 0.45f;
    [SerializeField, Min(0.02f)] private float pushCooldown = 0.1f;

    [Header("Impact Damage")]
    [SerializeField, Min(0f)] private float impactDamageSpeedThreshold = 6f;
    [SerializeField, Min(0f)] private float impactDamagePerSpeed = 2f;
    [SerializeField, Min(0f)] private float minimumImpactDamage = 2f;
    [SerializeField, Min(0f)] private float maximumImpactDamage = 12f;
    [SerializeField, Min(0f)] private float impactDamageCooldown = 0.75f;
    [SerializeField, Min(0f)] private float dropperAttributionDuration = 3f;

    [Header("Colliders")]
    [SerializeField] private EquippableColliderShape[] colliderShapes = Array.Empty<EquippableColliderShape>();

    [Header("Pickup Interaction")]
    [SerializeField] private bool generatePickupInteractionColliders = true;
    [SerializeField, Min(0f)] private float pickupInteractionPadding = 0.04f;

    public float Mass => mass;
    public float LinearDamping => linearDamping;
    public float AngularDamping => angularDamping;
    public float MaximumAngularVelocity => maximumAngularVelocity;
    public PhysicsMaterial PhysicsMaterial => physicsMaterial;
    public float DropForwardVelocity => dropForwardVelocity;
    public float DropUpwardVelocity => dropUpwardVelocity;
    public float DropAngularVelocity => dropAngularVelocity;
    public float DropClearance => dropClearance;
    public float CollisionGraceMinimumDuration => collisionGraceMinimumDuration;
    public float CollisionGraceMaximumDuration => collisionGraceMaximumDuration;
    public float PlayerPushVelocityChange => playerPushVelocityChange;
    public float NpcPushVelocityChange => npcPushVelocityChange;
    public float PushCooldown => pushCooldown;
    public float ImpactDamageSpeedThreshold => impactDamageSpeedThreshold;
    public float ImpactDamagePerSpeed => impactDamagePerSpeed;
    public float MinimumImpactDamage => minimumImpactDamage;
    public float MaximumImpactDamage => maximumImpactDamage;
    public float ImpactDamageCooldown => impactDamageCooldown;
    public float DropperAttributionDuration => dropperAttributionDuration;
    public EquippableColliderShape[] ColliderShapes => colliderShapes;
    public bool GeneratePickupInteractionColliders => generatePickupInteractionColliders;
    public float PickupInteractionPadding => Mathf.Max(0f, pickupInteractionPadding);
}
