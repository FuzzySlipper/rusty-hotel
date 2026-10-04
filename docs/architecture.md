# Product architecture

Hotel is a safe C# product with a walkable authored hotel section. The intended game is
in [design.md](design.md); [reuse.md](reuse.md) records one-time donor provenance.

> The product decides. The Engine guarantees.

## Owners

| Path or service | Responsibility |
| --- | --- |
| `src/Hotel.Game/Expedition/HotelExpedition.cs` | Refuge return/deposit policy, one complete checkpoint over Engine persistence, validation before restore and coherent recovery |
| `src/Hotel.Game/Expedition/CheckpointState.cs` | Versioned product values and source-generated JSON metadata; no Engine handles or presentation state |
| `src/Hotel.Game/HotelProduct.cs` | Explicit composition, Engine lifecycle callbacks, admitted updates and the Engine playtest command adapter |
| `src/Hotel.Game/Route/HotelRoute.cs` | Door/latch state, authored room identity, reading facts and the Engine world-interaction adapter |
| `src/Hotel.Game/Supplies/HotelSupplies.cs` | Carried stacks, collected-find identities, capacity and use rules, health/ammo/summon reserves and inventory revision |
| `src/Hotel.Game/Combat/HotelCombat.cs` | Selected weapon, admitted windup/commit/recovery/reload timing, damage policy, resident behavior and Engine spatial hit/approach calls |
| `src/Hotel.Game/Combat/CombatView.cs` | Retained low-poly resident/weapon meshes and attack poses contributed to the existing scene snapshot |
| `src/Hotel.Game/Spirits/HotelSpirit.cs` | Pact acquisition/equipping, semantic equip claims, summon eligibility and transient manifestation phase |
| `src/Hotel.Game/Spirits/SpiritView.cs` | Authored bell-headed moth mesh parts and admitted-time entrance, wing poses and departure |
| `src/Hotel.Game/Player/HotelPlayer.cs` | FPS input interpretation, authored body tuning, accepted player transform/motion and first-person camera |
| `src/Hotel.Game/Scene/HotelDefinition.cs` | Typed authored layout and tuning loaded through Engine content services |
| `src/Hotel.Game/Scene/HotelScene.cs` | Static appearances, paired door appearance/collision poses, GLB props, retained lights, collision placement and resource lifetimes |
| `src/Hotel.Game/Scene/RoomGeometry.cs` | Authored mesh vertices with world-metre texture coordinates, used by presentation and static collision |
| `src/Hotel.Game/Audio/HotelAmbience.cs` | Authored ambient clips and looping voices; Engine owns playback, spatialization and mixing |
| `src/Hotel.Game/Interface/HotelHud.cs` | One Engine UI stream publishing location, focus, readings, supply stacks, use eligibility and resource facts |
| `src/Hotel.Game/Interface/HotelDebugCommands.cs` | Generated Hotel inspection and explicit developer return-to-entrance commands over existing owners |
| `src/Hotel.Game/Interface/SuppliesDebugCommands.cs` | Explicit developer supply, health and consumption fixtures over the supplies owner |
| `src/Hotel.Game/Hotel.Game.csproj` | Product identity, entry, content/UI roots and host defaults |
| `content/hotel.json` | Refuge/corridor/rooms, metre-scale material repeats, props, lights, ambient cues and player tuning |
| `src/ui/main.js` | HUD, single foreground screen navigation, focus containment and Engine input-mode handoff |
| `src/ui/field-case.js` | Supplies/Spirits tabs, pocket selection/details, pact selection/equip claims and empty quick-access presentation |
| `src/ui/developer.js` | Lazy packaged Engine console mount, disposal and stale-mount cleanup |
| `src/ui/pause.js` | Foreground lifecycle request lifetime and error presentation; Engine port owns actual lifecycle state |
| `src/ui/hotel.css` | Product-authored responsive screen/HUD styling |
| Engine SDK/runtime | Admitted time/input, FPS physical input, look integration, character collision, camera interpolation, renderer, generated interop, host and browser shell |

## Lifecycle and data flow

