# Development and verification

Use the setup in [README](../README.md). `Directory.Build.props` selects the
matched Engine SDK/runtime; normal development needs no Engine or donor checkout.
`rusty --help` and the installed pair's contracts are the workflow reference.

## Focused checks

Run from the repository root. Node is needed only for the DOM unit checks;
Python and Blender are optional offline authoring tools.

```text
rusty build
dotnet run --project tests/Hotel.Smoke/Hotel.Smoke.csproj
node --test tests/ui/pause.test.mjs tests/ui/developer.test.mjs
```

Choose checks for the changed behavior:

| Check | What it establishes |
| --- | --- |
| `rusty build` | Compilation and ordinary CoreCLR product staging |
| `Hotel.Smoke` | Product callbacks and real Engine service calls: collision/input, route eligibility, supplies, combat, spirit rules, loot rolls and searches, growth by use and relics, complete checkpoints, content errors naming their file and field, and floor generation (keyed draws, mission graphs, modules, layouts, content pacing, Engine confirmation, travel, keys, floor saves, shifts and golden fingerprints) |
| DOM unit checks | Pause request failures/supersession/disposal and asynchronous console mount/cleanup |
| Ordinary hosted play | Rendered presentation, physical input, world/UI interaction, focus, pause and return to play |
| `rusty build --aot` | Explicit NativeAOT publish/fidelity check; still separate from a visible run |

### Floor generation tools

The golden fingerprints in `tests/Hotel.Smoke/goldens/floors.json` record named seeds' plan hashes for the current
generator version, with a stamp of the generator source and floor content. Smoke fails when either the named floors'
output or the stamp changes under the same version: bump `FloorSeed.CurrentVersion` (old saves' floors are then
refused, not reinterpreted), then record new goldens. The census and the floor bank are off the default path.

```text
HOTEL_FLOOR_GOLDENS=write dotnet run --project tests/Hotel.Smoke/Hotel.Smoke.csproj
HOTEL_FLOOR_CENSUS=50 dotnet run --project tests/Hotel.Smoke/Hotel.Smoke.csproj
HOTEL_FLOOR_SVG=/tmp/floor-bank HOTEL_FLOOR_CENSUS=10 dotnet run --project tests/Hotel.Smoke/Hotel.Smoke.csproj
```

The census reports, per depth, floors made, first tries, rooms, generation time and refusal reasons. The floor bank
writes one plan drawing per census floor: spaces by style, mission places labelled, ordinary doors, locks in red and
the shortcut's latch in amber.

The smoke harness supplies a unique temporary persistence root. It does not
render, play audio, mount the browser or prove host pause admission. DOM unit
checks do not prove pointer lock, drag gestures or layout. Keep those boundaries
in evidence rather than making a green test stand for an entire player feature.

## Developer console

Run `rusty dev --live-debug`, add `#developer=1` to the printed product URL,
then use F2 or Menu → Developer console. Both opt-ins matter: the hash exposes
the entry, while Engine controls whether the debug service is available.
Closing the console disposes the packaged panel; the normal HUD has no debug
panel traffic. Hotel owns its lifetime adapter, not its transport or parser.

Use the generated catalog for argument help and current command availability.

| Command | Effect and boundary |
| --- | --- |
| `hotel.inspect` | Observe current domain facts; does not change gameplay |
| `interaction.inspect` / `interaction.use` | Engine world-interaction inspection/assisted use, retaining reach, visibility, revision and eligibility checks |
| `hotel.dev.hold <item>` | Give one of a held item and wear it in the main hand through the ordinary wear rules |
| `hotel.dev.fill-tracks` | Fill every investigator track (health, stamina, summon, ammunition) to its maximum |
| `hotel.dev.return-to-entrance` | Reset live player, supplies, residents, pact and route to initial state; leave the existing checkpoint intact |
| `hotel.dev.floor.module <id> <turn>` | Build one room module alone beside the hotel at a quarter turn, with porches outside its doorways, and stand on its first porch facing in; `hotel.dev.goto` returns. Changes no saved state |
| `hotel.dev.floor <seed> <depth>` | Begin a run from a seed (stored as the run) and stand on its generated floor at a depth, arriving up its stairs with the current supplies; reports the floor inspection |
| `hotel.floor.inspect` | The run seed, depth and current floor: its trim style, identity hash, retry, places with their module, centre and door, every space and link with its midpoint, sizes, and the last generation's refusals and time |
| `hotel.dev.goto <space>` | Stand on a floor-plan space's first post, or at its centre when it has none, keeping the current facing, to inspect the built floor; changes no saved state. When a resident or furniture stands there, the player is placed on the nearest clear floor within the space (an Engine overlap query, up to 2 m away); with none, the command is refused |
| `hotel.dev.view <x> <z> <yaw> <pitch>` | Stand at a floor point facing a yaw and pitch in degrees, for repeatable captures from one viewpoint; changes no saved state. A point inside a wall, furniture or a resident moves to the nearest clear floor up to 2 m away, so the reported position may differ from the one asked; with none, the command is refused |
| `hotel.dev.give-supply` | Fixture grant subject to inventory capacity rules |
| `hotel.dev.set-health` | Fixture health change within authored bounds |
| `hotel.dev.apply-effect` | Fixture: apply a catalog effect to the investigator from a named source, under its stacking rule |
| `hotel.dev.effects` | Inspection: the investigator's active effects with stacks, seconds left, tick progress and ward |
| `hotel.dev.use-supply` | Use a zero-based pocket with its current revision and ordinary resource eligibility |

The return-to-entrance command is an unsaved excursion reset, despite its short
name. It is not checkpoint recovery or a position-only teleport. Subsequent
ordinary refuge saving records the resulting live state. Use an isolated save
for destructive fixture setup. Commands are diagnostic evidence; they do not
prove that a player can acquire, reach or use something through ordinary play.

## Hosted verification

Use the ordinary product, with the console closed, to check a player feature's
entry, action, feedback and return. Foreground screens must pause, allow their
normal semantic actions while paused, then resume before gameplay focus returns.
Include input clear and rapid close/reopen when changing that flow. Record actual
viewport dimensions; a requested size is not proof that the host used it.

Engine owns sessions and persistence roots. Inspect the session's reported
identity/root before a fresh-save test; a different port is not save isolation.
Use an isolated supported instance or source checkout/copy when necessary. Do
not overwrite another session's checkpoint for a test. Stop only hosts/sessions
you own, through their normal lifecycle, and retain cleanup receipts in Den.

Development `.runtime` state is disposable, not a supported player-save backup.
If a particular save is required for a reproduction, retain it as an explicit
fixture or evidence artifact with its source/pair identity; do not depend on a
temporary runtime path surviving a restage. A present invalid checkpoint must
still report a load error rather than silently overwriting it.

Record source identity, build/staging, launch, visible actions, command setup,
platform and remaining uncertainty separately in Den. Held-time/assisted input
does not establish unaided player pacing. Capture paths with service retention
limits are references, not permanent copies of their bytes. Keep task results,
screenshots and investigation logs out of the durable repository guides.
