# Project Technical Overview: RageQuitting (Goblin Bridge Builders)

## 1. Project Description
**RageQuitting** (Goblin Bridge Builders) is a cooperative first-person multiplayer construction and physics-logistics game developed in Unity (URP, Netcode for GameObjects, Unity Services Relay). Players control worker goblins cooperating in real-time to gather natural and geological resources, process raw elements through heavy industrial minigame machinery (blast furnaces, carpenter saws, concrete mixers), haul structural materials using multi-player physical carry systems and wheelbarrows, and assemble multi-stage modular bridges across hazardous ravines and waterways. The game features systemic world simulation including terrain excavation, concrete pouring and curing loops, rope attachment and sapling bending mechanics, water buoyancy and flow hazards, and hostile wildlife AI (resource-stealing pests, escorting beaver defenders, and charging cliff goats) that sabotage construction sites and carry off downed players.

## 2. Gameplay Flow / User Loop
1. **Multiplayer Room & Lobby**: Players launch into `MultiplayerStartScene` where `MultiplayerRoomManager` interfaces with Unity Services Authentication and Relay. The host creates a 4-player room and generates a join code; clients connect, select target gameplay scenes (`FPP_scene` or `Tutorial_scene`), and synchronize player slots.
2. **Spawn & Setup**: On scene load, `PlayerSpawnManager` and `GameplayManager` resolve spawn points. `PlayerNetworkSetup` activates local camera systems, first-person visual rigs, and input bindings (`PlayerGameInputActions`), while remote clients display third-person animated goblin avatars.
3. **Resource Extraction & Terrain Clearing**: Players equip tools (`EquippableItem` such as Axes and Pickaxes) to clear site obstacles and mine resource nodes (`BaseResourceNew`, `SubstanceExtractionZone`, `LooseSubstancePile`). Excavation sites require digging cycles with shovels and soil removal before foundation work can proceed.
4. **Material Processing & Minigames**: Raw materials (iron ore, coal, logs, clay, limestone) are delivered to factories (`BlastFurnaceFactory`, `CarpenterTableFactory`, `ConcreteMixerController`). Players complete tactile mechanical minigames (`IMinigame`), adjusting dial measurements, pumping bellows, and cranking mixer drums to produce structural components (`BridgeComponentSO`) and wet concrete batches.
5. **Logistics & Multi-Player Hauling**: Finished components and bulk materials are transported to construction zones via single-player pickup, multi-player coordinated physics hauling (`SharedCarryPhysicsBody` with dynamic player load balance and stamina drain), or heavy wheelbarrow transport (`WheelbarrowController`).
6. **Bridge Assembly & Foundation Engineering**: Players mount structural members into designated sockets (`BridgeMountSocket`, `BridgeConstructionSite`). Sub-assembly steps require specific tools: fastening diagonal bracing with rope controllers, tensioning saplings, pouring wet concrete into abutments, and hammering rivets until component assembling thresholds are met.
7. **Threat Mitigation & Player Revive**: Disturber AI (`NPCBrain`) disrupts operations by stealing stored resources, headbutting workers off ledges, and dragging downed teammates to drop zones. Players fight wildlife with equipped tools, revive fallen allies via `PlayerHealth` interaction, or respawn at camp after a cooldown.
8. **Bridge Completion & Session Reset**: `BridgeBuildingManager` tracks stage progression. When all stages (abutments, piers, girders, cross-beams, deck panels, bracing) reach completion, the bridge becomes structurally sound and the level objective is satisfied.

## 3. Architecture
The project is built on an **Event-Driven, Interface-Segregated, Server-Authoritative Architecture** integrated with Unity's **Netcode for GameObjects (NGO)** and ScriptableObject-driven design pipelines.

