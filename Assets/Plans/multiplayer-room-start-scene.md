# Project Overview 
- Game Title: RageQuitting (Goblin Bridge Builders)
- High-Level Concept: A multiplayer cooperative logistics and construction game where players cooperatively gather materials, process them in factories, and assemble complex bridge structures.
- Players: Multiplayer cooperative, up to 4 players (using Netcode for GameObjects)
- Inspiration / Reference Games: Overcooked, Moving Out, Unrailed
- Tone / Art Direction: Stylized, humorous, goblin-themed
- Target Platform: PC (Standalone Windows)
- Screen Orientation / Resolution: Landscape 1920x1080
- Render Pipeline: Universal Render Pipeline (URP)

# Game Mechanics 
## Core Gameplay Loop
Players start in a multiplayer start scene where they can host or join a room using a generated 6-character room code. Once connected via Unity Netcode + Relay, players gather in a lobby/room UI where up to 4 connected players are listed in real-time. (Subsequent progression to gameplay is disabled for now, focusing purely on room creation, joining, and player listing).

## Controls and Input Methods
The multiplayer start scene is entirely UI-based. Players use the mouse to click buttons (Create Room, Join Room, Leave Room, Copy Code) and the keyboard to type the room code in an input field.

# UI
A clean and intuitive Canvas-based layout with two main screens:
1. **Multiplayer Start Screen (Main Menu)**:
   - Game Title Header: "GOBLIN BRIDGE BUILDERS"
   - Status text: "Initializing Unity Services..." / "Ready to Connect"
   - "CREATE ROOM" button.
   - Room Code Input Field (TMP_InputField) with placeholder "ENTER ROOM CODE".
   - "JOIN ROOM" button.
2. **Room / Lobby Screen** (Visible after joining or hosting):
   - Room Code Display Header: "ROOM CODE: [XXXXXX]" (with a copy button).
   - Player Slots List: 4 vertical text slots showing the status of each player slot:
     - Slot 1: "Host (Local Player) [ID: 0]"
     - Slot 3: "Waiting for player..."
   - "LEAVE ROOM" button.

# Key Asset & Context
- Scene: `Assets/Scenes/MultiplayerStartScene.unity` (to be created, and added to Build Settings)
- Script: `Assets/Scripts/NetworkManagement/MultiplayerRoomManager.cs` (handles Unity Services, Authentication, Relay allocation, NGO hosting/connecting, and UI state synchronization).
- UI Canvas Prefab/Setup: Standard UGUI with TextMeshPro.

# Implementation Steps

## Step 1: Create the MultiplayerRoomManager Script
- **Description**: Implement a new C# script `MultiplayerRoomManager.cs` in `Assets/Scripts/NetworkManagement/`. This script handles Unity Services initialization, anonymous authentication, host Relay allocation, client Relay joining, and dynamic UI updates (syncing connected player count in real-time).
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: No

```csharp
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

            statusText.text = "Ready (ID: " + AuthenticationService.Instance.PlayerId.Substring(0, 8) + "...)";
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
    }

    private void SetButtonsInteractable(bool interactable)
    {
        createRoomButton.interactable = interactable;
        joinRoomButton.interactable = interactable;
    }
}
```

## Step 2: Create the MultiplayerStartScene Scene
- **Description**: Set up a new scene `Assets/Scenes/MultiplayerStartScene.unity` with the following hierarchy:
  - **Main Camera**: Standard camera with solid background color or stylized view.
  - **Directional Light**: Base light.
  - **NetworkManager GameObject**: Added with `NetworkManager` and `UnityTransport` components. Configure the `PlayerPrefab` to use `Assets/Prefabs/Player.prefab` to allow successful network spawning.
  - **UI Canvas**:
    - **Main Panel**:
      - Status text (TextMeshPro).
      - "Create Room" Button (uGUI).
      - Join Room Input Field (TMP_InputField).
      - "Join Room" Button (uGUI).
    - **Lobby Panel**:
      - Lobby Code text display (TextMeshPro).
      - "Copy Code" Button (uGUI).
      - Player Slots parent with 4 TextMeshProUGUI text fields.
      - "Leave Room" Button (uGUI).
  - Add the `MultiplayerRoomManager` component to a GameObject in the scene and hook up all inspector properties.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

## Step 3: Scene Registration and Configuration
- **Description**: Add the `Assets/Scenes/MultiplayerStartScene.unity` to Unity's Editor Build Settings as index 0 (so the game always starts in the multiplayer room setup scene).
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: No


# Verification & Testing

To thoroughly test the implementation:
1. **Local Test (Multiple Play Mode Instances)**:
   - Use the **Unity Multiplayer Play Mode** package (already in `manifest.json`) or create a Standalone Build.
   - Run Instance 1 as the Host: Click **Create Room**. Confirm that the status text transitions from initializing to authenticating, then generates a room code, displays it, and places the host in Slot 1 ("Host [ID: 0] (You)").
   - Copy the room code.
   - Run Instance 2 as the Client: Paste the room code into the Input Field and click **Join Room**. Verify that it joins, shifts to the Lobby Panel, and displays both Slot 1 ("Host [ID: 0]") and Slot 2 ("Player 2 [ID: 1] (You)") in green/white text in real-time.
2. **Cap and Bound Checks**:
   - Verify that trying to join with a non-existent or empty room code displays a clear error status without breaking.
   - Verify that clicking "Leave Room" successfully shuts down Netcode, resets the UI panels, and allows the player to host or join another room immediately.
