using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class PlayerInteractionLifecycleIsolationPlayModeTests
{
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private PlayModeTestScope scope;
    private FieldInfo equippedEventField;
    private Delegate[] originalSubscribers;
    private EventHandler unrelatedSubscriber;
    private Component interaction;
    private GameObject playerRoot;
    private Type inputType;
    private Type interactionType;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        scope = new PlayModeTestScope("PlayerInteractionLifecycle");
        Type equippableType = Type.GetType("EquippableItem, Assembly-CSharp");
        Assert.That(equippableType, Is.Not.Null);
        equippedEventField = equippableType.GetField("OnAnyItemEquipped", StaticMembers);
        Assert.That(equippedEventField, Is.Not.Null);
        originalSubscribers = ((Delegate)equippedEventField.GetValue(null))?.GetInvocationList() ?? Array.Empty<Delegate>();

        unrelatedSubscriber = OnUnrelatedItemEquipped;
        EventHandler current = (EventHandler)equippedEventField.GetValue(null);
        equippedEventField.SetValue(null, (EventHandler)Delegate.Combine(current, unrelatedSubscriber));

        inputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        interactionType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        Assert.That(new[] { inputType, interactionType }, Has.None.Null);
        yield break;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        PlayModeTestScope cleanupScope = scope;
        if (cleanupScope != null) yield return cleanupScope.Cleanup();

        RemoveOwnedSubscribers();
        RemoveUnrelatedSubscriber();
        AssertOriginalSubscribersPreserved();

        interaction = null;
        playerRoot = null;
        unrelatedSubscriber = null;
        originalSubscribers = null;
        equippedEventField = null;
        inputType = interactionType = null;
        scope = null;
        cleanupScope?.AssertCleanupSucceeded();
    }

    [UnityTest]
    public IEnumerator RepeatedStartAndDestroyUnsubscribesOnlyEachOwnStaticHandler()
    {
        for (int cycle = 0; cycle < 3; cycle++)
        {
            playerRoot = scope.Own(new GameObject("PlayerInteraction lifecycle probe " + cycle));
            playerRoot.SetActive(false);
            var input = (Behaviour)playerRoot.AddComponent(inputType);
            input.enabled = false;
            interaction = playerRoot.AddComponent(interactionType);
            playerRoot.SetActive(true);
            yield return null;

            EventHandler afterStart = (EventHandler)equippedEventField.GetValue(null);
            Assert.That(CountTarget(afterStart, interaction), Is.EqualTo(1),
                "PlayerInteractionNew.Start should attach exactly one handler to the real static event.");
            Assert.That(CountSubscriber(afterStart, unrelatedSubscriber), Is.EqualTo(1));

            Object.Destroy(playerRoot);
            yield return null;

            EventHandler afterDestroy = (EventHandler)equippedEventField.GetValue(null);
            Assert.That(CountTarget(afterDestroy, interaction), Is.Zero,
                "Destroyed PlayerInteractionNew instances must not remain in EquippableItem.OnAnyItemEquipped.");
            Assert.That(CountSubscriber(afterDestroy, unrelatedSubscriber), Is.EqualTo(1),
                "Unrelated subscribers must remain registered.");
            AssertOriginalSubscribersPreserved();
        }
    }

    private void OnUnrelatedItemEquipped(object sender, EventArgs args) { }

    private void RemoveOwnedSubscribers()
    {
        if (equippedEventField == null || ReferenceEquals(interaction, null)) return;
        EventHandler current = (EventHandler)equippedEventField.GetValue(null);
        if (current == null) return;
        foreach (Delegate subscriber in current.GetInvocationList())
            if (ReferenceEquals(subscriber.Target, interaction))
                current = (EventHandler)Delegate.Remove(current, subscriber);
        equippedEventField.SetValue(null, current);
    }

    private void RemoveUnrelatedSubscriber()
    {
        if (equippedEventField == null || unrelatedSubscriber == null) return;
        EventHandler current = (EventHandler)equippedEventField.GetValue(null);
        equippedEventField.SetValue(null, (EventHandler)Delegate.Remove(current, unrelatedSubscriber));
    }

    private int CountTarget(Delegate source, object target)
    {
        int count = 0;
        if (source == null) return count;
        foreach (Delegate subscriber in source.GetInvocationList())
            if (ReferenceEquals(subscriber.Target, target)) count++;
        return count;
    }

    private static int CountSubscriber(Delegate source, Delegate expected)
    {
        int count = 0;
        if (source == null) return count;
        foreach (Delegate subscriber in source.GetInvocationList())
            if (subscriber.Equals(expected)) count++;
        return count;
    }

    private void AssertOriginalSubscribersPreserved()
    {
        if (equippedEventField == null || originalSubscribers == null) return;
        Delegate current = (Delegate)equippedEventField.GetValue(null);
        foreach (Delegate original in originalSubscribers)
            Assert.That(CountSubscriber(current, original), Is.GreaterThanOrEqualTo(CountOriginal(original)),
                "The test must preserve every pre-existing event subscriber.");
    }

    private int CountOriginal(Delegate expected)
    {
        int count = 0;
        foreach (Delegate original in originalSubscribers)
            if (original.Equals(expected)) count++;
        return count;
    }
}