- **Centralized Manager / Singleton Pattern**: Core authoritative state machines (`GameplayManager`, `BridgeBuildingManager`, `NPCRegistry`, `MultiplayerRoomManager`) inherit from `SingletonMonobehaviour<T>` or implement static singleton accessors to govern session lifetimes, stage validation, and player registries.
- **Interface Segregation Pattern**: Player interaction and tool pipelines decouple strictly through contract interfaces (`IInteractableNew`, `IPickableNew`, `IDamageable`, `ISharedCarryObject`, `ISubstanceSource`, `ISubstanceSink`, `IConcreteBatchReceiver`, `IInteractionPromptProvider`, `IHighlightRendererProvider`, `IExternalImpulseReceiver`).
- **Data-Driven Workflow Pattern**: Bridge building stages, tool capabilities, substance container parameters, NPC behaviors, and physics carry profiles are defined via modular ScriptableObjects (`BridgeConstructionWorkflowSO`, `EquippableActionProfileSO`, `CarryPhysicsProfileSO`, `NPCBehaviorSO`, `WaterBodyProfileSO`).
- **Network Synchronization Model**: Game object state synchronization is managed through `NetworkVariable<T>` properties (such as health, component progress, and furnace temperature), RPC broadcasts for one-shot audio/visual impulses, and custom client/server transform synchronization (`ClientNetworkTransform`, `ServerNetworkTransform`).

`Location: Assets/Scripts/NewScripts`

## 4. Game Systems & Domain Concepts

### Construction & Modular Bridge System
A multi-stage engineering system governing excavation, component placement, fastening, and structural stage transitions across bridge landmarks.
- `Bridge`: Authoritative container tracking all structural bridge components and dispatching mounting/assembly lifecycle events.
- `BridgeBuildingManager`: Central manager coordinating bridge construction stages, checking prerequisite completions, and updating mountable component states from storage.
- `BridgeComponent`: Physical structural unit tracking mount states, required assembly work progress, and attached visual models.
- `BridgeConstructionSite`: Base workflow site managing site clearance, excavation substages, soil hardening timers, and concrete pouring states.
- `BridgeAbutmentConstructionSite`: Specialized foundation site requiring multi-layer soil excavation, reinforcement placement, and bulk concrete pouring.
- `BridgeGirderConstructionSite`: Girder assembly site requiring precise structural alignment and multi-point fastening work.
- `BridgeCrossBeamConstructionSite`: Horizontal beam site connecting primary girders with tool-based locking mechanisms.
- `BridgeDeckPanelConstructionSite`: Surface deck panel site requiring physical placement and tool hammering.
- `BridgeDiagonalBracingConstructionSite`: Lateral bracing site requiring rope anchoring and tensioning.
- `BridgeMountSocket`: Positional spatial socket validating component placement types and orientations.
- `BridgeStageInfoManager`: Aggregator broadcasting bridge construction stage progress to player HUDs and UI displays.
- `BridgeTargetResolver`: Raycast utility identifying relevant bridge components and sockets from player crosshair vectors.
- `FoundationExcavationVolume`: Visual and physical volume component reflecting terrain lowering during digging cycles.

*Design Pattern*: **State Machine & Template Method Pattern**. Sub-construction sites inherit from `BridgeConstructionSite` to override custom workflow steps, validation rules, and completion callbacks while adhering to a shared phase lifecycle.
*How to Extend*: Create a new ScriptableObject inheriting from `BridgeConstructionWorkflowSO` or a new MonoBehaviour inheriting from `BridgeConstructionSite`, define the stage sequence, and link appropriate `BridgeComponentSO` definitions in the Unity Inspector.

`Location: Assets/Scripts/NewScripts`

