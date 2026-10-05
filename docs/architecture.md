# Product architecture

Hotel is a safe C# product with an authored, checkpointed excursion. The intended game is
in [design.md](design.md); [reuse.md](reuse.md) records one-time donor provenance.

> The product decides. The Engine guarantees.

## Owners

| Path or service | Responsibility |
| --- | --- |
| `src/Hotel.Game/Expedition/HotelExpedition.cs` | Refuge return/deposit policy, one complete checkpoint over Engine persistence, validation before restore and coherent recovery |
| `src/Hotel.Game/Expedition/CheckpointState.cs` | Versioned product values and source-generated JSON metadata; no Engine handles or presentation state |
| `src/Hotel.Game/HotelProduct.cs` | Explicit composition, Engine lifecycle callbacks, admitted updates, floor travel (carrying the player's values into the next world, remembering left floors for the session) and the one world interaction that reads the current route |
| `src/Hotel.Game/HotelWorld.cs` | The owners of one excursion's world (scene, player, supplies, combat, spirit, route, expedition, ambience and views), built together for the floor the player stands on and disposed together when they take the stairs |
| `src/Hotel.Game/Floors/HotelFloors.cs` | The run seed (persisted in its own Engine state scope), the current depth, and each generated floor of the run, generated on first entry and kept for the session |
| `src/Hotel.Game/Floors/FloorExcursion.cs` | A generated floor as an `ExcursionDefinition`: display fixtures for finds, depth-namespaced ids, stairs, readings, residents and the bell |
| `src/Hotel.Game/Route/HotelRoute.cs` | Door/latch state, authored room identity, reading facts and the Engine world-interaction adapter |
| `src/Hotel.Game/Supplies/HotelSupplies.cs` | Carried stacks, collected-find identities, capacity and use rules, health/ammo/summon reserves and inventory revision |
| `src/Hotel.Game/Combat/HotelCombat.cs` | Selected weapon, admitted windup/commit/recovery/reload timing, damage policy, resident behavior and Engine spatial hit/approach calls |
| `src/Hotel.Game/Combat/CombatView.cs` | Retained low-poly resident/weapon meshes and attack poses contributed to the existing scene snapshot |
| `src/Hotel.Game/Spirits/HotelSpirit.cs` | Pact acquisition/equipping, semantic equip claims, summon eligibility and transient manifestation phase |
| `src/Hotel.Game/Spirits/SpiritView.cs` | Authored bell-headed moth mesh parts and admitted-time entrance, wing poses and departure |
| `src/Hotel.Game/Input/` | The authored binding table (`ControlBindings`) and its uses (`HotelControls`): Engine FPS walk/use keys, game-control presses, playtest actions, Controls screen rows, quick-pocket keys and the opening hint |
| `src/Hotel.Game/Player/HotelPlayer.cs` | FPS input interpretation, authored body tuning, accepted player transform/motion and first-person camera |
| `src/Hotel.Game/Content/` | Reading authored files through Engine content services (`Authored`), the strict JSON contract (`ContentJson`), and composition-only loading and validation of references between files (`HotelContent`, `ExcursionDefinition`) |
| `src/Hotel.Game/*/…Definition.cs`, `…Tuning.cs` | Each domain's typed authored records and the file it loads them from |
| `src/Hotel.Game/Scene/HotelScene.cs` | Static appearances, paired door appearance/collision poses, GLB props, retained lights, collision placement and resource lifetimes, and the developer preview of one extra built floor beside the hotel |
| `src/Hotel.Game/Scene/Kit/` | The architectural kit: authored floor plan, kit tuning and fixture catalog records, and `KitBuilder`, which turns a plan into walls (half per side, per space surface), trim, frames, floors, ceilings, seams, fixtures, lights, sockets, rooms and door openings |
| `src/Hotel.Game/Floors/` | Floor generation's shared ground: the floor seed (generator version, run, depth, shift), `FloorDraws`, the only randomness a generator may use (Engine keyed draws scoped `hotel.floor.<stage>.<purpose>`), and the canonical plan text hashed into a floor's identity |
| `src/Hotel.Game/Floors/Mission/` | A generated floor's mission graph: places (arrival, objective, keys, gates, bell, landmark, supplies, hazards, shortcut) and the edges between them (open, locked, one-way, latch); fail-atomic rules grown from authored weights; item-aware there-and-back validation |
| `src/Hotel.Game/Floors/Modules/` | Room modules for generated floors: the catalog and its lattice and doorway styles, the quarter-turn placement transform, realizing placed modules and mated doorways into one kit floor plan, and each module's isolated walkability self-check |
| `src/Hotel.Game/Floors/Layout/` | Laying a mission graph out as modules: lock regions, phased scored socket growth (stair core, a module per place, the latched service passage back to the stairs, fill rooms), the resolved `FloorLayout` record and its realization into one kit floor plan, and the item-aware lock-cut and reach check |
| `src/Hotel.Game/Floors/Content/` | Furnishing a laid-out floor at its modules' content sockets: arrival, the objective find, a key per lock, stop supplies (with the recovery item guaranteed before each hazard), the bell, hazard and extra residents whose leash keeps to their region, loose supplies and notices; the resolved `FloorContent` record and its pacing-budget check |
| `src/Hotel.Game/Floors/Confirm/` | Engine confirmation of a laid-out floor: its solid boxes as collision in a scratch spatial session, collision navigation for the player's own controller, every promised route both ways (the shortcut's latch shut on the way out), each lock held shut as a traversal overlay, and the first refused doorway named with the Engine's edge reading |
| `src/Hotel.Game/Floors/FloorGenerator.cs` | One floor end to end: mission graph per candidate, layout attempts per graph, Engine confirmation, deterministic retry by attempt then candidate, honest failure with every reason, and the floor's identity |
| `src/Hotel.Game/Scene/RoomGeometry.cs` | Authored mesh vertices with world-metre texture coordinates, used by presentation and static collision |
| `src/Hotel.Game/Audio/HotelAmbience.cs` | Authored ambient clips and looping voices; Engine owns playback, spatialization and mixing |
| `src/Hotel.Game/Interface/HotelHud.cs` | The `rusty.hotel.hud` projection: every UI fact written once, grouped by HUD region or screen |
| `src/Hotel.Game/Interface/UiValueWriter.cs` | Writes the Engine `UiValue` node/edge/UTF-8 tree and compares two values; the pinned SDK has no builder |
| `src/Hotel.Game/Interface/HotelDebugCommands.cs`, `HotelDeveloper.cs` | The generated Hotel command catalog and what it does: observation, look, explicit developer overrides (goto, module viewer, floor entry, return to entrance) and floor inspection with the floor's places, spaces and links |
| `src/Hotel.Game/Interface/SuppliesDebugCommands.cs` | Explicit developer supply, health and consumption fixtures over the supplies owner |
| `src/Hotel.Game/Hotel.Game.csproj` | Product identity, entry, content/UI roots and host defaults |
| `content/` | Authored data by domain: player, route, interface, scene surfaces, supplies, combat, spirits, and one folder per excursion. See [authoring](authoring.md). |
| `src/ui/main.js` | Composition, the single foreground-screen navigation, focus containment, lifecycle flow and Engine input-mode handoff |
| `src/ui/hud.js` | Exploration HUD regions and their drawing from projected facts |
| `src/ui/menu-screen.js`, `case-screen.js`, `controls-screen.js`, `reading-screen.js`, `console-screen.js` | One foreground screen each: its markup, drawing, and what happens on entering and leaving it; `screen.js` holds their shared helpers |
| `src/ui/field-case.js` | Supplies/Spirits tabs, pocket selection/details, pact selection/equip claims and quick-pocket presentation |
| `src/ui/developer.js` | Lazy packaged Engine console mount, disposal and stale-mount cleanup |
| `src/ui/pause.js` | Foreground lifecycle request lifetime and error presentation; Engine port owns actual lifecycle state |
| `src/ui/hotel.css` | Product-authored responsive screen/HUD styling |
| Engine SDK/runtime | Admitted time/input, FPS physical input, look integration, character collision, camera interpolation, renderer, generated interop, host and browser shell |

