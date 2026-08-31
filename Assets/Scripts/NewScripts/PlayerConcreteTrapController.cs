using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(PlayerHealth), typeof(DownedPlayerCarryable))]
[DefaultExecutionOrder(1100)]
public sealed class PlayerConcreteTrapController : NetworkBehaviour
{
    public const ulong NoWheelbarrowNetworkObjectId = ulong.MaxValue;
    private static readonly HashSet<PlayerConcreteTrapController> ActiveControllers =
        new HashSet<PlayerConcreteTrapController>();

    [SerializeField] private HardenedConcreteBreakProfileSO breakProfile;
    [SerializeField] private Material hardenedConcreteMaterial;
    [SerializeField] private Material crackMaterial;
    [SerializeField] private Vector3 blockLocalPosition = new Vector3(0f, 0.62f, 0f);
    [SerializeField] private Vector3 blockLocalScale = new Vector3(1.05f, 1.15f, 0.78f);

    private readonly NetworkVariable<PlayerConcreteTrapNetworkState> trapStateNetwork =
        new NetworkVariable<PlayerConcreteTrapNetworkState>(
            new PlayerConcreteTrapNetworkState(PlayerConcreteTrapState.None, NoWheelbarrowNetworkObjectId, 0f),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    private readonly Dictionary<ulong, double> nextWorkTimeByClient = new Dictionary<ulong, double>();

    private PlayerConcreteTrapNetworkState localState = new PlayerConcreteTrapNetworkState(
        PlayerConcreteTrapState.None,
        NoWheelbarrowNetworkObjectId,
        0f);
    private PlayerHealth playerHealth;
    private DownedPlayerCarryable carryable;
    private PlayerInteractionNew interaction;
    private PlayerActionController actionController;
    private PlayerSpiritLevelController spiritLevelController;
    private RopeToolController ropeToolController;
    private WheelbarrowPassengerVisualOverride passengerVisualOverride;
    private CarriedPlayerVisualOverride carriedVisualOverride;
    private WheelbarrowController localSourceWheelbarrow;
    private GameObject visualRoot;
    private GameObject[] crackStages = Array.Empty<GameObject>();
    private PlayerConcreteTrapTarget target;
    private double collapseCompleteAt;

    public PlayerConcreteTrapNetworkState CurrentState => IsNetworkStateActive ? trapStateNetwork.Value : localState;
    public PlayerConcreteTrapState State => CurrentState.State;
    public float Progress => CurrentState.Progress;
    public bool IsTrapped => State != PlayerConcreteTrapState.None;
    public bool IsInWheelbarrow => State == PlayerConcreteTrapState.InWheelbarrow;
    public bool IsEjected => State == PlayerConcreteTrapState.Ejected;
    public bool IsAttachedToWheelbarrow =>
        (State is PlayerConcreteTrapState.InWheelbarrow or PlayerConcreteTrapState.Collapsing) &&
        (CurrentState.SourceWheelbarrowNetworkObjectId != NoWheelbarrowNetworkObjectId || localSourceWheelbarrow != null);
    public bool BlocksGameplayInput => IsTrapped;
    public HardenedConcreteBreakProfileSO BreakProfile => breakProfile;
    public EquippableItemType RequiredTool => ResolveRequiredTool();
    public bool CanAcceptBreakInteraction => CanReceiveWork();
    public bool CanBeCarriedByHuman => State == PlayerConcreteTrapState.Ejected &&
        (carryable == null || !carryable.IsCarried);

    private bool IsNetworkStateActive =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    private void Awake()
    {
        ActiveControllers.Add(this);
        playerHealth = GetComponent<PlayerHealth>();
        carryable = GetComponent<DownedPlayerCarryable>();
        interaction = GetComponent<PlayerInteractionNew>();
        actionController = GetComponent<PlayerActionController>();
        spiritLevelController = GetComponent<PlayerSpiritLevelController>();
        ropeToolController = GetComponent<RopeToolController>();
        passengerVisualOverride = GetComponent<WheelbarrowPassengerVisualOverride>();
        carriedVisualOverride = GetComponent<CarriedPlayerVisualOverride>();
        EnsureVisualHierarchy();
        if (playerHealth != null) playerHealth.OnDownedStateChanged += HandleDownedStateChanged;
        ApplyState(default, CurrentState);
    }

    private void OnDestroy()
    {
        ActiveControllers.Remove(this);
        if (playerHealth != null) playerHealth.OnDownedStateChanged -= HandleDownedStateChanged;
    }

    internal static bool TryFindForOwner(ulong ownerClientId, out PlayerConcreteTrapController trap)
    {
        foreach (PlayerConcreteTrapController candidate in ActiveControllers)
        {
            if (candidate != null && candidate.OwnerClientId == ownerClientId && candidate.IsAttachedToWheelbarrow)
            {
                trap = candidate;
                return true;
            }
        }
        trap = null;
        return false;
    }

    public override void OnNetworkSpawn()
    {
        trapStateNetwork.OnValueChanged += ApplyState;
        ApplyState(default, trapStateNetwork.Value);
    }

    public override void OnNetworkDespawn()
    {
        trapStateNetwork.OnValueChanged -= ApplyState;
        if (IsServer && trapStateNetwork.Value.State != PlayerConcreteTrapState.None)
            ResolveSourceWheelbarrow(trapStateNetwork.Value)?.ClearHardenedPassengerConcreteForDisconnect(this);
        nextWorkTimeByClient.Clear();
    }

    private void Update()
    {
        if (State != PlayerConcreteTrapState.Collapsing) return;

        float duration = ResolveCollapseDuration();
        float normalized = collapseCompleteAt > 0d
            ? 1f - Mathf.Clamp01((float)((collapseCompleteAt - GetSynchronizedTime()) / duration))
            : 0f;
        if (visualRoot != null)
            visualRoot.transform.localScale = Vector3.Lerp(Vector3.one, new Vector3(1.12f, 0.05f, 1.12f), normalized);

        if ((!IsNetworkStateActive || IsServer) && collapseCompleteAt > 0d && GetSynchronizedTime() >= collapseCompleteAt)
            CompleteCollapse();
    }

    private void LateUpdate()
    {
        if (visualRoot == null) return;
        if (IsAttachedToWheelbarrow && passengerVisualOverride != null &&
            passengerVisualOverride.TryGetPresentedPlayerPose(out Vector3 position, out Quaternion rotation))
        {
            ApplyPresentedVisualPose(position, rotation);
            return;
        }

        if (State == PlayerConcreteTrapState.Ejected && carryable != null && carryable.IsCarried)
        {
            if (carriedVisualOverride == null) carriedVisualOverride = GetComponent<CarriedPlayerVisualOverride>();
            if (carriedVisualOverride != null &&
                carriedVisualOverride.TryGetPresentedPlayerPose(out position, out rotation))
            {
                ApplyPresentedVisualPose(position, rotation);
                return;
            }
        }

        visualRoot.transform.localPosition = Vector3.zero;
        visualRoot.transform.localRotation = Quaternion.identity;
    }

    private void ApplyPresentedVisualPose(Vector3 position, Quaternion rotation)
    {
        visualRoot.transform.SetPositionAndRotation(position + rotation * blockLocalPosition, rotation);
    }

    internal bool ActivateInWheelbarrow(WheelbarrowController wheelbarrow)
    {
        if (wheelbarrow == null || IsNetworkStateActive && !IsServer) return false;
        PlayerConcreteTrapNetworkState current = CurrentState;
        if (current.State == PlayerConcreteTrapState.InWheelbarrow && IsSourcedBy(wheelbarrow)) return true;
        if (current.State != PlayerConcreteTrapState.None) return false;

        carryable?.ForceDrop();
        localSourceWheelbarrow = wheelbarrow;
        SetState(new PlayerConcreteTrapNetworkState(
            PlayerConcreteTrapState.InWheelbarrow,
            wheelbarrow.NetworkObject != null && wheelbarrow.NetworkObject.IsSpawned
                ? wheelbarrow.NetworkObject.NetworkObjectId
                : NoWheelbarrowNetworkObjectId,
            0f));
        return true;
    }

    internal bool CompleteWheelbarrowEjection(WheelbarrowController wheelbarrow)
    {
        if (wheelbarrow == null || IsNetworkStateActive && !IsServer || !IsAttachedToWheelbarrow ||
            !IsSourcedBy(wheelbarrow)) return false;

        PlayerConcreteTrapState nextState = State == PlayerConcreteTrapState.Collapsing
            ? PlayerConcreteTrapState.Collapsing
            : PlayerConcreteTrapState.Ejected;
        localSourceWheelbarrow = null;
        SetState(new PlayerConcreteTrapNetworkState(
            nextState,
            NoWheelbarrowNetworkObjectId,
            Progress));
        wheelbarrow.ClearHardenedPassengerConcreteAfterEjection(this);
        return true;
    }

    internal void ClearForDisconnect(WheelbarrowController wheelbarrow)
    {
        if (IsNetworkStateActive && !IsServer) return;
        if (wheelbarrow != null && IsAttachedToWheelbarrow && IsSourcedBy(wheelbarrow))
            wheelbarrow.ClearHardenedPassengerConcreteForDisconnect(this);
        localSourceWheelbarrow = null;
        SetState(new PlayerConcreteTrapNetworkState(
            PlayerConcreteTrapState.None,
            NoWheelbarrowNetworkObjectId,
            0f));
    }

    public bool IsSourcedBy(WheelbarrowController wheelbarrow)
    {
        if (wheelbarrow == null) return false;
        if (!IsNetworkStateActive) return localSourceWheelbarrow == wheelbarrow && IsAttachedToWheelbarrow;
        return wheelbarrow.NetworkObject != null && wheelbarrow.NetworkObject.IsSpawned &&
               CurrentState.SourceWheelbarrowNetworkObjectId == wheelbarrow.NetworkObject.NetworkObjectId;
    }

    internal void RequestBreakWork(EquippableItemSO requestedItem)
    {
        if (requestedItem == null || requestedItem.itemType != ResolveRequiredTool()) return;
        if (!IsNetworkStateActive)
        {
            PlayerActionController actor = FindLocalActorTargetingThis();
            if (actor != null && ValidateLocalActor(actor, requestedItem)) ApplyValidatedWork(requestedItem.ConstructionWorkPower);
            return;
        }

        if (IsServer) TryApplyBreakWorkServer(NetworkManager.Singleton.LocalClientId, requestedItem.itemType);
        else RequestBreakWorkServerRpc((int)requestedItem.itemType);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestBreakWorkServerRpc(int requestedToolType, ServerRpcParams rpc = default)
    {
        TryApplyBreakWorkServer(rpc.Receive.SenderClientId, (EquippableItemType)requestedToolType);
    }

    private bool TryApplyBreakWorkServer(ulong senderClientId, EquippableItemType requestedToolType)
    {
        if (!IsServer || senderClientId == OwnerClientId || State is PlayerConcreteTrapState.None or PlayerConcreteTrapState.Collapsing ||
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client) ||
            client.PlayerObject == null) return false;

        NetworkObject actor = client.PlayerObject;
        PlayerHealth actorHealth = actor.GetComponent<PlayerHealth>();
        PlayerConcreteTrapController actorTrap = actor.GetComponent<PlayerConcreteTrapController>();
        PlayerInventory inventory = actor.GetComponent<PlayerInventory>();
        PlayerActionController actorAction = actor.GetComponent<PlayerActionController>();
        EquippableItemSO selected = inventory != null ? inventory.GetSelectedItemForServerValidation() : null;
        double now = GetSynchronizedTime();
        if (actorHealth == null || actorHealth.IsDowned || actorTrap != null && actorTrap.IsTrapped ||
            selected == null || selected.itemType != requestedToolType ||
            requestedToolType != ResolveRequiredTool() || actorAction == null || target == null ||
            !actorAction.CanPerformServerValidatedActionOn(target, selected) ||
            nextWorkTimeByClient.TryGetValue(senderClientId, out double nextAllowed) && now < nextAllowed ||
            !CanReceiveWork()) return false;

        nextWorkTimeByClient[senderClientId] = now + ResolveMinimumWorkInterval(selected);
        ApplyValidatedWork(selected.ConstructionWorkPower);
        return true;
    }

    private bool ValidateLocalActor(PlayerActionController actor, EquippableItemSO selected)
    {
        return actor != null && actor.gameObject != gameObject && selected != null &&
               (actor.GetComponent<PlayerConcreteTrapController>() == null ||
                !actor.GetComponent<PlayerConcreteTrapController>().IsTrapped) &&
               selected.itemType == ResolveRequiredTool() && target != null &&
               actor.CanPerformServerValidatedActionOn(target, selected) && CanReceiveWork();
    }

    private bool CanReceiveWork()
    {
        if (State == PlayerConcreteTrapState.InWheelbarrow)
        {
            WheelbarrowController source = ResolveSourceWheelbarrow(CurrentState);
            return source != null && source.DriverClientId == WheelbarrowController.NoClient;
        }
        return State == PlayerConcreteTrapState.Ejected && (carryable == null || !carryable.IsCarried);
    }

    private void ApplyValidatedWork(float workPower)
    {
        if (workPower <= 0f || !CanReceiveWork()) return;
        PlayerConcreteTrapNetworkState next = CurrentState;
        next.Progress = Mathf.Clamp(next.Progress + workPower, 0f, ResolveWorkRequired());
        if (next.Progress >= ResolveWorkRequired())
        {
            next.State = PlayerConcreteTrapState.Collapsing;
            collapseCompleteAt = GetSynchronizedTime() + ResolveCollapseDuration();
        }
        SetState(next);
    }

    private void CompleteCollapse()
    {
        if (State != PlayerConcreteTrapState.Collapsing) return;
        ResolveSourceWheelbarrow(CurrentState)?.CompleteHardenedPassengerConcreteBreak(this);
        localSourceWheelbarrow = null;
        collapseCompleteAt = 0d;
        SetState(new PlayerConcreteTrapNetworkState(PlayerConcreteTrapState.None, NoWheelbarrowNetworkObjectId, 0f));
    }

    private void SetState(PlayerConcreteTrapNetworkState next)
    {
        next.Progress = Mathf.Clamp(next.Progress, 0f, ResolveWorkRequired());
        PlayerConcreteTrapNetworkState previous = CurrentState;
        localState = next;
        if (IsNetworkStateActive && IsServer) trapStateNetwork.Value = next;
        else ApplyState(previous, next);
    }

    private void ApplyState(PlayerConcreteTrapNetworkState previous, PlayerConcreteTrapNetworkState current)
    {
        localState = current;
        if (current.State == PlayerConcreteTrapState.Collapsing && previous.State != PlayerConcreteTrapState.Collapsing)
            collapseCompleteAt = GetSynchronizedTime() + ResolveCollapseDuration();
        bool active = current.State != PlayerConcreteTrapState.None;
        if (visualRoot != null)
        {
            visualRoot.SetActive(active);
            visualRoot.transform.localScale = Vector3.one;
        }
        if (target != null)
            target.SetTargetEnabled(current.State is PlayerConcreteTrapState.InWheelbarrow or PlayerConcreteTrapState.Ejected);
        UpdateCrackVisuals(current.Progress);

        if (active && IsOwnerOrLocal())
        {
            interaction?.DropHeldObjectForStateChange();
            actionController?.CancelActionForStateChange();
        }
        if (active)
        {
            spiritLevelController?.CancelForConcreteTrap();
            ropeToolController?.CancelForConcreteTrap();
        }
        if (!active && previous.State != PlayerConcreteTrapState.None) collapseCompleteAt = 0d;
        UpdateHealthPause();
    }

    private void HandleDownedStateChanged(object sender, EventArgs e) => UpdateHealthPause();

    private void UpdateHealthPause()
    {
        if (playerHealth == null) return;
        if (IsTrapped && playerHealth.IsDowned) playerHealth.PauseRespawnTimerForConcreteTrap();
        else playerHealth.ResumeRespawnTimerAfterConcreteTrap();
    }

    private bool IsOwnerOrLocal() => !IsNetworkStateActive || IsOwner;

    private WheelbarrowController ResolveSourceWheelbarrow(PlayerConcreteTrapNetworkState state)
    {
        if (!IsNetworkStateActive) return localSourceWheelbarrow;
        if (state.SourceWheelbarrowNetworkObjectId == NoWheelbarrowNetworkObjectId) return null;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                state.SourceWheelbarrowNetworkObjectId,
                out NetworkObject source))
            return source.GetComponent<WheelbarrowController>();
        return null;
    }

    private PlayerActionController FindLocalActorTargetingThis()
    {
        foreach (PlayerActionController actor in FindObjectsByType<PlayerActionController>(FindObjectsSortMode.None))
        {
            PlayerInteractionNew actorInteraction = actor.GetComponent<PlayerInteractionNew>();
            if (actor.gameObject != gameObject && actorInteraction != null && actorInteraction.CurrentTarget == target) return actor;
        }
        return null;
    }

    private void EnsureVisualHierarchy()
    {
        Transform existing = transform.Find("PlayerConcreteTrapTarget");
        GameObject targetObject = existing != null ? existing.gameObject : new GameObject("PlayerConcreteTrapTarget");
        targetObject.transform.SetParent(transform, false);
        targetObject.transform.localPosition = blockLocalPosition;
        targetObject.layer = gameObject.layer;
        BoxCollider targetCollider = targetObject.GetComponent<BoxCollider>();
        if (targetCollider == null) targetCollider = targetObject.AddComponent<BoxCollider>();
        targetCollider.isTrigger = true;
        targetCollider.center = Vector3.zero;
        targetCollider.size = blockLocalScale;
        target = targetObject.GetComponent<PlayerConcreteTrapTarget>();
        if (target == null) target = targetObject.AddComponent<PlayerConcreteTrapTarget>();
        target.Initialize(this);
        target.SetInteractionCollider(targetCollider);

        Transform visual = targetObject.transform.Find("Visual");
        visualRoot = visual != null ? visual.gameObject : new GameObject("Visual");
        visualRoot.transform.SetParent(targetObject.transform, false);
        if (visualRoot.transform.childCount == 0)
        {
            CreateVisualCube("HardenedBlock", visualRoot.transform, Vector3.zero, blockLocalScale, hardenedConcreteMaterial);
            crackStages = new GameObject[3];
            for (int stage = 0; stage < crackStages.Length; stage++)
            {
                GameObject stageRoot = new GameObject($"CrackStage{stage + 1}");
                stageRoot.transform.SetParent(visualRoot.transform, false);
                crackStages[stage] = stageRoot;
                for (int line = 0; line <= stage; line++)
                {
                    Vector3 position = new Vector3((line - stage * 0.5f) * 0.22f, 0.06f + line * 0.18f, -blockLocalScale.z * 0.51f);
                    GameObject crack = CreateVisualCube(
                        $"Crack{line + 1}",
                        stageRoot.transform,
                        position,
                        new Vector3(0.035f, 0.52f, 0.025f),
                        crackMaterial);
                    crack.transform.localRotation = Quaternion.Euler(0f, 0f, line % 2 == 0 ? 24f : -30f);
                }
            }
        }
        else
        {
            crackStages = new GameObject[3];
            for (int i = 0; i < crackStages.Length; i++)
                crackStages[i] = visualRoot.transform.Find($"CrackStage{i + 1}")?.gameObject;
        }
    }

    private static GameObject CreateVisualCube(
        string name,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPosition;
        cube.transform.localScale = localScale;
        Collider collider = cube.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }
        Renderer renderer = cube.GetComponent<Renderer>();
        if (renderer != null && material != null) renderer.sharedMaterial = material;
        return cube;
    }

    private void UpdateCrackVisuals(float progress)
    {
        Vector3 thresholds = breakProfile != null ? breakProfile.CrackThresholds : new Vector3(1f, 34f, 67f);
        for (int i = 0; i < crackStages.Length; i++)
        {
            if (crackStages[i] == null) continue;
            float threshold = i == 0 ? thresholds.x : i == 1 ? thresholds.y : thresholds.z;
            crackStages[i].SetActive(progress >= threshold);
        }
    }

    private EquippableItemType ResolveRequiredTool() =>
        breakProfile != null ? breakProfile.RequiredTool : EquippableItemType.Pickaxe;
    private float ResolveWorkRequired() => breakProfile != null ? breakProfile.WorkRequired : 100f;
    private float ResolveCollapseDuration() => breakProfile != null ? breakProfile.CollapseDuration : 0.4f;

    private static float ResolveMinimumWorkInterval(EquippableItemSO item)
    {
        if (item == null) return 0.1f;
        if (item.actionProfile == null) return Mathf.Max(0.05f, item.actionCooldown);
        return Mathf.Max(0.05f,
            item.actionProfile.GetPhaseDuration(EquippableActionPhase.WindUp) +
            item.actionProfile.GetPhaseDuration(EquippableActionPhase.Strike) +
            item.actionProfile.GetPhaseDuration(EquippableActionPhase.ImpactFreeze) +
            item.actionProfile.GetPhaseDuration(EquippableActionPhase.Recovery) - 0.1f);
    }

    private double GetSynchronizedTime()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
            ? NetworkManager.Singleton.ServerTime.Time
            : Time.timeAsDouble;
    }
}

