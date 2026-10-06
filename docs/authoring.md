# Authoring the hotel

[Design](design.md) owns the direction; [architecture](architecture.md) names
the mutable owners. `content/` is the authored data, organized by domain. Each
file loads into one domain's typed record through Engine content services. Those
records (`*Definition.cs`, `*Tuning.cs` beside their owner) are the field reference.
There is no executable content scripting or second scene/state authority in the DOM.

Files are strict. An unknown member, a missing required value, a null in a
non-nullable field or a reference to an unknown id stops loading, with a message
naming `content/<file>` and the field. Each domain's loader also checks its own
file's shapes and ranges before any owner sees them: positions and directions are
three finite numbers, boxes and rooms have max strictly above min (rooms on x and z), counts, stack limits and
health are at least 1, durations are positive, fractions stay within 0–1, volumes
within 0–1. Checks that span files, such as known ids or a find fitting an empty
field case, run afterwards in `HotelContent`. When you add an authored value, add
its check beside the others. Fix the
data; there are no silent defaults.

## Where a change belongs

Reusable definitions live by domain. Where one excursion places things lives in
its own folder. Geometry is kept apart from tuning.

| File | Meaning and owner |
| --- | --- |
| `input/bindings.json` | Every control: Engine keys/pointer buttons for game actions, browser codes for screen shortcuts, labels, Controls-screen names, one binding per weapon (in weapon order) and per quick pocket, and the opening hint; `HotelControls` |
| `player/tuning.json` | Body/camera dimensions, movement and look tuning; `HotelPlayer` |
| `route/interaction.json` | Reach and focus distances/angles for every world interaction; `HotelRoute` |
| `route/messages.json` | Focus prompt wording and the labels of the take/notebook interactions; `HotelRoute` |
| `interface/tuning.json` | Field-case pocket count and how many appear as HUD quick pockets; `HotelHud`, `HotelSupplies` |
| `scene/surfaces.json` | Reusable surfaces: tint, texture, world-metre tile size, roughness, emission; `HotelScene` |
| `mechanics/stats.json` | The stat vocabulary every actor shares: attributes, stats derived from them, and the resource tracks those bound; `ActorStats` |
| `mechanics/damage.json` | Damage kinds and the bounds of each actor's resistance to them; `ActorStats` |
| `player/stats.json` | The investigator's stat block: attributes, derived bases, resistances and starting track points; `HotelSupplies` |
| `supplies/items.json` | Item kinds, effects, stack limits, names and descriptions (`{amount}`); `HotelSupplies` |
| `supplies/messages.json` | Supply notices, refusal reasons and how long a notice stays up; `HotelSupplies` |
| `combat/tuning.json` | Reload time and how long combat notices, hit and hurt flashes last; `HotelCombat` |
| `combat/weapons.json` | Weapon commitments, damage and its kind, range, ammunition cost, short name and HUD phase labels; `HotelCombat` |
| `combat/residents.json` | Resident kinds: behavior/silhouette, stat block, damage and its kind, sight, timing, leash, body and eye height; `HotelCombat`, `CombatView` |
| `combat/messages.json` | Combat notices and HUD action states; `HotelCombat` |
| `spirits/<id>.json` | One spirit's pact terms, cost/range, manifestation timing and placement offsets, its description and its own wording (call hint and result, phase names); `HotelSpirit` |
| `spirits/messages.json` | Pact notices and refusals shared by every spirit, using `{spirit}` and `{place}`; `HotelSpirit` |
| `expedition/messages.json` | Checkpoint receipts and status lines; `HotelExpedition` |
| `scene/kit.json` | The architectural kit: wall, floor and ceiling thickness, door-leaf tuning, trim styles (bands, moulding profiles and architraves), door frames and space styles (surface sets); `KitBuilder` |
| `scene/fixtures.json` | Reusable fixtures: lamps, sconces, desks, tables, packets, frames, pictures. Each is boxes in its own frame, plus lights and named sockets; `KitBuilder` |
| `excursions/<id>/plan.json` | The floor plan: spaces, the links between them, the fixtures placed in them, ambient light and GLB props; built by `KitBuilder` |
| `excursions/<id>/route.json` | Its doors (hung in door links, with the locked-side prompt), readable notices at sockets, stairs (direction and the fixture socket they are used at), and the fallback location label; `HotelRoute` |
| `excursions/<id>/placements.json` | Arrival point, refuge notebook socket, item finds at sockets, placed residents (id, kind and the post they stand on) spirit bells (socket, and the `place` their text names), and `fromAbove`, where the player stands after coming down the stairs |
| `floors/mission.json` | How generated floors' mission graphs grow: size budget, rule steps by depth, each rule's weight and per-floor limit, and the rules every floor finishes with; `MissionGenerator` |
| `floors/content.json` | How generated floors are furnished: the bell's spirit, the fallback location label, the display fixture each item is shown with, objective and supply item pools, finds per stop, loose supplies and extra residents by depth, which resident kinds stand in which module tags, the pacing budgets (recovery before hazards and residents, ammunition range by depth, the arrival margin), and how locked doors, the shortcut's latch and their keys look and read; `ContentPlacement`, `ContentPacing`, `FloorExcursion` |
| `floors/readings.json` | Notices generated floors may show at reading sockets, each used at most once per floor |
| `floors/generation.json` | How hard floor generation tries (layout attempts per mission graph, candidates per floor) and the navigation cell size and search budget floors are confirmed with; `FloorGenerator` |
| `floors/layout.json` | How mission graphs become modules: floor extent, rooms by depth, the modules that may stand for each kind of place and the doorway they join by, spine and fill weights, the service passage, tries and branching; `FloorEmbedding` |
| `floors/modules.json` | The room-module catalog: authoring lattice, generated floors' ambient light, the link, size and frame each doorway kind is built with, and the module files that exist; `ModuleCatalog` |
| `floors/modules/<id>.json` | One room module: kit spaces, links and fixtures in its own north-facing frame, tags, doorways and content sockets; `ModuleCatalog`, `ModuleCheck` |
| `excursions/<id>/ambience.json` | Its ambient loops: content path, gain and optional world position/range; `HotelAmbience` |