## Lifecycle and data flow

The packaged runtime loads the SDK-generated bind entry and constructs
`HotelProduct`. Hotel loads its content tree (`HotelContent`), creates static meshes/appearances and
places those same meshes in one Engine spatial session. One Engine entity store
holds the player's transform and motion. Static scene entities supply real
identities for their appearance and collision instances.

Content loading is composition-only. `HotelContent.Load` reads each file through
its domain record's `Load`, with the strict `ContentJson` contract: a missing value,
a null or an unknown member is an error. It then checks references between files,
such as surfaces, items, resident kinds, finds and the spirit bell. Every failure
names `content/<file>` and the field. The constructor hands each owner only its own
records. No owner receives `HotelContent` or reads another domain's file.
`HotelProduct.StartingExcursion` selects the excursion folder.

The constructor creates owners in one flat sequence. Each disposable owner is
registered as it is created; a construction failure or `Dispose` releases them
in reverse order. Adding an owner is one line. Every path that changes domain
state ends in one of two methods. `Publish` sends the scene views, camera sample
and HUD. `PublishInterface` sends HUD facts only, for paused claims, route
results and developer fixtures, which must not resample the camera at an
unadmitted time.

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
and clears readings. Physical Q calls the equipped spirit. Shutdown releases the checkpoint store and spirit/combat appearances,
then ambient voices before their clips, the UI stream,
camera, collision session, lights, appearances, meshes, materials, model/texture
resources and entity store in ownership order.

