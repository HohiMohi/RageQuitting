# Rope Tool and Network Tether

## Scope

`Rope` is a two-slot equippable item. V1 supports a charged throw, a loose
physical end, attachment to players, free single-carry resources and
wheelbarrows, length control, tension, obstruction checks, hard-limit
enforcement, stamina cost and target escape. `RopeTargetKind` includes
`Player`, `Resource` and `Wheelbarrow`.

V1 does not support NPC targets, static anchors, rope wrapping, climbing or
cutting the rope.

## Controls

| State | Input |
|---|---|
| `Ready` | Hold LMB to charge. Releasing LMB throws the rope end over a charge-dependent distance. |
| `Flying` | The networked physical endpoint resolves its first collision. |
| `Loose` | Hold LMB to reel the endpoint back; hold RMB to pay out. |
| `Attached` | Hold LMB to reel the target; hold RMB to pay out. |
| Attached target | Holder presses E to detach. A living target holds E to escape. |
| Attached wheelbarrow | E on an active righting point prioritizes `Righting`; E elsewhere detaches manually. |

Rope input takes priority over normal tool actions while `Rope` is selected.
Dropping the tool, downed, respawn, despawn, loss of control and modal UI cancel
the active rope safely. Existing cleanup also covers tool changes. Entering a
wheelbarrow as driver retracts its attached rope fully to `Ready` before role
assignment or physics ownership transfer, without spawning a loose endpoint.

## Physics

Throw charge linearly controls both speed and available length: an immediate
release provides about `4 m`, while a full `3 s` charge provides `15 m`.
While charging, the local owner sees a ballistic trajectory and a marker at the
predicted first collision or maximum rope length. The prediction includes the
endpoint radius, gravity and current camera direction.

The attached-target rest length remains clamped to `0.8-15 m`. An empty end can
reel below that minimum so it can return to the holder. Tension begins after
the dead zone. Resources receive a server-side spring and damping force at the
local hit point; this does not pull the holder. Player-player tether movement is
asymmetric by profile. The current `1/0` target/holder split applies the soft
correction entirely to the attached player, while both players retain normal
movement input. The hard distance limit still prevents either side from
stretching the rope indefinitely.

An unattached endpoint loses its throw momentum at the first invalid collision,
then remains a server-authoritative dynamic Rigidbody. Reeling shortens the rope
at `8 m/s`; tension pulls a grounded endpoint along its support surface instead
of moving it directly through the air. Walking away can likewise drag a landed
endpoint once the rope becomes taut. Attached targets reel at `4 m/s`. Only
reeling under tension uses up to `5 stamina/s`. At zero stamina reeling stops,
but the tether remains attached. Reaching the hard stretch stops further
reeling and removes only player movement that would increase the distance.
Movement toward the other end and tangential movement remain available. The
current profile does not break on overload; the legacy timed detach remains an
optional profile setting.

A wheelbarrow accepts a new rope only while `Free` or `Tipped`, at the exact
physical hit point. The server applies a custom spring with damping through
`AddForceAtPosition`, so the force naturally translates and rotates the body;
no `Joint` is created and no custom reaction force is applied to the holder.
The response weakens with current load mass. Passenger, secured-resource and
concrete mass continue to contribute through the wheelbarrow's existing
authoritative mass calculation.

The rope runtime delegates wheelbarrow towing to `WheelbarrowController`.
Tow force is accepted only by the current physics authority while the state is
`Free` or `Tipped`, the rope is not blocked, extension is positive and normalized
tension exceeds `0.04`. A short ground probe projects the pull direction onto
the support plane and limits its ground-normal contribution to `30%`. Force is
still applied at the exact attachment point, preserving controlled torque while
preventing a high attachment from turning the pull into lifting alone.

Active towing temporarily assigns `RopeTowContact` to every non-trigger physical
collider on the wheelbarrow. The material uses static friction `0.05`, dynamic
friction `0.03`, `Minimum` friction combine and zero bounce. Each collider's
original material is stored separately and restored once after `0.2 s` without
a valid tow signal. Explicit detach, a disallowed state including `Righting`,
despawn and loss of physics authority restore it immediately. While towing,
the idle brake is suspended and NavMesh obstacle carving is disabled; normal
settling and carving eligibility resume after towing ends.

An existing attachment survives the transition to `Righting`. Force pauses
and temporary friction is restored while the Rigidbody is kinematic; the rope
visual and attachment remain and towing resumes when the wheelbarrow returns to
`Free`. The rope does not itself change `Tipped` to `Free`. New attachment is
rejected in every other wheelbarrow state, and transitions to forbidden states
retract the rope fully to `Ready`. Docking, pouring and trapped states are
therefore unavailable rope targets. Entering `Driven` retains the existing full
retraction before ownership transfer. A passenger may still leave through the
existing safe-exit flow.

### Suspended players

