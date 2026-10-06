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
| `input/bindings.json` | Every control: Engine keys/pointer buttons for game actions, browser codes for screen shortcuts, labels, Controls-screen names, one binding per quick pocket, and the opening hint; `HotelControls` |
| `player/tuning.json` | Body/camera dimensions, movement and look tuning; `HotelPlayer` |
| `route/interaction.json` | Reach and focus distances/angles for every world interaction; `HotelRoute` |
| `route/messages.json` | Focus prompt wording and the labels of the take/notebook interactions; `HotelRoute` |
| `interface/tuning.json` | Field-case pocket count and how many appear as HUD quick pockets; `HotelHud`, `HotelSupplies` |
| `scene/surfaces.json` | Reusable surfaces: tint, texture, world-metre tile size, roughness, emission; `HotelScene` |
| `mechanics/stats.json` | The stat vocabulary every actor shares: attributes, stats derived from them, and the resource tracks those bound; `ActorStats` |
| `mechanics/damage.json` | Damage kinds and the bounds of each actor's resistance to them; `ActorStats` |
| `mechanics/effects.json` | Every effect an actor can bear: name, mark, stacking group and rule, duration and one kind's settings; `ActorEffects` |
| `mechanics/messages.json` | How an active effect's time, stacks and ward read on the HUD |
| `player/stats.json` | The investigator's stat block: attributes, derived bases, resistances and starting track points; `HotelSupplies` |
| `supplies/items.json` | Item kinds: form (stack or single), stack limit, classifications, capacity costs, and one role (use, wear or deposit); names and descriptions (`{track}` for what a use restores); `HotelSupplies`, `FieldCase` |
| `supplies/equipment.json` | Item classifications and the investigator's equipment slots with the classifications each accepts; `FieldCase` |
| `supplies/capacity.json` | The field case's capacity metrics (weight, space) and their limits; `FieldCase` |
| `supplies/messages.json` | Supply notices, refusal reasons and how long a notice stays up; `HotelSupplies` |
| `combat/tuning.json` | How long combat notices, hit and hurt flashes last; `HotelCombat` |
| `actions/actions.json` | Every action a hand or a resident uses: delivery, cost, timing, damage packets and their stat scaling, effects, HUD phase labels; `ActionResolution`, `HotelCombat` |
| `player/kit.json` | What the investigator wears and holds at the start and after a reset; `HotelSupplies` |
| `combat/residents.json` | Resident kinds composed from parts: look, faction, stat block, perception, movement, action choices, body and eye height, the loot table its remains give and the experience felling it gives; `HotelCombat`, `ResidentSenses`, `ResidentConduct` |
| `combat/held.json` | First-person held items: the hand's rest and phase offsets, the muzzle flash size, and per held look (`HeldLook`) its model, offset, rotation, scale and muzzle point, in camera space; `CombatView`, drawn on the Engine viewmodel layer |
| `combat/looks.json` | Resident silhouettes as boxes in their own frame, with arm, body and tell roles and the tell poses; `CombatView` |
| `combat/factions.json` | Factions, which pairs are hostile, and the investigator's faction; `HotelCombat`, `ResidentSenses` |
| `combat/messages.json` | Combat notices and HUD action states; `HotelCombat` |
| `progression/growth.json` | Levels (experience thresholds and what each level past the first adds) and skills grown by use (what practises them, the uses each rank needs, what each rank adds); `GrowthDefinition`, `InvestigatorGrowth` |
| `progression/messages.json` | What using a relic or tome says, and how growth since the last return reads on the refuge receipt |
| `loot/tables.json` | Loot tables: how many draws, and each entry's item (or nothing), count range and depth weight; residents' remains and containers name one; `LootCatalog` |
| `loot/qualities.json` | Quality tiers for generated single items: name around `{item}`, stat scale, affix count, depth weight; `LootCatalog` |
| `loot/affixes.json` | Affixes: name around `{item}`, the classifications they join, worn stats, hit contributions, on-hit effects, depth weight; `LootCatalog` |
| `spirits/roster.json` | Every spirit a pact can be made with; `HotelSpirit` |
| `spirits/<id>.json` | One spirit: welcome charges, its call `action` (costing only summon charge), its moth's `look` (`body` and the left and right `wings` as GLB models, each wing's `hinge` in the body's frame, and `scale`), visit timing and placement offsets, its description (`{range}`, `{cost}`, `{seconds}` from its action) and its own wording; `HotelSpirit`, `SpiritView` |
| `spirits/messages.json` | Pact notices and refusals shared by every spirit, using `{spirit}`; `HotelSpirit` |
| `expedition/messages.json` | Checkpoint receipts and status lines; `HotelExpedition` |
| `scene/kit.json` | The architectural kit: wall, floor and ceiling thickness, door-leaf tuning, trim styles (bands, moulding profiles and architraves), door frames and space styles (surface sets); `KitBuilder` |
| `scene/fixtures.json` | Reusable fixtures: lamps, sconces, desks, tables, find displays, frames, pictures. Each is boxes and GLB models in its own frame, plus lights and named sockets; `KitBuilder` |
| `excursions/<id>/plan.json` | The floor plan: spaces, the links between them, the fixtures placed in them, ambient light and GLB props; built by `KitBuilder` |
| `excursions/<id>/route.json` | Its doors (hung in door links, with the locked-side prompt), readable notices at sockets, stairs (direction and the fixture socket they are used at), and the fallback location label; `HotelRoute` |
| `excursions/<id>/placements.json` | Arrival point, refuge notebook socket, item finds at sockets, placed residents (id, kind and the post they stand on) spirit bells (socket, and the `place` their text names), and `fromAbove`, where the player stands after coming down the stairs |
| `floors/mission.json` | How generated floors' mission graphs grow: size budget, rule steps by depth, each rule's weight and per-floor limit, and the rules every floor finishes with; `MissionGenerator` |
| `floors/content.json` | How generated floors are furnished: the bell's spirit, the fallback location label, the display fixture each item is shown with, objective and supply item pools, finds per stop, loose supplies and extra residents by depth, searchable containers and how many each floor holds, which resident kinds stand in which module tags, the pacing budgets (recovery before hazards and residents, ammunition range by depth, the arrival margin), and how locked doors, the shortcut's latch and their keys look and read; `ContentPlacement`, `ContentPacing`, `FloorExcursion` |
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
definitions at runtime. Name a control as `{key.secondary}`, `{key.use}`, `{key.fieldCase}` and
so on; it becomes that control's label from `input/bindings.json` when the file loads.
Each text field accepts a fixed set of placeholders. An
unknown one fails loading with the file, the field and the allowed set. Text that
belongs to one place (a door's locked prompt, where a spirit's bell hides) is
authored with that placement. A screen's fixed chrome stays in its UI module, and
developer-console output stays in code.

