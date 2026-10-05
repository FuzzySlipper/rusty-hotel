# Authoring the hotel

[Design](design.md) owns the direction; [architecture](architecture.md) names
the mutable owners. `content/hotel.json` is the authored excursion definition,
loaded by `Scene/HotelDefinition.cs` through Engine content services. Its typed
records are the field reference. There is no executable content scripting or
second scene/state authority in the DOM.

## Where a change belongs

| Definition | Meaning and related owner |
| --- | --- |
| `spawn`, `spawnYawDegrees`, `player` | Refuge arrival, body/camera dimensions, movement and look tuning; `HotelPlayer` |
| `materials`, `boxes`, `lighting`, `models` | Surfaces, static room geometry, lights and GLB props; `HotelScene` |
| `ambience` | Content-backed loop path, gain and optional world position/range; `HotelAmbience` |
| `route` | Reach/focus tuning, doors/latches, room bounds, readable text; `HotelRoute` |
| `supplies` | Resource bounds, item kinds/effects/stack limits and find placements; `HotelSupplies` |
| `interface` | Pocket capacities and fallback location label; C# facts projected to the field case/HUD |
| `combat` | Weapon commitments, damage/range, resident placement/tells/behavior tuning; `HotelCombat` |
| `spirit` | The one pact, bell placement, welcome charges, cost/range and manifestation phases; `HotelSpirit` |
| `refuge` | Checkpoint identity and notebook interaction point; `HotelExpedition` through `HotelRoute` |

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

Add a notice or field log as a `route.readings` entry with a stable ID, focus
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