The packaged runtime loads the SDK-generated bind entry and constructs
`HotelProduct`. Hotel loads `hotel.json`, creates static meshes/appearances and
places those same meshes in one Engine spatial session. One Engine entity store
holds the player's transform and motion. Static scene entities supply real
identities for their appearance and collision instances.

`Start` publishes the scene, camera and HUD facts and starts Engine ambient voices. Each admitted update consumes Engine
input through `FpsInput`, integrates look through its helper, and applies one
`ProposeCharacterStep` per admitted fixed step. Only accepted Engine receipts
update player transform and motion. The camera's eye offset comes from authored
body/eye heights; camera sample time comes from admitted simulation facts.
There is no product clock, collision solver or render loop. A pending use edge bridges
zero-step input batches to the next admitted step; Engine FPS owns physical input.

Engine clear events clear FPS held/pending input. Pause and resume callbacks
also clear it; restart resets the player to the authored spawn and cuts the
camera to the new pose. Pending use, attack, selection and reload edges clear at
these same boundaries. No jump action is implemented. Restart restores the established refuge checkpoint
and clears readings. Physical Q calls the equipped spirit. Shutdown first
removes combat appearances, then releases ambient voices before their clips, the UI stream,
camera, collision session, lights, appearances, meshes, materials, model/texture
resources and entity store in ownership order.

Wall and floor UVs derive from authored world metres so adjoining wall sections
keep the same texture phase. The material owns repeat size, color, roughness and
emission. Engine admits PNG textures and retains material/light resources. GLB
props use the pinned SDK's Animation content/appearance API even when unrigged;
Hotel does not decode models or animate the recorder. Primitive props and solid
room boxes share the existing Engine collision session; decorative GLB props
are visual only and sit on solid furniture. Engine follows the active camera
for audio listening. The ventilation bed is global; the recorder bed has an
authored world position and range. Offline asset-authoring tools are not build
steps or runtime dependencies.

The UI owns only presentation and semantic navigation. Click-to-lock and mouse
delivery are handled by Engine. Escape opens the menu or closes the current
screen; I opens the field case. Engine interface mode releases held gameplay
input and pointer lock. Controls and the console use that same navigation owner. Return to hotel
restores Engine gameplay mode/focus after a successful resume. Every foreground
screen requests pause through the mounted Engine lifecycle port. Nested screen
navigation stays paused; the final exit requests resume. UI labels observe the
port's actual state, including changes made elsewhere. Failed, superseded or
disposed requests cannot authorize return to gameplay. The request adapter has
no simulation clock, transport or independent lifecycle state. See [ui.md](ui.md).

The product registers Engine's `PlaytestDebugModule` for observation, walking
bindings and bounded look assistance. These inspect or call the same player
owner and are available only through a live-debug-enabled host. They are not a
player UI or a replacement input path. `HotelDebugCommands` also exposes
`hotel.inspect` and `hotel.dev.return-to-entrance`; the latter calls the existing
route/player reset path, clears held input and republishes the camera/HUD.

`HotelRoute` supplies current candidates to Engine `WorldInteraction`, and uses
`InteractionVisibilityQuery` against the same retained spatial session as movement.
E consumes the Engine focused action; the Engine debug module shares that action
owner. Door policy checks the current revision and far-side latch, then moves the
leaf appearance and collision to the same authored pose. Open doors remain open until a checkpoint restore supplies their saved state. Readings publish title/text and a sequence for UI presentation;
they do not grant inventory. Room labels come from authored X/Z bounds.

