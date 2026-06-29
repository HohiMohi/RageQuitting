# Project Overview
- **Game Title**: Goblin Bridge Builders
- **High-Level Concept**: Multi-stage bridge construction co-op.
- **Task**: Create a dedicated test scene for bridge building mechanics.

# Game Mechanics
- **Core Gameplay Loop**: Harvest resources -> Craft components at workshops -> Deliver to Main Storage -> Mount & Assemble on Bridge.
- **Controls**: Standard FPP controls (WASD, Mouse, E to interact, Left-click to work/hit).

# UI
- **Timer HUD**: Displays remaining time and state.
- **Inventory/Stamina**: Existing player HUD.

# Key Asset & Context
- **Prefabs**:
  - `Assets/Prefabs/PlayerNew.prefab`: The player.
  - `Assets/Prefabs/New/Bridge/Bridge_prefab.prefab`: The bridge site.
  - `Assets/Prefabs/New/Resources/Factories/CarpenterTable.prefab` & `BlastFurnace_prefab.prefab`: Crafting stations.
  - `Assets/Prefabs/New/Resources/WoodResourceSource.prefab`: Wood source.
  - `Assets/Prefabs/New/Resources/BridgeConstructionMainStorage.prefab`: Main delivery point.
  - `Assets/Prefabs/New/EquippableItems/Axe.prefab` & `Pickaxe.prefab`: Tools.
- **Scripts**:
  - `GameplayManager.cs` & `BridgeBuildingManager.cs`: Core managers.
  - `GameTimerManager.cs`: Timer logic.

# Implementation Steps
1. **Create Scene**: Create `Assets/Scenes/BridgeTesting.unity`.
   - **Assigned role**: developer
2. **Setup Environment**: Add Plane floor, Lighting, and NavMeshSurface.
   - **Assigned role**: developer
3. **Instantiate Managers**: Place `GameplayManager`, `BridgeBuildingManager`, and `GameTimerManager`.
   - **Assigned role**: developer
4. **Place Gameplay Elements**:
   - Instantiate `PlayerNew` at (0, 1, 0).
   - Instantiate `Bridge_prefab` at a reasonable distance (e.g., 20m out).
   - Place `BridgeConstructionMainStorage` near the bridge.
   - Place `CarpenterTable` and `BlastFurnace` in a "workshop area" near the player.
   - Place `WoodResourceSource` and `CoalSource_prefab` near the workshop.
   - Drop an `Axe` and `Pickaxe` near the player's start.
   - **Assigned role**: developer
5. **Configure Managers**:
   - Assign the `Bridge` reference to `BridgeBuildingManager`.
   - Setup initial stages in `BridgeBuildingManager` if possible (requires looking at the SOs used for stages).
   - **Assigned role**: developer
6. **Setup UI**: Ensure a Canvas exists with `GameTimerUI`, `PlayerInventoryUI`, and `PlayerStaminaUI`.
   - **Assigned role**: developer
7. **Bake NavMesh**: Build the NavMesh for NPCs.
   - **Assigned role**: developer

# Verification & Testing
- Start scene: Player should move and interact.
- Time should tick down.
- Tools can be picked up and used on wood/rocks.
- Components can be "brought" to storage and then mounted on the bridge.
