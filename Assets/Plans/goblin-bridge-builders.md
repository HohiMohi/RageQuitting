# Project Overview
- **Game Title**: Goblin Bridge Builders
- **High-Level Concept**: A chaotic, fast-paced cooperative multiplayer game where a crew of goblin engineers must harvest resources, craft heavy bridge components through intense workshop minigames, and assemble a bridge to cross a gap before the level's timer runs out, all while dealing with cheeky local critters and aggressive pests that try to disrupt their work.
- **Players**: 2-4 players (Peer-to-Peer Online Co-op using Unity Netcode).
- **Inspiration / Reference Games**: *Overcooked* (chaotic task division, tight timers), *Valheim* (first-person gathering/crafting), *Orcs Must Die!* (fending off annoying pests while executing a goal).
- **Tone / Art Direction**: Cozy Low-Poly Fantasy (vibrant colors, cute but mischievous goblins, stylized nature using the existing Polyart Studio assets).
- **Target Platform**: PC (Steam / Peer-to-Peer).
- **Screen Orientation / Resolution**: Landscape (1920x1080).
- **Render Pipeline**: Universal Render Pipeline (URP) with low-poly stylized lighting.

---

# Game Mechanics

## Core Gameplay Loop
1. **Level Setup**: The level starts with a timer (e.g., 5-8 minutes). A large ravine or river separates the goblin crew from the target zone. A blueprint for a bridge is displayed, showing multiple building stages.
2. **Resource Harvesting**: Players use tools (axes, pickaxes) to harvest trees and rocks from `BaseResourceSource` spawners.
3. **Transport & Refining**:
   - Heavy raw resources are carried back to the workshop area. Large pieces slow players down or require two players to carry cooperatively.
   - Raw materials are stored in workshop buffers (e.g., `FurnaceStorage` for iron ore, `CarpenterTableFactory` for wood logs).
4. **Workshop Minigames**:
   - **Blast Furnace**: Players pump bellows and balance furnace heat using mouse/look input delta to smelt metal rivets and plates.
   - **Carpenter Table**: Players play a timing-based precision cutting minigame to carve wooden struts and beams.
5. **Bridge Assembly**:
   - Crafted components are stored in `MainStorageNew`, triggering their "mountable" status in the `BridgeBuildingManager` stage system.
   - Players pick up the heavy, crafted bridge segments (carrying them to the construction site, experiencing weight penalties).
   - Once positioned, players interact to "mount" and then "assemble" (hammer) the bridge piece in place.
6. **NPC Disruptions**:
   - **Thieving Pests (Neutral/Cheeky)**: Steal raw resources left unattended on the ground and run away with them.
   - **Saboteur Critters (Aggressive/Pests)**: Spawn from nearby burrows and actively mess with workshops (e.g., dousing furnace fires, clogging gears, or nibbling on wooden struts). Goblins must slap them with tools/shovels to chase them off.
7. **Win Condition**: Fully assemble all stages of the bridge and cross the ravine before the timer hits zero!

## Controls and Input Methods
- **WASD**: Move around.
- **Mouse / Camera**: Look around.
- **Left-Click (Action)**: Swing tool (axe/pickaxe) to harvest resources, or slap away pests.
- **E (Interact)**: Pick up items, drop items, store items in workshops, or mount bridge components.
- **Minigame Controls**:
  - **Blast Furnace**: Look Mouse-Y (Look Delta) to keep the heat slider inside the target temperature zone while the furnace runs.
  - **Carpenter Table**: Interactive timing bar UI.

---

# UI
- **Main HUD**:
  - **Timer Clock**: Large central progress bar/clock representing time remaining.
  - **Bridge Blueprint Tracker**: Visual checklist showing current stage requirements (e.g., "Need 1x Wood Arch, 2x Iron Support").
- **Workshop Interfaces**:
  - **Blast Furnace UI**: A thermometer slider showing the current heat, perfect zone, and critical failure zone.
  - **Carpenter Table UI**: Precision cutting progress indicator.