The reading screen uses the existing foreground pause, close and focus flow.
`HotelSupplies` owns item stacks and resource values. A world find is admitted
through Engine `InventoryStore`, registered to the existing player entity.
Hotel pockets retain stack identities only; quantities live in the Engine ledger.
Engine `Track` holds each resource and supplies clamped restore and bounded
spending. Hotel chooses item effects, eligibility and the pocket arrangement.
A world find is admitted
whole: existing stacks fill before empty pockets, and insufficient capacity
leaves the find untouched. Only successful admission removes its appearance
and interaction candidate. Room entry and opening the case never reset supplies.
The `hotel.supplies` typed claim reaches `HotelSupplies.HandleIntents` through
both Update and HandlePausedIntents. Only that owner mutates resources/stacks;
paused claims publish without stepping the world. Browser selection and drag
origin/revision are transient presentation, with no optimistic inventory mutation.
Keys 3–5 use pockets 0–2 on the next admitted step; clear/pause/resume cancel
pending presses. These pockets already participate in the whole checkpoint.
Use refuses defeated players; recovery remains the expedition owner's action.
Use and movement check the current inventory revision; full resources, empty
pockets and expedition keepsakes refuse consumption. Resource spending cannot
go below zero. Item definitions, stack limits, effects and find placements are
authored under `supplies` in `hotel.json`; pocket capacities are under `interface`.
Pickup notices expire using admitted simulation time.

`HotelHud` publishes changed typed facts through one Engine UI stream. The field
case observes stacks, capacity and selected details. Browser selection is
presentation only. Supplies use/movement and spirit equipping share Engine-owned
paused semantic delivery. Never temporarily resume gameplay or dispatch
developer commands to implement player menu actions.

## Combat

`HotelProduct` admits combat on the same fixed steps as movement. Engine FPS
physical state supplies attack, selection and reload edges; pending edges bridge
zero-step batches and clear on input clear/pause/resume. Holding attack does not
repeat. An accepted attack locks its direction and spends any ammunition once.
Windup resolves one Engine ray against current resident hitboxes and hotel
collision, followed by commitment and recovery. Misses and blocked shots retain
their cost. Weapon switching and reloading are refused during a committed action.
R loads one carried cartridge packet through the supplies owner's current
revision and use rules; damage or another inventory change can invalidate that
pending selection. This is a running-world firearm action, separate from paused
field-case controls.

Residents hold real identities in the existing scene EntityStore. Positions and
motion come from Engine character receipts; health uses Engine Track. The porter
approaches only with clear sight inside its authored territory, stops at striking
range, raises its hammer, commits to a direction and recovers. It uses static hotel
collision while approaching; player movement includes live resident body obstacles.
It does not search for routes or pursue around corners. The stationary Lamplighter
charges and fires along its locked direction; a sidestep or real world geometry
can stop its shot. No local navigation or collision mechanism is present.

`CombatView` owns only meshes/appearances and authored poses: raised arm, glowing
eye, strike, beam, recovery droop and first-person weapon. `HotelScene` publishes
one combined static/combat snapshot. The DOM receives weapon, action state and
brief hit/hurt notices; it does not render weapons or aim attacks. Authored weapon
and encounter numbers live under `combat` in `hotel.json`. Zero health suppresses
movement, use and further attacks; R invokes the expedition owner to restore the whole saved refuge checkpoint.

## Spirit pact and manifestation

`HotelSpirit` owns the single authored pact and equipped choice. `HotelRoute`
adds its bell to the existing Engine focus/visibility/use flow. Successful E use
acquires the pact once and restores its authored welcome charges through
`HotelSupplies`; it removes the waiting creature from the pedestal. Acquisition
does not automatically equip it.

The field case's Spirits view claims `hotel.spirit.equip`, carrying the selected
choice and pact revision. `HotelProduct.Update` and `HandlePausedIntents` route
those direct payloads to the same domain rule. Engine owns delivery, lifecycle,
ordering and stale bindings; Hotel checks acquisition, current pact revision,
active manifestation and defeat. Browser state never predicts an equipped pact.
Paused delivery publishes UI facts without advancing movement, combat or spirit
phase. Pending physical Q edges clear on input clear/pause/resume.

Q checks equipped state, health, current manifestation, charges and an Engine
ray to a living resident under the reticle. Walls and range block it. One accepted
call spends the shared charge once and asks `HotelCombat` to interrupt that
resident's pending attack and beam. It remains Interrupted through the authored
arrival/hold/departure interval, then returns Ready with a new tell; it never
replays the cancelled hit. No health damage or persistent follower is added.