### Resource, Factory & Substance Processing System
A manufacturing pipeline that handles extraction of raw geological and forestry materials and refines them into structural bridge parts via interactive machines.
- `BaseResourceNew`: World resource node providing harvestable items when struck by appropriate tools.
- `BaseResourceSO`: Data container defining resource identity, harvest tool requirements, hit durability, and drop prefabs.
- `BaseFactory`: Abstract manufacturing building managing resource intake, processing state, production queues, and finished item spawning.
- `BlastFurnaceFactory`: Smelting facility requiring thermal management to process ores into iron ingots and structural steel.
- `BlastFurnaceMinigame`: Interactive minigame regulating furnace temperature within target thresholds using bellows and fuel feeds.
- `Bellows`: Interactive station pumping air into the furnace to elevate internal temperature.
- `FurnaceStorage`: Specialized storage inventory holding fuel and ores adjacent to blast furnaces.
- `CarpenterTableFactory`: Woodworking facility converting timber logs into planks, cross-beams, and shaped deck panels.
- `CarpenterTableMinigame`: Mechanical cutting minigame requiring players to adjust cutting blades to target dimensions.
- `CarpenterDimensionCrank`: Physical interaction crank modifying carpentry cutting parameters.
- `ConcreteMixerController`: Heavy drum mixer combining aggregate, water, and cement into pourable wet concrete batches.
- `ConcreteMixerCrank`: Manual rotary crank used to rotate the mixing drum during concrete preparation.
- `ConcreteMixerModeLever`: Mechanical lever toggling the mixer between loading, mixing, and discharging states.
- `BaseStorageNew`: General storage container for raw resources and equippable tools.
- `MainStorageNew`: Global warehouse receiving processed bridge components and signaling the bridge building manager.
- `LooseSubstancePile`: World entity representing uncontained bulk substances (gravel, sand, dry cement).
- `SubstanceExtractionZone`: Spatial volume allowing players with buckets or shovels to scoop specific raw substances.
- `PortableSubstanceContainer`: Handheld container (bucket/trough) holding liquid or granular substances with pour interactions.

*Design Pattern*: **Factory Method & Strategy Pattern**. `BaseFactory` provides the template production flow, while specific factories delegate minigame mechanics to implementations of `IMinigame`.
*How to Extend*: Inherit from `BaseFactory` and `IMinigame`, implement `ExecuteMinigame()` and recipe validation using `ProductionRecipeSO`, and attach standard `ISubstanceSink` or `ISubstanceSource` interfaces.

`Location: Assets/Scripts/NewScripts`

### Player Interaction, Action & Locomotion System
A first-person character system handling continuous movement, physics hauling, stamina management, and context-sensitive tool operations.
- `PlayerNew`: Core player root marker component.
- `PlayerInputNew`: Wrapper around the Unity New Input System broadcasting typed action events to player subsystems.
- `PlayerInteractionNew`: Raycasting and spherecasting interaction engine resolving `IInteractableNew`, pickups, and multi-actor attachments.
- `PlayerActionController`: Network-authoritative combat and tool action controller executing swings, hit registration, cooldowns, and damage applications.
- `PlayerInventory`: Equippable slot manager tracking held tools and dispatching inventory state updates to HUDs.
- `PlayerHealth`: Health, damage, downing, and revive manager synchronized across the network with regeneration logic.
- `PlayerStaminaController`: Stamina manager regulating sprinting, heavy lifting, wheelbarrow hauling, and tool swinging costs.
- `PlayerTargetHighlightController`: Dynamic shader outline visualizer highlighting targeted interactable objects and sockets.
- `PlayerEquippableItemVisuals`: Visual manager updating first-person arms and third-person goblin props when switching tools.
- `PlayerAnimationController`: State machine driver feeding velocity, tool swinging, carrying, and downed states to Animator controllers.
- `PlayerFirstPersonArms`: First-person viewmodel animation and alignment controller.
- `PlayerCameraFeedbackComposer`: Procedural camera bobbing, trauma shake, and field-of-view modifier reacting to player movement.
- `DownedPlayerCarryable`: Component enabling downed goblin players to be picked up and hauled by teammates or NPC enemies.

*Design Pattern*: **Component-Based Architecture & Observer Pattern**. Subsystems subscribe directly to `PlayerInputNew` and `PlayerActionController` C# events (`OnActionPerformed`, `OnHeldObjectChanged`, `OnDownedStateChanged`) without tight cross-component references.
*How to Extend*: Add new gameplay verbs by extending `PlayerGameInputActions`, binding listeners in `PlayerInteractionNew` or `PlayerActionController`, and implementing interaction behavior on target objects via `IInteractableNew`.

`Location: Assets/Scripts/NewScripts`

