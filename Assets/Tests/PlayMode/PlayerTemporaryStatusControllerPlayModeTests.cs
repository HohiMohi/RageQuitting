using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;

public class PlayerTemporaryStatusControllerPlayModeTests
{
    private static readonly Type ControllerType = Type.GetType("PlayerTemporaryStatusController, Assembly-CSharp");
    private static readonly Type StatusType = Type.GetType("PlayerTemporaryStatusKind, Assembly-CSharp");
    private static readonly Type SnapshotType = Type.GetType("PlayerTemporaryStatusSnapshot, Assembly-CSharp");

    [Test]
    public void PlacementLockTransitionsToCrushedHandsOnlyForTheCurrentUnexpiredRevision()
    {
        Assert.That(ControllerType, Is.Not.Null);
        GameObject player = new GameObject("Temporary status test player");
        try
        {
            Component controller = player.AddComponent(ControllerType);
            Assert.That((bool)ControllerType.GetMethod("BeginPlacementLock").Invoke(controller, null), Is.True);
            ulong placementRevision = (ulong)ControllerType.GetProperty("CurrentRevision").GetValue(controller);
            Assert.That(placementRevision, Is.GreaterThan(0));
            Assert.That(ControllerType.GetProperty("BlocksControls").GetValue(controller), Is.EqualTo(true));
            Assert.That(ControllerType.GetProperty("BlocksActions").GetValue(controller), Is.EqualTo(true));

            MethodInfo applyInjury = ControllerType.GetMethod("TryApplyCrushedHands",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((bool)applyInjury.Invoke(controller, new object[] { placementRevision + 1 }), Is.False);
            Assert.That((bool)applyInjury.Invoke(controller, new object[] { placementRevision }), Is.True);
            ulong injuryRevision = (ulong)ControllerType.GetProperty("CurrentRevision").GetValue(controller);
            Assert.That(injuryRevision, Is.GreaterThan(placementRevision));
            Assert.That(ControllerType.GetProperty("IsCrushedHands").GetValue(controller), Is.EqualTo(true));
            Assert.That(ControllerType.GetProperty("BlocksControls").GetValue(controller), Is.EqualTo(false));
            Assert.That(ControllerType.GetProperty("BlocksActions").GetValue(controller), Is.EqualTo(true));
            Assert.That((bool)applyInjury.Invoke(controller, new object[] { placementRevision }), Is.False);
            Assert.That(ControllerType.GetProperty("CurrentRevision").GetValue(controller), Is.EqualTo(injuryRevision));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void OlderSnapshotsCannotReplaceNewerAuthoritativeState()
    {
        GameObject player = new GameObject("Temporary status snapshot test player");
        try
        {
            Component controller = player.AddComponent(ControllerType);
            MethodInfo apply = ControllerType.GetMethod("ApplyServerSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            object crushed = Enum.Parse(StatusType, "CrushedHands");
            object placement = Enum.Parse(StatusType, "PlacementLock");
            object newer = Activator.CreateInstance(SnapshotType, 8UL, crushed, Time.timeAsDouble + 30d);
            object older = Activator.CreateInstance(SnapshotType, 7UL, placement, Time.timeAsDouble + 30d);

            Assert.That((bool)apply.Invoke(controller, new[] { newer }), Is.True);
            Assert.That((bool)apply.Invoke(controller, new[] { older }), Is.False);
            Assert.That(ControllerType.GetProperty("CurrentRevision").GetValue(controller), Is.EqualTo(8UL));
            Assert.That(ControllerType.GetProperty("IsCrushedHands").GetValue(controller), Is.EqualTo(true));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void RespawnClearsTemporaryStatusButRestoreFullHealthDoesNot()
    {
        GameObject player = new GameObject("Temporary status respawn test player");
        try
        {
            Component controller = player.AddComponent(ControllerType);
            Type healthType = Type.GetType("PlayerHealth, Assembly-CSharp");
            Component health = player.AddComponent(healthType);
            ControllerType.GetMethod("BeginPlacementLock").Invoke(controller, null);
            healthType.GetMethod("RestoreFullHealth").Invoke(health, null);
            Assert.That(ControllerType.GetProperty("CurrentStatus").GetValue(controller).ToString(), Is.EqualTo("PlacementLock"));

            healthType.GetMethod("RespawnLocal", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(health, null);
            Assert.That(ControllerType.GetProperty("CurrentStatus").GetValue(controller).ToString(), Is.EqualTo("None"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [UnityTest]
    public IEnumerator PlacementLockExpiresAtItsConfiguredDeadline()
    {
        GameObject player = new GameObject("Temporary status expiry test player");
        Component controller = player.AddComponent(ControllerType);
        try
        {
            ControllerType.GetField("placementLockDurationSeconds", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, 0.03f);
            ControllerType.GetMethod("BeginPlacementLock").Invoke(controller, null);
            yield return new WaitForSeconds(0.08f);
            Assert.That(ControllerType.GetProperty("CurrentStatus").GetValue(controller).ToString(), Is.EqualTo("None"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }
}