- **Player Inventory / Carrying Overlay**:
  - Circular icon overlay indicating current equipped item or item being held above the goblin's head.
  - Cooperative indicator showing if another player is helping lift a heavy object.

---

# Key Asset & Context

### Scripts to Modify or Leverage:
1. `BridgeBuildingManager.cs` & `GameplayManager.cs`: Controls bridge building phases and mounting components.
2. `BlastFurnaceMinigame.cs` & `CarpenterTableMinigame.cs`: Handles crafting minigames.
3. `PlayerInteractionNew.cs`: Manages picking up, dropping, and carrying heavy items (with weight penalties).
4. `BaseResourceSource.cs`: Spawns logs and stones.

### New Scripts & Assets to Create:
1. `Assets/Scripts/NewScripts/NPCs/DisturberNPC.cs`: Handles behavior for cheeky pests and saboteur critters (patrolling, targeting resources, running away, or dousing furnace fires).
2. `Assets/Scripts/NewScripts/NPCs/NPCSpawner.cs`: Spawns pests periodically based on the level timer.
3. `Assets/Scripts/NewScripts/GameTimerManager.cs`: Manages the level timer, victory/defeat transitions, and level flow.
4. `Assets/Scripts/NewScripts/Weapons/DefenderSlapstick.cs`: Simple weapon component or extending `EquippableItem` to allow slapping pests.

---

# Implementation Steps

### Step 1: Complete the Core Level Timer & Flow
- **Description**: Implement a level countdown timer in `GameTimerManager.cs` that triggers a "Victory" event when the bridge is fully assembled (`isFullyAsembled` from `BridgeBuildingManager`), or a "Defeat" event when the timer reaches zero.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

### Step 2: NPC Spawning and AI Behavior (Cheeky Pests)
- **Description**: Implement the `DisturberNPC.cs` using Unity's AI Navigation (NavMeshAgent). Pests will spawn from burrows, detect items lying on the ground, navigate to them, pick them up, and run back to their burrow.
- **Assigned role**: developer
- **Dependencies**: None (Can use placeholder assets)
- **Parallelizable**: Yes

### Step 3: Workshop Sabotage Behaviors
- **Description**: Add behaviors to `DisturberNPC.cs` targeting active workshop stations. If an NPC reaches an active Blast Furnace, they can trigger a douse/extinguish event, resetting furnace progress. Goblins can strike/slap NPCs using the Action input to damage or scare them away, causing them to drop stolen resources.
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: No

### Step 4: Drop-Down Gravity for Pickable Objects
- **Description**: Update `PlayerInteractionNew.cs` drop logic (`DropObject`) so that dropped items are affected by gravity (adding a Rigidbody and restoring collision) instead of floating in the air.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

### Step 5: Integration, Level Design, and Testing
- **Description**: Assemble the elements in `FPP_scene.unity`. Bake the AI Navigation NavMesh, place resource sources, configure workshop stations, place NPC burrows, and set up the bridge assembly stages. Conduct multiplayer gameplay tests over Netcode to verify synchronization of minigames, heavy item penalties, and pest interactions.
- **Assigned role**: developer
- **Dependencies**: Step 1, Step 3, Step 4
- **Parallelizable**: No

---

# Verification & Testing

### Manual Testing
1. **Timer Check**: Start a level, wait for the timer to count down to 0, and confirm the Defeat overlay appears.
2. **Bridge Assembly Victory Check**: Craft and mount all bridge components to complete the bridge, and confirm the Victory overlay is triggered.
3. **Pest Sabotage Test**: Drop a log on the ground, verify that a spawned pest walks to it, picks it up, and runs away. Hit the pest with a tool to verify it drops the log and flees.
4. **Gravity Test**: Pick up a resource log, walk to a ledge, and press drop. Ensure the log falls naturally to the ground.
5. **Multiplayer Sync**: Connect two clients via Netcode, and verify that both players can cooperatively carry a heavy bridge component (reducing the speed penalty multiplier), and see minigame progress updates.