### Multi-Player Shared Carry & Physics Hauling System
A collaborative physics framework enabling multiple players to lift, balance, and steer oversized structural objects.
- `SharedCarryPhysicsBody`: Physics-driven object controller calculating collective forces, carry velocity, torque, and mass distribution across attached players.
- `SharedCarryCollisionController`: Collision and sweep validator preventing carried objects from clipping into level geometry.
- `SharedCarryAttachmentUtility`: Helper calculating local attachment grip points, player socket offsets, and rotational balance.
- `SharedCarryAnchorPreview`: World-space visualizer displaying valid grip handles and required player counts.
- `CarryPhysicsProfileSO`: ScriptableObject defining carry weight, movement speed penalties, understaffed stamina drain rates, and tipping thresholds.
- `ISharedCarryObject`: Interface exposing grip point validation, carrier registration, and cooperative movement modifiers.

*Design Pattern*: **Physics-Driven Cooperative Proxy**. Multiple player inputs aggregate into `SharedCarryPhysicsBody`, which executes synchronized physics updates on the server and smooths client visuals via `SharedCarryPlayerVisualOverride`.
*How to Extend*: Attach `SharedCarryPhysicsBody` and `SharedCarryCollisionController` to any structural prefab, assign a `CarryPhysicsProfileSO`, and configure mount sockets in the inspector.

`Location: Assets/Scripts/NewScripts`

### Wheelbarrow Transport System
A specialized physical vehicle system for transporting bulk loose materials, wet concrete batches, and structural parts across rough terrain.
- `WheelbarrowController`: Physical vehicle controller handling wheel friction, balance physics, tipping angles, cargo capacity, and dumping.
- `PlayerWheelbarrowController`: Player-side driving controller managing steering, push velocity, slope resistance, and boarding states.
- `WheelbarrowDockingStation`: Fixed docking point aligning wheelbarrows under concrete mixer chutes or factory unloaders.
- `WheelbarrowDockingVisualizer`: Visual alignment indicator highlighting valid docking zones.
- `WheelbarrowPourGripInteraction`: Interaction trigger initiating manual dumping or controlled concrete pouring.
- `WheelbarrowPouringMinigame`: Tilt-balancing minigame regulating pour flow rate to prevent spillage.
- `WheelbarrowProfileSO`: Configuration asset defining capacity, rolling resistance, turn radius, and tipping thresholds.

*Design Pattern*: **Vehicle Controller & State Driven Interaction**. Operates as a physically constrained vehicle transitioned between parked, pushed, docked, and pouring states.
*How to Extend*: Create new profiles using `WheelbarrowProfileSO` with modified center-of-mass and cargo limits, or create specialized cargo receivers using `IConcreteBatchReceiver`.

`Location: Assets/Scripts/NewScripts`

### Rope, Fastening & Sapling Mechanical System
A tactile physics toolset enabling flexible saplings to be tensioned, anchored, and fastened to structural bridge timbers.
- `RopeToolController`: Player tool controller handling aiming, firing, projectile simulation, and tensioning of rope lines.
- `RopeEndProjectile`: Physical or kinematic projectile securing rope ends to distant hit colliders and anchor sockets.
- `RopeAttachmentResolver`: Validation component checking anchor compatibility, distance limits, and obstruction line-of-sight.
- `RopeThrowTrajectoryPreview`: Parabolic arc visualizer rendering throwing trajectories in real time.
- `RopeToolProfileSO`: Configuration asset defining throw speed, maximum rope length, tension strength, and stamina consumption.
- `FlexibleSaplingController`: Interactive spring-tensioned sapling that can be bent, pulled, and locked into bridge structures.
- `FlexibleSaplingProfileSO`: Data asset specifying sapling elasticity, break limits, bending force, and wood durability.

*Design Pattern*: **Verlet/Spring Simulation & Target Resolution**. Handles elastic deformation and trajectory prediction decoupled from main player movement.
*How to Extend*: Attach `FlexibleSaplingController` to custom environmental trees and configure bend limits in `FlexibleSaplingProfileSO`.

`Location: Assets/Scripts/NewScripts`