These fields tune the implemented vocabulary. A new weapon is a held item with
actions, authored in content alone; a new delivery kind, resident behavior or
multiple spirits need a deliberate change to their domain owner;
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
attribute, an unknown id or a value out of bounds fails validation naming the file and field. Actions name the
`kind` of each damage packet they deal. Add a stat, track or damage kind to the vocabulary once; every block then
validates against it. A derived stat names the `quantum` its value rounds to (0 for none); `pace` is the derived stat
movement is scaled by, and the vocabulary must have it.

## Items and equipment

An item (`supplies/items.json`) is an Engine item definition. `form` is `Fungible` for things that stack up to
`stackLimit` or `Unique` for single things (stack limit 1). `classifications` name what kind of thing it is
(`supplies/equipment.json`); `costs` give its units of every capacity metric (`supplies/capacity.json`), and the field
case refuses a find whole when the Engine finds it would pass a limit, naming that metric. Each item has exactly one
role:

| Role | Settings | Meaning |
| --- | --- | --- |
| `use` | `restores` (track to points), `effects`, `action`, and for a relic or tome `growth` (`stats`, `practice`, `experience`; see Growth) | Used from a pocket or a quick key; consumed one at a time. From a quick key during play it is `action` (a self action), timed through the hands before the item is used |
| `wear` | `slots`, `exclusive`, `stats` | A single item worn in that many equipment slots that accept one of its classifications; `stats` add to stats or resistances (`resistance.<kind>`) as an Engine source while worn; no two worn items share an `exclusive` group |
| `deposit` | `true` | An expedition find, carried back and deposited at the refuge |

Slots (`supplies/equipment.json`) are the hands, the worn slots and the rings; a slot `accepts` classifications,
and exactly one slot is each `hand` (`Main`, `Off`). An item worn in a hand is held: its `wear.actions` give its
primary and secondary actions (at most two) and `wear.look` how it is drawn (`Bar`, `Pistol`, `Bell`, `Flare`); a
worn item has neither. `wear.contributions` change hits its wearer deals (`Outgoing`) or takes (`Incoming`), for
the listed damage kinds or all of them, at one `stage`: `Hit` (`prevent`: the hit is turned aside, with no damage and
no effects), `Damage` (`add` then `multiply`, before resistance) or `Applying` (`add` then `multiply`, after resistance
and before wards and health). At each stage the user's outgoing contributions come before the target's incoming ones.
A `Defeating` contribution (`retain` health from a killing blow) is spent when used, so it comes only with an effect
(`guard`). `player/kit.json`
names what is worn and held at the start, by item and slots.
Wearing into a full slot trades places with what it held. The first `quickPockets` pockets are the belt the quick keys
use: consumables are stacks, and Engine equipment holds single items only, so the belt is pocket layout rather than an
equipment slot. A description names a restored amount by its track (`{health}`).