Wall and floor UVs derive from authored world metres so adjoining wall sections
keep the same texture phase. The material owns repeat size, color, roughness and
emission. Engine admits PNG textures and retains material/light resources. GLB
props use the pinned SDK's Animation content/appearance API even when unrigged;
Hotel does not decode models or animate the recorder. Kit-built walls, floors, ceilings and solid fixture
parts share the existing Engine collision session; decorative GLB props
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
unsaved initial-excursion reset across player, supplies, combat, spirit and
route, clears held input and republishes the camera/HUD. It leaves the established
checkpoint unchanged; it is not a teleport or checkpoint recovery.

`HotelRoute` supplies current candidates to Engine `WorldInteraction`, and uses
`InteractionVisibilityQuery` against the same retained spatial session as movement.
Each focusable object is one private `Interactable` entry: entity, whether it is
currently offered, label, focus point, availability and use handler. Doors,
readings, finds, the spirit bell and the refuge notebook are entries built from
authored definitions in that stable order. `ReadInteraction` iterates them, and
`UseInteraction` rejects stale revisions and dispatches to the entry. A new kind of
world action adds entries and one handler. It does not add a second focus path.
Route receives Expedition's refuge return and the interface publish callback at
construction. The return is a product method resolved at use time, because
Expedition is built afterwards: it captures and restores Route's door state.
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
authored in `content/supplies/` and each excursion's `placements.json`; pocket capacities are in `content/interface/`.
Pickup notices expire using admitted simulation time.

`HotelHud` writes each fact exactly once, straight from the owners, into groups:
top-level `location`, `focusPrompt` and the single `notice` slot (priority spirit,
then combat, then supplies), then `condition`, `held`, `supplies` (with its
`pockets`), `spirit`, `reading` and `refuge`. A new fact is one line there plus its
use in the screen module that shows it. A value identical to the last published
one, compared node by node and byte by byte, is skipped, so no fact can be missed
by a revision someone forgot to bump. The field
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
and resident numbers live in `content/combat/`; resident placements in each excursion's `placements.json`. Zero health suppresses
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
player-facing description live in `content/spirits/<id>.json`; the bell position is an excursion placement.

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
before any owner is restored. Incomplete/malformed or semantically invalid state
fails startup with a checkpoint error and is not replaced. Save failures produce
a failed refuge receipt, preserving carried finds and the previous checkpoint.
There is no migration or silent fallback. The schema version is product policy. Content identities and validation limits
affect compatibility; see [authoring.md](authoring.md).

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