### Hostile NPC, Wildlife & Disturber AI System
A behavior-tree and utility AI framework governing environmental disturbers, wildlife factions, and combat/harassment encounters.
- `NPCBrain`: Central AI hub coordinating navigation, target scanning, behavior execution, carrier state, and faction alignment.
- `NPCBehaviorController`: Authoritative controller running state machines driven by `NPCBehaviorSO` definitions.
- `NPCBehaviorSO`: Abstract ScriptableObject blueprint defining AI state logic, priority scoring, movement goals, and actions.
- `ResourceThiefBehaviorSO`: Harassment AI behavior commanding NPCs to infiltrate storages, steal resources, and flee.
- `BeaverScoutBehaviorSO`: Reconnaissance AI locating target player stockpiles and alerting defensive units.
- `BeaverDefenderBehaviorSO`: Combat-focused AI protecting territory, escorting harvesters, and attacking goblins.
- `GoatBehaviorSO`: Territorial AI patrolling elevated terrain and charging players standing near cliffs or scaffolding.
- `GoatChargeController`: Kinetic controller executing wind-up, linear sprint acceleration, and high-impulse headbutt knockbacks.
- `GoatPushZone`: Trigger zone marking dangerous ledges where goats prioritize knockback attacks.
- `NPCCarrier`: Subsystem enabling NPCs to hoist stolen resources or downed players and carry them across the map.
- `NPCDownedPlayerDropPoint`: Destination point where disturber NPCs attempt to dump kidnapped players (such as into rivers or traps).
- `NPCSpawner`: Wave and condition-based spawner evaluating bridge stages and timers to instantiate enemy groups.
- `NPCSpawnGroupSO`: Configuration asset grouping NPC archetypes, spawn counts, and spawn weight tables.
- `NPCFactionMember`: Faction tag defining relationship stances (Friendly, Neutral, Hostile) via `NPCFactionRelationshipMatrixSO`.
- `NPCHealth`: Network-synchronized damage and mortality component triggering ragdolls or death effects.

*Design Pattern*: **Strategy & Blackboard/Behavior Pattern**. `NPCBrain` serves as the central context/blackboard, while swappable `NPCBehaviorSO` strategies dictate autonomous behavior without hardcoding enemy types.
*How to Extend*: Create a new ScriptableObject subclassing `NPCBehaviorSO`, implement `Tick()`, `Enter()`, and `Exit()`, and assign it to an `NPCDefinitionSO`.

`Location: Assets/Scripts/NewScripts/NPCs`

### Water Simulation & Environmental Impulse System
A water physics and environmental hazard system managing buoyancy, player swimming/drowning penalties, and kinetic impulse distribution.
- `WaterBody`: Trigger volume defining water surface height, current flow velocity, drag coefficients, and submersion depth.
- `WaterShorelineSegment`: Spline/mesh boundary tracking safe riverbank exit locations for players and swimming NPCs.
- `WaterBodyProfileSO`: Data asset defining current drag, player submersion slow factors, oxygen depletion rates, and splash audio.
- `PlayerWaterExposureController`: Player component tracking immersion depth, water stamina drain, and drowning hazard states.
- `NPCAquaticLocomotionController`: NavMesh and kinematic swimming controller guiding NPCs across waterways.
- `RiverBedCleanupZone`: Catch-all trigger catching dropped items and respawning them at designated recovery points (`EquippableItemRespawnPoint`).
- `PlayerExternalImpulseController`: Impulse receiver managing kinematic knockback velocity, stun timers, and ground friction recovery.
- `NPCExternalImpulseController`: NPC knockback receiver integrating kinetic forces into NavMeshAgent locomotion.

*Design Pattern*: **Physics Volume & Receiver Pattern**. Hazards trigger `IExternalImpulseReceiver` on entering entities, decoupling kinetic impact sources from character controllers.
*How to Extend*: Place `WaterBody` volumes with custom `WaterBodyProfileSO` assets in new scenes and mark shoreline exits using `WaterShorelineSegment`.

`Location: Assets/Scripts/NewScripts/Water`

