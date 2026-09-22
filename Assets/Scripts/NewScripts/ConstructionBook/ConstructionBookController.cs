using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(NetworkObject), typeof(Collider))]
public sealed class ConstructionBookController : NetworkBehaviour, IInteractableNew, IInteractionPromptProvider, IContextActionConsumer
{
    [SerializeField] private ConstructionBookCatalogSO catalog;
    [SerializeField] private ProductionRecipeSO[] carpenterRecipes = Array.Empty<ProductionRecipeSO>();
    [SerializeField, Min(0.5f)] private float interactionRange = 3.5f;
    [SerializeField, Min(0.05f)] private float turnDuration = 0.35f;
    [SerializeField, Min(0.05f)] private float edgeNudgeDuration = 0.12f;
    [SerializeField] private Transform pageSurfaceAnchor;

    private readonly NetworkVariable<int> currentIndex = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkList<ulong> connectedPlayerIds = new NetworkList<ulong>();
    private readonly ConstructionBookTurnQueue serverTurnQueue = new ConstructionBookTurnQueue(16, 2);
    private readonly Queue<int> pendingAnimatedIndices = new Queue<int>();
    private readonly HashSet<string> loggedIssues = new HashSet<string>();
    private readonly List<OrderedBridgeComponentRequirement> spreads = new List<OrderedBridgeComponentRequirement>();
    private readonly List<ulong> rosterIds = new List<ulong>();
    private List<ConstructionBookRosterMember> roster = new List<ConstructionBookRosterMember>();
    private string leftPageCache = string.Empty;
    private string rightPageCache = string.Empty;
    private ConstructionBookView worldView;
    private ConstructionBookCatalogSO.Entry currentEntry;
    private List<ConstructionBookMaterialLine> currentMaterials = new List<ConstructionBookMaterialLine>();
    private bool currentMaterialsAvailable;
    private RectTransform pagesRoot;
    private RectTransform turningPagePivot;
    private bool initialized;
    private int displayedIndex = -1;
    private Coroutine turnRoutine;
    private Coroutine nudgeRoutine;
    public event Action<int, bool> PageChanged;
    public int CurrentIndex => IsSpawned ? currentIndex.Value : Mathf.Max(0, displayedIndex);
    public int PageCount => spreads.Count;
    public bool CanGoPrevious => CurrentIndex > 0;
    public bool CanGoNext => CurrentIndex + 1 < spreads.Count;