Runtime assets (`materials/`, `models/`, `audio/`) stay in their own folders and are
referenced by path. When a new domain or kind of authored data arrives, give it its
own file in the matching folder rather than appending it to a neighbour.

### Player-facing text

Gameplay text lives with the domain that shows it, in its `messages.json` or in the
definition it describes. Write values as `{placeholders}` rather than repeating a
name or number: `{spirit}`, `{item}`, `{range}` and so on are filled from the
definitions at runtime. Name a control as `{key.reload}`, `{key.use}`, `{key.fieldCase}` and
so on; it becomes that control's label from `input/bindings.json` when the file loads.
Each text field accepts a fixed set of placeholders. An
unknown one fails loading with the file, the field and the allowed set. Text that
belongs to one place (a door's locked prompt, where a spirit's bell hides) is
authored with that placement. A screen's fixed chrome stays in its UI module, and
developer-console output stays in code.

These fields tune the implemented vocabulary. Additional weapon types, resident
behaviors or multiple spirits need a deliberate change to their domain owner;
adding arbitrary JSON entries alone does not implement them.

## Stats and damage

Every actor's numbers are Engine stats in one vocabulary (`mechanics/stats.json`). Attributes are base stats with
bounds. A derived stat has a base and bounds, and each point of an attribute named in its `from` adds `perPoint`; that
addition is an Engine stat source, so a value can be explained (`Explain`) down to the attribute it came from. A track
is a resource pool (health, stamina, summon charges, ammunition) bounded by a derived stat; raising its maximum keeps
its points. Damage has a kind (`mechanics/damage.json`); each actor holds a resistance to every kind, the fraction of
a hit it removes, with a negative resistance a weakness. A hit lands rounded after resistance and never takes more than
the health left.

An actor's stat block (`player/stats.json`, a resident kind's `stats`) gives every attribute, may set a derived stat's
own `bases`, lists its `resistances` (absent is none), and its tracks' `initial` points (absent is full). A missing
attribute, an unknown id or a value out of bounds fails validation naming the file and field. Weapons and resident
kinds name the `damageKind` they deal. Add a stat, track or damage kind to the vocabulary once; every block then
validates against it.

## Authoring a floor

Floors are authored as rooms, not boxes. The kit builds every wall, trim run, frame, floor and ceiling.

Positions are world metres, with Y up; north is −z, south +z, west −x and east +x. The arrival point is a body
centre, not feet or eyes. Fields named `Degrees` are degrees.

