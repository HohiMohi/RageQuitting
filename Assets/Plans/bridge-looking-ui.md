# Project Overview
- Game Title: RageQuitting (Goblin Bridge Builders)
- High-Level Concept: A cooperative multiplayer logistics and construction game where players harvest resources, process them into components, and assemble bridges under pressure.
- Players: Multiplayer (NGO) or single-player cooperative simulation.
- Tone / Art Direction: Playful cartoonish stylized 3D.
- Target Platform: PC (Standalone Windows 64-bit).
- Screen Orientation / Resolution: Landscape 1920x1080.
- Render Pipeline: URP (StarterAssetsURPAsset).

# Game Mechanics
## Core Gameplay Loop
1. Goblins harvest raw resources.
2. Manufacture high-quality building components at industrial stations.
3. Bring components directly to active "ghost" visualizers on the bridge.
4. Mount and assemble components to progress to the next construction stage.

## UI / UX Goal
Provide instant player feedback when looking at bridge slots/blueprints. When a player aims their crosshair/camera at a bridge component that is ready for mounting or assembling in the current stage, a clean screen-space HUD overlay should show its name and the required action (e.g., "Mount WoodenBasicSupport" or "Assemble WoodenBasicSupport").

# UI Layout Design
- **GameObject Name**: `LookingAtComponentUI` under the `UI` canvas in the `PlayerNew` prefab.
- **Position**: Center-bottom of the screen, below the center of the viewport, styled cleanly.
- **Components**:
  - Background Panel: A horizontal panel with a subtle dark semi-transparent tint (using the default UI background sprite or similar) to ensure high readability against any background.
  - Text: A `TextMeshProUGUI` component to display the component's name and required action in a highly legible font style.

# Key Assets & Context
1. **`Assets/Scripts/NewScripts/PlayerInteractionNew.cs`**:
   - Exposes a public getter `GetCurrentInteractable()` returning `_currentInteractable`.
2. **`Assets/Scripts/NewScripts/BridgeComponent.cs`**:
   - Exposes public properties `IsMounted`, `CanBeMounted`, `IsAssembled`, and `NeedAssembling` to determine current slot status.
3. **`Assets/Scripts/NewScripts/UI/LookingAtComponentUI.cs`** (to be created):
   - Handles querying the current interactable, determining if it is a relevant bridge component, and updating the Text/Visuals accordingly.

# Implementation Steps

## Step 1: Add GetCurrentInteractable to PlayerInteractionNew
- **Description**: Add a public getter or property `GetCurrentInteractable()` to `PlayerInteractionNew.cs` so that the HUD UI script can safely inspect what interactable object the player is targeting.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

## Step 2: Expose status properties on BridgeComponent
- **Description**: Expose the following public properties on `BridgeComponent.cs` to allow the HUD UI script to check if the component is active in the current stage and what action it needs:
  - `public bool IsMounted => isMounted;`
  - `public bool CanBeMounted => canBeMounted;`
  - `public bool IsAssembled => isAssembled;`
  - `public bool NeedAssembling => needAssembling;`
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

## Step 3: Create the LookingAtComponentUI script
- **Description**: Create `Assets/Scripts/NewScripts/UI/LookingAtComponentUI.cs`. This script will:
  - Reference the target Text component and root visual object.
  - In `Update()`, query the player's current interactable.
  - If it is a `BridgeComponent` that is ready to mount (`CanBeMounted && !IsMounted`) or ready to assemble (`IsMounted && !IsAssembled && NeedAssembling`), update the text to show the action + component name (e.g., `"Mount " + so.componentName` or `"Assemble " + so.componentName`) and show the UI root.
  - Otherwise, hide the UI root.
- **Assigned role**: developer
- **Dependencies**: Step 1, Step 2
- **Parallelizable**: No

## Step 4: Configure Prefab Hierarchy and UI Setup
- **Description**: Execute a C# script to load the `PlayerNew.prefab`, instantiate the `LookingAtComponentUI` GameObject structure under the `UI` canvas, configure Text and Panel alignments, attach the `LookingAtComponentUI` script, link the references, and save the prefab asset.
- **Assigned role**: developer
- **Dependencies**: Step 3
- **Parallelizable**: No

# Verification & Testing
1. **No-Look Test**: Run `FPP_scene` in play mode. Ensure no hover prompt UI is visible on screen by default.
2. **Mount Ghost Hover Test**: Look at an active, unmounted bridge component ghost (e.g., Support Column 0). Verify that the UI fades/pops in and displays "Mount WoodenBasicSupport".
3. **Assemble Hover Test**: After mounting the Support, look at it again. Since it requires assembly (hitting with a tool), verify that the UI updates to show "Assemble WoodenBasicSupport".
4. **Completed Component Hover Test**: Once the component is fully assembled, look at it. Verify that the UI disappears because it no longer requires mounting or assembling.
5. **Stage Transition UI Update**: Mount and assemble the Support columns to unlock the Roadway stage. Look at the Roadway ghost and verify that the UI displays "Mount WoodenBasicRoadway".
