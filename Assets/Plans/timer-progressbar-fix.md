# Project Overview
- Game Title: RageQuitting (Goblin Bridge Builders)
- High-Level Concept: A cooperative multiplayer logistics and construction game where players harvest resources, process them into components, and assemble bridges under pressure.
- Players: Single-player or cooperative multiplayer.
- Render Pipeline: URP.

# Game Mechanics
## Core Gameplay Loop
1. Goblins harvest raw resources.
2. Manufacture high-quality building components at industrial stations.
3. Bring components directly to active "ghost" visualizers on the bridge.
4. Mount and assemble components to progress to the next construction stage before the level timer expires.

# UI / UX Goal
Fix the game timer progress bar. While the digital timer text correctly counts down the seconds remaining, the horizontal progress bar graphic does not move. This is because Unity UI `Image` components set to `Image.Type = Filled` require a valid `Sprite` to be assigned; if the `Sprite` is `null` (None), the fill amount property is completely ignored by Unity and the bar remains full and static.

# Key Asset & Context
- **`Assets/Prefabs/PlayerNew.prefab`**:
  - `UI/GameTimerUI/ProgressBarBackground`: Needs a valid background panel sprite.
  - `UI/GameTimerUI/ProgressBarBackground/ProgressBarFill`: Needs a valid filled bar sprite (e.g. standard UI sprite) so that `fillAmount` works correctly horizontally.
- **Scenes (`FPP_scene.unity`, `BridgeTesting.unity`)**:
  - The scene instances of `PlayerNew` (or their overrides) must have these sprite assignments synchronized.

# Implementation Steps

## Step 1: Assign Built-in UI Sprites on PlayerNew.prefab
- **Description**: Open the `PlayerNew.prefab` asset, load its contents, retrieve the default built-in UI sprite `"UI/Skin/UISprite.psd"`, and assign it to both `ProgressBarBackground` and `ProgressBarFill` Image components. Ensure the `Image.Type` remains `Filled` for the fill bar and `Simple` or `Sliced` for the background.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

## Step 2: Synchronize Scene Instances of GameTimerUI
- **Description**: Open `FPP_scene.unity` and `BridgeTesting.unity`, locate any `GameTimerUI` instances, and ensure they have the valid UI sprite assigned to `ProgressBarBackground` and `ProgressBarFill` so that the fill animation functions at runtime in all scenes.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

# Verification & Testing
1. **Play Mode Test**: Enter Play Mode on `FPP_scene.unity`.
2. **Visual Progress Verification**: Observe the horizontal green progress bar. Verify that it decreases from right to left smoothly in synchronization with the digital timer.
3. **Color Warning Verification**: Let the timer run down below 60 seconds (warning threshold). Verify that both the digital text and the progress bar change to a vibrant warning red color.
