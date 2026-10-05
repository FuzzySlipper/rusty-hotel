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
| `supplies/resources.json` | Health, ammunition and summon bounds; `HotelSupplies` |
| `supplies/items.json` | Item kinds, effects, stack limits, names and descriptions (`{amount}`); `HotelSupplies` |
| `supplies/messages.json` | Supply notices, refusal reasons and how long a notice stays up; `HotelSupplies` |
| `combat/tuning.json` | Reload time and how long combat notices, hit and hurt flashes last; `HotelCombat` |
| `combat/weapons.json` | Weapon commitments, damage, range, ammunition cost, short name and HUD phase labels; `HotelCombat` |
| `combat/residents.json` | Resident kinds: behavior/silhouette, health, damage, sight, timing, leash, body and eye height; `HotelCombat`, `CombatView` |
| `combat/messages.json` | Combat notices and HUD action states; `HotelCombat` |
| `spirits/<id>.json` | One spirit's pact terms, cost/range, manifestation timing and placement offsets, its description and its own wording (call hint and result, phase names); `HotelSpirit` |
| `spirits/messages.json` | Pact notices and refusals shared by every spirit, using `{spirit}` and `{place}`; `HotelSpirit` |
| `expedition/messages.json` | Checkpoint receipts and status lines; `HotelExpedition` |
| `excursions/<id>/geometry.json` | That section's static boxes, GLB props and lights; `HotelScene` |
| `excursions/<id>/route.json` | Its doors/latches (with the locked-side prompt), readable notices, named room bounds and fallback location label; `HotelRoute` |
| `excursions/<id>/placements.json` | Arrival point, refuge, item finds, placed residents (id + kind) and spirit bells (with the `place` their text names) |
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

## Geometry and interactions

Positions are `[x, y, z]` in world metres, with Y up. Room boxes use minimum and
maximum corners. `spawn` is the player body centre, not its feet or eye; eye
height is measured from the feet by the player owner. Fields named `Degrees`
and door yaw values use degrees; the runtime converts them at the relevant owner.

Material IDs connect boxes and doors to surfaces. `tileWidth`/`tileHeight` set
world-metre repeats; room geometry projects UVs from world position so adjacent
wall sections share a pattern phase. Inspect near/far repeats at eye height in
motion. Preserve the broad wallpaper/fine dark carpet hierarchy from the two
original images in `docs/references/`.

Solid boxes participate in Engine collision. Decorative GLB props are visual
only; place them on solid furniture. A model does not become an interaction
simply by appearing in the scene. Add new world actions as `Interactable`
entries and a use handler in `HotelRoute`. A supply's find ID associates its authored boxes with
the collected state; disappearance must follow successful inventory admission.
Door open/closed appearance and collision use the same authored poses.

Add a notice or field log as a `readings` entry in the excursion's `route.json` with a stable ID, focus
point, label, title and text; dress its location with ordinary scene geometry.
It uses the existing reading screen and pause/resume flow. Readings themselves
do not grant items. Resource effects remain in Supplies; encounters remain in
Combat; a contextual receipt observes Expedition's result.

## Content identities and checkpoints

Checkpoint versioning lives in `Expedition/CheckpointState.cs` and
`HotelExpedition.Validate`. Saved values reference authored refuge, item, find,
door, weapon, spirit and resident identities. Keep those IDs stable when editing
labels or art. Pocket count, stack/resource bounds, resident roster/health/leash
and other validation rules also affect whether an existing save remains valid.
There is no automatic migration or fallback to a new game for invalid data.

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
