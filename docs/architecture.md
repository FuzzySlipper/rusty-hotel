# Product architecture

Hotel is a safe C# product with an authored, checkpointed excursion. The intended game is
in [design.md](design.md); [reuse.md](reuse.md) records one-time donor provenance.

> The product decides. The Engine guarantees.

## Owners

| Path or service | Responsibility |
| --- | --- |
| `src/Hotel.Game/Expedition/HotelExpedition.cs` | Refuge return/deposit policy, one complete checkpoint over Engine persistence, validation before restore and coherent recovery |
| `src/Hotel.Game/Expedition/CheckpointState.cs`, `Floors/FloorRecord.cs` | Versioned product values (the run and its floors' resolved plans from version 2) and source-generated JSON metadata; no Engine handles, boxes or presentation state |
| `src/Hotel.Game/HotelProduct.cs` | Explicit composition, Engine lifecycle callbacks, admitted updates, floor travel (carrying the player's values into the next world, remembering left floors for the session) and the one world interaction that reads the current route |
| `src/Hotel.Game/HotelWorld.cs` | The owners of one excursion's world (scene, player, supplies, combat, spirit, route, expedition, ambience and views), built together for the floor the player stands on and disposed together when they take the stairs |
| `src/Hotel.Game/Floors/HotelFloors.cs` | The run: its seed, the current depth, each generated floor (generated on first entry, then kept), what the player left each floor as, and the finds collected on them; captured into, validated against and restored from the checkpoint as resolved plans |
| `src/Hotel.Game/Floors/FloorExcursion.cs` | A generated floor as an `ExcursionDefinition`: display fixtures for finds, depth-namespaced ids, stairs, readings, residents and the bell |
| `src/Hotel.Game/Route/HotelRoute.cs` | Door, latch and lock state, the keys held on the current floor, room identity, reading and stair facts, and the Engine world-interaction adapter |
| `src/Hotel.Game/Mechanics/` | The stat vocabulary (`MechanicsDefinition`: attributes, derived stats, tracks, damage kinds, effects) and `ActorStats`, one actor's Engine `StatsComponent`: attribute-sourced derived stats, tracks, resistance-reduced damage, capture and restore. Each actor's `ActorEffects` keeps its Engine `EffectsComponent` and the product's time, ticks and wards in step, adds its stacks' stat sources, and advances only on the admitted seconds Supplies (the investigator) and Combat (residents) pass it |
| `src/Hotel.Game/Supplies/HotelSupplies.cs` | Collected-find identities, use, wear and move rules over the field case, the investigator's stats (health, ammunition and summon tracks, worn sources) and inventory revision |
| `src/Hotel.Game/Supplies/FieldCase.cs` | The field case over one Engine `InventoryStore`: pocket layout of stacks and single items, equipment slots and worn items, Engine capacity limits (weight, space), the worn items' stat sources, capture and restore |
| `src/Hotel.Game/Actions/` | The action catalog (`ActionDefinition`: delivery, cost, timing, scaled damage packets, effects), `ActionUser` (one actor's phase, committed aim and cooldowns on admitted seconds) and `ActionResolution` (Engine spatial queries per delivery, contributions, damage application, projectiles in flight) |
| `src/Hotel.Game/Combat/HotelCombat.cs` | Encounter policy over the action pipeline: the investigator's primary/secondary actions through what the hands hold, swapping hands, residents attacking with their kind's action, approach, impacts' feedback, resident capture and restore |
| `src/Hotel.Game/Progression/` | The growth vocabulary (`GrowthDefinition`: levels, skills grown by use, what relics and tomes teach) and the investigator's growth (`InvestigatorGrowth`, owned by Supplies): experience, uses by skill and relics, given to the investigator's stats as intrinsic Engine sources and to its hits as contributions |
| `src/Hotel.Game/Loot/` | The loot catalog (`LootCatalog`: qualities, affixes, tables), item generation and table rolls through one keyed-draw interface: floor generation's content draws, or `SearchLootDraws` under the run seed for searches made in play |
| `src/Hotel.Game/Residents/` | Resident kinds composed from parts (`ResidentDefinition`: look, faction, perception, movement, action choices), `ResidentSenses` (one Engine visibility query a step for every resident, near sense and awareness memory) and `ResidentConduct` (the movement parts and action choice, as Engine character steps) |
| `src/Hotel.Game/Combat/HotelEnemy.cs`, `PlayerActor.cs` | The two kinds of action actor: a placed resident (body, `ActorStats`, `ActionUser`) and the investigator (player body, supplies' stats, worn contributions) |
| `src/Hotel.Game/Combat/CombatView.cs` | Retained low-poly resident and held-item meshes, flares in flight and attack poses contributed to the existing scene snapshot |
| `src/Hotel.Game/Spirits/HotelSpirit.cs` | The roster's pacts: freeing spirits at bells, the pact slot, semantic equip claims, call eligibility through Combat's pact slot, and the transient visit |
| `src/Hotel.Game/Spirits/SpiritView.cs` | Authored bell-headed moth mesh parts and admitted-time entrance, wing poses and departure |
| `src/Hotel.Game/Input/` | The authored binding table (`ControlBindings`) and its uses (`HotelControls`): Engine FPS walk/use keys, game-control presses, playtest actions, Controls screen rows, quick-pocket keys and the opening hint |
| `src/Hotel.Game/Player/HotelPlayer.cs` | FPS input interpretation, authored body tuning, accepted player transform/motion and first-person camera |
| `src/Hotel.Game/Content/` | Reading authored files through Engine content services (`Authored`), the strict JSON contract (`ContentJson`), and composition-only loading and validation of references between files (`HotelContent`, `ExcursionDefinition`) |
| `src/Hotel.Game/*/…Definition.cs`, `…Tuning.cs` | Each domain's typed authored records and the file it loads them from |
| `src/Hotel.Game/Scene/HotelScene.cs` | Static appearances, paired door appearance/collision poses, GLB props, retained lights with the shadow budget and failing-lamp flicker, collision placement and resource lifetimes, and the developer preview of one extra built floor beside the hotel |
| `src/Hotel.Game/Scene/SceneLook.cs` | The camera look from `content/scene/look.json`: tone mapping and exposure, distance haze, and the shadow budget's tuning |
| `src/Hotel.Game/Scene/Kit/` | The architectural kit: authored floor plan, kit tuning (trim styles), fixture catalog records, and `KitBuilder`, which turns a plan into walls (half per side, per space surface), trim bands and swept mouldings, frames and architraves, floors, ceilings, seams, fixtures and their models, lights, sockets, rooms and door openings |
| `src/Hotel.Game/Floors/` | Floor generation's shared ground: the floor seed (generator version, run, depth, shift), `FloorDraws`, the only randomness a generator may use (Engine keyed draws scoped `hotel.floor.<stage>.<purpose>`), and the canonical plan text hashed into a floor's identity |
| `src/Hotel.Game/Floors/Mission/` | A generated floor's mission graph: places (arrival, objective, keys, gates, bell, landmark, supplies, hazards, shortcut) and the edges between them (open, locked, one-way, latch); fail-atomic rules grown from authored weights; item-aware there-and-back validation |
| `src/Hotel.Game/Floors/Modules/` | Room modules for generated floors: the catalog and its lattice and doorway styles, the quarter-turn placement transform, realizing placed modules and mated doorways into one kit floor plan, and each module's isolated walkability self-check |
| `src/Hotel.Game/Floors/Layout/` | Laying a mission graph out as modules: lock regions, phased scored socket growth (stair core, a module per place, the latched service passage back to the stairs, fill rooms) around any kept set a shifting floor keeps, the resolved `FloorLayout` record and its realization into one kit floor plan, and the item-aware lock-cut and reach check |
| `src/Hotel.Game/Floors/Content/` | Furnishing a laid-out floor at its modules' content sockets: arrival, the objective find, a key per lock, stop supplies (with the recovery item guaranteed before each hazard), the bell, hazard and extra residents whose leash keeps to their region, loose supplies and notices; the resolved `FloorContent` record and its pacing-budget check |
| `src/Hotel.Game/Floors/Confirm/` | Engine confirmation of a laid-out floor: its solid boxes as collision in a scratch spatial session, collision navigation for the player's own controller, every promised route both ways (the shortcut's latch shut on the way out), each lock held shut as a traversal overlay, and the first refused doorway named with the Engine's edge reading |
| `src/Hotel.Game/Floors/FloorGenerator.cs` | One floor end to end: mission graph per candidate, layout attempts per graph, Engine confirmation, deterministic retry by attempt then candidate, honest failure with every reason, and the floor's identity |
| `src/Hotel.Game/Scene/RoomGeometry.cs` | Authored mesh vertices with world-metre texture coordinates, used by presentation and static collision |
| `src/Hotel.Game/Audio/HotelAmbience.cs` | Authored ambient clips and looping voices; Engine owns playback, spatialization and mixing |
| `src/Hotel.Game/Interface/HotelHud.cs` | The `rusty.hotel.hud` projection: every UI fact written once, grouped by HUD region or screen |
| `src/Hotel.Game/Interface/UiValueWriter.cs` | Writes the Engine `UiValue` node/edge/UTF-8 tree and compares two values; the pinned SDK has no builder |
| `src/Hotel.Game/Interface/HotelDebugCommands.cs`, `HotelDeveloper.cs` | The generated Hotel command catalog and what it does: observation, look, explicit developer overrides (goto, module viewer, floor entry, return to entrance) and floor inspection with the floor's places, spaces and links |
| `src/Hotel.Game/Interface/SuppliesDebugCommands.cs` | Explicit developer supply, health and consumption fixtures over the supplies owner |
| `src/Hotel.Game/Hotel.Game.csproj` | Product identity, entry, content/UI roots and host defaults, including scene lighting: shadows on and no Engine default world lights |
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
camera to the new pose. Pending use, primary, secondary and swap-hands edges clear at
these same boundaries. No jump action is implemented. Restart restores the established refuge checkpoint
and clears readings. Physical Q calls the equipped spirit: `HotelSpirit` checks the pact's
own eligibility and passes its call action to Combat's pact slot, which admits, costs and
times it and lands its hold through the one resolution; the spirit's visit follows the landing. Shutdown releases the checkpoint store and spirit/combat appearances,
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
`FieldCase` pockets retain Engine identities only (a stack id or a single item's
entity, with a generated item's resolved roll, whose quality and affixes shape its
worn source); quantities, containment and capacity live in the Engine ledger, which
refuses an edit over a weight or space limit. Single items are worn through the
same store's equipment: the Engine checks slot classifications, slot counts and
exclusivity, and each equipment receipt's source activations become the stat
sources `HotelSupplies` hands to the investigator's `ActorStats`.
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
Keys 3–5 use pockets 0–2 through the hands: the item's use action is admitted and
timed like any other, and the supplies owner uses the item, at the revision it was
chosen at, when the action lands; clear/pause/resume cancel pending presses.
Paused field-case use is immediate. These pockets already participate in the whole checkpoint.
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
physical state supplies primary, secondary and swap-hands edges; pending edges
bridge zero-step batches and clear on input clear/pause/resume. Holding a control
does not repeat. Input never names a weapon: primary uses the first action of the
item in the main hand (or the off hand when the main hand is empty), secondary its
second action or else the off hand's first, and swap-hands trades the two hands'
items through one Engine equipment edit. Weapons are held items (`supplies/items.json`)
granting actions (`actions/actions.json`).

Every action, the investigator's and the residents', runs through one pipeline.
`ActionUser` refuses while busy or cooling down; the caller checks and spends the
action's track costs once at acceptance and locks the aim. The windup ends on an
admitted step and the action lands once through `ActionResolution`, which uses
Engine spatial queries in the scene session: a swept capsule (melee), a ray
(hitscan), segment casts each step (projectile), an AABB overlap plus a clear line
per body (area), or nothing (self). A landed hit scales each damage packet by the
user's stats, adds the user's outgoing and the target's incoming worn contributions,
then applies the target's resistance and wards, then the action's effects. An item
cost (a reload's cartridges) is used through the supplies owner when the action
lands. Commitment and recovery follow; misses and blocked shots keep their cost.
Actions in progress and cooldowns are not saved.

Residents hold real identities in the existing scene EntityStore. Positions and
motion come from Engine character receipts; health uses Engine Track. Each step
`ResidentSenses` runs one Engine visibility query for every watching resident's
cone against every living body; a hostile body seen, or within near sense with a
clear line, is noticed, and awareness of the nearest lasts the kind's memory.
`ResidentConduct` then turns an aware resident to its target, chooses the first
of its actions in reach, or closes in within its leash; an unaware one keeps its
post, walks its patrol or walks back. Hostility is by faction, so residents of
hostile factions fight each other through the same actions. The porter approaches only with clear sight inside its authored territory,
stops at striking range, raises its hammer, commits to a direction and recovers. It uses static hotel
collision while approaching; player movement includes live resident body obstacles.
It does not search for routes or pursue around corners. The stationary Lamplighter
charges and fires along its locked direction; a sidestep or real world geometry
can stop its shot. No local navigation or collision mechanism is present.

`CombatView` owns only meshes/appearances and authored poses: raised arm, glowing
eye, strike, beam, recovery droop, flares in flight and the held item by its authored look. `HotelScene` publishes
one combined static/combat snapshot. The DOM receives what the hands hold, action state and
brief hit/hurt notices; it does not render weapons or aim attacks. Actions live in `content/actions/`,
resident kinds in `content/combat/`; resident placements in each excursion's `placements.json`. Zero health suppresses
movement, use and further attacks; R invokes the expedition owner to restore the whole saved refuge checkpoint.

## Spirit pacts and manifestation

`HotelSpirit` owns the roster's pacts: which spirits are freed, the one in the pact
slot, and the brief visit a call brings. `HotelRoute` adds each of the excursion's
bells to the existing Engine focus/visibility/use flow. Successful E use frees that
bell's spirit once and restores its authored welcome charges through
`HotelSupplies`; it removes the waiting creature from the pedestal. Freeing does
not equip it.

The field case's Spirits view claims `hotel.spirit.equip` (`v2`), carrying the
chosen spirit (or none, to let the equipped one rest) and the pact revision.
`HotelProduct.Update` and `HandlePausedIntents` route those direct payloads to the
same domain rule. Engine owns delivery, lifecycle, ordering and stale bindings;
Hotel checks the pact was made, the current revision, an active visit and defeat.
Browser state never predicts an equipped pact. Paused delivery publishes UI facts
without advancing movement, combat or spirit phase. Pending physical Q edges clear
on input clear/pause/resume.

Q checks the pact's own eligibility (equipped, health, a current visit, charges,
and for a call that reaches a resident an Engine ray to one under the reticle),
then hands the equipped spirit's action to Combat's pact slot, which spends the
charge once, times it and lands it through the one resolution: a hold, a ward and
reveal on the investigator, a push or a lure of the residents in its area. A pushed
or lured resident is moved by the effect and does nothing else meanwhile. The visit
follows the landing; no health damage or persistent follower is added.

`SpiritView` builds one articulated moth for each spirit of the roster from its
authored look (bell head, eyes, antennae and scalloped wings) and contributes it
to `HotelScene`'s combined snapshot: idle at its bell until freed, then only during
its visit. Authored poses progress only on admitted steps. Arrival grows and flies
toward where the call landed, holding spreads the wings, and departure folds and
shrinks upward. This presentation has no collision authority or separate
animation clock. Pausing freezes it; defeat withdraws it. Checkpoint recovery
restores the pacts made and the pact slot and clears transient visits. Spirit
identity, call action, look, timing and description live in
`content/spirits/<id>.json`, listed by `content/spirits/roster.json`; bells are
excursion placements, and generated floors draw their bell's spirit by weight.

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
equipped choice, worn and held items, opened doors, resident stats/position/yaw,
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
The saved refuge identity and what the hands hold are meaningful player state;
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