## 5. Scene Overview
- `MultiplayerStartScene`: The official boot and lobby scene (Build Index 0). Hosts the `MultiplayerRoomManager` UI, Unity Services Relay room creation, room code entry, lobby slot synchronization, and scene transition routing.
- `FPP_scene`: The primary full-scale cooperative gameplay level containing bridge sites, natural resource groves, ore quarries, industrial factories, and hostile wildlife spawners.
- `FPP_scene_singleplayer`: Offline standalone variant of the main gameplay scene configured for local testing without network server requirements.
- `Tutorial_scene`: Onboarding level featuring guided introductory zones for basic harvesting, tool equip, storage transfer, and single-stage bridge assembly.
- `BridgeTesting`: Development sandbox scene dedicated to validating bridge component sockets, physics carry kinematics, and construction workflow steps.
- `MainMenuScene`: Legacy/alternative standalone menu interface for basic scene launching.
- `NGO_Setup`: Minimalist networking testbed scene used to verify Netcode for GameObjects transport, client connection, and player prefab spawning.
- `_Recovery/0`: Editor emergency recovery backup scene.

`Location: Assets/Scenes`

## 6. UI System
The user interface combines **UGUI (Unity UI)** for in-game HUDs, spatial world-space canvases, and factory menus, alongside **UI Toolkit** for runtime panel settings and theme definitions.

- `PlayerCrosshairUI` / `CrosshairDotGraphic`: Central reticle dynamically rendering interaction prompts, tool target brackets, and interaction progress fill circles.
- `PlayerHeldObjectUI`: HUD widget displaying currently carried item icons, stack counts, and multi-player carry weight indicators.
- `PlayerHealthUI` & `PlayerStaminaUI`: Radial and bar meters tracking player health points, damage flashes, stamina depletion, and exhaustion states.
- `PlayerRespawnPromptUI`: Overlay modal showing bleedout countdowns, revive prompt instructions, and respawn trigger buttons.
- `PlayerBridgeStageInfoUI` / `BridgeRequirementsUI`: Comprehensive status panel summarizing total bridge completion, active stage goals, and required component tallies.
- `LookingAtComponentUI` / `BridgeComponentUI`: Contextual tooltip displaying required fastening tools, concrete amounts, and assembly hit percentages when gazing at bridge nodes.
- `FactoryInteractionUI` / `FactoryInteractionUISingleButton`: Modal window docked to factories allowing goblins to select production recipes and inspect missing input resources.
- `FactoryStorageResourcesPanelUI` / `RequiredResourcesPanelUI`: Sub-panels rendering inventory contents inside adjacent factory storage hoppers.
- `ProductionMinigameUI` / `ProductionProgressUI`: Dynamic overlay displaying real-time dial targets, saw progress bars, and mixer rotation gauges.
- `FurnaceFuelPanelUI` / `FurnaceTemperatureWorldUI`: World-space floating thermal meter attached directly above blast furnaces.
- `PlayerWheelbarrowPouringUI`: HUD balance meter guiding the player during controlled wheelbarrow concrete tilting.
- `PlayerFlexibleSaplingUI`: Dynamic arc meter showing tension force and release angles when bending saplings.
- `PlayerGirderFasteningUI`: Step-by-step UI guide for multi-point girder alignment and fastening.
- `NPCHealthUI`: Floating world-space health bar hovering above damaged wildlife units.
- `GameTimerUI`: Match session stopwatch displaying elapsed construction time.
- `RestartLevelUI`: Administrative reset confirmation dialogue for reloading active gameplay scenes.
- `NetcodeUI`: Legacy Netcode connection debug panel with Host/Server/Client buttons.
- `LookAtCamera`: Utility billboard component orienting world-space UI canvases toward the active local camera.

*Screen Flow & Extension*: In-game HUDs bind to model change events upon `OnNetworkSpawn` (e.g., `PlayerHealth.OnHealthChanged`, `BridgeBuildingManager.BridgeComponentMountableStatusUpdate`). To create a new UI screen, build a Canvas prefab with `LookAtCamera` or Screen-Space Overlay, bind its controller to the appropriate domain event, and utilize `CrosshairDotGraphic` for interaction feedback.

`Location: Assets/Scripts/NewScripts/UI` and `Assets/UI Toolkit`

