# Project Technical Overview: RageQuitting (Goblin Bridge Builders)

## 1. Project Description
**RageQuitting** (working title: "Goblin Bridge Builders") is a multiplayer cooperative logistics and construction game. Players take on the role of goblins tasked with gathering raw resources, processing them into construction materials at various factories, and assembling complex bridge structures. The core experience centers on team coordination, efficient resource management, and a multi-stage construction loop involving physical gathering, industrial processing, and manual assembly using specialized tools.

## 2. Gameplay Flow / User Loop
1.  **Preparation**: Players join through the main menu (utilizing Netcode for GameObjects) and enter a gameplay scene (e.g., `FPP_scene`).
2.  **Gathering**: Players use tools (Axe, Pickaxe) to harvest raw resources from `BaseResourceSource` objects.
3.  **Logistics**: Harvested items are physically carried or stored in `BaseStorageNew`. Larger items may impose movement penalties.
4.  **Processing**: Resources are brought to factories (e.g., `BlastFurnaceFactory`, `CarpenterTableFactory`) to produce `BridgeComponentSO` parts. This often involves minigames (`IMinigame`).
5.  **Construction**: Produced components are delivered to the `MainStorageNew`, which then makes them available for mounting on the `Bridge`.
6.  **Assembly**: Players mount components onto the bridge and use tools to "assemble" them (progress-based action) until the bridge stage is complete.
7.  **Progression**: Completing a bridge stage unlocks the next set of components until the entire structure is finished.

## 3. Architecture
The project follows a **Decoupled Event-Driven Architecture** combined with a **Singleton Manager** pattern for global state. Communication between the physical world, the player, and the game state is handled primarily through C# Events and Interfaces.

-   **Manager Pattern**: `GameplayManager` and `BridgeBuildingManager` act as central authorities for game state and construction progress.
-   **Interface-Based Interaction**: Interaction is abstracted through `IInteractableNew`, `IPickableNew`, and `IDamageable`, allowing the player to interact with diverse objects without tight coupling.
-   **Data-Driven Logic**: Most item properties and construction requirements are defined in `ScriptableObjects`.
-   **State Synchronization**: Uses Unity **Netcode for GameObjects (NGO)** for multiplayer state sync (evident in `NetworkManagement` and `NGO_Minimal_Setup`).

`Location: Assets/Scripts/NewScripts`

## 4. Game Systems & Domain Concepts

### Construction System
A multi-stage system that governs the physical building of structures.
-   `Bridge`: The container for all `BridgeComponent` objects in a scene.
-   `BridgeBuildingManager`: Manages the sequence of construction stages and tracks which components are ready for mounting.
-   `BridgeComponent`: A physical part of the bridge that can be in states: Unmounted, Ready, Mounted, or Assembled.
-   `BridgeComponentSO`: Data container defining the requirements and type for a specific component.

`Location: Assets/Scripts/NewScripts`

### Resource & Factory System
Handles the transformation of raw materials into bridge parts.
-   `BaseResourceSource`: World objects that provide raw resources when "damaged" by tools.
-   `BaseFactory`: Base class for production buildings; handles resource requirements and spawning produced items.
-   `BaseStorageNew` / `MainStorageNew`: Systems for storing and retrieving resources or finished components.
-   `IMinigame`: Interface for factory-specific tasks (e.g., `BlastFurnaceMinigame`).

`Location: Assets/Scripts/NewScripts`

### Player Interaction & Action System
Manages how the player interacts with the world and uses tools.
-   `PlayerInteractionNew`: Handles raycasting for `IInteractableNew` and the physical picking up/dropping of objects.
-   `PlayerActionController`: Manages tool usage (Axe/Pickaxe) by detecting `IDamageable` targets and applying "damage" (work progress).
-   `PlayerInventory`: Tracks equippable items (tools) and triggers UI updates.
-   `PlayerInputNew`: Wraps the Unity Input System and broadcasts events to other player components.

`Location: Assets/Scripts/NewScripts`

## 5. Scene Overview
-   **JoinMenuTest**: The entry point for multiplayer connection and hosting.
-   **FPP_scene**: The primary gameplay scene containing the bridge site, factories, and resource nodes.
-   **Tutorial_01**: Introductory scene for teaching mechanics.
-   **SampleScene / TestScenes**: Sandbox environments for testing building mechanics and grid systems.

`Location: Assets/Scenes`

## 6. UI System
The project uses a mix of **UGUI** and **UI Toolkit**.
-   `NetcodeUI`: Handles the multiplayer connection interface.
-   `FactoryInteractionUI`: A world-space or overlay UI for selecting items to produce at factories.
-   `PlayerInventoryUI`: Displays currently held tools and items.
-   `GameTimerManager`: Tracks and displays session time.

`Location: Assets/Scripts/NewScripts/UI` and `Assets/UI Toolkit`

## 7. Asset & Data Model
-   **ScriptableObjects**: 
    -   `EquippableItemSO`: Defines tool stats (damage, range, cooldown).
    -   `BridgeComponentSO`: Defines construction requirements.
    -   `BaseResourceSO`: Defines raw material types.
-   **Prefabs**: 
    -   `PlayerNew`: The main player character.
    -   `BridgeComponents`: Prefabs for the various parts of the bridge.
    -   `Resources`: Prefabs for logs, stones, etc.
-   **Naming Convention**: Follows a `Base[System]New` naming pattern for many core scripts, suggesting a refactor from an older system.

`Location: Assets/ScriptableObjectAssets/New` and `Assets/Prefabs/New`

## 8. Notes, Caveats & Gotchas
-   **Interaction Layering**: The `PlayerInteractionNew` uses `Physics.RaycastAll`. If multiple interactables are overlapping, the script iterates through them; ensure proper collider placement to avoid unintended interactions.
-   **Movement Penalty**: Picking up heavy objects applies a multiplier to movement speed. If the `minAmountOfPlayersNeeded` is higher than the current carriers, the penalty scales.
-   **IDamageable for Work**: In this project, `IDamageable` is used not just for health, but for **work progress** on resource nodes and bridge assembly. 
-   **Singleton Dependency**: `GameplayManager.Instance` and `BridgeBuildingManager.Instance` are heavily relied upon; ensure these are present in any test scene.
-   **Assembly Requirement**: Some bridge components require "Assembling" (hitting with a tool) after being "Mounted" (interacted with by hand). Check the `needAssembling` flag in `BridgeComponentSO`.