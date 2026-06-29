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

## Problem Statement
The current interaction and looking systems use `Physics.RaycastAll` which shoots an infinitely thin, pixel-perfect line from the camera center. This makes aiming at small colliders (such as the pickaxe, raw resource items, or buttons) feel extremely clunky, frustrated, and surgical. Furthermore:
1. `Physics.RaycastAll` returns hit results in an **arbitrary, unsorted order**, meaning a far-away object can sometimes take priority over a closer one.
2. `Physics.RaycastAll` does not respect physical walls/obstacles automatically, meaning players can sometimes see hover UI or interact through solid walls unless properly filtered.

## Proposed Solution
We will upgrade both `HandleInteract` and `CheckLookAtInteractable` in `PlayerInteractionNew.cs` to use a **Sorted, Obstacle-Aware Spherecast**:
1. **Spherecast**: Instead of a pixel-thin line, shoot a sphere with a customizable radius (e.g., `0.25f` meters). This adds a forgiving "thick ray" that easily registers small items under the player's general crosshair area.
2. **Sorting**: Sort the hit array ascendingly by `hit.distance` so the physically closest objects are always processed first.
3. **Obstacle Blocking**: Iterate through the sorted hits. If we encounter a solid, non-trigger obstacle (like a wall, floor, or giant grey block) before an interactable, we stop processing (breaking the loop). This ensures players cannot interact with objects hidden behind solid walls, while still letting them interact through transparent triggers.

# UI
- No changes to UI files are required. This is a physics and detection enhancement.

# Key Asset & Context
- **`Assets/Scripts/NewScripts/PlayerInteractionNew.cs`**:
  - Add serialized field `[SerializeField] private float interactSphereRadius = 0.25f;`.
  - Rewrite `HandleInteract()` and `CheckLookAtInteractable()` to use `Physics.SphereCastAll`, sort by distance, and respect obstacle blocking.

# Implementation Steps

## Step 1: Update PlayerInteractionNew.cs with Spherecast detection
- **Description**: Add `interactSphereRadius` and rewrite both `HandleInteract()` and `CheckLookAtInteractable()` methods to use the sorted, obstacle-aware Spherecast logic.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: No

## Step 2: Configure PlayerNew Prefab Values
- **Description**: Inspect and ensure `interactSphereRadius` is set to `0.25f` on the `PlayerNew` prefab asset.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

# Verification & Testing
1. **Small Item Selection Test**: Look at the Pickaxe on the floor without pointing absolutely pixel-perfectly at its center. Verify that the UI card above it instantly and reliably appears.
2. **Distance Ordering Test**: Look at two close-by objects aligned behind one another. Verify that only the closest interactable is targeted and receives the LookedAt hover effect.
3. **Obstacle Blocking Test**: Look at the Pickaxe behind a solid wall or the giant grey cube. Verify that the Pickaxe UI does NOT appear through the solid geometry.