## Loot and generated items

A single worn or held item placed on a generated floor, or given by a loot table, is generated: a quality is drawn by
its depth weight (`loot/qualities.json`), then that many distinct affixes that fit the item's classifications
(`loot/affixes.json`). Its name reads through its quality and affixes; its quality scales its own worn stats, and each
affix adds stats, hit contributions or effects its holder's hits put on what they strike. The resolved roll (item,
quality, affixes) is placed, carried, worn and saved; nothing is drawn again for it.

A loot table (`loot/tables.json`) draws `rolls` times; each draw picks an entry by its depth weight (an entry with no
`item` gives nothing) and a count in its range. A resident kind names the table its remains give (`loot`); generated
floors place `containers` (`floors/content.json`: a name, a socket fixture with a focus and no find parts, a table and
a depth weight; `containersPerFloor`). Remains are searchable once their resident has fallen, containers at once;
each is searched once, by the run seed, the depth (the west wing counts as the first) and its own id, so a restored
checkpoint gives the same things. A search gives everything it found or, short of room, nothing and waits.
A search made is collected like a find: it stays made when the player leaves and returns, and the checkpoint keeps
it. When a floor shifts, its residents start fresh, so their remains can be searched again.

Weights in `floors/content.json` (supplies, objectives, residents) are depth curves (`base`, `perDepth`, `max`), so
deeper floors draw different things inside the same pacing budgets.

## Growth

The investigator grows through a run; nothing is chosen or allocated. Each level, skill rank and relic is an intrinsic
Engine source on the investigator's stats (`growth.level`, `growth.skill.<id>`, `growth.relic.<item>`), so a stat's
explanation names it.

- **Experience and levels** (`progression/growth.json` `levels`): felling a resident gives its kind's `experience`;
  `experience` lists the threshold of each level from the first (0), and `perLevel` is what every level past the
  first adds.
