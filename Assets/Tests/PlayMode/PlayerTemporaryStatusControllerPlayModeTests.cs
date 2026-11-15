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
    public void DomainStatusUpdatesAreIdempotentAndClearOnlyTheMatchingEpisode()
    {
        GameObject player = new GameObject("Domain status registry test player");
        try
        {
            Component controller = player.AddComponent(ControllerType);
            Type domainKind = Type.GetType("PlayerDomainStatusKind, Assembly-CSharp");
            object waterExposure = Enum.Parse(domainKind, "WaterExposure");
            MethodInfo set = ControllerType.GetMethod("SetDomainStatus", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo get = ControllerType.GetMethod("GetDomainStatus", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo clear = ControllerType.GetMethod("ClearDomainStatus", BindingFlags.Instance | BindingFlags.NonPublic);
            object[] setArgs = { waterExposure, 0UL, 12d, 2.5f, 2 };

            Assert.That((bool)set.Invoke(controller, setArgs), Is.True);
            ulong episode = (ulong)setArgs[1];
            Assert.That(episode, Is.GreaterThan(0UL));
            ulong revision = (ulong)ControllerType.GetProperty("CurrentRevision").GetValue(controller);
            Assert.That((bool)set.Invoke(controller, setArgs), Is.True);
            Assert.That(ControllerType.GetProperty("CurrentRevision").GetValue(controller), Is.EqualTo(revision),
                "A repeated profile sample should not rewrite an unchanged domain entry.");

            object entry = get.Invoke(controller, new[] { waterExposure });
            Assert.That((ulong)entry.GetType().GetField("InstanceId").GetValue(entry), Is.EqualTo(episode));
            Assert.That((double)entry.GetType().GetField("DomainStartedAt").GetValue(entry), Is.EqualTo(12d));
            Assert.That((int)entry.GetType().GetField("DomainData").GetValue(entry), Is.EqualTo(2));

            Assert.That((bool)clear.Invoke(controller, new object[] { waterExposure, episode + 1 }), Is.False,
                "A stale episode cannot clear the current status.");
            Assert.That((bool)clear.Invoke(controller, new object[] { waterExposure, episode }), Is.True);
            object[] nextArgs = { waterExposure, 0UL, 20d, 1f, 1 };
            Assert.That((bool)set.Invoke(controller, nextArgs), Is.True);
            Assert.That((ulong)nextArgs[1], Is.GreaterThan(episode), "Reentry receives a fresh instance token.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [UnityTest]
    public IEnumerator TumbleweedReleaseIsEpisodeOwnedAndRespawnResetKeepsImmunity()
    {
        GameObject player = new GameObject("Tumbleweed episode registry test player");
        try
        {
            Component controller = player.AddComponent(ControllerType);
            MethodInfo beginType = ControllerType.GetMethod("BeginTumbleweedCapture", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(ulong), typeof(int), typeof(float), typeof(Vector3), typeof(Quaternion), typeof(ulong).MakeByRefType() }, null);
            MethodInfo complete = ControllerType.GetMethod("CompleteTumbleweedCapture", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo reset = ControllerType.GetMethod("ResetTemporaryStatuses", BindingFlags.Instance | BindingFlags.Public);
            object[] firstArgs = { ulong.MaxValue, 0, 0.1f, Vector3.zero, Quaternion.identity, 0UL };
            Assert.That((bool)beginType.Invoke(controller, firstArgs), Is.True);
            ulong firstEpisode = (ulong)firstArgs[5];
            Assert.That((bool)complete.Invoke(controller,
                new object[] { firstEpisode, 0.03f, new Vector3(2f, 3f, 4f), Quaternion.identity, Vector3.up }), Is.True);
            Assert.That((bool)ControllerType.GetProperty("IsTumbleweedImmune").GetValue(controller), Is.True);
            reset.Invoke(controller, null);
            Assert.That((bool)ControllerType.GetProperty("IsTumbleweedImmune").GetValue(controller), Is.True,
                "A full respawn reset preserves active release immunity.");
            yield return new WaitForSeconds(0.05f);

            object[] secondArgs = { ulong.MaxValue, 1, 0.2f, Vector3.zero, Quaternion.identity, 0UL };
            Assert.That((bool)beginType.Invoke(controller, secondArgs), Is.True);
            ulong secondEpisode = (ulong)secondArgs[5];
            Assert.That(secondEpisode, Is.GreaterThan(firstEpisode));
            Assert.That((bool)complete.Invoke(controller,
                new object[] { firstEpisode, 1f, Vector3.zero, Quaternion.identity, Vector3.zero }), Is.False,
                "An old source cannot clear a newer capture or publish its release pose.");
            Assert.That((ulong)ControllerType.GetProperty("TumbleweedCaptureEpisodeId", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller), Is.EqualTo(secondEpisode));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [UnityTest]
    public IEnumerator WaterGeometryDrainsInWadingAndUsesContinuousSharedHazardDeadlines()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject waterObject = new GameObject("Water status integration volume");
        GameObject player = new GameObject("Water status integration player");
        ScriptableObject profile = ScriptableObject.CreateInstance(TypeByName("WaterBodyProfileSO")) as ScriptableObject;
        try
        {
            floor.transform.position = new Vector3(0f, -0.75f, 0f);
            floor.transform.localScale = new Vector3(8f, 0.5f, 8f);
            BoxCollider volume = waterObject.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.size = new Vector3(8f, 4f, 8f);
            Component body = waterObject.AddComponent(TypeByName("WaterBody"));
            SetPrivateField(body, "gameplayVolume", volume);
            SetPrivateField(body, "profile", profile);
            SetProfile(profile, maximumSafeDepth: 0.7f, staminaDrain: 1f, unsupportedGrace: 0.45f, exhaustionGrace: 0.16f);

            player.AddComponent<Unity.Netcode.NetworkObject>();
            Component health = player.AddComponent(TypeByName("PlayerHealth"));
            Component status = player.AddComponent(ControllerType);
            Component stamina = player.AddComponent(TypeByName("PlayerStaminaController"));
            Component water = player.AddComponent(TypeByName("PlayerWaterExposureController"));
            SetPrivateField(stamina, "currentStaminaLocal", 5f);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.12f);

            Assert.That(water.GetType().GetProperty("CurrentState").GetValue(water).ToString(), Is.EqualTo("Wading"));
            Assert.That(GetWaterDrain(stamina), Is.EqualTo(1f).Within(0.001f));

            SetPrivateField(profile, "maximumSafeWadingDepth", 0.2f);
            yield return new WaitForSeconds(0.12f);
            Assert.That(water.GetType().GetProperty("CurrentState").GetValue(water).ToString(), Is.EqualTo("Unsafe"));
            Assert.That((float)water.GetType().GetProperty("RemainingUnsupportedTime").GetValue(water), Is.GreaterThan(0.2f));

            SetPrivateField(profile, "unsupportedGraceDuration", 0.9f);
            yield return new WaitForSeconds(0.2f);
            Assert.That((bool)health.GetType().GetProperty("IsDowned").GetValue(health), Is.False,
                "Changing the profile extends the deadline from the original continuous exposure start.");
            Assert.That((float)water.GetType().GetProperty("RemainingUnsupportedTime").GetValue(water), Is.GreaterThan(0.45f));

            player.transform.position = new Vector3(20f, 0f, 0f);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.14f);
            Assert.That(water.GetType().GetProperty("CurrentState").GetValue(water).ToString(), Is.EqualTo("None"));
            Assert.That((float)water.GetType().GetProperty("RemainingUnsupportedTime").GetValue(water), Is.Zero);

            player.transform.position = Vector3.zero;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.12f);
            Assert.That(water.GetType().GetProperty("CurrentState").GetValue(water).ToString(), Is.EqualTo("Unsafe"));
            Assert.That((float)water.GetType().GetProperty("RemainingUnsupportedTime").GetValue(water), Is.GreaterThan(0.7f),
                "Reentry starts a new hazard timer after leaving the water.");

            Assert.That((bool)ControllerType.GetMethod("BeginTumbleweedCapture").Invoke(status,
                new object[] { 1UL, 0, 2f, Vector3.zero, Quaternion.identity }), Is.True);
            yield return new WaitForSeconds(0.14f);
            Assert.That(water.GetType().GetProperty("CurrentState").GetValue(water).ToString(), Is.EqualTo("None"));
            Assert.That(GetWaterDrain(stamina), Is.Zero);
            Assert.That((float)water.GetType().GetProperty("RemainingUnsupportedTime").GetValue(water), Is.Zero);
            Assert.That((bool)ControllerType.GetMethod("EndTumbleweedCapture").Invoke(status, new object[] { 0f }), Is.True);
            yield return new WaitForSeconds(0.12f);
            Assert.That(water.GetType().GetProperty("CurrentState").GetValue(water).ToString(), Is.EqualTo("Unsafe"));
            Assert.That((float)water.GetType().GetProperty("RemainingUnsupportedTime").GetValue(water), Is.GreaterThan(0.7f),
                "Hazard exposure restarts after capture ends.");

            SetPrivateField(profile, "unsupportedGraceDuration", 1.5f);
            SetPrivateField(stamina, "currentStaminaLocal", 0f);
            yield return new WaitForSeconds(0.24f);
            Assert.That((bool)health.GetType().GetProperty("IsDowned").GetValue(health), Is.True,
                "The shorter exhaustion deadline can down the player while unsupported exposure continues.");
            Assert.That((float)water.GetType().GetProperty("RemainingUnsupportedTime").GetValue(water), Is.Zero);
            Assert.That((float)water.GetType().GetProperty("RemainingExhaustionGrace").GetValue(water), Is.Zero);
            Assert.That(GetWaterDrain(stamina), Is.Zero, "Water drain clears immediately when downing occurs.");
            Assert.That((bool)water.GetType().GetProperty("IsInUnsafeWater").GetValue(water), Is.True,
                "Unsafe exposure remains available for downed floating after warning timers clear.");
            Assert.That((float)health.GetType().GetProperty("CurrentHealth").GetValue(health), Is.Zero);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(waterObject);
            UnityEngine.Object.DestroyImmediate(floor);
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    [UnityTest]
    public IEnumerator ConcreteDomainCancelsActionsAndBlocksMovementWhileKeepingLookAvailable()
    {
        GameObject player = new GameObject("Concrete domain input test player");
        player.AddComponent<Unity.Netcode.NetworkObject>();
        Component controller = player.AddComponent(ControllerType);
        Type inputType = TypeByName("PlayerInputNew");
        Component input = player.AddComponent(inputType);
        FieldInfo canceledEvent = inputType.GetField("OnActionCanceled");
        int canceledCount = 0;
        EventHandler canceled = (_, __) => canceledCount++;
        canceledEvent.SetValue(input, Delegate.Combine((EventHandler)canceledEvent.GetValue(input), canceled));
        try
        {
            inputType.GetMethod("InitializeInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, null);
            inputType.GetField("actionHeld", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(input, true);
            Type domainKind = TypeByName("PlayerDomainStatusKind");
            object concrete = Enum.Parse(domainKind, "ConcreteTrap");
            MethodInfo set = ControllerType.GetMethod("SetDomainStatus", BindingFlags.Instance | BindingFlags.NonPublic);
            object[] reservedSynthetic = { -2001, 10f, false, 1f, false, false, 0UL };
            Assert.That((bool)ControllerType.GetMethod("TryApplyStatus").Invoke(controller, reservedSynthetic), Is.False,
                "Generic synthetic statuses cannot claim the reserved concrete domain ID.");
            object[] args = { concrete, 0UL, 0d, 0f, 0 };
            Assert.That((bool)set.Invoke(controller, args), Is.True);
            ulong instanceId = (ulong)args[1];

            Assert.That(canceledCount, Is.EqualTo(1));
            Assert.That(inputType.GetField("actionHeld", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input), Is.EqualTo(false));
            Assert.That(ControllerType.GetProperty("BlocksMovement").GetValue(controller), Is.EqualTo(true));
            Assert.That(ControllerType.GetProperty("BlocksActions").GetValue(controller), Is.EqualTo(true));
            Assert.That(ControllerType.GetProperty("BlocksLook").GetValue(controller), Is.EqualTo(false));
            Assert.That(inputType.GetProperty("IsMovementInputBlocked").GetValue(input), Is.EqualTo(true));
            Assert.That(inputType.GetProperty("IsWorldActionBlocked").GetValue(input), Is.EqualTo(true));
            Assert.That(inputType.GetProperty("IsLookInputBlocked").GetValue(input), Is.EqualTo(false));

            ulong revision = (ulong)ControllerType.GetProperty("CurrentRevision").GetValue(controller);
            Assert.That((bool)set.Invoke(controller, args), Is.True);
            Assert.That((ulong)args[1], Is.EqualTo(instanceId));
            Assert.That(ControllerType.GetProperty("CurrentRevision").GetValue(controller), Is.EqualTo(revision),
                "Concrete phase progress does not refresh or rewrite its presence-only domain status.");
        }
        finally
        {
            canceledEvent.SetValue(input, Delegate.Remove((EventHandler)canceledEvent.GetValue(input), canceled));
            inputType.GetMethod("DisposeInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, null);
            UnityEngine.Object.DestroyImmediate(player);
        }
        yield return null;
    }

    private static Type TypeByName(string typeName) => Type.GetType(typeName + ", Assembly-CSharp");

    private static void SetPrivateField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void SetProfile(object profile, float maximumSafeDepth, float staminaDrain,
        float unsupportedGrace, float exhaustionGrace)
    {
        SetPrivateField(profile, "maximumSafeWadingDepth", maximumSafeDepth);
        SetPrivateField(profile, "staminaDrainPerSecond", staminaDrain);
        SetPrivateField(profile, "unsupportedGraceDuration", unsupportedGrace);
        SetPrivateField(profile, "exhaustionWarningDuration", exhaustionGrace);
        SetPrivateField(profile, "groundMask", (LayerMask)(~0));
    }

    private static float GetWaterDrain(Component stamina)
    {
        object rates = stamina.GetType().GetField("serverDrainRates", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stamina);
        MethodInfo tryGetValue = rates.GetType().GetMethod("TryGetValue");
        object[] args = { Enum.Parse(TypeByName("StaminaDrainSource"), "Water"), 0f };
        return (bool)tryGetValue.Invoke(rates, args) ? (float)args[1] : 0f;
    }

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

            ulong placementCommitId = (ulong)ControllerType.GetProperty("PlacementCommitId").GetValue(controller);
            MethodInfo applyInjury = ControllerType.GetMethod("TryApplyCrushedHands", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((bool)applyInjury.Invoke(controller, new object[] { placementCommitId }), Is.True);
            healthType.GetMethod("RestoreFullHealth").Invoke(health, null);
            Assert.That(ControllerType.GetProperty("CurrentStatus").GetValue(controller).ToString(), Is.EqualTo("CrushedHands"),
                "Restoring health does not clear an active injury status.");

            healthType.GetMethod("RespawnLocal", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(health, null);
            Assert.That(ControllerType.GetProperty("CurrentStatus").GetValue(controller).ToString(), Is.EqualTo("None"));

            MethodInfo beginCapture = ControllerType.GetMethod("BeginTumbleweedCapture", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(ulong), typeof(int), typeof(float), typeof(Vector3), typeof(Quaternion), typeof(ulong).MakeByRefType() }, null);
            MethodInfo completeCapture = ControllerType.GetMethod("CompleteTumbleweedCapture", BindingFlags.Instance | BindingFlags.NonPublic);
            object[] captureArgs = { ulong.MaxValue, 0, 0.1f, Vector3.zero, Quaternion.identity, 0UL };
            Assert.That((bool)beginCapture.Invoke(controller, captureArgs), Is.True);
            Assert.That((bool)completeCapture.Invoke(controller,
                new object[] { (ulong)captureArgs[5], 5f, player.transform.position, player.transform.rotation, Vector3.up }), Is.True);
            Assert.That((bool)ControllerType.GetProperty("IsTumbleweedImmune").GetValue(controller), Is.True);
            healthType.GetMethod("RestoreFullHealth").Invoke(health, null);
            Assert.That((bool)ControllerType.GetProperty("IsTumbleweedImmune").GetValue(controller), Is.True,
                "Revive and health restoration preserve the independent tumbleweed immunity deadline.");
            healthType.GetMethod("RespawnLocal", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(health, null);
            Assert.That((bool)ControllerType.GetProperty("IsTumbleweedImmune").GetValue(controller), Is.True,
                "Full player respawn preserves the active immunity entry while clearing other temporary statuses.");
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

    [Test]
    public void StatusEventsAndDeadlinesRemoveOnlyTheMatchingCurrentInstance()
    {
        GameObject player = new GameObject("Temporary status condition test player");
        try
        {
            Component controller = player.AddComponent(ControllerType);
            MethodInfo apply = ControllerType.GetMethod("TryApplyStatus");
            MethodInfo signal = ControllerType.GetMethod("SignalStatusEvent");
            object[] timedAndEvent = { 101, 10f, true, 0.5f, false, true, 0UL };
            Assert.That((bool)apply.Invoke(controller, timedAndEvent), Is.True);
            ulong oldInstance = (ulong)timedAndEvent[6];
            object[] refreshed = { 101, 10f, true, 0.25f, true, false, 0UL };
            Assert.That((bool)apply.Invoke(controller, refreshed), Is.True);
            ulong currentInstance = (ulong)refreshed[6];
            Assert.That(currentInstance, Is.GreaterThan(oldInstance));
            Assert.That((bool)signal.Invoke(controller, new object[] { 101, oldInstance }), Is.False,
                "A stale event cannot clear a refreshed status instance.");
            Assert.That((bool)signal.Invoke(controller, new object[] { 101, currentInstance }), Is.True);
            Assert.That((bool)signal.Invoke(controller, new object[] { 101, currentInstance }), Is.False);

            object[] eventOnly = { 102, -1f, true, 1f, true, false, 0UL };
            Assert.That((bool)apply.Invoke(controller, eventOnly), Is.True);
            ulong eventOnlyId = (ulong)eventOnly[6];
            Assert.That(ControllerType.GetProperty("BlocksControls").GetValue(controller), Is.EqualTo(true));
            Assert.That((bool)signal.Invoke(controller, new object[] { 102, eventOnlyId }), Is.True);
            Assert.That(ControllerType.GetProperty("BlocksControls").GetValue(controller), Is.EqualTo(false));

            object[] timeOnly = { 103, 0.04f, false, 1f, false, true, 0UL };
            Assert.That((bool)apply.Invoke(controller, timeOnly), Is.True);
            Assert.That((bool)signal.Invoke(controller, new object[] { 103, (ulong)timeOnly[6] }), Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(player); }
    }

    [Test]
    public void PlacementCommitTokensSurviveOtherStatusesAndRejectOldNailEvents()
    {
        GameObject player = new GameObject("Placement commit identity test player");
        try
        {
            Component controller = player.AddComponent(ControllerType);
            MethodInfo begin = ControllerType.GetMethod("BeginPlacementLock");
            MethodInfo injury = ControllerType.GetMethod("TryApplyCrushedHands", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((bool)begin.Invoke(controller, null), Is.True);
            ulong firstId = (ulong)ControllerType.GetProperty("PlacementCommitId").GetValue(controller);
            object[] synthetic = { 501, 20f, false, 1f, false, false, 0UL };
            Assert.That((bool)ControllerType.GetMethod("TryApplyStatus").Invoke(controller, synthetic), Is.True);
            Assert.That((bool)begin.Invoke(controller, null), Is.True, "An accepted new placement gets a fresh token.");
            ulong secondId = (ulong)ControllerType.GetProperty("PlacementCommitId").GetValue(controller);
            Assert.That(secondId, Is.GreaterThan(firstId));
            Assert.That((bool)injury.Invoke(controller, new object[] { firstId }), Is.False);
            Assert.That((bool)injury.Invoke(controller, new object[] { secondId }), Is.True);
            Assert.That((float)ControllerType.GetMethod("GetStatusRemainingSeconds").Invoke(controller, new object[] { 501 }), Is.GreaterThan(0f));
            Assert.That((bool)injury.Invoke(controller, new object[] { secondId }), Is.False,
                "The consumed placement token cannot apply injury twice.");
        }
        finally { UnityEngine.Object.DestroyImmediate(player); }
    }

    [Test]
    public void ActiveStatusModifiersMultiplyAndBlockersAggregate()
    {
        GameObject player = new GameObject("Temporary status modifiers test player");
        try
        {
            Component controller = player.AddComponent(ControllerType);
            MethodInfo apply = ControllerType.GetMethod("TryApplyStatus");
            object[] first = { 201, 30f, false, 0.5f, true, false, 0UL };
            object[] second = { 202, 30f, false, 0.8f, false, true, 0UL };
            Assert.That((bool)apply.Invoke(controller, first), Is.True);
            Assert.That((bool)apply.Invoke(controller, second), Is.True);
            Assert.That((float)ControllerType.GetProperty("MovementMultiplier").GetValue(controller), Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(ControllerType.GetProperty("BlocksControls").GetValue(controller), Is.EqualTo(true));
            Assert.That(ControllerType.GetProperty("BlocksActions").GetValue(controller), Is.EqualTo(true));
        }
        finally { UnityEngine.Object.DestroyImmediate(player); }
    }

    [Test]
    public void StatusCapacityFailurePreservesPlacementLockAndCommitToken()
    {
        GameObject player = new GameObject("Temporary status capacity test player");
        try
        {
            Component controller = player.AddComponent(ControllerType);
            Assert.That((bool)ControllerType.GetMethod("BeginPlacementLock").Invoke(controller, null), Is.True);
            ulong placementCommitId = (ulong)ControllerType.GetProperty("PlacementCommitId").GetValue(controller);
            MethodInfo apply = ControllerType.GetMethod("TryApplyStatus");
            bool capacityReached = false;
            for (int statusId = 1000; statusId < 1100; statusId++)
            {
                object[] args = { statusId, 60f, false, 1f, false, false, 0UL };
                if (!(bool)apply.Invoke(controller, args))
                {
                    capacityReached = true;
                    break;
                }
            }

            Assert.That(capacityReached, Is.True);
            Assert.That(ControllerType.GetProperty("IsPlacementLockActive").GetValue(controller), Is.EqualTo(true));
            Assert.That((ulong)ControllerType.GetProperty("PlacementCommitId").GetValue(controller), Is.EqualTo(placementCommitId));
        }
        finally { UnityEngine.Object.DestroyImmediate(player); }
    }

    [UnityTest]
    public IEnumerator DurationOrEventWhicheverOccursFirstEndsStatus()
    {
        GameObject player = new GameObject("Temporary status first condition test player");
        try
        {
            Component controller = player.AddComponent(ControllerType);
            MethodInfo apply = ControllerType.GetMethod("TryApplyStatus");
            MethodInfo signal = ControllerType.GetMethod("SignalStatusEvent");
            object[] deadlineWins = { 301, 0.04f, true, 1f, false, true, 0UL };
            Assert.That((bool)apply.Invoke(controller, deadlineWins), Is.True);
            yield return new WaitForSeconds(0.08f);
            Assert.That(ControllerType.GetProperty("BlocksActions").GetValue(controller), Is.EqualTo(false));
            Assert.That((bool)signal.Invoke(controller, new object[] { 301, (ulong)deadlineWins[6] }), Is.False,
                "The event cannot revive or remove an already expired instance.");
        }
        finally { UnityEngine.Object.DestroyImmediate(player); }
    }

    [UnityTest]
    public IEnumerator RefreshResetsDeadlineAndFiresActivationOnlyForInactiveToActive()
    {
        GameObject player = new GameObject("Temporary status refresh test player");
        Component controller = player.AddComponent(ControllerType);
        EventInfo activated = ControllerType.GetEvent("StatusActivated");
        int activationCount = 0;
        Action<int, ulong> callback = (_, __) => activationCount++;
        activated.AddEventHandler(controller, callback);
        try
        {
            MethodInfo apply = ControllerType.GetMethod("TryApplyStatus");
            MethodInfo signal = ControllerType.GetMethod("SignalStatusEvent");
            object[] first = { 601, 0.18f, true, 0.75f, false, false, 0UL };
            Assert.That((bool)apply.Invoke(controller, first), Is.True);
            ulong oldInstance = (ulong)first[6];
            yield return new WaitForSeconds(0.08f);
            float beforeRefresh = (float)ControllerType.GetMethod("GetStatusRemainingSeconds").Invoke(controller, new object[] { 601 });

            object[] refreshed = { 601, 0.18f, true, 0.75f, false, false, 0UL };
            Assert.That((bool)apply.Invoke(controller, refreshed), Is.True);
            ulong refreshedInstance = (ulong)refreshed[6];
            float afterRefresh = (float)ControllerType.GetMethod("GetStatusRemainingSeconds").Invoke(controller, new object[] { 601 });
            Assert.That(refreshedInstance, Is.GreaterThan(oldInstance));
            Assert.That(afterRefresh, Is.GreaterThan(beforeRefresh));
            Assert.That(activationCount, Is.EqualTo(1), "Refreshing an active instance does not reapply activation effects.");
            Assert.That((bool)signal.Invoke(controller, new object[] { 601, oldInstance }), Is.False);
            Assert.That((bool)signal.Invoke(controller, new object[] { 601, refreshedInstance }), Is.True);
        }
        finally
        {
            activated.RemoveEventHandler(controller, callback);
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [UnityTest]
    public IEnumerator PlacementLockDeadlineUsesStrictlyActiveBeforeAndExpiredAtBoundary()
    {
        GameObject player = new GameObject("Placement lock exact deadline test player");
        Component controller = player.AddComponent(ControllerType);
        GameObject expiredPlayer = new GameObject("Expired placement lock test player");
        Component expiredController = expiredPlayer.AddComponent(ControllerType);
        FieldInfo duration = ControllerType.GetField("placementLockDurationSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo begin = ControllerType.GetMethod("BeginPlacementLock");
        MethodInfo injury = ControllerType.GetMethod("TryApplyCrushedHands", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            duration.SetValue(controller, 0f);
            Assert.That((bool)begin.Invoke(controller, null), Is.True);
            ulong boundaryId = (ulong)ControllerType.GetProperty("PlacementCommitId").GetValue(controller);
            Assert.That(ControllerType.GetProperty("IsPlacementLockActive").GetValue(controller), Is.EqualTo(false),
                "At ExpiresAt == now the lock is already expired.");
            Assert.That((bool)injury.Invoke(controller, new object[] { boundaryId }), Is.False);

            duration.SetValue(controller, 0.2f);
            Assert.That((bool)begin.Invoke(controller, null), Is.True);
            ulong activeId = (ulong)ControllerType.GetProperty("PlacementCommitId").GetValue(controller);
            Assert.That((bool)ControllerType.GetProperty("IsPlacementLockActive").GetValue(controller), Is.True);
            Assert.That((bool)injury.Invoke(controller, new object[] { activeId }), Is.True,
                "A prepared nail can apply injury strictly before the deadline.");

            FieldInfo expiredDuration = ControllerType.GetField("placementLockDurationSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
            expiredDuration.SetValue(expiredController, 0.05f);
            Assert.That((bool)begin.Invoke(expiredController, null), Is.True);
            ulong expiredId = (ulong)ControllerType.GetProperty("PlacementCommitId").GetValue(expiredController);
            yield return new WaitForSeconds(0.24f);
            Assert.That(ControllerType.GetProperty("IsPlacementLockActive").GetValue(expiredController), Is.EqualTo(false));
            Assert.That((bool)injury.Invoke(expiredController, new object[] { expiredId }), Is.False,
                "An event received after the deadline cannot apply injury.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(expiredPlayer);
        }
    }

    [UnityTest]
    public IEnumerator BlockingStatusCancelsHeldActionAndRequiresANewPressAfterRelease()
    {
        GameObject player = new GameObject("Temporary status action cancellation test player");
        player.AddComponent<Unity.Netcode.NetworkObject>();
        Component controller = player.AddComponent(ControllerType);
        Type inputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        Component input = player.AddComponent(inputType);
        FieldInfo canceledEvent = inputType.GetField("OnActionCanceled");
        int canceledCount = 0;
        EventHandler canceled = (_, __) => canceledCount++;
        canceledEvent.SetValue(input, Delegate.Combine((EventHandler)canceledEvent.GetValue(input), canceled));
        try
        {
            inputType.GetMethod("InitializeInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, null);
            inputType.GetField("actionHeld", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(input, true);
            MethodInfo apply = ControllerType.GetMethod("TryApplyStatus");
            object[] blocked = { 801, -1f, true, 1f, false, true, 0UL };
            Assert.That((bool)apply.Invoke(controller, blocked), Is.True);
            Assert.That(canceledCount, Is.EqualTo(1));
            Assert.That(inputType.GetField("actionHeld", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input), Is.EqualTo(false));

            MethodInfo actionPerformed = inputType.GetMethod("Action_performed", BindingFlags.Instance | BindingFlags.NonPublic);
            actionPerformed.Invoke(input, new object[] { default(UnityEngine.InputSystem.InputAction.CallbackContext) });
            Assert.That(inputType.GetField("actionHeld", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input), Is.EqualTo(false),
                "A held input does not resume automatically while the action blocker is active.");

            MethodInfo signal = ControllerType.GetMethod("SignalStatusEvent");
            Assert.That((bool)signal.Invoke(controller, new object[] { 801, (ulong)blocked[6] }), Is.True);
            actionPerformed.Invoke(input, new object[] { default(UnityEngine.InputSystem.InputAction.CallbackContext) });
            Assert.That(inputType.GetField("actionHeld", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input), Is.EqualTo(true),
                "The action resumes only after a new press callback after the blocker ends.");
        }
        finally
        {
            canceledEvent.SetValue(input, Delegate.Remove((EventHandler)canceledEvent.GetValue(input), canceled));
            inputType.GetMethod("DisposeInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, null);
            UnityEngine.Object.DestroyImmediate(player);
        }
        yield return null;
    }
}