An airborne attached player is simulated locally as a damped pendulum. Gravity,
limited holder-anchor motion and light tangential movement input build real
`swingVelocity`; releasing input does not immediately remove momentum. The
constraint removes only the radial velocity that would lengthen an already taut
rope, both before and after `CharacterController.Move`. Inward radial movement
can still create slack.

Length correction is a separate positional constraint. It accelerates up to
`3 m/s` at `12 m/s^2` and is never written back into swing momentum. Holder
anchor transfer is limited to `10 m/s`, preventing a network transform jump
from becoming an impulse. Wall contacts remove only velocity directed into the
surface, so vertical and tangential motion remain available.

A single grounded frame does not end suspension. Ground contact must remain
stable for `0.15 s`, and the timer resets when contact is lost or the taut rope
still requires a meaningful upward correction. Landing and detach preserve
only physical swing momentum; positional correction never becomes release
velocity.

A solid obstruction pauses reeling and force transfer and exposes
`Rope blocked` in the HUD. The straight line resumes after visibility returns.
There are no bend points in V1.

## Networking

`RopeToolController` lives on `PlayerNew.prefab`. The server owns the rope
state, endpoint and target IDs, target local point, length, tension, blocked
state and escape progress. Clients send input intent only. Target selection,
ownership, active item and one-rope-per-target reservation are validated by the
server.

Wheelbarrows reuse the existing target network ID, target kind and local hit
point state for synchronization and late join. The feature adds no cyclic RPC
or new `NetworkVariable`; the one-rope-per-target rule remains unchanged.

`RopeEndProjectile.prefab` is a server-authoritative Rigidbody with
`ServerNetworkTransform`; remote clients keep a kinematic replica. Landing,
ground support and endpoint pull forces are resolved only by the server.
`RopeThrowTrajectoryPreview` is owner-only and does not add network state.
Player tether correction is locally predicted; the server controls length,
obstruction and the hard limit. Swing, landing hysteresis and correction
parameters are replicated in `RopePlayerConstraintSettings`, so the target does
not need the holder's local profile asset. Late join reconstructs the rope from
NetworkVariables and the synchronized endpoint.

## Assets and Prefabs

| Asset | Purpose |
|---|---|
| `Rope.asset` | Two-slot equippable definition and inventory icon. |
| `RopeToolProfile.asset` | Throw, length, force, stamina and escape tuning. |
| `EquippableWorldPhysicsProfile_Rope.asset` | 2 kg world physics and compound colliders. |
| `Rope.prefab` | Physical world pickup and procedural two-handed spool model. |
| `RopeEndProjectile.prefab` | Networked weighted endpoint. |
| `Rope.mat` / `RopeLine.mat` | World model and line materials. |

Both rope prefabs must remain registered in `DefaultNetworkPrefabs.asset` and
`NGO_Minimal_Setup/NetworkPrefabsList.asset`. `Rope.asset` must remain in the
`PlayerInventory.equippableItemCatalog`.

## Configuration

`RopeToolProfileSO` exposes wheelbarrow spring, damping, acceleration,
pull-speed and load-multiplier tuning. The current baseline is `20`, `6`,
`6 m/s^2`, `2.5 m/s` and `0.45`, respectively.

`WheelbarrowProfileSO` exposes the towing contact material, activation tension
`0.04`, release delay `0.2 s`, maximum ground-normal contribution `0.3` and
ground-probe distance. Runtime diagnostics expose whether towing is active,
current tension, ground collider and normal, resolved direction and
acceleration, and the number of colliders using the temporary material.

## Validation

Unity compilation and the Console completed without new errors. The physical
probe passed for empty and loaded wheelbarrows in both `Free` and `Tipped`.
Final scenarios translated about `2.15-2.74 m` in `2 s`, with maximum observed
speed `2.375 m/s`. All `12` collider materials were restored in every covered
scenario, including slack, explicit detach and a missing tow signal. `dotnet
build` completed with `0` errors and `78` pre-existing warnings.

The Unity Pipeline `StandaloneWindows64` player build also completed
successfully. Temporary output was written to
`Temp/CodexValidation/RageQuitting.exe`; the build took `30.6 s`, produced
`206,542,412` bytes (about `206.5 MB`) and reported `0` errors with `76` project
warnings. The warnings were existing project debt, including obsolete NGO
`ServerRpc` APIs, hidden `NetworkBehaviour` members and missing scripts on the
legacy `Assets/Prefabs/Player.prefab`; no towing compile or build failure was
introduced.

Manual multiplayer validation was not run and remains required before release,
together with manual coverage of driver entry, late join, competing ropes and
lifecycle cleanup under real network conditions.

## Tutorial Scene

`Tutorial_scene` contains one `TutorialGameplay/Rope_Tutorial` at approximately
`(-9.5, 0.72, -23.5)` and `ToolRack_RespawnPoints/Rope_Return_1`. The rope
uses the existing equippable return flow after contact with the river bed.