    private void Awake() { EnsureWorldUi(); }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        InitializeData();
        currentIndex.OnValueChanged += OnIndexChanged;
        connectedPlayerIds.OnListChanged += OnRosterListChanged;
        NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager != null)
        {
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            if (manager.SceneManager != null) manager.SceneManager.OnLoadEventCompleted += OnNetworkSceneLoadCompleted;
        }
        if (IsServer)
        {
            PopulateServerRosterFromConnections();
        }
        else RefreshRoster();
        SnapTo(currentIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        currentIndex.OnValueChanged -= OnIndexChanged;
        connectedPlayerIds.OnListChanged -= OnRosterListChanged;
        NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager != null)
        {
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
            if (manager.SceneManager != null) manager.SceneManager.OnLoadEventCompleted -= OnNetworkSceneLoadCompleted;
        }
        serverTurnQueue.Clear();
        pendingAnimatedIndices.Clear();
        if (turnRoutine != null) StopCoroutine(turnRoutine);
        turnRoutine = null;
        if (nudgeRoutine != null) StopCoroutine(nudgeRoutine);
        nudgeRoutine = null;
        PageChanged = null;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (IsServer && IsSpawned) ProcessServerTurnQueue();
    }

    private void Start()
    {
        if (!IsSpawned)
        {
            InitializeData();
            RefreshRoster();
            SnapTo(0);
        }
    }

    private void InitializeData()
    {
        if (initialized) return;
        initialized = true;
        spreads.Clear();
        IReadOnlyList<OrderedBridgeComponentRequirement> ordered = GameplayManager.Instance != null
            ? GameplayManager.Instance.GetOrderedBridgeComponentRequirements() : Array.Empty<OrderedBridgeComponentRequirement>();
        spreads.AddRange(ordered);
    }

    public void Interact(Transform interactor)
    {
        PlayerConstructionBookUI ui = interactor != null ? interactor.GetComponentInParent<PlayerConstructionBookUI>() : null;
        if (ui != null) ui.Open(this);
    }
    public void LookedAt(Transform interactor) { }
    public void LookedAway(Transform interactor) { }
    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Open construction book"));
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action, "Previous page"));
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.ActionAlt, "Next page"));
    }

    public bool TryConsumeContextAction(ContextActionKind action, Transform interactor)
    {
        RequestTurn(action == ContextActionKind.Primary ? -1 : 1, interactor);
        return true;
    }

    public void RequestTurn(int delta, Transform requester)
    {
        if (delta != -1 && delta != 1) return;
        InitializeData();
        int requested = Mathf.Clamp(CurrentIndex + delta, 0, Mathf.Max(0, spreads.Count - 1));
        if (requested == CurrentIndex)
        {
            if (isActiveAndEnabled && turnRoutine == null)
            {
                if (nudgeRoutine != null) StopCoroutine(nudgeRoutine);
                nudgeRoutine = StartCoroutine(EdgeNudge(delta));
            }
            return;
        }
        NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (!IsSpawned || manager == null || !manager.IsListening)
        {
            AnimateTo(requested);
            return;
        }
        if (IsServer)
        {
            ulong id = manager.LocalClientId;
            TryQueueTurnServer(delta, id);
        }
        else RequestTurnServerRpc(delta);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestTurnServerRpc(int delta, ServerRpcParams rpcParams = default) => TryQueueTurnServer(delta, rpcParams.Receive.SenderClientId);

    private void TryQueueTurnServer(int delta, ulong sender)
    {
        if (!TryValidateSender(delta, sender, out _)) return;
        int target = currentIndex.Value + delta;
        if (target < 0 || target >= spreads.Count) return;
        serverTurnQueue.TryEnqueue(sender, delta);
    }

    private bool TryValidateSender(int delta, ulong sender, out NetworkClient client)
    {
        client = null;
        NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        return (delta == -1 || delta == 1) && manager != null &&
               manager.ConnectedClients.TryGetValue(sender, out client) && client.PlayerObject != null &&
               Vector3.Distance(client.PlayerObject.transform.position, transform.position) <= interactionRange;
    }

    private void ProcessServerTurnQueue()
    {
        NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager == null) return;
        double now = manager.ServerTime.Time;
        int remaining = serverTurnQueue.Count;
        while (remaining-- > 0 && serverTurnQueue.TryDequeue(now, out ConstructionBookTurnRequest request))
        {
            if (!TryValidateSender(request.Delta, request.SenderClientId, out _)) continue;
            int target = currentIndex.Value + request.Delta;
            if (target < 0 || target >= spreads.Count) continue;
            currentIndex.Value = target;
            serverTurnQueue.MarkApplied(now, turnDuration);
            break;
        }
    }

    private void OnIndexChanged(int previous, int next) => AnimateTo(next);
    private void AnimateTo(int next)
    {
        if (!isActiveAndEnabled) { SnapTo(next); return; }
        if (turnRoutine != null)
        {
            pendingAnimatedIndices.Enqueue(next);
            return;
        }
        if (nudgeRoutine != null)
        {
            StopCoroutine(nudgeRoutine);
            nudgeRoutine = null;
            turningPagePivot.localRotation = Quaternion.identity;
        }
        turnRoutine = StartCoroutine(TurnPage(next));
    }
    private IEnumerator TurnPage(int next)
    {
        int direction = next >= displayedIndex ? 1 : -1;
        float half = turnDuration * 0.5f;
        for (float t = 0; t < half; t += Time.unscaledDeltaTime)
        { turningPagePivot.localRotation = Quaternion.Euler(0f, Mathf.Lerp(0f, 82f * direction, t / half), 0f); yield return null; }
        Render(next);
        for (float t = 0; t < half; t += Time.unscaledDeltaTime)
        { turningPagePivot.localRotation = Quaternion.Euler(0f, Mathf.Lerp(-82f * direction, 0f, t / half), 0f); yield return null; }
        turningPagePivot.localRotation = Quaternion.identity;
        turnRoutine = null;
        if (pendingAnimatedIndices.Count > 0) AnimateTo(pendingAnimatedIndices.Dequeue());
    }
    private IEnumerator EdgeNudge(int direction)
    {
        for (float t = 0; t < edgeNudgeDuration; t += Time.unscaledDeltaTime)
        { turningPagePivot.localRotation = Quaternion.Euler(0f, Mathf.Sin(t / edgeNudgeDuration * Mathf.PI) * 8f * direction, 0f); yield return null; }
        turningPagePivot.localRotation = Quaternion.identity;
        nudgeRoutine = null;
    }
    private void SnapTo(int index)
    {
        if (turnRoutine != null) StopCoroutine(turnRoutine);
        turnRoutine = null;
        if (nudgeRoutine != null) StopCoroutine(nudgeRoutine);
        nudgeRoutine = null;
        pendingAnimatedIndices.Clear();
        Render(index, true);
        if (turningPagePivot != null) turningPagePivot.localRotation = Quaternion.identity;
    }

    private void Render(int index, bool snap = false)
    {
        displayedIndex = Mathf.Clamp(index, 0, Mathf.Max(0, spreads.Count - 1));
        if (spreads.Count == 0) { leftPageCache = "CONSTRUCTION BOOK\n\nData unavailable"; rightPageCache = "Data unavailable"; currentEntry = null; currentMaterials.Clear(); currentMaterialsAvailable = false; }
        else
        {
            OrderedBridgeComponentRequirement spread = spreads[displayedIndex];
            ConstructionBookCatalogSO.Entry entry = null;
            if (catalog != null) catalog.TryGetEntry(spread.Component, out entry);
            currentEntry = entry;
            string title = entry != null && !string.IsNullOrWhiteSpace(entry.title) ? entry.title : spread.Component.componentName;
            StringBuilder left = new StringBuilder().Append(title).Append("  x").Append(spread.RequiredCount).Append("\n\nMATERIALS\n");
            currentMaterialsAvailable = ConstructionBookData.TryResolveRecipe(spread.Component, spread.RequiredCount, carpenterRecipes, out List<ConstructionBookMaterialLine> materials, out string issue);
            currentMaterials = materials;
            if (!currentMaterialsAvailable)
            { left.Append("Data unavailable"); WarnOnce(issue); }
            else foreach (ConstructionBookMaterialLine material in materials)
                left.Append("• ").Append(material.Resource.resourceName).Append(": ").Append(material.PerUnit).Append(" each · ").Append(material.Total).Append(" total\n");
            leftPageCache = left.ToString();
            StringBuilder right = new StringBuilder().Append("STEPS\n\n");
            if (entry == null || entry.steps == null || entry.steps.Length == 0)
            { right.Append("Data unavailable"); WarnOnce($"missing authored steps for '{spread.Component.name}'"); }
            else
            {
                for (int i = 0; i < entry.steps.Length; i++)
                {
                    ConstructionBookCatalogSO.Step step = entry.steps[i];
                    right.Append(i + 1).Append(". ").Append(step.instruction);
                    if (step.requirements != null && step.requirements.Length > 0)
                    { right.Append("  ["); for (int r = 0; r < step.requirements.Length; r++) { if (r > 0) right.Append(" + "); right.Append(step.requirements[r].displayName); } right.Append(']'); }
                    right.Append('\n');
                    foreach (ConstructionBookRosterMember member in roster)
                    {
                        NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
                        bool local = manager != null && member.ClientId == manager.LocalClientId;
                        right.Append(local ? "  <b>☐ " : "  ☐ ").Append(member.Label).Append(local ? "</b>" : string.Empty);
                    }
                    right.Append("\n\n");
                }
            }
            rightPageCache = right.ToString();
        }
        PopulateView(worldView);
        PageChanged?.Invoke(displayedIndex, snap);
    }

    public string GetLeftPageText() => leftPageCache;
    public string GetRightPageText() => rightPageCache;
    public void PopulateView(ConstructionBookView view)
    {
        if (view == null) return;
        int count = spreads.Count > 0 ? spreads[Mathf.Clamp(displayedIndex, 0, spreads.Count - 1)].RequiredCount : 0;
        NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        ulong localId = manager != null && manager.IsListening ? manager.LocalClientId : 0;
        view.Render(currentEntry, count, Mathf.Max(0, displayedIndex), spreads.Count, currentMaterials, currentMaterialsAvailable, roster, localId);
    }
    private void WarnOnce(string issue) { if (!string.IsNullOrEmpty(issue) && loggedIssues.Add(issue)) Debug.LogWarning($"ConstructionBook: {issue}; spread retained with Data unavailable.", this); }
    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        AddConnectedClientId(clientId);
    }
    private void OnNetworkSceneLoadCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServer) return;
        PopulateServerRosterFromConnections();
        PopulateView(worldView);
        PageChanged?.Invoke(Mathf.Max(0, displayedIndex), true);
    }
    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;
        serverTurnQueue.RemoveSender(clientId);
        for (int i = connectedPlayerIds.Count - 1; i >= 0; i--)
            if (connectedPlayerIds[i] == clientId) connectedPlayerIds.RemoveAt(i);
    }
    private void PopulateServerRosterFromConnections()
    {
        NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager == null) return;
        List<ulong> connectionIds = new List<ulong>();
        IReadOnlyList<ulong> connectedIds = manager.ConnectedClientsIds;
        for (int i = 0; i < connectedIds.Count; i++) connectionIds.Add(connectedIds[i]);
        connectionIds.Sort();
        for (int i = connectedPlayerIds.Count - 1; i >= 0; i--)
            if (!connectionIds.Contains(connectedPlayerIds[i])) connectedPlayerIds.RemoveAt(i);
        foreach (ulong clientId in connectionIds) AddConnectedClientId(clientId);
        RefreshRoster();
    }
    private void AddConnectedClientId(ulong clientId)
    {
        for (int i = 0; i < connectedPlayerIds.Count; i++)
            if (connectedPlayerIds[i] == clientId) return;
        int insertIndex = 0;
        while (insertIndex < connectedPlayerIds.Count && connectedPlayerIds[insertIndex] < clientId) insertIndex++;
        connectedPlayerIds.Insert(insertIndex, clientId);
    }

    private void OnRosterListChanged(NetworkListEvent<ulong> _)
    {
        RefreshRoster();
        PopulateView(worldView);
        PageChanged?.Invoke(Mathf.Max(0, displayedIndex), true);
    }
    private void RefreshRoster()
    {
        rosterIds.Clear();
        NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager != null && manager.IsListening)
        {
            foreach (ulong id in connectedPlayerIds) rosterIds.Add(id);
            bool hostHasPlayer = rosterIds.Contains(Unity.Netcode.NetworkManager.ServerClientId);
            roster = ConstructionBookRoster.Build(rosterIds, hostHasPlayer, Unity.Netcode.NetworkManager.ServerClientId);
        }
        else roster = ConstructionBookRoster.Build(new List<ulong> { 0 }, true, 0);
    }

    private void EnsureWorldUi()
    {
        if (pagesRoot != null) return;
        GameObject canvasObject = new GameObject("WorldPages", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Transform parent = pageSurfaceAnchor != null ? pageSurfaceAnchor : transform;
        canvasObject.transform.SetParent(parent, false);
        canvasObject.transform.localPosition = Vector3.zero;
        canvasObject.transform.localRotation = Quaternion.identity;
        canvasObject.transform.localScale = new Vector3(0.00048f, -0.00048f, 0.00048f);
        Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        pagesRoot = canvasObject.GetComponent<RectTransform>(); pagesRoot.sizeDelta = new Vector2(2048f, 1400f);
        worldView = canvasObject.AddComponent<ConstructionBookView>();
        worldView.Build(new Color(0.86f, 0.72f, 0.48f, 1f), new Color(0.13f, 0.09f, 0.05f));
        turningPagePivot = worldView.TurningPagePivot;
    }
}
