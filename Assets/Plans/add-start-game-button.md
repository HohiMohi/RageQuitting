# Project Overview
- **Game Title**: RageQuitting (Goblin Bridge Builders)
- **High-Level Concept**: Multiplayer cooperative logistics and construction game where players build bridges.
- **Players**: Multiplayer (NGO)
- **Target Platform**: Standalone Windows
- **Render Pipeline**: URP

# Game Mechanics
## Core Gameplay Loop
The player starts in a lobby, creates or joins a room, and then transitions to the main gameplay scene (`FPP_scene`) to start the bridge-building tasks.

# UI
## Lobby UI
The Lobby Panel in `MultiplayerStartScene` will be updated to include a "Start Game" button. This button will only be visible and functional for the Host after a room is created.

# Key Assets & Context
- **Scene**: `Assets/Scenes/MultiplayerStartScene.unity` - The initial menu scene.
- **Scene**: `Assets/Scenes/FPP_scene.unity` - The target gameplay scene.
- **Script**: `Assets/Scripts/NetworkManagement/MultiplayerRoomManager.cs` - Manages the room lobby and scene transitions.

# Implementation Steps
## 1. Project Configuration
- **Add Scene to Build Settings**: Add `Assets/Scenes/FPP_scene.unity` to the Unity Build Settings to allow scene loading during gameplay.
- **Assigned role**: developer
- **Dependencies**: None

## 2. Script Modification: MultiplayerRoomManager.cs
- **Add Button Reference**: Add `[SerializeField] private Button startGameButton;` to the class.
- **Initialize Listener**: In `Start()`, add `startGameButton.onClick.AddListener(OnStartGameClicked);`.
- **Implement OnStartGameClicked**:
  ```csharp
  private void OnStartGameClicked()
  {
      if (NetworkManager.Singleton.IsServer)
      {
          NetworkManager.Singleton.SceneManager.LoadScene("FPP_scene", UnityEngine.SceneManagement.LoadSceneMode.Single);
      }
  }
  ```
- **Update Visibility**: In `UpdatePlayerSlots()`, set the `startGameButton` visibility:
  ```csharp
  startGameButton.gameObject.SetActive(NetworkManager.Singleton.IsHost);
  ```
- **Update SetupManager**: (Optional but good for consistency) Add the button to the `SetupManager` method parameters and assignment.
- **Assigned role**: developer
- **Dependencies**: None

## 3. UI Implementation: MultiplayerStartScene
- **Create Start Game Button**: 
    - Duplicate `LeaveRoomButton` inside `LobbyPanel` and rename it to `StartGameButton`.
    - Change the text to "START GAME".
    - Adjust position to be above `LeaveRoomButton` (e.g., Y = -150).
- **Assign Button to Manager**: Drag the new button into the `startGameButton` slot on the `MultiplayerRoomManager` component.
- **Assigned role**: developer
- **Dependencies**: Step 2

# Verification & Testing
- **Host Test**: Run the game as a Host. Create a room. Verify "Start Game" button appears. Click it and verify transition to `FPP_scene`.
- **Client Test**: Run another instance as a Client. Join the room. Verify "Start Game" button is NOT visible. Verify transition to `FPP_scene` occurs when the Host clicks the button.
- **Build Test**: Ensure the project builds successfully with the new scene included.
