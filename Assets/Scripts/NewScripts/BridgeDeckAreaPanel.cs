using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public struct BridgeDeckAreaPanelSnapshot : INetworkSerializable, IEquatable<BridgeDeckAreaPanelSnapshot>
{
    public int LengthMillimeters;
    public int WidthMillimeters;
    public int ThicknessMillimeters;
    public int SourceLengthMillimeters;
    public int SourceMassGrams;
    public int CutOffsetMillimeters;
    public int ProgressMilliseconds;
    public ulong OperatorClientId;
    public ulong OperatorNetworkObjectId;
    public uint OperatorRequestSequence;
    public bool CutStarted;
    public bool AreaSecured;
    public bool AreaLocked;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref LengthMillimeters);
        serializer.SerializeValue(ref WidthMillimeters);
        serializer.SerializeValue(ref ThicknessMillimeters);
        serializer.SerializeValue(ref SourceLengthMillimeters);
        serializer.SerializeValue(ref SourceMassGrams);
        serializer.SerializeValue(ref CutOffsetMillimeters);
        serializer.SerializeValue(ref ProgressMilliseconds);
        serializer.SerializeValue(ref OperatorClientId);
        serializer.SerializeValue(ref OperatorNetworkObjectId);
        serializer.SerializeValue(ref OperatorRequestSequence);
        serializer.SerializeValue(ref CutStarted);
        serializer.SerializeValue(ref AreaSecured);
        serializer.SerializeValue(ref AreaLocked);
    }

    public bool Equals(BridgeDeckAreaPanelSnapshot other) => LengthMillimeters == other.LengthMillimeters
        && WidthMillimeters == other.WidthMillimeters && ThicknessMillimeters == other.ThicknessMillimeters
        && SourceLengthMillimeters == other.SourceLengthMillimeters && SourceMassGrams == other.SourceMassGrams
        && CutOffsetMillimeters == other.CutOffsetMillimeters
        && ProgressMilliseconds == other.ProgressMilliseconds && OperatorClientId == other.OperatorClientId
        && OperatorNetworkObjectId == other.OperatorNetworkObjectId
        && OperatorRequestSequence == other.OperatorRequestSequence && CutStarted == other.CutStarted
        && AreaSecured == other.AreaSecured && AreaLocked == other.AreaLocked;
    public override bool Equals(object obj) => obj is BridgeDeckAreaPanelSnapshot other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(LengthMillimeters, WidthMillimeters, ThicknessMillimeters,
        SourceLengthMillimeters, SourceMassGrams, CutOffsetMillimeters, ProgressMilliseconds,
        HashCode.Combine(OperatorClientId, OperatorNetworkObjectId, OperatorRequestSequence, CutStarted, AreaSecured, AreaLocked));
    public static bool operator ==(BridgeDeckAreaPanelSnapshot a, BridgeDeckAreaPanelSnapshot b) => a.Equals(b);
    public static bool operator !=(BridgeDeckAreaPanelSnapshot a, BridgeDeckAreaPanelSnapshot b) => !a.Equals(b);
}

