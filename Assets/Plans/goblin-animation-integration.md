# Project Overview

- **Game Title**: RageQuitting (Goblin Bridge Builders)
- **High-Level Concept**: A cooperative multiplayer game where goblins collect resources, process them in factories, and assemble bridge structures under time and coordination pressure.
- **Players**: Multiplayer cooperative (using Unity Netcode for GameObjects)
- **Inspiration / Reference Games**: Overcooked, Valheim, Poly Bridge
- **Tone / Art Direction**: Stylized, humorous, low-poly, featuring goblin characters
- **Target Platform**: PC (StandaloneWindows64)
- **Screen Orientation / Resolution**: Landscape (1920x1080)
- **Render Pipeline**: Universal Render Pipeline (URP)

---

# Game Mechanics

## Core Gameplay Loop
Players gather materials (logs, stone, coal), refine them at production tables (Carpenter Table, Blast Furnace), transport finished components, and use tools to assemble bridge segments. Efficient communication and task delegation are required to optimize resource pathways and speed up bridge assembly.

## Controls and Input Methods
The active input system supports both the Legacy Input Manager and the New Input System. Movement is handled via standard WASD and mouse look, with action-driven inputs for picking up items, using gathering tools (Axe, Pickaxe), and building bridge components.

---

# UI
Not directly applicable to the animation integration task, but we will ensure that any character animations sync nicely with visual feedback systems such as movement, carrying, and tool action states.

---

# Key Asset & Context

- **Target Model**: `Assets/Meshes/Goblin_AI2_Assets/selected.fbx`
  - A humanoid-shaped skeleton with standard biped bone names (`Hip`, `Spine`, `L_Thigh`, `R_Thigh`, `Head`, etc.).
  - Currently imported with Generic Animation Type and No Avatar.
- **Existing Assets**:
  - `Assets/Meshes/Goblin_AI2_Assets/Running.anim`: An existing loopable humanoid animation clip.
  - `Assets/Meshes/Goblin_AI2.prefab`: A base prefab containing the mesh renderer and armature, but lacking an Animator.
- **Target Output Directory**: `Assets/Characters/GoblinAnimationSetup`
- **Output Assets**:
  - `GoblinAvatar.asset`: Humanoid Avatar created from the imported FBX.
  - `GoblinAnimatorController.controller`: Animator Controller containing Idle, Running, and Sprinting locomotion states.
  - `Goblin_AI2_Animated.prefab`: An updated prefab containing the Mesh, Animator, Avatar, and Animator Controller, ready for placement in scenes or player/NPC controllers.

---

# Implementation Steps

### Step 1: Model Import Configuration (Convert to Humanoid)
- **Description**: Configure the FBX ModelImporter for `Assets/Meshes/Goblin_AI2_Assets/selected.fbx` to use `AnimationType = Humanoid` and generate its own Avatar. This makes the model compatible with humanoid animations.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: No

### Step 2: Source and Prepare Animation Clips
- **Description**: Sourcing/generation of the animation clips:
  - **Idle**: Sourced from existing project humanoid clips (like `Stand--Idle.anim.fbx`) or generated using `GenerateHumanoidAnimation` (prompt: "A humanoid character standing in a neutral idle pose, breathing slightly").
  - **Running**: Sourced from `Assets/Meshes/Goblin_AI2_Assets/Running.anim` (which is already configured as humanoid) or standard `Locomotion--Run_N`.
  - **Sprinting**: Generated using `GenerateHumanoidAnimation` (prompt: "A fast humanoid sprinting forward, aggressive posture, loopable") or mapped from standard third-person sprint assets.
  - Ensure all locomotion clips have `loopTime = true` configured.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: Yes (with other asset setup)

### Step 3: Create Animator Controller & Locomotion Setup
- **Description**: Create `GoblinAnimatorController.controller` in the output folder. Add a float parameter `Speed`. We will set up a **1D Locomotion Blend Tree** which is the standard, cleanest approach in Unity for smooth transitions between Idle, Running, and Sprinting based on `Speed` value (e.g. 0.0 = Idle, 2.0 = Running, 5.0 = Sprinting).
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: No

### Step 4: Assemble and Save Prefab
- **Description**: Create a prefab wrapper `Goblin_AI2_Animated.prefab`. Instantiate the model, add an `Animator` component, assign the generated Humanoid Avatar and the new Animator Controller. Configure `CharacterController` bounds if this is to be used as a playable prefab.
- **Assigned role**: developer
- **Dependencies**: Step 3
- **Parallelizable**: No

### Step 5: Verification & Testing in Sandbox Scene
- **Description**: Instantiate the animated prefab in a test scene and write a lightweight tester script or play a preview in the Animator window to verify that changing the `Speed` parameter smoothly blends from Idle to Running and Sprinting.
- **Assigned role**: explorer
- **Dependencies**: Step 4
- **Parallelizable**: No

---

# Verification & Testing

1. **Avatar Validation**: Use a C# script to load the generated `GoblinAvatar` and verify `avatar.isValid == true` and `avatar.isHuman == true`.
2. **Animation Loop Settings**: Verify that `Idle`, `Running`, and `Sprinting` clips are configured with `loopTime = true`.
3. **Animator Controller States**: Verify the Animator Controller contains a `Speed` float parameter and a Blend Tree incorporating all three states.
4. **Console Log Check**: Ensure no errors or warnings are thrown regarding bone mapping or clip incompatibility during import or controller playback.
