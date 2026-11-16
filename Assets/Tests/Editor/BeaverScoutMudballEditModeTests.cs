using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class BeaverScoutMudballEditModeTests
{
    private const string ScoutPath = "Assets/Prefabs/New/NPC/NPC_BeaverScout.prefab";
    private const string BallPath = "Assets/Prefabs/New/NPC/BeaverScoutMudball.prefab";
    private const string SplashPath = "Assets/Prefabs/New/NPC/BeaverScoutMudSplash.prefab";

    [Test]
    public void ScoutBehaviorAndPrefabExposeMudballConfiguration()
    {
        Type behaviorType = Type.GetType("BeaverScoutBehaviorSO, Assembly-CSharp");
        UnityEngine.Object behavior = AssetDatabase.LoadAssetAtPath(
            "Assets/ScriptableObjectAssets/New/NPC/BeaverScoutBehavior.asset", behaviorType);
        GameObject scout = AssetDatabase.LoadAssetAtPath<GameObject>(ScoutPath);
        Assert.That(behavior, Is.Not.Null);
        Assert.That(scout, Is.Not.Null);
        Type throwerType = Type.GetType("BeaverScoutMudThrow, Assembly-CSharp");
        Component thrower = scout.GetComponent(throwerType);
        Assert.That(thrower, Is.Not.Null);
        Assert.That(GetField<Transform>(thrower, "releasePoint"), Is.Not.Null);
        Assert.That(GetField<float>(behavior, "mudballThrowRange"), Is.EqualTo(10f));
        Assert.That(GetField<float>(behavior, "mudballCooldown"), Is.EqualTo(45f));
        Assert.That(GetField<float>(behavior, "mudballWindup"), Is.EqualTo(0.4f));
        Assert.That(GetField<float>(behavior, "mudballMudGainPercentagePoints"), Is.EqualTo(15f));
        Type mudballType = Type.GetType("BeaverScoutMudball, Assembly-CSharp");
        Assert.That(GetField<UnityEngine.Object>(behavior, "mudballPrefab"), Is.EqualTo(
            AssetDatabase.LoadAssetAtPath<GameObject>(BallPath).GetComponent(mudballType)));
        Assert.That(GetField<UnityEngine.Object>(behavior, "mudballImpulseProfile"), Is.Not.Null);
    }

    [Test]
    public void BallIsSolidServerPhysicsPrefabAndRegisteredForNetworkSpawn()
    {
        GameObject ball = AssetDatabase.LoadAssetAtPath<GameObject>(BallPath);
        Assert.That(ball, Is.Not.Null);
        Assert.That(ball.GetComponent(Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime")), Is.Not.Null);
        Assert.That(ball.GetComponent(Type.GetType("Unity.Netcode.Components.NetworkTransform, Unity.Netcode.Runtime")), Is.Not.Null);
        Assert.That(ball.GetComponent(Type.GetType("Unity.Netcode.Components.NetworkRigidbody, Unity.Netcode.Runtime")), Is.Not.Null);
        Rigidbody body = ball.GetComponent<Rigidbody>();
        SphereCollider collider = ball.GetComponent<SphereCollider>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body.isKinematic, Is.False);
        Assert.That(body.useGravity, Is.True);
        Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousDynamic));
        Assert.That(collider, Is.Not.Null);
        Assert.That(collider.isTrigger, Is.False);
        Assert.That(collider.radius, Is.EqualTo(0.5f).Within(0.001f));

        Type listType = Type.GetType("Unity.Netcode.NetworkPrefabsList, Unity.Netcode.Runtime");
        UnityEngine.Object list = AssetDatabase.LoadAssetAtPath("Assets/DefaultNetworkPrefabs.asset", listType);
        Assert.That(list, Is.Not.Null);
        Assert.That((bool)listType.GetMethod("Contains", new[] { typeof(GameObject) }).Invoke(list, new object[] { ball }), Is.True);
    }

    [Test]
    public void ContactSplashIsAShortNonLoopingParticleBurst()
    {
        GameObject splash = AssetDatabase.LoadAssetAtPath<GameObject>(SplashPath);
        Assert.That(splash, Is.Not.Null);
        Assert.That(splash.GetComponent<Collider>(), Is.Null);
        ParticleSystem particleSystem = splash.GetComponent<ParticleSystem>();
        Assert.That(particleSystem, Is.Not.Null);
        Assert.That(particleSystem.main.loop, Is.False);
        Assert.That(particleSystem.main.duration, Is.LessThanOrEqualTo(0.5f));
        ParticleSystemRenderer renderer = splash.GetComponent<ParticleSystemRenderer>();
        Assert.That(renderer, Is.Not.Null);
        Assert.That(renderer.renderMode, Is.EqualTo(ParticleSystemRenderMode.Mesh));
        Assert.That(renderer.sharedMaterial, Is.Not.Null);
        Assert.That(renderer.mesh, Is.Not.Null);
    }

    [Test]
    public void MudImpulseHasLightHorizontalOnlyKnockbackAndNeverDropsHeldItems()
    {
        Type profileType = Type.GetType("ExternalImpulseProfileSO, Assembly-CSharp");
        UnityEngine.Object profile = AssetDatabase.LoadAssetAtPath(
            "Assets/ScriptableObjectAssets/New/NPC/BeaverScoutMudImpulse.asset", profileType);
        Assert.That(profile, Is.Not.Null);
        Assert.That(GetField<float>(profile, "horizontalSpeed"), Is.EqualTo(3.5f));
        Assert.That(GetField<float>(profile, "verticalSpeed"), Is.Zero);
        Assert.That(GetField<float>(profile, "movementControlMultiplier"), Is.EqualTo(0.85f));
        Assert.That(GetField<bool>(profile, "forceDropHeldObject"), Is.False);
    }

    private static T GetField<T>(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        return (T)field.GetValue(instance);
    }
}
