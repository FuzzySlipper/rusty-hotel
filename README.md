# Hotel Endless

A new first-person exploration game set in an impossible 1970s hotel: scrounge
for supplies, approach fights deliberately, and call on brief demon summons.
Read the [game design](docs/design.md) and [sibling reuse guidance](docs/reuse.md).
The [UI contract](docs/ui.md) separates curated player flows from the opt-in
Engine developer console.
Tasks and implementation progress live in Den project `rusty-hotel`.

## Current implementation

A walkable first-person hotel loop: a refuge, corridor, two side rooms, a survey room and a service return
with solid walls, floors and doorways. Teal medallion and angular wallpaper,
dark carpet, wood trim, warm lamps and analog expedition props establish its
visual direction. Quiet ventilation and a nearby tape-machine loop supply
ambience. [Asset sources and authoring](art/README.md) retain the generated
originals, prompts and editable recorder model.
C# composes Engine FPS input, character
collision and camera services. A compact HUD, Supplies/Spirits field case, menu
and opt-in Engine command console establish the interface. The case starts
empty. E collects dressings, cartridges, binding incense and the survey reel;
the field case shows their stacks and selected details. Health and resource
reserves come from the C# supplies owner. Select a pocket to use its supply, drag a stack to another pocket, or choose
Move stack and activate a destination with the keyboard. Matching stacks merge;
different supplies swap. The first three pockets also appear on the HUD: keys
3–5 use their contents during exploration. Case actions run through Engine
paused-intent delivery, so arranging or using supplies does not advance the world.
The pry bar and survey pistol have committed windups and recovery. A porter in
the west room closes distance and raises its hammer before swinging; the survey
room's Lamplighter charges a shot along a fixed direction. Retreat, sidestep or
use a doorway for cover. Encounters are sparse and the pry bar needs no ammunition.
The notice beside the refuge exit introduces the missing survey reel, nearby
supplies and the return notebook. E opens it in the same focused reading screen
as the survey-room field log. The survey-room door opens with E.
Follow the service passage and unlatch the return door from the far side to open
a shorter route back. Collected finds stay removed as part of the refuge checkpoint.

Click the world to capture the mouse, use WASD to walk and the mouse to look.
E uses the focused door or object. I opens the field case. Escape closes the active screen or opens the pause menu.
Left click or Ctrl attacks once per press; 1 selects the pry bar, 2 the pistol.
Collect a cartridge packet with E, then R loads it while in the world. The east
refuge table holds a starter packet. When overwhelmed, R restores the whole previous refuge checkpoint. Before your
first return this is the initial excursion state.
In the linen room, E frees Hushwing from its bell and grants two summon charges.
Open I → Spirits to equip the pact. Aim at a living resident within six paces
and press Q: the bell-headed moth interrupts it for three seconds, then departs.
Each accepted call costs one charge; invalid calls cost nothing. You can attack
or retreat during its intervention. Pausing freezes its presence, and defeat
ends it. Recovery restores the pact and charges recorded at the checkpoint.

At the refuge, aim at the notebook on the west desk and press E to record a
checkpoint. Carried expedition finds move into the refuge ledger; the return
receipt confirms what was secured and saved. Close it to resume exploring.
Saving retains current health and resources; it does not refill them. The pause
menu shows the current checkpoint status. Defeat recovery and reopening the
hotel restore its supplies, pocket arrangement, pact, selected weapon, doors,
collected finds and resident health/positions together. Recovery starts at the
authored refuge arrival with no unfinished attacks or summons. Changes after
the last refuge return are discarded; pausing or quitting does not save them.
A missing save establishes an initial checkpoint. A present unreadable or invalid
save reports a load error and is never silently replaced. Engine owns storage;
ordinary dev saves live in its disposable `.runtime/persistence` area.

Foreground screens request a real Engine pause and release the mouse; their
status follows the Engine's answer. Closing a child returns to its previous
screen while paused. Resume hotel or closing the final screen resumes the Engine
before restoring gameplay focus. The product clears held input in its Engine
pause/resume callbacks. Failed transitions remain visible in the screen.

