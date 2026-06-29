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
Extend the `LookingAtComponentUI` screen-space HUD overlay. When looking at a mounted component that requires assembling, a circular progress bar (radial fill) will visualize the current assembly completion progress (0-100%).

# UI Layout Design
- **GameObject Name**: `ProgressCircleHolder` under `LookingAtComponentUI/Background` in the `PlayerNew` prefab.
- **Position**: Placed on the right side of the horizontal bar, or inside the panel.
- **Components**:
  - `ProgressBarBackground` (Image): A circular ring/track image with low opacity.
  - `ProgressBarFill` (Image): A circular image with `Image.Type = Filled`, `FillMethod = Radial360`, and `FillOrigin = Top`, using a green or yellow color to represent progress.

# Key Assets & Context
1. **`Assets/Scripts/NewScripts/BridgeComponent.cs`**:
   - Add a public method `GetAssemblingProgressNormalized()` that returns `currentAssemblingProgress / assemblingProgressNeeded`.
2. **`Assets/Scripts/NewScripts/UI/LookingAtComponentUI.cs`**:
   - Reference `UnityEngine.UI.Image assemblingProgressBar` and `GameObject progressCircleHolder`.
   - Update the fill amount in `Update()` whenever `isReadyToAssemble` is true. Show the progress holder during assembly, and hide it during mounting.

# Implementation Steps

## Step 1: Add GetAssemblingProgressNormalized to BridgeComponent.cs
- **Description**: Expose normalized assembly progress from `BridgeComponent.cs`.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

## Step 2: Update LookingAtComponentUI.cs with Progress Bar Logic
- **Description**: Modify `LookingAtComponentUI.cs` to manage the circle progress bar state and fill amount.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

## Step 3: Create UI Circle progress bar hierarchy inside PlayerNew.prefab
- **Description**: Add the circular progress bar image elements under the `LookingAtComponentUI` inside the `PlayerNew` prefab, and link them to the script references.
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: No

# Verification & Testing
1. **Initial Mount Test**: Verify that when looking at an unmounted ghost, the circular progress bar is hidden (only the text "Mount WoodenBasicSupport" is shown).
2. **Assembly Hover Test**: After mounting, look at the component. Verify that the circular progress bar appears and is empty (0%).
3. **Assembly Progress Test**: Hit the support column with the Hammer/Pickaxe. Verify that with each hit, the circle progress bar fills up radially.
4. **Completion Test**: Verify that when assembly is complete (100%), the HUD UI hides.
