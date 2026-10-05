# Authoring the hotel

[Design](design.md) owns the direction; [architecture](architecture.md) names
the mutable owners. `content/` is the authored data, organized by domain. Each
file loads into one domain's typed record through Engine content services. Those
records (`*Definition.cs`, `*Tuning.cs` beside their owner) are the field reference.
There is no executable content scripting or second scene/state authority in the DOM.

Files are strict. An unknown member, a missing required value, a null in a
non-nullable field or a reference to an unknown id stops loading, with a message
naming `content/<file>` and the field. Fix the data; there are no silent defaults.

## Where a change belongs

Reusable definitions live by domain. Where one excursion places things lives in
its own folder. Geometry is kept apart from tuning.

| File | Meaning and owner |
| --- | --- |
| `player/tuning.json` | Body/camera dimensions, movement and look tuning; `HotelPlayer` |
| `route/interaction.json` | Reach and focus distances/angles for every world interaction; `HotelRoute` |
| `interface/tuning.json` | Field-case pocket count and how many appear as HUD quick pockets; `HotelHud`, `HotelSupplies` |
| `scene/surfaces.json` | Reusable surfaces: tint, texture, world-metre tile size, roughness, emission; `HotelScene` |
| `supplies/resources.json` | Health, ammunition and summon bounds; `HotelSupplies` |
| `supplies/items.json` | Item kinds, effects, stack limits, names and descriptions; `HotelSupplies` |
| `combat/weapons.json` | Weapon commitments, damage, range, ammunition cost and reload time; `HotelCombat` |
| `combat/residents.json` | Resident kinds: behavior/silhouette, health, damage, sight, timing, leash, body; `HotelCombat`, `CombatView` |
| `spirits/<id>.json` | One spirit's pact terms, cost/range and manifestation timing; `HotelSpirit` |
| `excursions/<id>/geometry.json` | That section's static boxes, GLB props and lights; `HotelScene` |
| `excursions/<id>/route.json` | Its doors/latches, readable notices, named room bounds and fallback location label; `HotelRoute` |
| `excursions/<id>/placements.json` | Arrival point, refuge, item finds, placed residents (id + kind) and spirit bells |
| `excursions/<id>/ambience.json` | Its ambient loops: content path, gain and optional world position/range; `HotelAmbience` |

Runtime assets (`materials/`, `models/`, `audio/`) stay in their own folders and are
referenced by path. When a new domain or kind of authored data arrives, give it its
own file in the matching folder rather than appending it to a neighbour. Put
player-facing text with the domain that shows it; do not write it into code.

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