`SpiritView` supplies a bell head, eyes, antennae and scalloped articulated wings
to `HotelScene`'s combined snapshot. Authored poses progress only on admitted
steps. Arrival grows and flies toward the resident, hushing spreads the wings,
and departure folds/shrinks upward. This presentation has no collision authority
or separate animation clock. Pausing freezes it; defeat withdraws it. Checkpoint recovery restores acquired/equipped values and clears transient
manifestation poses and timing. Spirit identity, rule values, position and
player-facing description live in `hotel.json`.

## Refuge checkpoint

`HotelExpedition` composes explicit capture/validation/restore operations on the
existing Supplies, Combat, Spirit, Player and Route owners. The notebook is a
candidate in the same Engine WorldInteraction adapter; E records a return only
through that adapter's visibility, range, focus and current revision checks.
An unfinished attack, active summon or defeat refuses saving. The contextual
refuge receipt uses the existing foreground pause/focus/close flow; the menu
observes checkpoint status. Neither UI nor a developer transport performs saves.

One `ProductStateStore<CheckpointState>` with source-generated
`JsonProductStateCodec` saves the entire value under `hotel.checkpoints` /
`refuge/current`. Engine owns storage and durable write/error semantics. A return
captures pocket order/quantities, collected find IDs, resource amounts, pact and
equipped choice, selected weapon, opened doors, resident health/position/yaw,
refuge identity and secured expedition-find IDs. Deposited expedition stacks
are omitted from the proposed carried pockets. Only after a successful Engine
write does the live supplies owner settle that deposit and the checkpoint owner
replace its restore point. No per-domain files, browser storage or shutdown save
can create mixed checkpoints.

Startup distinguishes missing from present. Missing creates the valid initial
checkpoint; a present value is decoded and validated against the current content
before any owner is mutated. Incomplete/malformed or semantically invalid state
fails startup with a checkpoint error and is not replaced. Save failures produce
a failed refuge receipt, preserving carried finds and the previous checkpoint.
There is no migration or silent fallback. The schema version is product policy.

Recovery restores that entire checkpoint, including loot availability and
resident positions/health, then places the player at the authored refuge spawn.
The saved refuge identity and selected weapon are meaningful player state;
velocity, held/pending input, combat commitments, beams, notices and temporary
spirit poses are deliberately cleared at this refuge boundary. Living residents
resume Ready, dead residents remain defeated. Health and resource values are
preserved, not replenished. Before the first return this restores the initial
excursion; later it restores the last successful return. Pause, close and dispose
do not save unsaved expedition changes. Dispose releases the Engine store.

## Developer access

The developer console requires a product URL opt-in (`#developer=1`) and the
Engine host's separate `--live-debug` opt-in. Closed UI does not import or mount
the debug panel or create its transport. Opening imports the Engine-owned
`@rusty-engine/live-debug` entry through the runtime's import map and mounts
its dock presentation in a bounded dropdown. Closing disposes its requests and
DOM. A mount that resolves after closing is immediately disposed. Hotel owns
only this lifetime/navigation adapter, not the console parser, catalog,
transcript or transport. A disabled debug host reports unavailable in the
Engine panel while the game and field case remain usable.

## Build and host

`Directory.Build.props` pins one immutable SDK/runtime pair and names the Hotel
project. `rusty install` installs the pair into the shared cache; `rusty dev`
owns staging, watching, runtime replacement and serving. `rusty build` stages
CoreCLR through `StageRustyEngineCoreClrProduct`; `rusty build --aot` is an
explicit NativeAOT fidelity/release check. Generated bindings remain ignored
output. No sibling checkout is a runtime or build dependency.

The product supplies C#, DOM assets and content; host binaries and browser
assets stay with Engine. `.den-serve.json` launches the ordinary product for
an owned playtest session and explicitly enables live-debug for that session.
The `engine-pair` workflow advances the pin only after build and serve checks.

Before adding a mechanism, check the installed safe SDK and the owners above.
A missing Engine capability belongs in an upstream request, not a local host,
transport, scheduler or renderer.
