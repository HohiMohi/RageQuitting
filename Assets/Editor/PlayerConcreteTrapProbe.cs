using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerConcreteTrapProbe
{
    private const string PlayerPrefabPath = "Assets/Prefabs/PlayerNew.prefab";
    private const string WheelbarrowPrefabPath = "Assets/Prefabs/New/Wheelbarrow.prefab";
    private const string PouringProfilePath = "Assets/GeneratedAssets/Wheelbarrow/ConcretePouringProfile.asset";
    private const string BreakProfilePath = "Assets/GeneratedAssets/Wheelbarrow/HardenedConcreteBreakProfile.asset";

    [MenuItem("Tools/RageQuitting/Validate Player Concrete Trap")]
    public static string ValidateFromMenu()
    {
        string message = Validate();
        Debug.Log(message);
        return message;
    }

    public static string Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
            throw new InvalidOperationException("BLOCKED: validation requires an idle EditMode Editor.");
        ValidateActivationOrder(false);
        ValidateActivationOrder(true);
        ValidatePauseReasonCoexistenceAndDrop();
        ValidateCollapseTransitionAndTippedEjection();
        ValidateCarryEligibility();
        ValidateSharedProfileAndFallback();
        ValidatePrefabWiring();

        const string message = "Player concrete trap probe passed: activation orders, pause reasons, " +
            "collapse/tip, carry eligibility, required tool/fallback, pourable gates and prefab wiring.";
        return message;
    }

    private static void ValidateActivationOrder(bool concreteFirst)
    {
        WithPair((player, wheelbarrow, trap) =>
        {
            MethodInfo setPassenger = RequireMethod(typeof(WheelbarrowController), "SetPassenger");
            MethodInfo setConcrete = RequireMethod(typeof(WheelbarrowController), "SetConcreteLoads");
            wheelbarrow.SetEditorConcreteTrapProbePassenger(trap);

            if (concreteFirst)
            {
                setConcrete.Invoke(wheelbarrow, new object[] { 1 });
                Require(!trap.IsTrapped, "Concrete without a passenger activated the trap.");
                setPassenger.Invoke(wheelbarrow, new object[] { trap.OwnerClientId });
            }
            else
            {
                setPassenger.Invoke(wheelbarrow, new object[] { trap.OwnerClientId });
                Require(!trap.IsTrapped, "Passenger without concrete activated the trap.");
                setConcrete.Invoke(wheelbarrow, new object[] { 1 });
            }

            Require(trap.IsInWheelbarrow && trap.IsSourcedBy(wheelbarrow),
                $"Trap did not activate for {(concreteFirst ? "concrete-first" : "passenger-first")} order.");
            Require(wheelbarrow.HasHardenedPassengerConcrete && !wheelbarrow.HasPourableConcrete,
                "Hardened passenger concrete was still reported as pourable.");
        });
    }

    private static void ValidatePauseReasonCoexistenceAndDrop()
    {
        WithPair((player, wheelbarrow, trap) =>
        {
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            health.DamageReceived(health.MaxHealth + 1f);
            Require(health.IsDowned, "Pause probe could not down the player.");

            wheelbarrow.SetEditorConcreteTrapProbePassenger(trap);
            Require((bool)RequireMethod(typeof(PlayerConcreteTrapController), "ActivateInWheelbarrow")
                .Invoke(trap, new object[] { wheelbarrow }), "Pause probe could not activate the trap.");
            Invoke(typeof(PlayerHealth), health, "PauseRespawnTimerForNpcCarry");
            Require(health.IsRespawnTimerPausedByConcreteTrap && health.IsRespawnTimerPausedByNpcCarry,
                $"Both respawn pause reasons were not retained: reasons={health.ActiveRespawnPauseReasons}, " +
                $"downed={health.IsDowned}, trap={trap.State}.");

            float beforeDrop = health.GetRespawnTimeRemaining();
            Invoke(typeof(PlayerHealth), health, "ResumeRespawnTimerAfterNpcCarry");
            Require(health.IsRespawnTimerPausedByConcreteTrap && !health.IsRespawnTimerPausedByNpcCarry &&
                    health.IsRespawnTimerPaused,
                "Human/NPC drop cleared the concrete-trap pause reason.");
            Require(Mathf.Abs(health.GetRespawnTimeRemaining() - beforeDrop) < 0.05f,
                "Respawn countdown advanced while the concrete pause remained active.");
        });
    }

    private static void ValidateCollapseTransitionAndTippedEjection()
    {
        WithPair((player, wheelbarrow, trap) =>
        {
            MethodInfo setPassenger = RequireMethod(typeof(WheelbarrowController), "SetPassenger");
            MethodInfo setConcrete = RequireMethod(typeof(WheelbarrowController), "SetConcreteLoads");
            wheelbarrow.SetEditorConcreteTrapProbePassenger(trap);
            setPassenger.Invoke(wheelbarrow, new object[] { trap.OwnerClientId });
            setConcrete.Invoke(wheelbarrow, new object[] { 1 });

            Invoke(typeof(PlayerConcreteTrapController), trap, "ApplyValidatedWork", 100f);
            Require(trap.State == PlayerConcreteTrapState.Collapsing && trap.IsAttachedToWheelbarrow,
                "Trap did not enter attached Collapsing state.");

            FieldInfo deadline = RequireField(typeof(PlayerConcreteTrapController), "collapseCompleteAt");
            deadline.SetValue(trap, 0d);
            PlayerConcreteTrapNetworkState current = trap.CurrentState;
            Invoke(typeof(PlayerConcreteTrapController), trap, "ApplyState",
                new PlayerConcreteTrapNetworkState(PlayerConcreteTrapState.Ejected,
                    PlayerConcreteTrapController.NoWheelbarrowNetworkObjectId, current.Progress), current);
            Require((double)deadline.GetValue(trap) > Time.timeAsDouble,
                "A peer entering Collapsing did not create a local collapse deadline.");

            int spillBefore = (int)RequireField(typeof(WheelbarrowController), "localSpillSequence").GetValue(wheelbarrow);
            Invoke(typeof(WheelbarrowController), wheelbarrow, "TipOver");
            int spillAfter = (int)RequireField(typeof(WheelbarrowController), "localSpillSequence").GetValue(wheelbarrow);
            Require(spillAfter == spillBefore, "TipOver spilled attached hardened passenger concrete.");

            if (trap.IsAttachedToWheelbarrow)
                Invoke(typeof(PlayerConcreteTrapController), trap, "CompleteWheelbarrowEjection", wheelbarrow);
            Require(trap.State == PlayerConcreteTrapState.Collapsing && !trap.IsAttachedToWheelbarrow,
                "Tipped ejection did not preserve Collapsing while clearing the source relation.");
            Require(trap.Progress >= 100f && wheelbarrow.ConcreteLoads == 0,
                "Tipped collapse lost work progress or left wheelbarrow concrete cargo.");
        });
    }

    private static void ValidateCarryEligibility()
    {
        WithPair((player, wheelbarrow, trap) =>
        {
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            DownedPlayerCarryable carryable = player.GetComponent<DownedPlayerCarryable>();
            wheelbarrow.SetEditorConcreteTrapProbePassenger(trap);
            Invoke(typeof(PlayerConcreteTrapController), trap, "ActivateInWheelbarrow", wheelbarrow);
            health.DamageReceived(health.MaxHealth + 1f);
            Require(health.IsDowned && !carryable.CanBeCarriedByHuman && !carryable.CanBeCarriedByNpc,
                "A downed InWheelbarrow trap was carryable.");
            Invoke(typeof(PlayerConcreteTrapController), trap, "CompleteWheelbarrowEjection", wheelbarrow);
            Require(carryable.CanBeCarriedByHuman && !carryable.CanBeCarriedByNpc,
                "An ejected trap did not allow human-only carrying.");
        });

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Scene previewScene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject player = PrefabUtility.InstantiatePrefab(playerPrefab, previewScene) as GameObject;
            InitializePlayerForEditModeProbe(player);
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            DownedPlayerCarryable carryable = player.GetComponent<DownedPlayerCarryable>();
            health.DamageReceived(health.MaxHealth + 1f);
            Require(carryable.CanBeCarriedByHuman && carryable.CanBeCarriedByNpc,
                "Ordinary downed carry eligibility regressed.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    private static void ValidateSharedProfileAndFallback()
    {
        HardenedConcreteBreakProfileSO shared = AssetDatabase.LoadAssetAtPath<HardenedConcreteBreakProfileSO>(BreakProfilePath);
        ConcretePouringProfileSO configured = AssetDatabase.LoadAssetAtPath<ConcretePouringProfileSO>(PouringProfilePath);
        Require(shared != null && configured != null, "Shared concrete profiles are missing.");
        Require(shared.RequiredTool == EquippableItemType.Pickaxe &&
                configured.HardenedConcreteBreakProfile == shared &&
                configured.FailedConcreteRequiredTool == shared.RequiredTool,
            "Configured failed concrete does not use the shared required tool.");

        ConcretePouringProfileSO fallback = ScriptableObject.CreateInstance<ConcretePouringProfileSO>();
        try
        {
            Require(fallback.FailedConcreteRequiredTool == EquippableItemType.Pickaxe &&
                    Mathf.Approximately(fallback.FailedConcreteWorkRequired, 100f),
                "Legacy concrete profile fallback is invalid.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fallback);
        }
    }

    private static void ValidatePrefabWiring()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        PlayerConcreteTrapController trap = prefab != null ? prefab.GetComponent<PlayerConcreteTrapController>() : null;
        Require(trap != null, "Player prefab has no PlayerConcreteTrapController.");
        SerializedObject serialized = new SerializedObject(trap);
        Require(serialized.FindProperty("breakProfile").objectReferenceValue != null &&
                serialized.FindProperty("hardenedConcreteMaterial").objectReferenceValue != null &&
                serialized.FindProperty("crackMaterial").objectReferenceValue != null,
            "Player concrete trap prefab references are incomplete.");
    }

    private static void WithPair(Action<GameObject, WheelbarrowController, PlayerConcreteTrapController> action)
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        GameObject wheelbarrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WheelbarrowPrefabPath);
        Require(playerPrefab != null && wheelbarrowPrefab != null, "Player or wheelbarrow prefab is missing.");
        Scene previewScene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject player = PrefabUtility.InstantiatePrefab(playerPrefab, previewScene) as GameObject;
            GameObject wheelbarrowObject = PrefabUtility.InstantiatePrefab(wheelbarrowPrefab, previewScene) as GameObject;
            InitializePlayerForEditModeProbe(player);
            PlayerConcreteTrapController trap = player.GetComponent<PlayerConcreteTrapController>();
            WheelbarrowController wheelbarrow = wheelbarrowObject.GetComponent<WheelbarrowController>();
            Require(trap != null && wheelbarrow != null, "Probe prefab components are missing.");
            action(player, wheelbarrow, trap);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    private static void InitializePlayerForEditModeProbe(GameObject player)
    {
        Invoke(typeof(PlayerHealth), player.GetComponent<PlayerHealth>(), "Awake");
        Invoke(typeof(DownedPlayerCarryable), player.GetComponent<DownedPlayerCarryable>(), "Awake");
        Invoke(typeof(PlayerConcreteTrapController), player.GetComponent<PlayerConcreteTrapController>(), "Awake");
    }

    private static MethodInfo RequireMethod(Type type, string name)
    {
        MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null) throw new MissingMethodException(type.Name, name);
        return method;
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(type.Name, name);
        return field;
    }

    private static object Invoke(Type type, object instance, string method, params object[] args)
    {
        try
        {
            return RequireMethod(type, method).Invoke(instance, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw new InvalidOperationException($"{type.Name}.{method} failed: {exception.InnerException.Message}",
                exception.InnerException);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
