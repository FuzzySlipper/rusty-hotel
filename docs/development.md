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
| DOM unit checks | Pause request failures/supersession/disposal and asynchronous developer shell mount/close/cleanup |
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

## Developer shell

Run `rusty dev --live-debug` (add `--output window` for a window), then use F2
or Menu → Developer shell. Without `--live-debug` the host has no debug
service and the UI offers no shell.
The shell is the Engine's developer shell docked at the left beside play (which keeps running): two inspection views
of the held assembly, the first-person workbench and the console. Closing it (F2, Escape or its Close) disposes every
panel and view, returns the hands to what they hold and input to play; the normal HUD has no debug panel traffic.
Hotel owns its lifetime adapter, not its transport, parser or views.

**Inspection views.** The upper view frames the shown look's main grip from the side, the lower its support grip from
underneath (C# inspection targets `main-grip` and `off-grip`, camera space, framed along the item's turn;
`engine.inspect.target list`). Each pane's bar picks the subject and framing, captures the view and toggles overlays
in that view; drag orbits, Shift-drag pans, the wheel zooms. While the shell is open the player view shows the stance
guide (`held.guide`) and the hand fit's contact probes (`hands.contact`).

**First-person workbench.** The Engine tunables panel over Hotel's page.
- **Pick and play:** choose a look and action, hold a key's pose, scrub to any moment or play at a speed.
- **Sliders:** the look's stance (every look in it moves), its nudge, its hands' shape (each finger joint's curl and
  each thumb joint's curl and swing), the held key's pose and the arms, with the stance guide as a toggle.
- **Grip placement:** model-space contacts and separate main/support-hand turns. Seat the palm on the handle before
  fitting the digits. These controls save and reload live along with the per-finger shape.
- **Keys:** each key's phase, its moment through that phase, held-item visibility and the curve it eases in by (for every look sharing the
  motion). The timeline follows, and a key moved out of time order is reported, since content refuses it.

Every control is bound to where it is saved (a content file and a JSON pointer). The panel marks what differs from
the file and gives each file **Save** and **Reset**. Save writes only the changed values into the checkout's content
file, one line per value (the Engine content source); the running game keeps going. Saving works only under `rusty
dev`; elsewhere the panel tunes live without Save.

Residents keep acting while it is open: tune in a quiet room, or alone with `hotel.dev.floor.module`.

The held-presentation files (`combat/held.json`, `stances.json`, `held-motion.json`, `arms.json`) are declared live
content (`RustyEngineLiveContent`): an edit made in an editor under `rusty dev` reloads into the running game
(`HotelProduct.ReloadHeld`) instead of restarting it. A change to what exists (a look, model, motion, stance or the
arms' rig) is reported as needing a restart.

Hotel describes the page in `Interface/Workbench/HotelWorkbench.cs` (choosers, the motion viewer's timeline) and
`ViewmodelTunables.cs` (each control, the owner call that applies it, and its source). The Engine's
`TunablesDebugModule` serves it (`tunables.describe`, `tunables.select`, `tunables.set`), and its
tunables panel draws it in the developer shell (`src/ui/developer.js`). The same commands, with `engine.content.source.read` and
`engine.content.source.edit`, tune and save without a page.

Use the generated catalog for argument help and current command availability.

| Command | Effect and boundary |
| --- | --- |
| `hotel.inspect` | Observe current domain facts; does not change gameplay. `armsShortfall` is how far each holding wrist was drawn from its grip, as the Engine IK solve reports it (metres; the off hand's -1 when it holds nothing): more than a centimetre is a grip out of the arm's reach |
| `interaction.inspect` / `interaction.use` | Engine world-interaction inspection/assisted use, retaining reach, visibility, revision and eligibility checks |
| `hotel.dev.hold <item>` | Give one of a held item and wear it in the main hand through the ordinary wear rules |
| `hotel.dev.set-track <track> <value>` | Set one investigator track (for example `ammunition`) within its bounds |
| `hotel.dev.motion <look> <action>` and `.key`, `.next`, `.prev`, `.at`, `.play`, `.pose`, `.off` | The held-motion viewer: step a look's action keys, play it at a speed, try pose changes (docs/authoring.md, "Held motion") |
| `hotel.dev.arms.model <path>` | Show another arms model (a variant built from `art/arms`) with the same hold until the floor is rebuilt; `combat/arms.json` is unchanged |
| `tunables.describe` / `tunables.select <chooser> <value>` / `tunables.set <id> <value>` | The first-person workbench's page (Engine `TunablesDebugModule`; the developer shell shows its panel). `engine.content.source.read <path>` and `engine.content.source.edit <path> <sha256> <edits>` save into source content under `rusty dev` |
| `hotel.dev.stance`, `.set`, `.hand`, `hotel.dev.held.nudge` | The held stance guide: draw a look's stance beside it, change stances and nudge looks live (docs/authoring.md, "Held stances") |
| `hotel.dev.hands.fit <look> <main\|off\|both>`, `hotel.dev.hands.contact <look> <hands>` | The hand fit: curl a look's hands onto its model at rest from open (Engine model surface query), or inspect current curls, thumb swings, distance and penetration without changing the shape; each probe is marked in the overlay set `hands.contact` (a wire sphere at the digit's radius, red past the skin, a label per digit with the report's words), cleared when the motion viewer shows another look. Contact names where its grip landmarks are measured: `engine.inspect.landmarks held.landmarks` (docs/authoring.md), drawn as the overlay set `held.landmarks`. A fit changes the loaded content only; the workbench's Hands group (its "fit to the model" choice does the same) shows it unsaved |
| `hotel.dev.arms.offhand <rx> <ry> <rz> <px> <py> <pz>` | Try the off hand's hold on two-handed looks live; prints its JSON for `combat/arms.json` |
| `hotel.dev.arms.reach <sx> <sy> <sz> <px> <py> <pz>` | Try the right shoulder's camera-space place and the elbow pole live; prints their JSON for `combat/arms.json` |
| `hotel.dev.arms.hand <rx> <ry> <rz> <px> <py> <pz>` | Try the main hand's hold (turn from the held item, palm offset) live; prints its JSON for `combat/arms.json` |
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