- **Skills grown by use** (`skills`): a landed hit that takes health with one of a skill's `practice.damageKinds`, or one
  of its `practice.actions` landing (a pact's call), is one use. `ranks` are the uses each rank needs, increasing; the
  last is mastery, past which practice is not counted. Each rank adds `perRank` stats and the skill's hit
  `contributions` once more.
- **Relics and tomes** are items whose `use.growth` teaches: `stats` are permanent (a relic, kept as its own source
  for the run), `practice` gives uses of skills (a tome) and `experience` adds to it. Using one consumes it; a tome
  whose skills are mastered is refused and kept.

Growth is saved with the supplies and restored before the stats, so track maximums include it. A defeat returns to the
checkpoint's growth like everything else. The refuge receipt names the level and ranks reached since the last return
(`progression/messages.json`).

## Residents

A resident kind (`combat/residents.json`) is composed from parts; adding a kind from existing parts is a content edit.

| Part | Fields | Meaning |
| --- | --- | --- |
| `look` | a look id | its silhouette and tells (`combat/looks.json`): boxes with a `Body`, `Arm` or `Tell` role; `armWindup` and `armStrike` move the arm, `droop` lowers it in recovery, and `Tell` parts light during an attack |
| `faction` | a faction id | who it is hostile to (`combat/factions.json`); it notices and attacks hostile bodies, residents included |
| `perception` | `range`, `fieldOfView`, `memory`, `nearSense` | an Engine visibility cone from where it faces, a near radius it senses all around, and how long it stays aware after losing sight |
| `movement` | `speed`, `leash`, one of `post`, `patrol` (`points`, `pause`), `stalk`, `ambush` (`trigger`); optional `flee` (`below`, `distance`) | how it keeps its post when unaware and closes in within its leash when aware; an ambusher notices nothing until a hostile is within its trigger; a fleeing resident below that fraction of health backs away instead of attacking |
| `actions` | `action`, `minimum` | its attacks in order of preference: the first ready, affordable and within reach whose target is at least `minimum` away |
| `stats` | a stat block | as for every actor |

A resident placed by an excursion or a generated floor is restored within its movement's range (its leash or its
patrol's farthest point) of its post.

## Actions

An action (`actions/actions.json`) is used by a hand or by a resident (a kind names its `attack`). `delivery.kind`
decides how it reaches what it affects, each through an Engine spatial query, and which delivery fields it needs:

| Kind | Fields | Lands on |
| --- | --- | --- |
| `Melee` | `range`, `width` | the first body a capsule of that width meets, swept along the aim |
| `Hitscan` | `range` | the first body or surface along the aim |
| `Projectile` | `range`, `width`, `speed` | the first body or surface it meets in flight, cast step by step on admitted time |
| `Area` | `range` (0 is the user), `radius` | every body within the radius of the point along the aim, with a clear line from it |
| `Self` | none | the user: no damage, only `selfEffects` |

`cost.tracks` are spent once when the action is accepted, and an action whose cost cannot be met is refused whole;
`cost.item` names a classification, one carried item of which is used by its own use when the action lands.
`timing` gives `windup`, `commit`, `recovery` and `cooldown` in admitted seconds. Each `damage` packet has a `kind`,
an `amount` and `scaling` (a stat and how much each point adds). `windupLabel` and `commitLabel` are the HUD's
phase words. A resident starts its attack when the investigator is within its reach (an area's range plus radius).

## Effects

An effect (`mechanics/effects.json`) is something an actor bears for a while: an Engine effect entry in its stacking
`group`, with the product's time left, tick progress and ward. Each sets exactly one kind:

| Kind | Settings | While it lasts |
| --- | --- | --- |
| `stat` | `stat`, `amount` | adds `amount` per stack to an attribute or derived stat, as an Engine source (what derives from an attribute follows) |
| `restore` | `track`, `amount`, `interval` | restores `amount` per stack to the track every `interval` seconds |
| `damage` | `damageKind`, `amount`, `interval` | deals `amount` per stack of that kind every `interval` seconds, against resistance and wards |
| `ward` | `absorb`, `damageKinds` | takes up to `absorb` per stack of hits of those kinds before health; spent, it ends |
| `hold` | none (`{}`) | its bearer neither moves nor attacks; a held resident stands interrupted |
| `slow` | `factor` | multiplies pace; the strongest slow wins |
| `reveal` | `radius`, `sensed` | the HUD names how many living residents are within `radius`, through walls (`{count}`) |
| `light` | `color`, `intensity`, `range`, `lift` | a shadowless light carried at the bearer's eye |
| `guard` | a contribution (`stage`, `side`, `kinds`, and its fields) | brings that hit contribution while it lasts; a `Defeating` guard ends when it saves its bearer |
| `push` | `speed` | drives a resident bearer away from where the effect came from (its user, or an area's centre) |
| `lure` | `speed` | draws a resident bearer toward where the effect came from, heedless of anyone |

`stacking` decides how another application meets an effect already in its group. `Independent` keeps one instance per
source (an item, a resident, a weapon, a spirit) up to `maximumInstances`, and the same source again restarts its own;
`Refresh` keeps one instance, adds a stack up to `maximumStacks` and restarts it; `Replace` swaps in a fresh one. Only
`Refresh` gathers stacks and only `Independent` holds several instances, so the other limit is 1; every effect in a
group shares one rule. `duration` and `interval` are admitted seconds: effects advance only with the simulation, so a
paused game holds them. An over-time effect's last tick lands as it expires.

Items name the `effects` their use applies; actions name the `effects` a hit puts on what it strikes and the
`selfEffects` on their user; the spirit names the hold its call puts on a resident (`effect`). An unknown effect id fails validation
naming the referring file and field. A full track refuses an item only when its effects would do no more than restore
that full track.

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
footprint so its sockets stay where finds and readings rest. A model may turn by its own `yawDegrees`, and with
`find` it shows the fixture's find and disappears when the find is taken, as find parts do; the find displays are
built this way. A fixture needs at least one part or model.

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
same way, on the floor it was left on and in the refuge, beside its pose. Version 10 saves the investigator's growth with the supplies: experience, uses by skill and the relics taken in. Version 9 saves a generated item's roll (quality and affixes) with its pocket or worn slot, and searches made among
the collected ids. Version 8 saves the pacts as the spirits freed and the one in the pact slot. Version 7 drops the selected weapon: weapons are held items, saved with the worn items. Version 6 keeps the field case's worn items (the item and the slots it fills) beside the pockets, restored and
equipped before the stats so track maximums include what is worn. Version 5 keeps each actor's effects with
its stats: the effect, who applied it, its stacks, time left, time since its last tick and ward left, re-admitted in
their saved order after the bases and before the track points, so the stat sources they hold are rebuilt. A checkpoint of another version is refused
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