**Spaces.** A space is a room or corridor. `min`/`max` give `[x, z]` on its *wall centrelines*, and `style` names
a surface set in the kit, which `floor`, `wall` or `ceiling` may override. Spaces that touch share the wall
between them. Each space builds the half of every wall on its own side, in its own wallpaper, with its floor's trim style
on its face. Spaces never overlap. Every space is also a named room for the HUD location. A space's `posts` are
named `[x, z]` floor points inside it, addressed as `"space.post"`; residents stand on them.

**Links.** A link joins two touching spaces along the one wall they share:
- `Open` removes the whole shared wall.
- `Door` cuts an opening of `width` × `height` centred at `at` along the wall, with an optional kit `frame`.
- `Passage` is a door without a leaf, open to the ceiling unless it has a `height`.
- `Hatch` is a raised opening: `height` tall above a `sill`, with wall (and its skirting) below. Only a hatch has a
  sill.

Openings on one wall never overlap and lie within the wall the two spaces share.
Above an opening the wall continues as a lintel, and trim bands above the opening (picture rail, cornice) run
across it. A shared wall without a link stays a solid partition. An outer wall is any edge with no neighbour.

**Fixtures.** Each placement names a catalog `kind` and how it mounts:
- Floor or ceiling fixtures stand in a `space` `at` `[x, z]`, with optional quarter `turn`s and `mirror`.
- Wall fixtures sit on a space's `edge`, `along` it, facing into the room.
- Socket fixtures go `on` an earlier fixture's socket, written `"instance.socket"`.

Every part of a fixture, after its turn and mirror, stays inside its space between the walls' inner faces; a
socket fixture stays inside the space of the fixture it sits on. Giving a placement an `id` makes its sockets
addressable. A fixture whose parts show a find names the `find`, and
those parts disappear when it is taken. Fixture lights are the floor's point lights; a placement may override
`intensity` and `range`, and may make its lamp a failing one with `flicker` (`depth` 0–1 of its intensity it sags by,
about every `seconds`). Flicker is sparse and slow, and holds still while the game is paused; give at most one
lamp a floor a flicker, never one the player reads a threat by.

