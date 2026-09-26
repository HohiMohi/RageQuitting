# Project Overview
- **Game Title**: RageQuitting (Goblin Bridge Builders)
- **High-Level Concept**: Multiplayer cooperative logistics and construction game where players build bridges.
- **Players**: Multiplayer (Netcode for GameObjects).
- **Tone / Art Direction**: Cooperative/Chaotic.
- **Target Platform**: PC (StandaloneWindows64).
- **Render Pipeline**: URP.

# Game Mechanics
## Core Gameplay Loop
Players gather raw resources, process them in factories to create bridge components, transport these components to the bridge site, and assemble them. Stamina is a key mechanic for sprinting and carrying heavy items.
## Controls and Input Methods
- **WASD**: Movement
- **Shift**: Sprint (consumes stamina)
- **E / Left Click**: Interact/Action
- **Q**: Drop Item

# UI
- **Stamina Bar**: Displays `_currentStamina / MaxStamina`. Currently broken because `MaxStamina` is 0.

# Key Asset & Context
- `Assets/Prefabs/PlayerNew.prefab`: The primary player prefab.
- `Assets/StarterAssets/FirstPersonController/Prefabs/PlayerCapsule.prefab`: Secondary/Default player prefab.
- `Assets/StarterAssets/FirstPersonController/Scripts/FirstPersonController.cs`: The script managing movement and stamina.

# Implementation Steps
1. **Configure Stamina in Player Prefabs**:
   - Update `Assets/Prefabs/PlayerNew.prefab`: Set `MaxStamina` to 5.0 and `StaminaRegenerationTimeout` to 2.0.
   - Update `Assets/StarterAssets/FirstPersonController/Prefabs/PlayerCapsule.prefab`: Set `MaxStamina` to 5.0 and `StaminaRegenerationTimeout` to 2.0.
   - **Role**: developer
   - **Dependencies**: None

2. **Fix Typo and Log Message**:
   - In `Assets/StarterAssets/FirstPersonController/Scripts/FirstPersonController.cs`, change the log message "You have been crashed by Holded Item." to "You have been crushed by Holded Item." to match the user's report and common terminology for weight.
   - **Role**: developer
   - **Dependencies**: None

3. **Verify Stamina Recovery Logic**:
   - Ensure `_currentStamina` is initialized to `MaxStamina` in `Start()`. (Already exists in code, but good to verify after prefab changes).
   - **Role**: explorer
   - **Dependencies**: Step 1

# Verification & Testing
- **Sprint Test**: Start the game (local or multiplayer), hold Shift while moving. Verify the player speed increases and the stamina bar (if visible) decreases.
- **Carrying Test**: Pick up an item with a movement penalty. Verify the player does not immediately get the "crushed" message. Verify stamina decreases over time.
- **Regeneration Test**: Stop sprinting/carrying and wait 2 seconds. Verify stamina starts to regenerate.
- **Multiplayer Sync**: Verify that these changes work for both the Host and Clients. (Since stamina is managed locally per player, this should be naturally handled by the prefab update).
