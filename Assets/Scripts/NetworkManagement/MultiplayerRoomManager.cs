using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class MultiplayerRoomManager : NetworkBehaviour
{
    public static MultiplayerRoomManager Instance { get; private set; }

    [Header("UI Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject lobbyPanel;

    [Header("Main Panel UI")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private TMP_InputField joinCodeInputField;
    [SerializeField] private Button joinRoomButton;

    [Header("Lobby Panel UI")]
    [SerializeField] private TMP_Text lobbyCodeText;
    [SerializeField] private Button leaveRoomButton;
    [SerializeField] private Button copyCodeButton;
    [FormerlySerializedAs("startGameButton")]
    [SerializeField] private Button startFppSceneButton;
    [SerializeField] private Button startTutorialSceneButton;
    [SerializeField] private TMP_Text[] playerSlotTexts; // Array of 4 text components

    private string currentRoomCode;
    private bool sceneLoadInProgress;
    private const int MAX_PLAYERS = 4;
    private const int MIN_JOIN_CODE_LENGTH = 6;
    private const int MAX_JOIN_CODE_LENGTH = 12;
    private const string RELAY_JOIN_CODE_ALPHABET = "6789BCDFGHJKLMNPQRTW";
    private readonly NetworkList<ulong> connectedPlayerIds = new NetworkList<ulong>();

    public IReadOnlyList<ulong> ConnectedPlayerIds
    {
        get
        {
            List<ulong> snapshot = new List<ulong>(connectedPlayerIds.Count);
            for (int i = 0; i < connectedPlayerIds.Count; i++)
            {
                snapshot.Add(connectedPlayerIds[i]);
            }

            return snapshot;
        }
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private async void Start()
    {
        // Deactivate lobby panel, activate main panel
        mainPanel.SetActive(true);
        lobbyPanel.SetActive(false);

        // Set up buttons
        createRoomButton.onClick.AddListener(OnCreateRoomClicked);
        joinRoomButton.onClick.AddListener(OnJoinRoomClicked);
        joinCodeInputField.characterLimit = MAX_JOIN_CODE_LENGTH;
        joinCodeInputField.onValueChanged.AddListener(OnJoinCodeChanged);
        leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
        copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
        startFppSceneButton.onClick.AddListener(OnStartFppSceneClicked);
        if (startTutorialSceneButton != null)
        {
            startTutorialSceneButton.onClick.AddListener(OnStartTutorialSceneClicked);
        }

        // Keep buttons inactive until initialized
        createRoomButton.interactable = false;
        joinRoomButton.interactable = false;

        await InitializeUnityServicesAsync();
    }

    public override void OnNetworkSpawn()
    {
        connectedPlayerIds.OnListChanged += OnConnectedPlayerIdsChanged;
        if (IsServer)
        {
            RebuildConnectedPlayerIds();
        }

        UpdatePlayerSlots();
    }

    public override void OnNetworkDespawn()
    {
        connectedPlayerIds.OnListChanged -= OnConnectedPlayerIdsChanged;
        base.OnNetworkDespawn();
    }

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private async Task InitializeUnityServicesAsync()
    {
        try
        {
            statusText.text = "Initializing Unity Services...";
            await UnityServices.InitializeAsync();

            statusText.text = "Authenticating Player...";
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            statusText.text = "Ready (ID: " + AuthenticationService.Instance.PlayerId.Substring(0, Mathf.Min(8, AuthenticationService.Instance.PlayerId.Length)) + "...)";
            createRoomButton.interactable = true;
            joinRoomButton.interactable = true;
        }
        catch (Exception ex)
        {
            statusText.text = "Initialization Error: " + ex.Message;
            Debug.LogError("Failed to initialize Unity Services: " + ex);
        }
    }

    private async void OnCreateRoomClicked()
    {
        try
        {
            SetButtonsInteractable(false);
            statusText.text = "Requesting Relay Room...";

            // Create Relay allocation
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MAX_PLAYERS - 1);
            currentRoomCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // Configure Transport
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            var relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            transport.SetRelayServerData(relayServerData);

            // Start Host
            NetworkManager.Singleton.StartHost();

            // Set UI Status
            lobbyCodeText.text = currentRoomCode;
            mainPanel.SetActive(false);
            lobbyPanel.SetActive(true);
            UpdatePlayerSlots();
        }
        catch (Exception ex)
        {
            statusText.text = "Create Room Failed: " + ex.Message;
            SetButtonsInteractable(true);
            Debug.LogError("Create Room Exception: " + ex);
        }
    }

    private async void OnJoinRoomClicked()
    {
        string joinCode = NormalizeJoinCode(joinCodeInputField.text);
        joinCodeInputField.SetTextWithoutNotify(joinCode);
        if (!IsValidJoinCode(joinCode))
        {
            statusText.text = "Invalid room code. Use 6-12 valid characters.";
            return;
        }

        try
        {
            SetButtonsInteractable(false);
            statusText.text = "Joining Relay Room: " + joinCode + "...";

            // Join Relay Allocation
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            currentRoomCode = joinCode;

            // Configure Transport
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            transport.SetRelayServerData(relayServerData);

            // Start Client
            NetworkManager.Singleton.StartClient();

            // Set UI Status
            lobbyCodeText.text = currentRoomCode;
            mainPanel.SetActive(false);
            lobbyPanel.SetActive(true);
            UpdatePlayerSlots();
        }
        catch (RelayServiceException ex)
        {
            HandleJoinRelayException(ex);
            SetButtonsInteractable(true);
        }
        catch (Exception ex)
        {
            statusText.text = "Join room failed.";
            SetButtonsInteractable(true);
            Debug.LogError("Join Room Exception: " + ex);
        }
    }

    private void OnLeaveRoomClicked()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        lobbyPanel.SetActive(false);
        mainPanel.SetActive(true);
        joinCodeInputField.text = "";
        statusText.text = "Disconnected from room.";
        SetButtonsInteractable(true);
    }

    private void OnCopyCodeClicked()
    {
        if (!string.IsNullOrEmpty(currentRoomCode))
        {
            GUIUtility.systemCopyBuffer = currentRoomCode;
            statusText.text = "Code copied to clipboard!";
        }
    }

    private void OnStartFppSceneClicked()
    {
        TryStartGameplayScene(GameplaySceneRegistry.FppSceneName);
    }

    private void OnStartTutorialSceneClicked()
    {
        TryStartGameplayScene(GameplaySceneRegistry.TutorialSceneName);
    }

    private void TryStartGameplayScene(string sceneName)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (sceneLoadInProgress || networkManager == null || !networkManager.IsHost || !GameplaySceneRegistry.IsGameplayScene(sceneName))
        {
            return;
        }

        sceneLoadInProgress = true;
        SetGameplaySceneButtonsInteractable(false);
        statusText.text = $"Loading {sceneName}...";

        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(
            sceneName,
            UnityEngine.SceneManagement.LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            sceneLoadInProgress = false;
            statusText.text = $"Could not load {sceneName}: {status}.";
            SetGameplaySceneButtonsInteractable(true);
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            AddConnectedPlayer(clientId);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            connectedPlayerIds.Remove(clientId);
        }

        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            // If we got disconnected from the host
            OnLeaveRoomClicked();
            statusText.text = "Disconnected from host.";
        }
    }

    private void OnConnectedPlayerIdsChanged(NetworkListEvent<ulong> changeEvent)
    {
        UpdatePlayerSlots();
    }

    private void RebuildConnectedPlayerIds()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        List<ulong> clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        clientIds.Sort();
        connectedPlayerIds.Clear();
        for (int i = 0; i < clientIds.Count && i < MAX_PLAYERS; i++)
        {
            connectedPlayerIds.Add(clientIds[i]);
        }
    }

    private void AddConnectedPlayer(ulong clientId)
    {
        if (connectedPlayerIds.Contains(clientId) || connectedPlayerIds.Count >= MAX_PLAYERS)
        {
            return;
        }

        int insertIndex = 0;
        while (insertIndex < connectedPlayerIds.Count && connectedPlayerIds[insertIndex] < clientId)
        {
            insertIndex++;
        }

        connectedPlayerIds.Insert(insertIndex, clientId);
    }

    private void UpdatePlayerSlots()
    {
        if (NetworkManager.Singleton == null) return;

        // Clear all slots
        for (int i = 0; i < playerSlotTexts.Length; i++)
        {
            playerSlotTexts[i].text = "Waiting for player...";
            playerSlotTexts[i].color = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        }

        int maxSlots = Mathf.Min(connectedPlayerIds.Count, playerSlotTexts.Length);

        for (int i = 0; i < maxSlots; i++)
        {
            ulong clientId = connectedPlayerIds[i];
            bool isLocal = clientId == NetworkManager.Singleton.LocalClientId;
            bool isHost = clientId == NetworkManager.ServerClientId;

            string label = "";
            if (isHost)
            {
                label = "Host [ID: " + clientId + "]";
            }
            else
            {
                label = "Player " + (i + 1) + " [ID: " + clientId + "]";
            }

            if (isLocal)
            {
                label += " (You)";
            }

            playerSlotTexts[i].text = label;
            playerSlotTexts[i].color = isLocal ? Color.green : Color.white;
        }

        bool hostCanStart = NetworkManager.Singleton.IsHost;
        startFppSceneButton.gameObject.SetActive(hostCanStart);
        if (startTutorialSceneButton != null)
        {
            startTutorialSceneButton.gameObject.SetActive(hostCanStart);
        }

        SetGameplaySceneButtonsInteractable(hostCanStart && !sceneLoadInProgress);
    }

    private void OnJoinCodeChanged(string value)
    {
        string normalizedValue = NormalizeJoinCode(value);
        if (normalizedValue == value)
        {
            return;
        }

        int originalCaretPosition = Mathf.Clamp(joinCodeInputField.caretPosition, 0, value.Length);
        int normalizedCaretPosition = NormalizeJoinCode(value.Substring(0, originalCaretPosition)).Length;
        joinCodeInputField.SetTextWithoutNotify(normalizedValue);
        joinCodeInputField.caretPosition = Mathf.Min(normalizedCaretPosition, normalizedValue.Length);
        joinCodeInputField.selectionAnchorPosition = joinCodeInputField.caretPosition;
        joinCodeInputField.selectionFocusPosition = joinCodeInputField.caretPosition;
    }

    internal static string NormalizeJoinCode(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        char[] normalizedCharacters = new char[Mathf.Min(value.Length, MAX_JOIN_CODE_LENGTH)];
        int normalizedLength = 0;
        for (int i = 0; i < value.Length && normalizedLength < MAX_JOIN_CODE_LENGTH; i++)
        {
            char character = char.ToUpperInvariant(value[i]);
            if (RELAY_JOIN_CODE_ALPHABET.IndexOf(character) < 0)
            {
                continue;
            }

            normalizedCharacters[normalizedLength++] = character;
        }

        return new string(normalizedCharacters, 0, normalizedLength);
    }

    internal static bool IsValidJoinCode(string joinCode)
    {
        if (string.IsNullOrEmpty(joinCode)
            || joinCode.Length < MIN_JOIN_CODE_LENGTH
            || joinCode.Length > MAX_JOIN_CODE_LENGTH)
        {
            return false;
        }

        for (int i = 0; i < joinCode.Length; i++)
        {
            if (RELAY_JOIN_CODE_ALPHABET.IndexOf(joinCode[i]) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private void HandleJoinRelayException(RelayServiceException exception)
    {
        switch (exception.Reason)
        {
            case RelayExceptionReason.JoinCodeNotFound:
            case RelayExceptionReason.AllocationNotFound:
            case RelayExceptionReason.EntityNotFound:
            case RelayExceptionReason.Gone:
                statusText.text = "Room not found or expired.";
                return;
            case RelayExceptionReason.InvalidRequest:
            case RelayExceptionReason.InvalidArgument:
                statusText.text = "Invalid room code. Use 6-12 valid characters.";
                return;
            case RelayExceptionReason.NetworkError:
            case RelayExceptionReason.RequestTimeOut:
            case RelayExceptionReason.ServiceUnavailable:
            case RelayExceptionReason.GatewayTimeout:
                statusText.text = "Could not reach Relay. Try again.";
                return;
            default:
                statusText.text = "Join room failed.";
                Debug.LogError("Join Room Relay Exception: " + exception);
                return;
        }
    }

    private void SetGameplaySceneButtonsInteractable(bool interactable)
    {
        startFppSceneButton.interactable = interactable;
        if (startTutorialSceneButton != null)
        {
            startTutorialSceneButton.interactable = interactable;
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        createRoomButton.interactable = interactable;
        joinRoomButton.interactable = interactable;
    }

    public void SetupManager(
        GameObject mainPanel,
        GameObject lobbyPanel,
        TMP_Text statusText,
        Button createRoomButton,
        TMP_InputField joinCodeInputField,
        Button joinRoomButton,
        TMP_Text lobbyCodeText,
        Button leaveRoomButton,
        Button copyCodeButton,
        Button startFppSceneButton,
        Button startTutorialSceneButton,
        TMP_Text[] playerSlotTexts)
    {
        this.mainPanel = mainPanel;
        this.lobbyPanel = lobbyPanel;
        this.statusText = statusText;
        this.createRoomButton = createRoomButton;
        this.joinCodeInputField = joinCodeInputField;
        this.joinRoomButton = joinRoomButton;
        this.lobbyCodeText = lobbyCodeText;
        this.leaveRoomButton = leaveRoomButton;
        this.copyCodeButton = copyCodeButton;
        this.startFppSceneButton = startFppSceneButton;
        this.startTutorialSceneButton = startTutorialSceneButton;
        this.playerSlotTexts = playerSlotTexts;
    }
}
