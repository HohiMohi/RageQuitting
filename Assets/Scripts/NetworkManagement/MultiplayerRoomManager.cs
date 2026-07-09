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
using UnityEngine.UI;

public class MultiplayerRoomManager : MonoBehaviour
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
    [SerializeField] private Button startGameButton;
    [SerializeField] private TMP_Text[] playerSlotTexts; // Array of 4 text components

    private string currentRoomCode;
    private const int MAX_PLAYERS = 4;

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
        leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
        copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
        startGameButton.onClick.AddListener(OnStartGameClicked);

        // Keep buttons inactive until initialized
createRoomButton.interactable = false;
        joinRoomButton.interactable = false;

        await InitializeUnityServicesAsync();
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
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MAX_PLAYERS);
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
        string joinCode = joinCodeInputField.text.Trim().ToUpper();
        if (string.IsNullOrEmpty(joinCode) || joinCode.Length < 6)
        {
            statusText.text = "Invalid Room Code! (Must be 6 characters)";
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
        catch (Exception ex)
        {
            statusText.text = "Join Room Failed: " + ex.Message;
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

    private void OnStartGameClicked()
    {
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene("FPP_scene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    private void OnClientConnected(ulong clientId)
{
        UpdatePlayerSlots();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            // If we got disconnected from the host
            OnLeaveRoomClicked();
            statusText.text = "Disconnected from host.";
        }
        else
        {
            UpdatePlayerSlots();
        }
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

        var connectedClients = NetworkManager.Singleton.ConnectedClientsList;
        int maxSlots = Mathf.Min(connectedClients.Count, playerSlotTexts.Length);

        for (int i = 0; i < maxSlots; i++)
        {
            var client = connectedClients[i];
            bool isLocal = client.ClientId == NetworkManager.Singleton.LocalClientId;
            bool isHost = client.ClientId == NetworkManager.ServerClientId;

            string label = "";
            if (isHost)
            {
                label = "Host [ID: " + client.ClientId + "]";
            }
            else
            {
                label = "Player " + (i + 1) + " [ID: " + client.ClientId + "]";
            }

            if (isLocal)
            {
                label += " (You)";
            }

            playerSlotTexts[i].text = label;
            playerSlotTexts[i].color = isLocal ? Color.green : Color.white;
        }

        startGameButton.gameObject.SetActive(NetworkManager.Singleton.IsHost);
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
        Button startGameButton,
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
        this.startGameButton = startGameButton;
        this.playerSlotTexts = playerSlotTexts;
    }
}