The focused native smoke check runs with
`dotnet run --project tests/Hotel.Smoke/Hotel.Smoke.csproj`. It exercises real
Engine collision and product input/lifecycle callbacks; it does not establish
browser focus behavior or player-facing pause.
`node --test tests/ui/*.test.mjs` checks console loading/cleanup and lifecycle
request rejection, superseded replies and disposal during a pending resume.

For developer access, launch `rusty dev --live-debug` and append `#developer=1`
to the printed URL. Open Developer console from the menu or press F2. The
packaged Engine console discovers `hotel.inspect` and
`hotel.dev.return-to-entrance`. The latter is an explicit developer position
and route reset. Engine `interaction.inspect` and `interaction.use` expose the same
world interactions; targeted use assists aim but still checks reach, visibility,
revision and Hotel eligibility. `hotel.dev.give-supply`, `hotel.dev.set-health`
and `hotel.dev.use-supply` provide explicit developer fixtures over the same
supplies rules; they use the same supplies rules as player controls. Without host debug enabled, the panel reports that it is unavailable.
The normal HUD has no debug traffic or telemetry panel.

## Setup

Engine pairs target Linux x64 and Windows x64. Install the .NET 10 SDK, `curl`
and `tar`. NativeAOT also needs the platform compiler/linker prerequisites
(Clang and zlib development headers on Linux). Get the Engine's `rusty`
command once:

```bash
curl -fsSL https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.sh | bash
```

On Windows, in PowerShell:

```powershell
irm https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.ps1 | iex
```

Then, from this repository:

```bash
rusty status
rusty install
rusty dev --port 8787
```

Open the URL printed by the host and click the hotel view to begin walking.
`rusty dev` runs the pinned pair's runtime: CoreCLR loads the product, and
changes to declared C#, UI, or content inputs rebuild and reload it. See
`rusty dev --help` for `--bind-host`, `--live-debug`, and `--debugger`.

`Directory.Build.props` pins the exact SDK/runtime pair and names the product
project (`<RustyEngineProject>`) that `rusty dev` and `rusty build` use without
`--project`. `rusty install`
downloads it once into the shared Engine cache, and later builds and runs work
offline. No Engine source checkout is required.

To adopt the newest published pair deliberately:

```bash
rusty update
rusty build
```

`rusty update` lists the release notes to read; include the changed
`Directory.Build.props` in the resulting source change. This repository's
`engine-pair` workflow does the same every six hours: it moves the pin only
after the product builds and serves on the new pair, and otherwise opens an
`engine-pair-update` issue with the build output and the notes to read. For an explicit
NativeAOT fidelity/release check:

```bash
rusty build --aot
```

## Repository shape

| Path | Responsibility |
| --- | --- |
| `src/Hotel.Game/` | Safe C# composition, player body/camera, scene and product metadata |
| `src/ui/main.js` | DOM presentation and semantic input |
| `content/hotel.json` | Authored layout, materials, lights, props, ambience and player tuning |
| `content/materials/`, `content/models/`, `content/audio/` | Runtime assets loaded by Engine content services |
| `art/` | Source assets, generation provenance and offline model preparation |
| `Directory.Build.props` | Matched Engine SDK/runtime pin and default product project |
| `docs/architecture.md` | Current ownership and data flow |
| `docs/ui.md` | DOM companion contract |
| `docs/agent-review/` | Reusable review workflow and lane packets |

The SDK generates the product's bind entry point inside its ordinary build;
there is no composition project. The Engine runtime supplies the host and
browser shell. Product metadata, input intents, content/UI
roots, and projection identity live in the ordinary `.csproj`.

Read [AGENTS.md](AGENTS.md) before extending the product. Keep instructions
about current behavior and ownership; exact dependency identities belong in
configuration, and task status belongs in the task system.