/// <summary>Opt-in physical deck plate. Its pivot is at the bottom center; dimensions are per instance.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject), typeof(BoxCollider), typeof(Rigidbody))]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class BridgeDeckAreaPanel : NetworkBehaviour, IInteractionPromptProvider
{
    private static readonly List<BridgeDeckAreaPanel> ActivePanels = new();
    private readonly NetworkVariable<BridgeDeckAreaPanelSnapshot> replicated = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private static readonly int[] FaceTriangles = BuildFaceTriangles();

    [SerializeField, Min(1)] private int lengthMillimeters = 2500;
    [SerializeField, Min(1)] private int widthMillimeters = 2400;
    [SerializeField, Min(1)] private int thicknessMillimeters = 200;
    [SerializeField, Min(1)] private int sourceLengthMillimeters = 2500;
    [SerializeField] private BridgeDeckAreaProfile geometryProfile;
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private BoxCollider boxCollider;
    [SerializeField] private Rigidbody body;

    private readonly List<MountingNail> nailScratch = new();
    private readonly StringBuilder interactionPromptBuilder = new(128);
    private readonly Vector3[] supportPoints = new Vector3[9];
    private readonly RaycastHit[] supportHitScratch = new RaycastHit[16];
    private Mesh generatedMesh;
    private BridgeDeckAreaPanelSnapshot localState;
    private float stableSeconds;
    private float syncAccumulator;
    private double activeElapsedSeconds;
    private double offlineActiveElapsedSeconds;
    private bool subscribed;
    private bool completionCommitted;
    private readonly Dictionary<ulong, uint> lastSequenceByActor = new();
    private ulong activeOperatorNetworkObjectId = ulong.MaxValue;
    private uint activeOperatorSequence;
    private double lastHeartbeatServerTime;
    private Vector3 lastAimOrigin;
    private Vector3 lastAimDirection;
    private SawCutProfileSO activeProfile;

    public int LengthMillimeters => ReadState().LengthMillimeters;
    public int WidthMillimeters => ReadState().WidthMillimeters;
    public int ThicknessMillimeters => ReadState().ThicknessMillimeters;
    public int SourceLengthMillimeters => ReadState().SourceLengthMillimeters;
    public int CutOffsetMillimeters => ReadState().CutOffsetMillimeters;
    public int ProgressMilliseconds => ReadState().ProgressMilliseconds;
    public bool CutStarted => ReadState().CutStarted;
    public bool IsSecuredForArea => ReadState().AreaSecured && !CutStarted;
    public bool IsLockedForCompletedArea => ReadState().AreaLocked;
    public BridgeDeckAreaPanelSnapshot State => ReadState();
    public BridgeDeckAreaProfile GeometryProfile => geometryProfile;
    public Rigidbody PhysicsBody => body;
    public BoxCollider PhysicalCollider => boxCollider;
    public event Action<BridgeDeckAreaPanel> StateChanged;

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (prompts == null) return;
        if (TryGetComponent(out MountableBridgeComponent mountable) && mountable.CanBePickedUp && !mountable.IsPickedUp)
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Pick up"));
        AddAreaInformationPrompts(prompts);
    }

    internal void AddAreaInformationPrompts(List<InteractionPrompt> prompts)
    {
        if (prompts == null || !BridgeDeckAreaRequirement.TryGetPanelStagePresentation(this, out bool presented,
                out BridgeDeckAreaStageResult result, out BridgeDeckAreaRequirement requirement)) return;
        if (!presented || result == null) return;
        if (requirement == null || !requirement.TryGetPanelPresentationDiagnostic(this, result,
                out BridgeDeckAreaDiagnostic diagnostic, out bool hasFourPhysicalNails)) return;
        interactionPromptBuilder.Clear();
        interactionPromptBuilder.Append(BridgeDeckAreaRequirement.FormatDiagnostic(diagnostic,
            hasFourPhysicalNails || IsSecuredForArea, IsLockedForCompletedArea));
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, interactionPromptBuilder.ToString()));
    }

    private void OnEnable()
    {
        if (!ActivePanels.Contains(this)) ActivePanels.Add(this);
    }

    private void OnDisable()
    {
        ActivePanels.Remove(this);
        if (IsSpawned && IsServer && !localState.AreaLocked && localState.OperatorClientId != ulong.MaxValue) PauseServer();
    }

    public static void CopyActivePanels(UnityEngine.SceneManagement.Scene scene, NetworkManager manager,
        List<BridgeDeckAreaPanel> results)
    {
        if (results == null) return;
        results.Clear();
        for (int i = ActivePanels.Count - 1; i >= 0; i--)
        {
            BridgeDeckAreaPanel panel = ActivePanels[i];
            if (panel == null) { ActivePanels.RemoveAt(i); continue; }
            if (!panel.isActiveAndEnabled || !panel.gameObject.activeInHierarchy || panel.gameObject.scene != scene) continue;
            if (manager != null && manager.IsListening && (!panel.IsSpawned || panel.NetworkManager != manager)) continue;
            results.Add(panel);
        }
    }

    public void SetAreaAssessmentState(bool secured, bool validPose, bool forcePublish)
    {
        if (IsSpawned && !IsServer) return;
        BridgeDeckAreaPanelSnapshot state = ReadState();
        if (state.AreaLocked && !secured) return;
        bool next = secured && validPose && !state.CutStarted;
        if (state.AreaSecured == next) return;
        state.AreaSecured = next;
        localState = state;
        if (IsSpawned && IsServer) replicated.Value = state;
        else if (forcePublish) StateChanged?.Invoke(this);
    }

    public void ApplyMaterialProfile(BridgeDeckAreaProfile profile)
    {
        if (profile == null || (IsSpawned && !IsServer)) return;
        BridgeDeckAreaPanelSnapshot state = ReadState();
        bool stock = state.LengthMillimeters == state.SourceLengthMillimeters;
        bool dimensionsChanged = false;
        geometryProfile = profile;
        if (stock)
        {
            int nextLength = MetersToMillimeters(profile.stockLengthMeters);
            int nextWidth = MetersToMillimeters(profile.stockWidthMeters);
            int nextThickness = MetersToMillimeters(profile.stockThicknessMeters);
            dimensionsChanged = state.LengthMillimeters != nextLength || state.WidthMillimeters != nextWidth
                || state.ThicknessMillimeters != nextThickness || state.SourceLengthMillimeters != nextLength;
            state.LengthMillimeters = state.SourceLengthMillimeters = lengthMillimeters = sourceLengthMillimeters = nextLength;
            state.WidthMillimeters = widthMillimeters = nextWidth;
            state.ThicknessMillimeters = thicknessMillimeters = nextThickness;
            state.SourceMassGrams = Mathf.Max(1, Mathf.RoundToInt(profile.fullStockMassKilograms * 1000f));
        }
        else if (geometryProfile == null) geometryProfile = profile;
        if (state.Equals(localState) && !dimensionsChanged) return;
        localState = state;
        if (body != null) body.mass = FragmentMassKilograms(state.LengthMillimeters);
        if (dimensionsChanged) RebuildGeometry();
        if (IsSpawned && IsServer) replicated.Value = state;
    }

    public bool SetAreaLock(bool locked)
    {
        if (IsSpawned && !IsServer) return false;
        BridgeDeckAreaPanelSnapshot state = ReadState();
        if (!locked && state.AreaLocked) return false;
        if (locked && !state.AreaSecured) return false;
        if (state.AreaLocked == locked) return true;
        state.AreaLocked = locked;
        localState = state;
        if (IsSpawned && IsServer) replicated.Value = state;
        StateChanged?.Invoke(this);
        return true;
    }

    private void Awake()
    {
        CacheComponents();
        localState = new BridgeDeckAreaPanelSnapshot
        {
            LengthMillimeters = geometryProfile != null ? MetersToMillimeters(geometryProfile.stockLengthMeters) : Mathf.Max(1, lengthMillimeters),
            WidthMillimeters = geometryProfile != null ? MetersToMillimeters(geometryProfile.stockWidthMeters) : Mathf.Max(1, widthMillimeters),
            ThicknessMillimeters = geometryProfile != null ? MetersToMillimeters(geometryProfile.stockThicknessMeters) : Mathf.Max(1, thicknessMillimeters),
            SourceLengthMillimeters = geometryProfile != null ? MetersToMillimeters(geometryProfile.stockLengthMeters) : Mathf.Max(1, sourceLengthMillimeters),
            SourceMassGrams = geometryProfile != null ? Mathf.Max(1, Mathf.RoundToInt(geometryProfile.fullStockMassKilograms * 1000f)) : 1000,
            OperatorClientId = ulong.MaxValue
        };
        lengthMillimeters = localState.LengthMillimeters;
        widthMillimeters = localState.WidthMillimeters;
        thicknessMillimeters = localState.ThicknessMillimeters;
        sourceLengthMillimeters = localState.SourceLengthMillimeters;
        if (body != null) body.mass = FragmentMassKilograms(localState.LengthMillimeters);
        RebuildGeometry();
    }

    public override void OnNetworkSpawn()
    {
        CacheComponents();
        replicated.OnValueChanged += OnReplicatedChanged;
        subscribed = true;
        if (IsServer)
        {
            if (localState.LengthMillimeters <= 0) Awake();
            replicated.Value = localState;
        }
        else if (replicated.Value.LengthMillimeters > 0) ApplySnapshot(replicated.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (subscribed) replicated.OnValueChanged -= OnReplicatedChanged;
        subscribed = false;
        if (IsServer) localState.OperatorClientId = ulong.MaxValue;
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (generatedMesh != null)
        {
            if (Application.isPlaying) Destroy(generatedMesh);
            else DestroyImmediate(generatedMesh);
        }
        base.OnDestroy();
    }

    private void CacheComponents()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (boxCollider == null) boxCollider = GetComponent<BoxCollider>();
        if (body == null) body = GetComponent<Rigidbody>();
    }

    private BridgeDeckAreaPanelSnapshot ReadState() => IsSpawned && NetworkManager != null && NetworkManager.IsListening
        ? replicated.Value : localState;

    private void OnReplicatedChanged(BridgeDeckAreaPanelSnapshot previous, BridgeDeckAreaPanelSnapshot current)
    {
        ApplySnapshot(current);
        StateChanged?.Invoke(this);
    }

    private void ApplySnapshot(BridgeDeckAreaPanelSnapshot value)
    {
        if (value.LengthMillimeters <= 0 || value.WidthMillimeters <= 0 || value.ThicknessMillimeters <= 0) return;
        bool dimensionsChanged = localState.LengthMillimeters != value.LengthMillimeters
            || localState.WidthMillimeters != value.WidthMillimeters
            || localState.ThicknessMillimeters != value.ThicknessMillimeters;
        localState = value;
        lengthMillimeters = value.LengthMillimeters;
        widthMillimeters = value.WidthMillimeters;
        thicknessMillimeters = value.ThicknessMillimeters;
        sourceLengthMillimeters = Mathf.Max(value.SourceLengthMillimeters, value.LengthMillimeters);
        if (dimensionsChanged) RebuildGeometry();
        if (body != null) body.mass = FragmentMassKilograms(value.LengthMillimeters);
    }

    private void RebuildGeometry()
    {
        CacheComponents();
        if (meshFilter == null || boxCollider == null) return;
        float length = Mathf.Max(1, localState.LengthMillimeters) * 0.001f;
        float width = Mathf.Max(1, localState.WidthMillimeters) * 0.001f;
        float thickness = Mathf.Max(1, localState.ThicknessMillimeters) * 0.001f;
        Vector3[] corners = { new(-length*.5f,0,-width*.5f), new(length*.5f,0,-width*.5f), new(length*.5f,0,width*.5f), new(-length*.5f,0,width*.5f), new(-length*.5f,thickness,-width*.5f), new(length*.5f,thickness,-width*.5f), new(length*.5f,thickness,width*.5f), new(-length*.5f,thickness,width*.5f) };
        int[] faceCorners = { 0,1,2,3, 4,7,6,5, 0,4,5,1, 3,2,6,7, 0,3,7,4, 1,5,6,2 };
        Vector3[] vertices = new Vector3[24];
        Vector2[] uv = new Vector2[24];
        for (int face = 0; face < 6; face++)
        {
            int baseIndex = face * 4;
            int c0 = faceCorners[face * 4], c1 = faceCorners[face * 4 + 1], c2 = faceCorners[face * 4 + 2], c3 = faceCorners[face * 4 + 3];
            vertices[baseIndex] = corners[c0]; vertices[baseIndex + 1] = corners[c1]; vertices[baseIndex + 2] = corners[c2]; vertices[baseIndex + 3] = corners[c3];
            float u = Vector3.Distance(corners[c0], corners[c1]);
            float v = Vector3.Distance(corners[c0], corners[c3]);
            uv[baseIndex] = Vector2.zero; uv[baseIndex + 1] = new Vector2(u, 0);
            uv[baseIndex + 2] = new Vector2(u, v); uv[baseIndex + 3] = new Vector2(0, v);
        }
        int[] triangles = new int[36];
        for (int i = 0; i < 6; i++) { int b = i * 4, t = i * 6; triangles[t] = b; triangles[t+1] = b+1; triangles[t+2] = b+2; triangles[t+3] = b; triangles[t+4] = b+2; triangles[t+5] = b+3; }
        if (generatedMesh == null) generatedMesh = new Mesh { name = "Generated Deck Plate" };
        generatedMesh.Clear();
        generatedMesh.vertices = vertices;
        generatedMesh.triangles = triangles;
        generatedMesh.uv = uv;
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();
        meshFilter.sharedMesh = generatedMesh;
        boxCollider.center = new Vector3(0f, thickness * .5f, 0f);
        boxCollider.size = new Vector3(length, thickness, width);
        if (meshRenderer != null) meshRenderer.enabled = true;
    }

    public bool TryGetCut(int offsetMillimeters, SawCutProfileSO profile, out BridgeDeckAreaCut cut)
    {
        cut = default;
        if (!TryGetRules(profile, out BridgeDeckAreaRules rules)) return false;
        return BridgeDeckAreaGeometry.TryCreateCut(rules, LengthMillimeters, offsetMillimeters, out cut);
    }

    private bool TryGetRules(SawCutProfileSO profile, out BridgeDeckAreaRules rules)
    {
        BridgeDeckAreaRules defaults = BridgeDeckAreaRules.Defaults;
        BridgeDeckAreaProfile sawCanonical = profile != null ? profile.geometryProfile : null;
        if (geometryProfile != null && sawCanonical != null && geometryProfile != sawCanonical)
        {
            rules = defaults;
            return false;
        }
        BridgeDeckAreaProfile canonical = geometryProfile != null ? geometryProfile : sawCanonical;
        float kerf = canonical != null ? canonical.sawKerfMeters : defaults.Kerf;
        rules = new BridgeDeckAreaRules(canonical != null ? canonical.areaLengthMeters : defaults.AreaLength,
            canonical != null ? canonical.areaWidthMeters : defaults.AreaWidth,
            canonical != null ? canonical.stockLengthMeters : defaults.StockLength,
            canonical != null ? canonical.stockWidthMeters : defaults.StockWidth,
            canonical != null ? canonical.stockThicknessMeters : defaults.StockThickness,
            canonical != null ? canonical.minimumFragmentLengthMeters : defaults.MinimumFragmentLength,
            canonical != null ? canonical.maximumJointWidthMeters : defaults.MaximumJointWidth,
            canonical != null ? canonical.boundaryMarginMeters : defaults.BoundaryMargin,
            canonical != null ? canonical.orientationToleranceDegrees : defaults.OrientationToleranceDegrees,
            canonical != null ? canonical.undersideHeightToleranceMeters : defaults.UndersideHeightTolerance,
            canonical != null ? canonical.geometryEpsilonMeters : defaults.Epsilon, kerf,
            profile != null ? profile.activeCutSeconds : defaults.ActiveCutSeconds,
            canonical != null ? canonical.fullStockMassKilograms : defaults.FullStockMass);
        return IsFinite(rules.Kerf) && rules.Kerf >= 0f && IsFinite(rules.MinimumFragmentLength)
            && rules.MinimumFragmentLength > 0f && IsFinite(rules.ActiveCutSeconds) && rules.ActiveCutSeconds > 0f
            && canonical != null && IsFinite(canonical.fullStockMassKilograms) && canonical.fullStockMassKilograms > 0f
            && IsFinite(rules.Epsilon) && rules.Epsilon >= defaults.Epsilon;
    }

    private static int MetersToMillimeters(float value) => Mathf.Max(1, Mathf.RoundToInt(value * 1000f));

    public int MinimumFragmentLengthMillimeters(SawCutProfileSO profile)
    {
        BridgeDeckAreaProfile canonical = geometryProfile != null ? geometryProfile : profile != null ? profile.geometryProfile : null;
        return Mathf.CeilToInt((canonical != null ? canonical.minimumFragmentLengthMeters
            : BridgeDeckAreaRules.Defaults.MinimumFragmentLength) * 1000f);
    }

    public bool TryBeginCutOffline(int offsetMillimeters, ulong actorId, SawCutProfileSO profile)
    {
        if (IsSpawned || actorId == ulong.MaxValue || IsLockedForCompletedArea || localState.CutStarted
            || !TryGetCut(offsetMillimeters, profile, out _) || localState.OperatorClientId != ulong.MaxValue
            || !IsEligible(profile, true)) return false;
        localState.CutOffsetMillimeters = offsetMillimeters;
        if (body != null) body.mass = FragmentMassKilograms(localState.LengthMillimeters);
        localState.CutStarted = true;
        localState.OperatorClientId = actorId;
        activeProfile = profile;
        offlineActiveElapsedSeconds = localState.ProgressMilliseconds / 1000d;
        activeOperatorNetworkObjectId = actorId;
        lastHeartbeatServerTime = Time.timeAsDouble;
        StateChanged?.Invoke(this);
        return true;
    }

    public bool ResumeCutOffline(ulong actorId, SawCutProfileSO profile)
    {
        if (IsSpawned || actorId == ulong.MaxValue || IsLockedForCompletedArea || !localState.CutStarted
            || localState.OperatorClientId != ulong.MaxValue || completionCommitted || !IsEligible(profile, true)) return false;
        localState.OperatorClientId = actorId;
        activeOperatorNetworkObjectId = actorId;
        activeProfile = profile;
        StateChanged?.Invoke(this);
        return true;
    }

    public void PauseCutOffline(ulong actorId)
    {
        if (IsSpawned || localState.OperatorClientId != actorId) return;
        localState.OperatorClientId = ulong.MaxValue;
        activeOperatorNetworkObjectId = ulong.MaxValue;
        localState.ProgressMilliseconds = (int)Math.Floor(offlineActiveElapsedSeconds * 1000d);
        StateChanged?.Invoke(this);
    }

    public void AdvanceCutOffline(ulong actorId, float activeSeconds, SawCutProfileSO profile)
    {
        if (IsSpawned || actorId != localState.OperatorClientId || !localState.CutStarted
            || !IsFinite(activeSeconds) || activeSeconds <= 0f || !TryGetRules(profile, out BridgeDeckAreaRules rules)) return;
        offlineActiveElapsedSeconds += activeSeconds;
        localState.ProgressMilliseconds = Mathf.Min(Mathf.CeilToInt(rules.ActiveCutSeconds * 1000f),
            (int)Math.Floor(offlineActiveElapsedSeconds * 1000d));
        StateChanged?.Invoke(this);
    }

    public bool IsCutComplete(SawCutProfileSO profile) => TryGetRules(profile, out BridgeDeckAreaRules rules)
        && CutStarted && ProgressMilliseconds >= Mathf.CeilToInt(rules.ActiveCutSeconds * 1000f);

    public bool TryGetFragmentLengths(SawCutProfileSO profile, out BridgeDeckAreaCut cut) =>
        TryGetCut(CutOffsetMillimeters, profile, out cut);

    public float FragmentMassKilograms(int fragmentLengthMillimeters) =>
        BridgeDeckAreaGeometry.MassForLength(localState.SourceMassGrams * .001f,
            fragmentLengthMillimeters, localState.SourceLengthMillimeters);

    private void Update()
    {
        if (!IsServer || !IsSpawned || !localState.CutStarted || localState.OperatorClientId == ulong.MaxValue) return;
        SawCutProfileSO profile = GetSawProfileForOperator(localState.OperatorClientId);
        NetworkObject actor = FindOperatorObject();
        PlayerActionController controller = actor != null ? actor.GetComponent<PlayerActionController>() : null;
        if (profile == null || controller == null || !IsFreshHeartbeat() || !controller.ServerSawRequestIsValid(this,
                lastAimOrigin, lastAimDirection, localState.OperatorClientId) || !IsEligible(profile, true)
            || !TryGetRules(profile, out BridgeDeckAreaRules rules)) { PauseServer(); return; }
        float delta = Time.deltaTime;
        if (!IsFinite(delta) || delta <= 0f) return;
        activeElapsedSeconds += delta;
        syncAccumulator += delta;
        localState.ProgressMilliseconds = Mathf.Min(Mathf.CeilToInt(rules.ActiveCutSeconds * 1000f),
            (int)Math.Floor(activeElapsedSeconds * 1000d));
        if (activeElapsedSeconds >= rules.ActiveCutSeconds)
        {
            replicated.Value = localState;
            TryCommitFragmentsServer(localState.OperatorClientId, rules);
            return;
        }
        if (syncAccumulator >= .1f)
        {
            syncAccumulator = 0f;
            replicated.Value = localState;
        }
    }

    private SawCutProfileSO GetSawProfileForOperator(ulong clientId)
    {
        if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
            || client.PlayerObject == null) return null;
        PlayerActionController controller = client.PlayerObject.GetComponent<PlayerActionController>();
        return controller != null ? controller.SelectedSawProfile : null;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestBeginCutServerRpc(ulong actorNetworkObjectId, Vector3 rayOrigin,
        Vector3 rayDirection, uint sequence, ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!IsServer || NetworkManager == null || completionCommitted || sequence == 0
            || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(actorNetworkObjectId, out NetworkObject actor)
            || actor == null || actor.OwnerClientId != senderClientId
            || !actor.TryGetComponent(out PlayerActionController controller)
            || !controller.ServerSawRequestIsValid(this, rayOrigin, rayDirection, senderClientId)) return;
        SawCutProfileSO profile = controller.SelectedSawProfile;
        if (profile == null || !IsEligible(profile, true)) return;
        bool resuming = localState.CutStarted;
        int serverOffsetMillimeters = localState.CutOffsetMillimeters;
        if (!resuming)
        {
            RaycastHit[] hits = controller.GetSawAimHits(new Ray(rayOrigin, rayDirection.normalized),
                profile.maximumRangeMeters, out int hitCount);
            RaycastHit? validatedHit = null;
            for (int i = 0; i < hitCount; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.root == actor.transform.root
                    || !controller.BelongsToThisNetworkSession(hits[i].collider)) continue;
                validatedHit = hits[i];
                break;
            }
            if (!validatedHit.HasValue || validatedHit.Value.collider.GetComponentInParent<BridgeDeckAreaPanel>() != this) return;
            Vector3 localHit = transform.InverseTransformPoint(validatedHit.Value.point);
            serverOffsetMillimeters = Mathf.RoundToInt((localHit.x + localState.LengthMillimeters * .0005f) * 1000f);
            if (!TryGetCut(serverOffsetMillimeters, profile, out _)) return;
        }
        if (lastSequenceByActor.TryGetValue(actorNetworkObjectId, out uint lastSequence) && sequence <= lastSequence) return;
        lastSequenceByActor[actorNetworkObjectId] = sequence;
        if (localState.CutStarted)
        {
            if (localState.OperatorClientId != ulong.MaxValue) return;
        }
        else
        {
            localState.CutStarted = true;
            localState.CutOffsetMillimeters = serverOffsetMillimeters;
            activeElapsedSeconds = localState.ProgressMilliseconds / 1000d;
        }
        localState.OperatorClientId = senderClientId;
        localState.OperatorNetworkObjectId = actorNetworkObjectId;
        localState.OperatorRequestSequence = sequence;
        activeOperatorNetworkObjectId = actorNetworkObjectId;
        activeOperatorSequence = sequence;
        activeProfile = profile;
        if (body != null) body.mass = FragmentMassKilograms(localState.LengthMillimeters);
        activeElapsedSeconds = localState.ProgressMilliseconds / 1000d;
        syncAccumulator = 0f;
        lastAimOrigin = rayOrigin;
        lastAimDirection = rayDirection;
        lastHeartbeatServerTime = NetworkManager.ServerTime.Time;
        replicated.Value = localState;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RefreshCutServerRpc(ulong actorNetworkObjectId, Vector3 rayOrigin, Vector3 rayDirection,
        uint sequence, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!IsServer || sequence == 0 || localState.OperatorClientId != sender
            || localState.OperatorNetworkObjectId != actorNetworkObjectId
            || localState.OperatorRequestSequence != sequence) return;
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(actorNetworkObjectId, out NetworkObject actor)
            || actor == null || actor.OwnerClientId != sender
            || !actor.TryGetComponent(out PlayerActionController controller)
            || !controller.ServerSawRequestIsValid(this, rayOrigin, rayDirection, sender)
            || controller.SelectedSawProfile == null || !IsEligible(controller.SelectedSawProfile, true))
        { PauseServer(); return; }
        lastAimOrigin = rayOrigin;
        lastAimDirection = rayDirection;
        lastHeartbeatServerTime = NetworkManager.ServerTime.Time;
    }

    [ServerRpc(RequireOwnership = false)]
    public void PauseCutServerRpc(ulong actorNetworkObjectId, uint sequence, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!IsServer || sequence == 0 || localState.OperatorClientId != sender
            || localState.OperatorNetworkObjectId != actorNetworkObjectId
            || localState.OperatorRequestSequence != sequence
            || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(actorNetworkObjectId, out NetworkObject actor)
            || actor == null || actor.OwnerClientId != sender) return;
        PauseServer();
    }

    private void PauseServer()
    {
        localState.ProgressMilliseconds = (int)Math.Floor(activeElapsedSeconds * 1000d);
        syncAccumulator = 0f;
        localState.OperatorClientId = ulong.MaxValue;
        localState.OperatorNetworkObjectId = ulong.MaxValue;
        activeOperatorNetworkObjectId = ulong.MaxValue;
        activeOperatorSequence = 0;
        replicated.Value = localState;
    }

    public bool IsEligible(SawCutProfileSO profile, bool requireReleased)
    {
        return string.IsNullOrEmpty(GetSawCutUnavailableReason(profile));
    }

    public string GetSawCutUnavailableReason(SawCutProfileSO profile)
    {
        if (profile == null) return "no valid Saw profile";
        if (IsLockedForCompletedArea) return "the completed area is locked";
        if (!isActiveAndEnabled || body == null || boxCollider == null || !boxCollider.enabled || boxCollider.isTrigger)
            return "the plate is unavailable";
        if (HasAssociatedNails()) return "remove the attached nails";
        TryGetComponent(out MountableBridgeComponent carry);
        if (carry != null && carry.IsPickedUp) return "release the carried plate";
        string supportReason = GetSawSupportUnavailableReason(profile);
        if (!string.IsNullOrEmpty(supportReason)) return supportReason;
        if (body.isKinematic) return "the plate is not movable stock";
        if (body.linearVelocity.magnitude > profile.maximumLinearSpeed
            || body.angularVelocity.magnitude * Mathf.Rad2Deg > profile.maximumAngularSpeedDegrees)
            return "let the plate stop moving";
        if (stableSeconds < profile.stableSeconds) return "wait for the plate to settle";
        return null;
    }

    /// <summary>
    /// Checks only the static underside support shared by server eligibility and client HUD feedback.
    /// Client replicas cannot authoritatively judge Rigidbody motion or stability, so those checks
    /// remain server-side while static terrain support can still be explained locally.
    /// </summary>
    public string GetSawSupportUnavailableReason(SawCutProfileSO profile) =>
        HasStaticSupport(profile) ? null : "the plate needs broad, static support from below";

    public bool IsSupportedFromBelow(SawCutProfileSO profile) => HasStaticSupport(profile);

    public string GetSawCutUnavailableReasonForClientReplica(SawCutProfileSO profile)
    {
        if (profile == null) return "no valid Saw profile";
        if (IsLockedForCompletedArea) return "the completed area is locked";
        if (HasAssociatedNails()) return "remove the attached nails";
        TryGetComponent(out MountableBridgeComponent carry);
        if (carry != null && carry.IsPickedUp) return "release the carried plate";
        return GetSawSupportUnavailableReason(profile);
    }

    private void FixedUpdate()
    {
        SawCutProfileSO profile = activeProfile;
        float speedLimit = profile != null ? profile.maximumLinearSpeed : .05f;
        float angularLimit = profile != null ? profile.maximumAngularSpeedDegrees : 3f;
        if (body != null && body.linearVelocity.magnitude <= speedLimit
            && body.angularVelocity.magnitude * Mathf.Rad2Deg <= angularLimit && HasStaticSupport(profile))
            stableSeconds += Time.fixedDeltaTime;
        else stableSeconds = 0f;
    }

    private bool HasStaticSupport(SawCutProfileSO profile)
    {
        if (boxCollider == null) return false;
        float length = localState.LengthMillimeters * .001f, width = localState.WidthMillimeters * .001f;
        BridgeDeckAreaProfile canonical = geometryProfile != null ? geometryProfile : profile != null ? profile.geometryProfile : null;
        float heightTolerance = canonical != null
            ? canonical.undersideHeightToleranceMeters : BridgeDeckAreaRules.Defaults.UndersideHeightTolerance;

        int cornerHits = 0;
        for (int i = 0; i < 4; i++)
        {
            float x = i == 0 || i == 3 ? -length * .5f : length * .5f;
            float z = i < 2 ? -width * .5f : width * .5f;
            if (HasStaticSupportAtPoint(new Vector3(x, 0f, z), heightTolerance)) cornerHits++;
        }
        // Retain the original predicate for stock supported at three corners, including
        // intentional three-point support layouts. The interior grid is a complementary
        // witness for rough Terrain where only the extreme corner rays miss the ground.
        if (cornerHits >= 3) return true;

        // Extreme corners are poor support witnesses on uneven Terrain: they may be over
        // valleys even while most of the underside rests on static ground. Sample a distributed
        // interior grid instead, requiring its centre, coverage on all rows, and support spanning
        // at least two columns. The ray height, length, normal, and static-body checks stay exact.
        int sample = 0;
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 3; column++)
            supportPoints[sample++] = new Vector3((column - 1) * length * .25f, 0f,
                (row - 1) * width * .25f);
        int hits = 0;
        int supportedRows = 0;
        int supportedColumns = 0;
        bool centerSupported = false;
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 3; column++)
        {
            int i = row * 3 + column;
            bool supported = HasStaticSupportAtPoint(supportPoints[i], heightTolerance);
            if (supported)
            {
                hits++;
                supportedRows |= 1 << row;
                supportedColumns |= 1 << column;
                if (row == 1 && column == 1) centerSupported = true;
            }
        }
        int columnCount = ((supportedColumns & 1) != 0 ? 1 : 0)
            + ((supportedColumns & 2) != 0 ? 1 : 0)
            + ((supportedColumns & 4) != 0 ? 1 : 0);
        return hits >= 5 && centerSupported && supportedRows == 0b111 && columnCount >= 2;
    }

    private bool HasStaticSupportAtPoint(Vector3 localPoint, float heightTolerance)
    {
        Vector3 point = transform.TransformPoint(localPoint);
        Vector3 origin = point + Vector3.up * (heightTolerance + .005f);
        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, supportHitScratch,
            heightTolerance + .015f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hitCount == supportHitScratch.Length) return false; // Fail closed if the candidate set was truncated.
        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            RaycastHit hit = supportHitScratch[hitIndex];
            Collider candidate = hit.collider;
            if (candidate == null || candidate == boxCollider || candidate.transform.root == transform.root
                || Vector3.Dot(hit.normal, Vector3.up) < .65f) continue;
            BridgeDeckAreaPanel replica = candidate.GetComponentInParent<BridgeDeckAreaPanel>();
            if (replica != null && IsSpawned && replica.IsSpawned && replica.NetworkManager != NetworkManager) continue;
            Rigidbody supportingBody = candidate.attachedRigidbody;
            if ((supportingBody != null && !supportingBody.isKinematic)
                || candidate.GetComponentInParent<PlayerHealth>() != null) continue;
            return true;
        }
        return false;
    }

    private bool HasAssociatedNails()
    {
        MountingNail.CopyActiveNails(gameObject.scene, NetworkManager, nailScratch);
        foreach (MountingNail nail in nailScratch)
        {
            if (nail == null || nail.State == MountingNailState.Loose) continue;
            if (nail.PreparedSurface != null && nail.PreparedSurface.transform.root == transform.root) return true;
            if (nail.TryGetAttachmentInfo(out MountingNailAttachmentInfo info)
                && (info.FirstTarget != null && info.FirstTarget.root == transform.root
                    || info.SecondTarget != null && info.SecondTarget.root == transform.root)) return true;
        }
        return false;
    }

    private void TryCommitFragmentsServer(ulong clientId, BridgeDeckAreaRules rules)
    {
        if (completionCommitted || !IsServer || !TryGetFragmentLengths(GetSawProfileForOperator(clientId), out BridgeDeckAreaCut cut))
        { PauseServer(); return; }
        SawCutProfileSO profile = GetSawProfileForOperator(clientId);
        GameObject prefab = profile != null ? profile.fragmentNetworkPrefab : null;
        NetworkObject prefabNetworkObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
        if (prefabNetworkObject == null || prefabNetworkObject.SpawnWithObservers)
        { PauseServer(); return; }
        completionCommitted = true;
        Vector3 sourcePosition = transform.position;
        Quaternion sourceRotation = transform.rotation;
        float length = localState.LengthMillimeters * .001f;
        float firstLength = cut.FirstLengthMillimeters * .001f;
        float secondLength = cut.SecondLengthMillimeters * .001f;
        float kerf = rules.Kerf;
        Vector3 firstPosition = sourcePosition + sourceRotation * new Vector3(-length * .5f + firstLength * .5f, 0f, 0f);
        Vector3 secondPosition = sourcePosition + sourceRotation * new Vector3(-length * .5f + firstLength + kerf + secondLength * .5f, 0f, 0f);
        NetworkObject first = null, second = null;
        Scene previousActiveScene = SceneManager.GetActiveScene();
        try
        {
            if (previousActiveScene != gameObject.scene && !SceneManager.SetActiveScene(gameObject.scene))
                throw new InvalidOperationException("Could not activate source scene for fragment creation.");
            // Spawn through this object's manager so prefab registration, overrides, and
            // ownership all stay within the same NGO session (SpawnWithOwnership uses Singleton).
            first = NetworkManager.SpawnManager.InstantiateAndSpawn(prefabNetworkObject,
                NetworkManager.ServerClientId, true, false, false, firstPosition, sourceRotation);
            if (first == null || !first.IsSpawned || first.NetworkManager != NetworkManager)
                throw new InvalidOperationException("First fragment could not be spawned by the source NetworkManager.");
            ConfigureFragment(first, cut.FirstLengthMillimeters, localState);

            // Both objects remain server-only until their per-instance snapshots are configured.
            // Keep the first reference so any second-spawn/configuration failure rolls it back.
            second = NetworkManager.SpawnManager.InstantiateAndSpawn(prefabNetworkObject,
                NetworkManager.ServerClientId, true, false, false, secondPosition, sourceRotation);
            if (second == null || !second.IsSpawned || second.NetworkManager != NetworkManager)
                throw new InvalidOperationException("Second fragment could not be spawned by the source NetworkManager.");
            ConfigureFragment(second, cut.SecondLengthMillimeters, localState);

            // Fragments are spawned server-only so observers never receive a stock-sized
            // collider or default dimensions. Publish both only after both snapshots are ready.
            first.SpawnWithObservers = true;
            second.SpawnWithObservers = true;
            foreach (ulong observerClientId in NetworkManager.ConnectedClientsIds)
            {
                if (observerClientId == NetworkManager.ServerClientId) continue;
                first.NetworkShow(observerClientId);
                second.NetworkShow(observerClientId);
            }
            NetworkObject.Despawn(true);
        }
        catch (Exception exception)
        {
            if (first != null) { if (first.IsSpawned) first.Despawn(true); else Destroy(first.gameObject); }
            if (second != null) { if (second.IsSpawned) second.Despawn(true); else Destroy(second.gameObject); }
            completionCommitted = false;
            PauseServer();
            Debug.LogError($"Deck plate cut transaction rolled back: {exception.Message}", this);
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded
                && SceneManager.GetActiveScene() != previousActiveScene)
                SceneManager.SetActiveScene(previousActiveScene);
        }
    }

    private static void ConfigureFragment(NetworkObject networkObject, int lengthMillimeters,
        BridgeDeckAreaPanelSnapshot source)
    {
        BridgeDeckAreaPanel panel = networkObject.GetComponent<BridgeDeckAreaPanel>();
        if (panel == null) throw new InvalidOperationException("Fragment prefab requires BridgeDeckAreaPanel.");
        panel.InitializeFragment(lengthMillimeters, source.WidthMillimeters, source.ThicknessMillimeters,
            source.SourceLengthMillimeters, source.SourceMassGrams);
    }

    public bool TryCompleteOfflineCut(SawCutProfileSO profile)
    {
        if (IsSpawned || completionCommitted || !IsCutComplete(profile)
            || localState.OperatorClientId == ulong.MaxValue
            || !TryGetFragmentLengths(profile, out BridgeDeckAreaCut cut)) return false;
        completionCommitted = true;
        float length = localState.LengthMillimeters * .001f;
        float firstLength = cut.FirstLengthMillimeters * .001f;
        float secondLength = cut.SecondLengthMillimeters * .001f;
        if (!TryGetRules(profile, out BridgeDeckAreaRules rules)) { completionCommitted = false; return false; }
        float kerf = rules.Kerf;
        Vector3 origin = transform.position;
        Quaternion rotation = transform.rotation;
        Vector3 firstPosition = origin + rotation * new Vector3(-length * .5f + firstLength * .5f, 0f, 0f);
        Vector3 secondPosition = origin + rotation * new Vector3(-length * .5f + firstLength + kerf + secondLength * .5f, 0f, 0f);
        GameObject prefab = profile != null && profile.fragmentNetworkPrefab != null ? profile.fragmentNetworkPrefab : gameObject;
        GameObject first = null, second = null;
        try
        {
            first = Instantiate(prefab, firstPosition, rotation);
            second = Instantiate(prefab, secondPosition, rotation);
            ConfigureOfflineFragment(first, cut.FirstLengthMillimeters);
            ConfigureOfflineFragment(second, cut.SecondLengthMillimeters);
            Destroy(gameObject);
            return true;
        }
        catch (Exception exception)
        {
            if (first != null) Destroy(first);
            if (second != null) Destroy(second);
            completionCommitted = false;
            PauseCutOffline(localState.OperatorClientId);
            Debug.LogError($"Offline deck plate cut transaction rolled back: {exception.Message}", this);
            return false;
        }
    }

    private void ConfigureOfflineFragment(GameObject fragment, int length)
    {
        BridgeDeckAreaPanel panel = fragment != null ? fragment.GetComponent<BridgeDeckAreaPanel>() : null;
        if (panel == null) throw new InvalidOperationException("Fragment source requires BridgeDeckAreaPanel.");
        panel.InitializeFragment(length, localState.WidthMillimeters, localState.ThicknessMillimeters,
            localState.SourceLengthMillimeters, localState.SourceMassGrams);
        panel.activeProfile = activeProfile;
    }

    public void InitializeFragment(int length, int width, int thickness, int sourceLength, int sourceMassGrams = 1000)
    {
        if (IsSpawned && !IsServer || length <= 0 || width <= 0 || thickness <= 0 || sourceLength < length) return;
        localState = new BridgeDeckAreaPanelSnapshot
        {
            LengthMillimeters = length, WidthMillimeters = width, ThicknessMillimeters = thickness,
            SourceLengthMillimeters = sourceLength, SourceMassGrams = Mathf.Max(1, sourceMassGrams),
            OperatorClientId = ulong.MaxValue
        };
        if (body != null) body.mass = FragmentMassKilograms(length);
        RebuildGeometry();
        if (IsSpawned && IsServer) replicated.Value = localState;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private NetworkObject FindOperatorObject()
    {
        if (NetworkManager == null || activeOperatorNetworkObjectId == ulong.MaxValue) return null;
        return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(activeOperatorNetworkObjectId, out NetworkObject actor) ? actor : null;
    }
    private bool IsFreshHeartbeat() => NetworkManager != null
        && NetworkManager.ServerTime.Time - lastHeartbeatServerTime <= .25d
        && localState.OperatorNetworkObjectId != ulong.MaxValue
        && NetworkManager.ConnectedClients.ContainsKey(localState.OperatorClientId);
    private static int[] BuildFaceTriangles()
    {
        int[] result = new int[36];
        for (int face = 0; face < 6; face++) { int b = face * 4, t = face * 6; result[t] = b; result[t+1] = b+1; result[t+2] = b+2; result[t+3] = b; result[t+4] = b+2; result[t+5] = b+3; }
        return result;
    }
}