public sealed class PlayerConcreteTrapTarget : MonoBehaviour, IInteractableNew, IDamageable,
    IInteractionPromptProvider, IActionImpactSurfaceProvider
{
    private PlayerConcreteTrapController controller;
    private Collider interactionCollider;

    public ActionImpactSurfaceType ImpactSurfaceType => ActionImpactSurfaceType.Stone;
    public EquippableItemType RequiredTool => controller != null
        ? controller.RequiredTool
        : EquippableItemType.Pickaxe;
    public bool CanAcceptBreakInteraction => controller != null && controller.CanAcceptBreakInteraction;

    internal void Initialize(PlayerConcreteTrapController owner) => controller = owner;
    internal void SetInteractionCollider(Collider collider) => interactionCollider = collider;
    internal void SetTargetEnabled(bool enabled)
    {
        this.enabled = enabled;
        if (interactionCollider != null) interactionCollider.enabled = enabled;
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage) =>
        controller?.RequestBreakWork(equippableItemSO);
    public void DamageReceived(float damage) { }
    public void Interact(Transform interactor)
    {
        if (controller != null && controller.CanBeCarriedByHuman)
            controller.GetComponent<DownedPlayerCarryable>()?.TryRequestPickup(interactor);
    }
    public void LookedAt(Transform interactor) { }
    public void LookedAway(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (controller == null || !controller.IsTrapped || controller.State == PlayerConcreteTrapState.Collapsing) return;
        if (controller.CanAcceptBreakInteraction)
            prompts.Add(new InteractionPrompt(
                PlayerInputActionKind.Action,
                $"Break hardened concrete: {Mathf.CeilToInt(controller.Progress)} / " +
                $"{Mathf.CeilToInt(controller.BreakProfile != null ? controller.BreakProfile.WorkRequired : 100f)}"));
        if (controller.CanBeCarriedByHuman)
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Carry"));
    }
}