**Sockets.** The route's readings, the refuge notebook, finds and spirit bells name fixture sockets
(`refuge-desk.notebook`), and residents name posts (`west.porter`), not coordinates, so moving a desk or a room moves
everything on it. A resident's body centre stands half its kind's height above its post. Socket names are unique. Doors name their door link, the end they hinge at
(`Start` is the opening's lower coordinate), the space they open into, and for a latch the space it opens from.
The kit derives the leaf's pose, size and use prompt from the opening.

**Trim styles.** A floor plan names its `trimStyle`, one of `scene/kit.json`'s `trimStyles`. A style gives the bands for
each trim role a space style names (`dressed` rooms and corridors, `plain` service spaces), and every style must
dress every role. A band is a plain box `depth` proud, or, with a `profile`, a moulding: `[out, up]` points from the
wall face at the band's foot round to the wall face at its head, swept along every wall run. A style's `architraves`
dress door frames by frame id with a `[out, across]` profile swept up both jambs and across the head on both wall
faces. The west wing names its style in `plan.json`; a generated floor draws one of `floors/layout.json`'s
`trimStyles` and keeps it through every shift, and the module catalog names the style a module is built in alone.

**Fixture models.** A fixture may show authored GLB `models` at an `offset` in its frame, turned with it; a model
never collides and is not mirrored, so give a mirrored fixture a symmetric model. Where it must block, give it a
`collider` part: a solid box that is never drawn. Furniture is built this way, its models fitted to the colliders'
footprint so its sockets stay where finds and readings rest. A fixture needs at least one part or model.

A new kind of furnishing goes into `scene/fixtures.json` once, then is placed wherever it is wanted. A new
surface set or trim style goes into `scene/kit.json`. One-off geometry has no place in a plan; make it a fixture.
Inspect a built floor with the developer command `hotel.dev.goto <space>`.

**Lighting.** Hotel lights itself: the Engine's default world light rig is off and shadows are on
(`Hotel.Game.csproj`), so a floor is lit by its ambient (`lighting` in the plan) and its fixture lamps alone. A
fixture light's `shadow` says whether it may cast. Each shadowed point light renders the scene six more times a
frame, so `content/scene/look.json` `shadows` grants shadows to at most `budget` lamps at once: those in the eye's own
room first (a lamp in another room counts `otherRoomPenalty` metres farther), then by distance, with lamps already
casting held by `hysteresis` metres so the choice does not swap back and forth. The same file sets the camera's tone
mapping and exposure and the linear haze that swallows distance. Check new lighting from eye height for the
readability of doors, finds, resident tells and the spirit, and record its frame time.

Material IDs connect fixtures, styles and doors to surfaces. `tileWidth`/`tileHeight` set
world-metre repeats, and a textured surface may add a `normalMap` (a linear tangent-space map tiled like its texture)
with its `normalScale`; give both or neither. Keep wallpaper relief faint (printed paper, not stucco); room geometry projects UVs from world position so adjacent
wall sections share a pattern phase. Inspect near/far repeats at eye height in
motion. Preserve the broad wallpaper/fine dark carpet hierarchy from the two
original images in `docs/references/`.

Built walls, floors, ceilings and solid fixture parts take part in Engine collision. Decorative GLB props are visual
only; place them on solid furniture. A model does not become an interaction
simply by appearing in the scene. Add new world actions as `Interactable`
entries and a use handler in `HotelRoute`. A find's ID ties the fixture parts that show it to the collected
state; they disappear only after successful inventory admission. Door open/closed appearance and collision use the
same derived poses.

Add a notice or field log as a fixture with a `focus` socket, plus a `readings` entry in the excursion's
`route.json` with a stable ID, that socket, label, title and text.
It uses the existing reading screen and pause/resume flow. Readings themselves
do not grant items. Resource effects remain in Supplies; encounters remain in
Combat; a contextual receipt observes Expedition's result.

## Authoring a room module

Generated floors are assembled from room modules. A module is a small floor plan in its own frame: `[0, 0]` is its
north-west corner and `size` is `[width, depth]` in metres, with the module authored facing north and given quarter
turns when it is placed. Its spaces, links and fixtures are written exactly as in a floor plan, and every wall
centreline, size and doorway position lies on the catalog's `cell` lattice.

**Doorways** are where neighbours join. Each names its `kind` (`CorridorDoor`, `ServiceDoor`, `FireDoor`, `Archway`
or `Stair`), the `space` it opens from, the `edge` of that space, which must be the module's outer wall, and the
position `at` along it. Two placed doorways mate when they are the same kind at the same point on facing walls; the
catalog's doorway style for that kind then builds the link, so every corridor door is the same size and frame. An
unmated doorway stays wall. A `Stair` doorway cuts no wall: it marks where the flight to other floors leaves.

**Content sockets** say what a module offers floor content: a `Find`, `ResidentPost`, `Reading`, `Landmark`, `Bell`
or `Notebook`, each naming a fixture socket (`table.top`) or a space post (`room.post`). Modules never decide finds
themselves: no module fixture names a `find`. **Tags** (`Corridor`, `Guest`, `Service`, `Public`, `Refuge`,
`Landmark`, `StairCore`, `DeadEnd`, `SetPieceOnce`, `Keepable`) tell the floor grammar where a module may go.

Every module passes an isolated self-check when content loads. It is built alone with a porch outside each doorway,
and its floor is walked on a 0.1 m planning grid for the player's body: anything solid between step height and head
height, widened by the body radius, blocks. Every doorway must reach every other, every resident post must be on
reached floor, and every other socket must be within interaction reach of it. A fixture that blocks a doorway fails
naming the module file and that doorway. Add a module by writing its file and listing its id in `floors/modules.json`;
view it with `hotel.dev.floor.module <id> <turn>`.

Generated content takes its ids from where it stands: a find, resident or notice is `<placement>/<socket id>`
(`p7/table`), so the same floor always names it the same way. A stair core needs one `Arrival` socket, where the
player stands on entering the floor.

## Tuning generated floors

A generated floor is made in stages, each from its own file. Every random choice is a keyed Engine draw, so the same
run, depth and shift always make the same floor.

1. The mission graph (`floors/mission.json`): places and the locks between them, grown by weighted rules and
   validated there and back.
2. The layout (`floors/layout.json`): modules for each place, joined socket to socket, the service passage and fill
   rooms, checked so every lock cuts what it guards.
3. The content (`floors/content.json`, `floors/readings.json`): finds, keys, residents, the bell and notices at
   content sockets, within the pacing budgets.
4. Engine confirmation (`floors/generation.json`): every promised route walked both ways for the player's body, with
   retries by attempt and then by candidate.

Change what a floor holds in content and the module files, not in code. Any change that alters generated floors
needs a generator version bump and new goldens (see [development](development.md)).

## Content identities and checkpoints

Checkpoint versioning lives in `Expedition/CheckpointState.cs` and
`HotelExpedition.Validate`. Saved values reference authored refuge, item, find,
door, weapon, spirit and resident identities. Keep those IDs stable when editing
labels or art. Pocket count, stack/resource bounds, resident roster/health/leash
and other validation rules also affect whether an existing save remains valid.
There is no automatic migration or fallback to a new game for invalid data.

Version 2 adds the run: its seed and every generated floor visited in it, kept as the floor's identity and resolved
plan (mission graph, layout and content, never boxes), with what the player left it as (open doors, residents, keys)
and the finds collected on it. A stored floor is rebuilt from its plan without drawing, after its generator version
and the canonical hash of its plan are checked; a floor made by another generator version is refused like any other
invalid data, so a generator change is a version bump in `FloorSeed`. Generated ids are `floor-<depth>/<placement>/<socket>`.
Version 3 keeps the investigator's resources as Engine stats: every stat's base and every track's current points,
restored bases first, then the derived sources they feed, then track points. Version 4 keeps each resident's stats the
same way, on the floor it was left on and in the refuge, beside its pose. A checkpoint of another version is refused
like any other invalid data.

Recording the refuge checkpoint marks every visited floor due to shift; the next climb to it generates it again under
the next shift number around its kept set (stair core, landmark, every door the player opened or holds the key to,
with the rooms on both sides, and the shortcut passage once unlatched), whose placements keep their ids. A kept door
hangs again as `floor-<depth>/door/<link>`, open if it was opened; while its key is held, opened or not, it stays
locked to that key, which becomes `floor-<depth>/key/<link>` so no new lock shares it. A kept unlatched shortcut is already open, so the shifted mission
graph hangs its shortcut off a place no lock guards. New placements of a shift are numbered `s<shift>p<n>`, so a re-rolled room never reuses an
old room's ids. Secured generated finds are kept by item in the run, since their rooms may shift away.

### Adding saved state

1. In the owning domain, extend its `Capture`, `Validate` and `Restore`. `Restore`
   starts from `Reset`, so the authored starting value belongs in `Reset` too.
2. Add the field to `Expedition/CheckpointState.cs`. Bump `Version` only for an
   intentional incompatible change (see below).
3. For a new owning domain only: call it from `HotelExpedition.Capture`, `Validate`
   and `Apply`. `Apply` is the one restore path, used for loading, recovery and the
   developer reset alike; place the call where its comment's ordering requires.
4. Optionally expose it in `HotelObservation` (`HotelProduct.cs`) for inspection.

Nothing else restores state. The initial excursion is captured once from the
owners' starting values, so there is no separate reset list to keep in step.

When adding meaningful persistent state, extend its existing owner's
capture/validate/restore methods and the single complete checkpoint value.
Validate before restore; save before settling expedition deposits. Keep native
handles, input edges, unfinished attacks and transient summon poses out of saves.
Recovery deliberately starts at the refuge with the stored resource values.

Saved values reference ids, never file layout, so files can be split or moved
without touching saves. Placed residents keep their own `id`; their `kind` may change.

Treat an intentional incompatible content change as such. Use an isolated fresh
development save for authoring checks; do not silently clear a user's save or
introduce a compatibility framework without an actual product requirement.
Check the changed ordinary world/UI path and the corresponding domain checks;
update checkpoint checks when persistent meaning changes.

## Source assets

Keep runtime PNG/GLB/WAV files in `content/`. Keep original generated images,
exact prompts, editable models, conversion scripts, portable hashes and final
use limitations in `art/`; see [asset authoring](../art/README.md). Keep provider
credentials and temporary signed URLs out of both docs and provenance.
Offline tools never run as a prerequisite of `rusty dev` or `rusty build`.

Generator/reviewer execution history belongs in Den. Preserve the final asset's
bounded purpose here: the recorder is static desk dressing, while primitive
residents and Hushwing are authored presentations in the existing scene. Judge
new assets beside those materials and silhouettes in actual Hotel lighting.
