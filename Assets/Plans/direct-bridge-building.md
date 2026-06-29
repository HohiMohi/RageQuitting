# Project Overview
- Game Title: RageQuitting (Goblin Bridge Builders)
- High-Level Concept: A cooperative multiplayer logistics and construction game where players harvest resources, process them into components, and assemble bridges under pressure.
- Players: Multiplayer (NGO) or single-player cooperative simulation.
- Inspiration / Reference Games: Overcooked, Tools Up!, Moving Out.
- Tone / Art Direction: Playful cartoonish stylized 3D.
- Target Platform: PC (Standalone Windows 64-bit).
- Screen Orientation / Resolution: Landscape 1920x1080.
- Render Pipeline: URP (StarterAssetsURPAsset).

# Game Mechanics
## Core Gameplay Loop
1. **Harvesting**: Goblins harvest raw resources (Wood, Stone, Ore) using tools.
2. **Processing**: Players bring raw resources to industrial stations (Carpenter Table, Blast Furnace) to manufacture high-quality building components.
3. **Transport**: Carrying raw or processed components directly to the bridge construction site.
4. **Direct Placement**: Directly matching components to active "ghost" visualizers on the bridge rather than delivering to central storage.
5. **Final Construction**: Performing manual finishing/tool actions on mounted components to complete construction.

## Controls and Input Methods
- **Keyboard / Mouse (StarterAssets First Person Controller)**:
  - WASD for movement.
  - Mouse look to aim/raycast.
  - Left Mouse Click (or Action key) to use tools.
  - Interaction Key (E) to pick up, drop, or directly interact with ghosts.

# UI
- **Required UI Changes**:
  - The UI showing "needed equippable items" should correctly initialize on the first frame if the component already has its `BridgeComponentSO` assigned, eliminating the race condition.

# Key Asset & Context
1. **`Assets/Scripts/NewScripts/BridgeComponent.cs`**:
   - Manages mounting state, collision state, translucent/opaque visuals, and assembling progress.
2. **`Assets/Scripts/NewScripts/PlayerInteractionNew.cs`**:
   - Exposes a public getter `GetPickedUpGameObject()` to check the item currently held by the player.
3. **`Assets/Scripts/NewScripts/GameplayManager.cs`**:
   - Central state tracker for bridge building stages.
4. **`Assets/Materials/TransparentBlue_Mat.mat`** (to be created):
   - A semi-transparent URP material (using `Universal Render Pipeline/Lit` shader, Transparent surface type, and low alpha color) to render the "ghost" placeholder visuals.

# Implementation Steps

## Step 1: Update PlayerInteractionNew
- **Description**: Add a public getter or property `GetPickedUpGameObject()` to `PlayerInteractionNew.cs` so that other interactable elements (like the bridge components) can safely query what object the player is carrying.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

## Step 2: Create URP Translucent Material
- **Description**: Create a dedicated URP translucent material `TransparentBlue_Mat.mat` under `Assets/Materials/` with:
  - Shader: `Universal Render Pipeline/Lit`
  - Surface Type: `Transparent`
  - Blending Mode: `Alpha`
  - Base Color: Semi-transparent cyan/blue (e.g., RGBA 0.0, 0.6, 1.0, 0.4)
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

## Step 3: Implement Ghost Visual and Collision Settings in BridgeComponent
- **Description**: Modify `BridgeComponent.cs` to support the direct-to-bridge assembly flow:
  - Add `[SerializeField] private Material ghostMaterial;` to allow assigning the transparent material.
  - In `Awake()`, find all `Collider` components in `readyForMountingVisualsGameObject` and set `isTrigger = true` so they do not block players.
  - In `Start()` or `Awake()`, if `readyForMountingVisualsGameObject` is active and `ghostMaterial` is assigned, apply it to all child renderers.
  - Modify `Interact(Transform interactor)`:
    - Get the player's `PlayerInteractionNew` component.
    - Check if the player is holding a `MountableBridgeComponent` matching the required `bridgeComponentSO` of the world object.
    - If there is a match:
      - Call `playerInteraction.RemovePickedUpObject()` to consume the item from the player's hands.
      - Proceed with mounting (turn off translucent ghost, turn on opaque visual, set `isMounted = true`, fire `ComponentMounted` event).
      - If assembly is not needed, set `isAssembled = true` and fire `ComponentAssembled` event.
    - If there is no match or the player is holding the wrong item, log or do nothing.
  - Modify `LookedAt(Transform interactor)` to only trigger highlights/hovers if the player is holding the proper component.
- **Assigned role**: developer
- **Dependencies**: Step 1, Step 2
- **Parallelizable**: No

## Step 4: Robust UI Synchronization
- **Description**: Modify `BridgeComponentNeededEquippableItemUI.cs` in `Start()`:
  - Check `if (bridgeComponent.GetBridgeComponentSO() != null)`, and if so, call `PrepareUI()` immediately to prevent race conditions during scene startup where the SO is pre-assigned in the inspector.
- **Assigned role**: developer
- **Dependencies**: Step 3
- **Parallelizable**: Yes

## Step 5: Update GameplayManager stage activation logic
- **Description**: Modify `GameplayManager.UpdateComponentsCanBeMountedProperty()`:
  - Automatically set `bridgeComponentDataArray[componentIndex].CanBeMounted = true` for active stage components at the start of each stage.
- **Assigned role**: developer
- **Dependencies**: Step 3
- **Parallelizable**: Yes

## Step 6: Configure Scene & Prefab Data
- **Description**:
  - Assign the new `TransparentBlue_Mat.mat` to the `ghostMaterial` field on `BridgeComponent` prefabs or instances.
  - In `FPP_scene.unity` and `BridgeTesting.unity`, pre-assign `bridgeComponentSO` fields on both `GameplayManager`'s `bridgeComponentDataArray` and the individual `BridgeComponent` objects:
    - Component ID 0: Assign `WoodenBasicSupport` SO.
    - Component ID 1: Assign `WoodenBasicSupport` SO.
    - Component ID 2: Assign `WoodenBasicRoadway` SO.
- **Assigned role**: developer
- **Dependencies**: Step 5
- **Parallelizable**: No

# Verification & Testing
1. **Walk-Through Test**: Launch `FPP_scene` in Play Mode. Ensure the active stage components are rendered as translucent cyan "ghosts". Verify the player can walk through them without collision.
2. **Interaction Restriction Test**: Try interacting (pressing E) on the ghosts with empty hands or holding a raw resource. Ensure they do not mount or trigger assembly.
3. **Correct Placement Test**: Harvest Wood, make a Support component at the Carpenter Table, pick it up, and interact with the Support ghost. Verify the support component is consumed from the player's hands, the translucent model is replaced with the fully opaque wooden support mesh, and the player cannot walk through the opaque mesh.
4. **Stage Transition Test**: Mount and assemble both Support columns. Verify that the Roadway ghost automatically appears on top of them (advancing to the next stage).
