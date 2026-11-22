using System.Collections;
using System.Reflection;
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class SprintExhaustionLatchPlayModeTests
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnityTest]
    public IEnumerator ExhaustionRequiresANewPressAfterRecovery()
    {
        var player = new GameObject("Sprint exhaustion regression");
        player.SetActive(false);
        player.AddComponent(Runtime("PlayerInputNew"));
        Component stamina = player.AddComponent(Runtime("PlayerStaminaController"));
        Component controller = player.AddComponent(Runtime("StarterAssets.FirstPersonController"));

        try
        {
            player.SetActive(true);
            Field(controller, "_isSprinting").SetValue(controller, true);
            Field(stamina, "currentStaminaLocal").SetValue(stamina, 0.1f);
            stamina.GetType().GetMethod("SetAuthoritativeDrainSource").Invoke(stamina,
                new[] { Enum.Parse(Runtime("StaminaDrainSource"), "Sprint"), (object)20f });
            stamina.GetType().GetMethod("SimulateLocal", PrivateInstance).Invoke(stamina, new object[] { 0.01f });

            Assert.That((bool)Property(controller, "IsSprinting").GetValue(controller), Is.False,
                "Live zero stamina must end sprint even while legacy stamina is still positive.");

            stamina.GetType().GetMethod("RestoreFullStamina").Invoke(stamina, null);
            Assert.That((float)Property(stamina, "CurrentStamina").GetValue(stamina), Is.GreaterThan(0f));
            RaiseSprint(controller, true, true);
            Assert.That((bool)Property(controller, "IsSprinting").GetValue(controller), Is.False,
                "A temporary-status refresh must not restore a held sprint after exhaustion.");

            RaiseSprint(controller, true, false);
            Assert.That((bool)Property(controller, "IsSprinting").GetValue(controller), Is.True,
                "A genuine new press may sprint after stamina recovers.");

            Field(stamina, "currentStaminaLocal").SetValue(stamina, 0.1f);
            stamina.GetType().GetMethod("SimulateLocal", PrivateInstance).Invoke(stamina, new object[] { 0.01f });
            RaiseSprint(controller, true, false);
            Assert.That((bool)Property(controller, "IsSprinting").GetValue(controller), Is.False,
                "A press at zero stamina must not be buffered until regeneration.");

            stamina.GetType().GetMethod("RestoreFullStamina").Invoke(stamina, null);
            RaiseSprint(controller, true, true);
            Assert.That((bool)Property(controller, "IsSprinting").GetValue(controller), Is.False,
                "Recovery after a press at zero must not replay the rejected press.");

            RaiseSprint(controller, true, false);
            Assert.That((bool)Property(controller, "IsSprinting").GetValue(controller), Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }

        yield return null;
        LogAssert.NoUnexpectedReceived();
    }

    private static Type Runtime(string fullName) => Type.GetType(fullName + ", Assembly-CSharp");

    private static FieldInfo Field(Component component, string name) =>
        component.GetType().GetField(name, PrivateInstance);

    private static PropertyInfo Property(Component component, string name) =>
        component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);

    private static void RaiseSprint(Component controller, bool sprinting, bool statusRefresh)
    {
        Type argsType = Runtime("PlayerInputNew+OnSprintArgs");
        object args = System.Activator.CreateInstance(argsType);
        argsType.GetField("IsSprinting").SetValue(args, sprinting);
        argsType.GetField("IsStatusRefresh").SetValue(args, statusRefresh);
        controller.GetType().GetMethod("PlayerInputNew_OnSprint", PrivateInstance)
            .Invoke(controller, new[] { null, args });
    }
}