## 7. Asset & Data Model
- **ScriptableObjects**:
  - `BaseResourceSO`: Raw resource parameters (durability, drop table, extraction tools).
  - `EquippableItemSO`: Tool configurations (hit damage, attack range, action cooldown, stamina cost, animation type).
  - `EquippableActionProfileSO`: Hitbox curves, swing impact timing, and particle impact assignments.
  - `BridgeComponentSO`: Structural part definitions (stage level, assembly hits required, dimensions, mount type).
  - `BridgeConstructionWorkflowSO` (and derivatives): Workflow requirements (excavation cycles, concrete loads, curing timers).
  - `ProductionRecipeSO`: Input ingredient costs, processing durations, and output component prefabs for factories.
  - `CarryPhysicsProfileSO`: Mass, linear drag, angular drag, and multi-player speed penalty curves.
  - `WheelbarrowProfileSO`: Cargo capacities, balance tolerances, and pouring speeds.
  - `NPCDefinitionSO` / `NPCBehaviorSO`: AI combat statistics, detection radiuses, and behavior state scripts.
  - `NPCFactionSO` / `NPCFactionRelationshipMatrixSO`: Faction alignment matrices and hostility tables.
  - `WaterBodyProfileSO`: Water drag, oxygen depletion, and swim velocity modifiers.
- **Prefabs**:
  - `PlayerNew` / `PlayerPrefab`: Main goblin player character with network transform, first/third-person rigs, and interaction volumes.
  - `BridgeComponent` variants: Modular bridge parts (Abutment, Pier, Girder, CrossBeam, DeckPanel, DiagonalBracing).
  - Factory & Machine prefabs: `BlastFurnace`, `CarpenterTable`, `ConcreteMixer`, `Bellows`.
  - Harassment AI units: `BeaverDefender`, `ResourceThief`, `GoatDisturber`.
  - Equippable Tools: `Axe`, `Pickaxe`, `Shovel`, `Hammer`, `RopeTool`, `Bucket`.
- **Directory Structure & Naming**:
  - `Assets/Scripts/NewScripts`: Primary modern gameplay logic refactored with clean interfaces.
  - `Assets/Scripts/NetworkManagement`: Multiplayer connection, room management, and player network spawning.
  - `Assets/ScriptableObjectAssets/New`: Central repository of runtime game configuration assets.
  - `Assets/Prefabs/New`: Production prefabs utilizing updated component workflows.

`Location: Assets/ScriptableObjectAssets/New` and `Assets/Prefabs/New`

## 8. Notes, Caveats & Gotchas
- **Dual Input System Configuration**: Project settings enable both the New Input System and Legacy Input Manager; all new gameplay logic strictly relies on `PlayerGameInputActions` generated classes (`PlayerInputNew.cs`). Do not use `Input.GetKeyDown`.
- **Netcode Server Authority vs Client Prediction**: Player locomotion and aim rotations utilize client prediction (`ClientNetworkTransform`), whereas health changes, inventory mutations, component assembly progress, and NPC behaviors execute exclusively on the Server/Host (`IsServer` checks).
- **IDamageable Dual Purpose**: In this codebase, `IDamageable` represents generalized work impact as well as physical health. Hitting a tree with an axe, a rock with a pickaxe, a bridge bolt with a hammer, or an enemy goblin all channel through `IDamageable.TakeDamage()`.
- **Understaffed Shared Carry Penalties**: Hauling objects with fewer players than `minAmountOfPlayersNeeded` applies aggressive movement speed penalties and continuously drains stamina via `PlayerStaminaController`. If stamina reaches zero, the player involuntarily drops their grip.
- **Concrete Curing & Soil Hardening Deadlines**: Foundation excavation pits require loose soil to be removed before soil hardening timers expire; likewise, poured wet concrete must complete its synchronized drying timer (`BridgeConstructionSite.IsConcreteDrying`) before structural girders can be mounted on top.
- **Missing Singletons in Test Scenes**: `BridgeBuildingManager`, `GameplayManager`, and `NPCRegistry` are accessed statically across interactable components. Any new test scene must include these manager prefabs to avoid runtime `NullReferenceException` crashes.
- **Downed Player Respawn Lock**: When an NPC kidnapper carries a downed player (`DownedPlayerCarryable.IsCarriedByNPC`), the player's respawn countdown timer is paused until the carrier is struck by a teammate and drops the victim.