using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum ConcreteMixerMode : byte { Mixing, Pouring }
public enum ConcreteMixerBatchState : byte { Empty, Loading, Mixing, ConcreteReady, RuinedMix }

[RequireComponent(typeof(NetworkObject))]
public class ConcreteMixerController : NetworkBehaviour, IInteractableNew, IInteractionPromptProvider,
    ISubstanceSink, ICarriedResourceSink
{
    public const ulong NoCrankOwner = ulong.MaxValue;

    [Header("Configuration")]
    [SerializeField] private ConcreteMixerProfileSO profile;
    [SerializeField] private ContainerSubstanceSO waterSubstance;
    [SerializeField] private ContainerSubstanceSO gravelSubstance;
    [SerializeField] private BaseResourceSO cementBagResource;
    [Header("Interaction")]
    [SerializeField] private Transform crankInteractionPoint;
    [SerializeField] private Transform loadingInteractionPoint;
    [SerializeField] private WheelbarrowDockingStation concreteOutputStation;
    [Header("Visuals")]
    [SerializeField] private Transform drumPivot;
    [SerializeField] private Transform drumSpinVisual;
    [SerializeField] private Vector3 mixingDrumLocalEuler = new Vector3(0f, 0f, 55f);
    [SerializeField] private Vector3 pouringDrumLocalEuler = new Vector3(0f, 0f, -25f);
    [SerializeField] private GameObject spillVisual;
    [SerializeField, Min(0.05f)] private float spillVisualDuration = 1.25f;
    [SerializeField, Min(0.1f)] private float drumRotationSpeed = 120f;

    private readonly NetworkVariable<byte> modeNetwork = new NetworkVariable<byte>((byte)ConcreteMixerMode.Mixing);
    private readonly NetworkVariable<byte> batchStateNetwork = new NetworkVariable<byte>((byte)ConcreteMixerBatchState.Empty);
    private readonly NetworkVariable<int> waterUnitsNetwork = new NetworkVariable<int>();
    private readonly NetworkVariable<int> gravelUnitsNetwork = new NetworkVariable<int>();
    private readonly NetworkVariable<int> cementBagsNetwork = new NetworkVariable<int>();
    private readonly NetworkVariable<float> mixingDegreesNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<float> drumRotationDegreesNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<ulong> crankOwnerNetwork = new NetworkVariable<ulong>(NoCrankOwner);
    private readonly NetworkVariable<int> dumpSequenceNetwork = new NetworkVariable<int>();

    private ConcreteMixerMode localMode = ConcreteMixerMode.Mixing;
    private ConcreteMixerBatchState localBatchState = ConcreteMixerBatchState.Empty;
    private int localWaterUnits;
    private int localGravelUnits;
    private int localCementBags;
    private float localMixingDegrees;
    private float localDrumRotationDegrees;
    private ulong localCrankOwner = NoCrankOwner;
    private int localDumpSequence;
    private float lastAcceptedCrankInputTime;
    private bool isDumping;
    private float displayedDrumSpinDegrees;

    public void LookedAt(Transform interactor) { }

    public void LookedAway(Transform interactor) { }
    private Coroutine spillRoutine;

    public ConcreteMixerMode Mode => IsNetworkSessionActive() ? (ConcreteMixerMode)modeNetwork.Value : localMode;
    public ConcreteMixerBatchState BatchState => IsNetworkSessionActive() ? (ConcreteMixerBatchState)batchStateNetwork.Value : localBatchState;
    public int WaterUnits => IsNetworkSessionActive() ? waterUnitsNetwork.Value : localWaterUnits;
    public int GravelUnits => IsNetworkSessionActive() ? gravelUnitsNetwork.Value : localGravelUnits;
    public int CementBags => IsNetworkSessionActive() ? cementBagsNetwork.Value : localCementBags;
    public float MixingDegrees => IsNetworkSessionActive() ? mixingDegreesNetwork.Value : localMixingDegrees;
    public float DrumRotationDegrees => IsNetworkSessionActive() ? drumRotationDegreesNetwork.Value : localDrumRotationDegrees;
    public float MixingProgress => Mathf.Clamp01(MixingDegrees / RequiredMixingDegrees);
    public int UsedVolume => WaterUnits + GravelUnits + CementBags * (profile != null ? profile.CementBagVolume : 3);
    public float MaximumMixingProgress
    {
        get
        {
            int capacity = profile != null ? profile.DrumCapacity : 15;
            int minimumVolume = profile != null ? profile.MinimumLoadedVolumeToStartMixing : 6;
            return UsedVolume < minimumVolume ? 0f : Mathf.Clamp01((float)UsedVolume / capacity);
        }
    }
    public float MaximumMixingDegrees => RequiredMixingDegrees * MaximumMixingProgress;
    public bool CanAccumulateMixingProgress => MaximumMixingDegrees > MixingDegrees + 0.01f;
    public ulong CrankOwner => IsNetworkSessionActive() ? crankOwnerNetwork.Value : localCrankOwner;
    public ConcreteMixerProfileSO Profile => profile;
    public bool IsCrankAvailable => Mode == ConcreteMixerMode.Mixing && !isDumping &&
        BatchState != ConcreteMixerBatchState.ConcreteReady && BatchState != ConcreteMixerBatchState.RuinedMix &&
        CrankOwner == NoCrankOwner;

    private float RequiredMixingDegrees => (profile != null ? profile.RequiredRotations : 6) * 360f;

    public event EventHandler StateChanged;
    public event EventHandler CrankGranted;
    public event EventHandler CrankRevoked;
    public event Action<string> CrankDenied;

    private void Awake()
    {
        if (spillVisual != null) spillVisual.SetActive(false);
        ApplyDrumPose(true);
    }

    private void Start()
    {
        if (!IsNetworkSessionActive()) ApplyTestingReadyBatch();
    }

    public override void OnNetworkSpawn()
    {
        modeNetwork.OnValueChanged += OnModeChanged;
        batchStateNetwork.OnValueChanged += OnByteStateChanged;
        waterUnitsNetwork.OnValueChanged += OnIntStateChanged;
        gravelUnitsNetwork.OnValueChanged += OnIntStateChanged;
        cementBagsNetwork.OnValueChanged += OnIntStateChanged;
        mixingDegreesNetwork.OnValueChanged += OnFloatStateChanged;
        drumRotationDegreesNetwork.OnValueChanged += OnFloatStateChanged;
        crankOwnerNetwork.OnValueChanged += OnCrankOwnerChanged;
        dumpSequenceNetwork.OnValueChanged += OnDumpSequenceChanged;
        if (IsServer)
        {
            modeNetwork.Value = (byte)ConcreteMixerMode.Mixing;
            crankOwnerNetwork.Value = NoCrankOwner;
            ApplyTestingReadyBatch();
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
        ApplyDrumPose(true);
        RaiseStateChanged();
    }

    public override void OnNetworkDespawn()
    {
        modeNetwork.OnValueChanged -= OnModeChanged;
        batchStateNetwork.OnValueChanged -= OnByteStateChanged;
        waterUnitsNetwork.OnValueChanged -= OnIntStateChanged;
        gravelUnitsNetwork.OnValueChanged -= OnIntStateChanged;
        cementBagsNetwork.OnValueChanged -= OnIntStateChanged;
        mixingDegreesNetwork.OnValueChanged -= OnFloatStateChanged;
        drumRotationDegreesNetwork.OnValueChanged -= OnFloatStateChanged;
        crankOwnerNetwork.OnValueChanged -= OnCrankOwnerChanged;
        dumpSequenceNetwork.OnValueChanged -= OnDumpSequenceChanged;
        if (NetworkManager != null && IsServer) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void Update()
    {
        ApplyDrumPose(false);
        if (HasSimulationAuthority() && CrankOwner != NoCrankOwner && !ValidateCrankOwner(CrankOwner, out _))
        {
            ReleaseCrank(CrankOwner, true);
        }
    }

    public void Interact(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, GetStatusText()));
    }

    public bool CanAccept(PortableSubstanceContainer container)
    {
        if (container == null || profile == null || Mode != ConcreteMixerMode.Mixing || isDumping ||
            BatchState == ConcreteMixerBatchState.ConcreteReady || BatchState == ConcreteMixerBatchState.RuinedMix ||
            container.CurrentUnits != container.Capacity) return false;
        ContainerSubstanceSO substance = container.CurrentSubstance;
        return (substance == waterSubstance || substance == gravelSubstance) &&
               UsedVolume + container.CurrentUnits <= profile.DrumCapacity;
    }

    public bool TryDeposit(PortableSubstanceContainer container, PlayerInteractionNew player)
    {
        if (!HasSimulationAuthority() || player == null || !CanAccept(container) || !IsPlayerNear(player, loadingInteractionPoint)) return false;
        ContainerSubstanceSO substance = container.CurrentSubstance;
        int transferred = container.TryRemoveUnits(substance, container.CurrentUnits);
        if (transferred <= 0) return false;
        if (substance == waterSubstance) SetWaterUnits(WaterUnits + transferred);
        else SetGravelUnits(GravelUnits + transferred);
        PromoteLoadingState();
        return true;
    }

    public string GetDepositPrompt(PortableSubstanceContainer container)
    {
        if (Mode != ConcreteMixerMode.Mixing) return "Set mixer to mixing mode";
        if (BatchState == ConcreteMixerBatchState.ConcreteReady || BatchState == ConcreteMixerBatchState.RuinedMix)
            return "Empty mixer before loading";
        if (container == null || container.CurrentUnits < container.Capacity) return "A full bucket is required";
        return CanAccept(container) ? $"Hold to add {container.CurrentSubstance.DisplayName}" : "Mixer cannot accept this bucket";
    }

    public bool TryDepositCarriedResource(PlayerInteractionNew player, BaseResourceNew resource)
    {
        if (player == null || resource == null || resource.GetBaseResourceSO() != cementBagResource) return false;
        if (!IsNetworkSessionActive()) return DepositCementLocal(player, resource);
        if (IsServer) return DepositCementServer(NetworkManager.LocalClientId, resource.NetworkObjectId);
        RequestDepositCementServerRpc(resource.NetworkObjectId);
        return true;
    }

    public void RequestBeginCrank(Transform interactor)
    {
        if (!IsNetworkSessionActive())
        {
            if (!IsCrankAvailable) { CrankDenied?.Invoke("Crank is unavailable"); return; }
            localCrankOwner = 0;
            SetBatchState(ConcreteMixerBatchState.Mixing);
            CrankGranted?.Invoke(this, EventArgs.Empty);
            RaiseStateChanged();
            return;
        }
        if (IsServer) TryBeginCrankServer(NetworkManager.LocalClientId);
        else RequestBeginCrankServerRpc();
    }

    public void RequestCrankDelta(float clockwiseDegrees)
    {
        if (clockwiseDegrees <= 0f) return;
        float inputMaximumDegrees = MaximumMixingDegrees;
        if (!IsNetworkSessionActive()) ApplyCrankDelta(clockwiseDegrees, inputMaximumDegrees, 0);
        else if (IsServer) ApplyCrankDelta(clockwiseDegrees, inputMaximumDegrees, NetworkManager.LocalClientId);
        else RequestCrankDeltaServerRpc(clockwiseDegrees, inputMaximumDegrees);
    }

    public void RequestReleaseCrank()
    {
        if (!IsNetworkSessionActive()) ReleaseCrank(0, false);
        else if (IsServer) ReleaseCrank(NetworkManager.LocalClientId, false);
        else RequestReleaseCrankServerRpc();
    }

    public void RequestToggleMode(Transform interactor)
    {
        if (!IsNetworkSessionActive()) ToggleMode(0);
        else if (IsServer) ToggleMode(NetworkManager.LocalClientId);
        else RequestToggleModeServerRpc();
    }

    public string GetStatusText()
    {
        string status = BatchState switch
        {
            ConcreteMixerBatchState.Empty => "Mixer empty",
            ConcreteMixerBatchState.ConcreteReady => "Concrete ready",
            ConcreteMixerBatchState.RuinedMix => "Ruined mix - empty mixer",
            _ => $"Water {WaterUnits}/{(profile != null ? profile.RequiredWaterUnits : 6)} | " +
                 $"Gravel {GravelUnits}/{(profile != null ? profile.RequiredGravelUnits : 6)} | " +
                 $"Cement {CementBags}/{(profile != null ? profile.RequiredCementBags : 1)} | Mix {MixingProgress:P0}"
        };

        if (Mode != ConcreteMixerMode.Mixing ||
            BatchState == ConcreteMixerBatchState.ConcreteReady ||
            BatchState == ConcreteMixerBatchState.RuinedMix)
        {
            return status;
        }

        int minimumVolume = profile != null ? profile.MinimumLoadedVolumeToStartMixing : 6;
        if (UsedVolume < minimumVolume)
        {
            return $"{status} | Add at least two loads to start mixing";
        }
        if (!CanAccumulateMixingProgress && MaximumMixingProgress < 1f)
        {
            return $"{status} | Add ingredients to continue";
        }
        return status;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestDepositCementServerRpc(ulong resourceId, ServerRpcParams rpcParams = default) => DepositCementServer(rpcParams.Receive.SenderClientId, resourceId);
    [ServerRpc(RequireOwnership = false)]
    private void RequestBeginCrankServerRpc(ServerRpcParams rpcParams = default) => TryBeginCrankServer(rpcParams.Receive.SenderClientId);
    [ServerRpc(RequireOwnership = false)]
    private void RequestCrankDeltaServerRpc(float degrees, float inputMaximumDegrees, ServerRpcParams rpcParams = default) =>
        ApplyCrankDelta(degrees, inputMaximumDegrees, rpcParams.Receive.SenderClientId);
    [ServerRpc(RequireOwnership = false)]
    private void RequestReleaseCrankServerRpc(ServerRpcParams rpcParams = default) => ReleaseCrank(rpcParams.Receive.SenderClientId, false);
    [ServerRpc(RequireOwnership = false)]
    private void RequestToggleModeServerRpc(ServerRpcParams rpcParams = default) => ToggleMode(rpcParams.Receive.SenderClientId);

    private bool DepositCementServer(ulong clientId, ulong resourceId)
    {
        if (NetworkManager == null || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(resourceId, out NetworkObject resourceObject) ||
            !resourceObject.TryGetComponent(out BaseResourceNew resource) || resource.GetBaseResourceSO() != cementBagResource ||
            !resource.IsHeldBy(clientId) || !TryGetPlayer(clientId, out PlayerInteractionNew player)) return false;
        return DepositCementLocal(player, resource);
    }

    private bool DepositCementLocal(PlayerInteractionNew player, BaseResourceNew resource)
    {
        int bagVolume = profile != null ? profile.CementBagVolume : 3;
        if (!HasSimulationAuthority() || Mode != ConcreteMixerMode.Mixing || isDumping ||
            BatchState == ConcreteMixerBatchState.ConcreteReady || BatchState == ConcreteMixerBatchState.RuinedMix ||
            UsedVolume + bagVolume > (profile != null ? profile.DrumCapacity : 15) || !IsPlayerNear(player, loadingInteractionPoint)) return false;
        SetCementBags(CementBags + 1);
        PromoteLoadingState();
        resource.RemoveFromWorld(EnvironmentalRemovalReason.Unknown);
        return true;
    }

    private void TryBeginCrankServer(ulong clientId)
    {
        if (!IsCrankAvailable || !ValidateCrankOwner(clientId, out _))
        {
            CrankDeniedClientRpc("Crank is unavailable", Target(clientId));
            return;
        }
        crankOwnerNetwork.Value = clientId;
        lastAcceptedCrankInputTime = Time.time;
        SetBatchState(ConcreteMixerBatchState.Mixing);
        CrankGrantedClientRpc(Target(clientId));
        RaiseStateChanged();
    }

    private void ApplyCrankDelta(float requestedDegrees, float inputMaximumDegrees, ulong clientId)
    {
        if (!HasSimulationAuthority() || CrankOwner != clientId || Mode != ConcreteMixerMode.Mixing ||
            BatchState != ConcreteMixerBatchState.Mixing || !ValidateCrankOwner(clientId, out _)) return;
        float maximumSpeed = profile != null ? profile.MaximumCrankAngularSpeed : 240f;
        float elapsed = Mathf.Clamp(Time.time - lastAcceptedCrankInputTime, 0.01f, 0.2f);
        float accepted = Mathf.Min(Mathf.Max(0f, requestedDegrees), maximumSpeed * (elapsed + 0.025f));
        lastAcceptedCrankInputTime = Time.time;
        if (accepted <= 0f) return;
        SetDrumRotationDegrees(DrumRotationDegrees + accepted);
        if (!CanAccumulateMixingProgress) return;
        float requestMaximumDegrees = Mathf.Min(
            MaximumMixingDegrees,
            Mathf.Max(MixingDegrees, inputMaximumDegrees));
        SetMixingDegrees(Mathf.Min(requestMaximumDegrees, MixingDegrees + accepted));
        if (MixingDegrees < RequiredMixingDegrees - 0.01f) return;
        bool validRecipe = profile != null && WaterUnits == profile.RequiredWaterUnits &&
            GravelUnits == profile.RequiredGravelUnits && CementBags == profile.RequiredCementBags;
        SetBatchState(validRecipe ? ConcreteMixerBatchState.ConcreteReady : ConcreteMixerBatchState.RuinedMix);
        ReleaseCrank(clientId, true);
    }

    private void ToggleMode(ulong clientId)
    {
        if (!HasSimulationAuthority() || !TryGetPlayer(clientId, out PlayerInteractionNew player) || !IsPlayerNear(player, transform)) return;
        if (Mode == ConcreteMixerMode.Mixing)
        {
            SetMode(ConcreteMixerMode.Pouring);
            ReleaseCrank(CrankOwner, true);
            if (!isDumping) StartCoroutine(DumpAfterDelay());
        }
        else if (!isDumping) SetMode(ConcreteMixerMode.Mixing);
    }

    private IEnumerator DumpAfterDelay()
    {
        isDumping = true;
        yield return new WaitForSeconds(profile != null ? profile.PouringDelay : 0.75f);
        bool transferred = BatchState == ConcreteMixerBatchState.ConcreteReady && concreteOutputStation != null &&
            concreteOutputStation.TryReceiveConcreteBatch(this);
        SetWaterUnits(0); SetGravelUnits(0); SetCementBags(0); SetMixingDegrees(0f); SetDrumRotationDegrees(0f);
        if (profile != null && profile.AlwaysReadyConcreteForTesting) ApplyTestingReadyBatch();
        else SetBatchState(ConcreteMixerBatchState.Empty);
        if (!transferred) SetDumpSequence(localDumpSequence + 1);
        isDumping = false;
    }

    private void ApplyTestingReadyBatch()
    {
        if (!HasSimulationAuthority() || profile == null || !profile.AlwaysReadyConcreteForTesting) return;
        SetWaterUnits(profile.RequiredWaterUnits);
        SetGravelUnits(profile.RequiredGravelUnits);
        SetCementBags(profile.RequiredCementBags);
        SetMixingDegrees(RequiredMixingDegrees);
        SetDrumRotationDegrees(0f);
        SetBatchState(ConcreteMixerBatchState.ConcreteReady);
    }

    private void ReleaseCrank(ulong clientId, bool force)
    {
        if (CrankOwner == NoCrankOwner || (!force && CrankOwner != clientId)) return;
        ulong previousOwner = CrankOwner;
        SetCrankOwner(NoCrankOwner);
        if (IsNetworkSessionActive() && previousOwner != NoCrankOwner && NetworkManager.ConnectedClients.ContainsKey(previousOwner))
            CrankRevokedClientRpc(Target(previousOwner));
        else CrankRevoked?.Invoke(this, EventArgs.Empty);
        RaiseStateChanged();
    }

    private void PromoteLoadingState()
    {
        SetBatchState(MixingDegrees > 0f || CrankOwner != NoCrankOwner ? ConcreteMixerBatchState.Mixing : ConcreteMixerBatchState.Loading);
        RaiseStateChanged();
    }

    private bool ValidateCrankOwner(ulong clientId, out PlayerInteractionNew player)
    {
        if (!TryGetPlayer(clientId, out player) || !IsPlayerNear(player, crankInteractionPoint)) return false;
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        return health == null || !health.IsDowned;
    }

    private bool TryGetPlayer(ulong clientId, out PlayerInteractionNew player)
    {
        player = null;
        if (!IsNetworkSessionActive())
        {
            player = FindFirstObjectByType<PlayerInteractionNew>();
            return player != null;
        }
        return NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null &&
               client.PlayerObject.TryGetComponent(out player);
    }

    private bool IsPlayerNear(PlayerInteractionNew player, Transform point)
    {
        float distance = profile != null ? profile.InteractionDistance : 3.5f;
        Vector3 target = point != null ? point.position : transform.position;
        return player != null && Vector3.Distance(player.transform.position, target) <= distance;
    }

    private void ApplyDrumPose(bool immediate)
    {
        if (drumPivot == null) return;
        Quaternion target = Quaternion.Euler(Mode == ConcreteMixerMode.Mixing ? mixingDrumLocalEuler : pouringDrumLocalEuler);
        drumPivot.localRotation = immediate ? target : Quaternion.RotateTowards(drumPivot.localRotation, target, drumRotationSpeed * Time.deltaTime);
        if (drumSpinVisual != null)
        {
            float targetSpin = Mathf.Repeat(DrumRotationDegrees, 360f);
            displayedDrumSpinDegrees = immediate
                ? targetSpin
                : Mathf.MoveTowardsAngle(displayedDrumSpinDegrees, targetSpin, 360f * Time.deltaTime);
            drumSpinVisual.localRotation = Quaternion.Euler(displayedDrumSpinDegrees, 0f, 0f);
        }
    }

    private void PlaySpillVisual()
    {
        if (spillVisual == null) return;
        if (spillRoutine != null) StopCoroutine(spillRoutine);
        spillRoutine = StartCoroutine(SpillVisualRoutine());
    }

    private IEnumerator SpillVisualRoutine()
    {
        spillVisual.SetActive(true);
        yield return new WaitForSeconds(spillVisualDuration);
        spillVisual.SetActive(false);
        spillRoutine = null;
    }

    private void SetMode(ConcreteMixerMode value) { if (IsNetworkSessionActive()) modeNetwork.Value = (byte)value; else { localMode = value; ApplyDrumPose(false); RaiseStateChanged(); } }
    private void SetBatchState(ConcreteMixerBatchState value) { if (IsNetworkSessionActive()) batchStateNetwork.Value = (byte)value; else { localBatchState = value; RaiseStateChanged(); } }
    private void SetWaterUnits(int value) { if (IsNetworkSessionActive()) waterUnitsNetwork.Value = value; else { localWaterUnits = value; RaiseStateChanged(); } }
    private void SetGravelUnits(int value) { if (IsNetworkSessionActive()) gravelUnitsNetwork.Value = value; else { localGravelUnits = value; RaiseStateChanged(); } }
    private void SetCementBags(int value) { if (IsNetworkSessionActive()) cementBagsNetwork.Value = value; else { localCementBags = value; RaiseStateChanged(); } }
    private void SetMixingDegrees(float value) { if (IsNetworkSessionActive()) mixingDegreesNetwork.Value = value; else { localMixingDegrees = value; RaiseStateChanged(); } }
    private void SetDrumRotationDegrees(float value) { if (IsNetworkSessionActive()) drumRotationDegreesNetwork.Value = value; else { localDrumRotationDegrees = value; RaiseStateChanged(); } }
    private void SetCrankOwner(ulong value) { if (IsNetworkSessionActive()) crankOwnerNetwork.Value = value; else localCrankOwner = value; }
    private void SetDumpSequence(int value) { localDumpSequence = value; if (IsNetworkSessionActive()) dumpSequenceNetwork.Value = value; else PlaySpillVisual(); }
    private void OnModeChanged(byte previous, byte current) { ApplyDrumPose(false); RaiseStateChanged(); }
    private void OnByteStateChanged(byte previous, byte current) => RaiseStateChanged();
    private void OnIntStateChanged(int previous, int current) => RaiseStateChanged();
    private void OnFloatStateChanged(float previous, float current) => RaiseStateChanged();
    private void OnCrankOwnerChanged(ulong previous, ulong current) => RaiseStateChanged();
    private void OnDumpSequenceChanged(int previous, int current) { localDumpSequence = current; PlaySpillVisual(); RaiseStateChanged(); }
    private void OnClientDisconnected(ulong clientId) { if (CrankOwner == clientId) ReleaseCrank(clientId, true); }
    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    [ClientRpc] private void CrankGrantedClientRpc(ClientRpcParams rpcParams = default) => CrankGranted?.Invoke(this, EventArgs.Empty);
    [ClientRpc] private void CrankRevokedClientRpc(ClientRpcParams rpcParams = default) => CrankRevoked?.Invoke(this, EventArgs.Empty);
    [ClientRpc] private void CrankDeniedClientRpc(string reason, ClientRpcParams rpcParams = default) => CrankDenied?.Invoke(reason);
    private static ClientRpcParams Target(ulong clientId) => new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
    private bool IsNetworkSessionActive() => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool HasSimulationAuthority() => !IsNetworkSessionActive() || IsServer;
}
