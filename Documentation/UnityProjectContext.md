# Unity Project Context

<!-- unity-onboarding:generated:start -->

## Project Summary

- Project root: `D:\Programy\UnityProjects\RageQuitting`
- Cooperative first-person multiplayer game about gathering, processing, carrying and assembling bridge components.
- Visual direction: cozy, playful, stylized low-poly fantasy with vibrant colors.
- Primary target: PC / Steam / `StandaloneWindows64`, 60 FPS.
- Last analyzed: `2026-09-11`
- Last analyzed commit: `376452cf54ff3094c2b5f0b856dd96c5920393c0` on `AIPrototype2` (dirty worktree preserved).

## Confirmed Environment

- Unity: `6000.3.18f1`, revision `5ebeb53e4c07` (`ProjectSettings/ProjectVersion.txt`).
- Render pipeline: URP `17.3.0`, linear color space, SDR display output.
- Input: Input System `1.19.0`.
- Networking: Netcode for GameObjects `2.12.0`; first-party gameplay confirms server-authoritative multiplayer.
- Camera: Cinemachine `2.10.7`; FPP gameplay uses a world camera plus a runtime-created arms overlay camera.
- Frame target: `Application.targetFrameRate = 60` in `Assets/Scripts/NewScripts/FrameRateSettings.cs`.

## Important Packages And Frameworks

| Area | Finding | Confidence | Evidence |
|---|---|---|---|
| Rendering | URP and Shader Graph `17.3.0` | Confirmed | `Packages/manifest.json` |
| Legacy post | Post Processing Stack v2 `3.5.4` remains installed; likely unused | Confirmed package / likely unused | Manifest and asset-reference search |
| Multiplayer | NGO `2.12.0`, server-authoritative gameplay | Confirmed | Manifest and `Assets/Scripts` |
| Input/camera | Input System `1.19.0`, Cinemachine `2.10.7` | Confirmed | `Packages/manifest.json` |
| Tests/tooling | Test Framework `1.6.0`, Unity Pipeline `0.5.0-exp.1` | Confirmed | `Packages/manifest.json` |

## Directory Structure

| Path | Purpose | Confidence |
|---|---|---|
| `Assets/Scripts/NewScripts` | Main gameplay runtime code | Confirmed |
| `Assets/Scripts/NetworkManagement` | Session/network flow | Confirmed |
| `Assets/StarterAssets/FirstPersonController` | FPP controller, camera prefab and related settings | Confirmed |
| `Assets/ScriptableObjectAssets/New` | Gameplay profiles and data | Confirmed |
| `Assets/Prefabs/New` | Current gameplay prefabs | Confirmed |
| `Assets/Scenes` | Build and gameplay scenes | Confirmed |
| `Assets/Tests` | EditMode and PlayMode test assemblies | Confirmed |

## Assembly Boundaries

| Assembly | Responsibility | Notes |
|---|---|---|
| `Assembly-CSharp` | Most first-party runtime MonoBehaviours and gameplay | Likely monolithic; no first-party runtime asmdefs found |
| `AgentHarness.EditModeTests` | EditMode validation tests | Confirmed asmdef |
| `AgentHarness.PlayModeTests` | PlayMode validation tests | Confirmed asmdef |

## Scenes And Startup Flow

- Enabled build scenes: `MultiplayerStartScene`, `NGO_Setup`, `MainMenuScene`, `FPP_scene`, `Tutorial_scene`.
- Main flow starts in `MultiplayerStartScene`; the host loads `FPP_scene` or `Tutorial_scene`.
- `NGO_Setup` and `MainMenuScene` are retained entry/test points.

## Architecture

| Pattern | Finding | Confidence |
|---|---|---|
| Runtime composition | MonoBehaviour-centric scene/prefab architecture | Confirmed |
| Data | ScriptableObject profiles/data under `Assets/ScriptableObjectAssets/New` | Confirmed |
| Multiplayer | NGO with server authority for gameplay, AI and shared-carry physics | Confirmed |
| Presentation | Local camera, UI and feedback are not network-synchronized | Confirmed |

## Coding Conventions

- Names and Unity Inspector identifiers are English; project documentation is primarily Polish.
- Gameplay data belongs in ScriptableObjects, shared configuration in prefabs, and level-specific composition in scene overrides.
- New network prefabs require `NetworkObject` and registration in `DefaultNetworkPrefabs`.
- Detailed style conventions were not exhaustively audited during this read-only onboarding.

## Testing And Validation

- EditMode: `Assets/Tests/Editor/AgentHarness.EditModeTests.asmdef`.
- PlayMode: `Assets/Tests/PlayMode/AgentHarness.PlayModeTests.asmdef`.
- Project harness: `Tools/AgentHarness/Invoke-UnityPreflight.ps1`; use strict `Fast` for documentation/tooling and `Gameplay` for runtime or asset changes.
- No Play Mode, tests, builds or preflight runs were performed during this read-only analysis.

## Available Unity Tooling

- Connected Editor tooling was available and read-only inspected: connection/version, build/graphics/quality/lighting settings, scene listing and hierarchy, asset search, console, test discovery, screenshots and C# evaluation.
- Unity CLI and `com.unity.pipeline` are available. Tool names and schemas should be rediscovered before each task.
- No mutating Editor command was used.

## Important Constraints

- Check the live Editor before any scene, prefab or asset edit; drive reachable Editor state through Unity CLI rather than hand-editing YAML.
- Preserve the dirty worktree and unrelated changes.
- FPP post-processing must account for the camera stack: Unity requires the pass on the last camera, and TAA is incompatible with camera stacking.
- Keep multiplayer authority and local-only presentation boundaries intact.

## Unknowns And Confidence

- `Tutorial_scene` has no authored direct or prefab-dependent Light, Volume or ReflectionProbe; deeply runtime-created lighting was not observed in code searches.
- Minimum supported GPU, resolution/quality targets, time-of-day requirements and final lighting art direction need user decisions.
- APV versus legacy Light Probe Groups requires a measured prototype.
- The ownership and freshness of `FPP_scene` lighting data should be audited before a re-bake.

## Source Files Inspected

- `ProjectSettings/ProjectVersion.txt`, `ProjectSettings/GraphicsSettings.asset`, `ProjectSettings/QualitySettings.asset`
- `Packages/manifest.json`
- Build settings and read-only connected-Editor inspection
- Representative files under `Assets/Scripts`, `Assets/Scenes`, `Assets/StarterAssets`, `Assets/Tests` and `Assets/Plans`
- Existing project documentation and `AGENTS.md`

<!-- unity-onboarding:generated:end -->
