# Hotel Endless

A new first-person exploration game set in an impossible 1970s hotel: scrounge
for supplies, approach fights deliberately, and call on brief demon summons.
Read the [game design](docs/design.md) and [sibling reuse guidance](docs/reuse.md).
The [UI contract](docs/ui.md) separates curated player flows from the opt-in
Engine developer console.
Tasks and implementation progress live in Den project `rusty-hotel`.

## Play

The authored excursion connects a refuge, patterned corridor, two side rooms,
a survey room and a service shortcut. Read the notice beside the refuge exit,
scrounge supplies, face the porter and Lamplighter, free Hushwing from the linen
room bell, and bring the survey reel back to the west desk's notebook.
Teal wallpaper, burgundy carpet, warm lamps and quiet analog ambience establish
the direction. [Design](docs/design.md) describes the larger game; current
acceptance, limitations and next work live in Den, not this feature description.

Click the hotel view to capture the mouse. Menu → Controls also lists bindings.

| Control | Action |
| --- | --- |
| WASD / mouse | Walk / look |
| E | Use the focused object, collect a find, read or open a door |
| Left click / left Ctrl | Commit one attack per press |
| 1 / 2 | Select pry bar / survey pistol |
| R | Use carried cartridges while exploring; recover the checkpoint when downed |
| Q | Call the equipped spirit toward a living resident in range |
| 3 / 4 / 5 | Use the first three field-case pockets |
| I | Open the field case |
| Escape | Close the foreground screen or open the menu |

In the field case, select a supply to use it. Drag between pockets, or choose
Move stack then activate a destination with the keyboard. Matching stacks merge;
different supplies swap. Spirits contains the acquired pact and its equip action.
Hushwing interrupts a resident briefly; an accepted call spends one charge.
Foreground screens pause the world, and their status follows the Engine response.

Use the refuge notebook to save and secure expedition finds. Saving keeps current
health and resources; it does not refill them. Defeat recovery and relaunch restore
the whole checkpoint at the refuge, including pockets, pact, doors, loot and
residents. Before the first return, recovery restores the initial excursion.
**Pausing or quitting does not save changes after the last refuge return.**
An invalid existing save reports an error rather than silently starting over.

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

## Development and documentation

| Read | Purpose |
| --- | --- |
| [AGENTS.md](AGENTS.md) | Agent ownership, UI and workflow rules |
| [Design](docs/design.md) | Accepted direction, first-excursion scope and deliberately open choices |
| [Architecture](docs/architecture.md) | Implemented owners, admitted update path and checkpoint contract |
| [UI](docs/ui.md) | Curated player surfaces, focus, pause and semantic actions |
| [Authoring](docs/authoring.md) | Content fields, stable identities, assets and save compatibility |
| [Development](docs/development.md) | Focused checks, developer console and isolated playtesting |
| [Reuse](docs/reuse.md) | One-time donor provenance and adaptation boundaries |
| [Art sources](art/README.md) | Original images, prompts, editable models and offline preparation |
| [Review](docs/agent-review/README.md) | Proportional review questions and evidence boundaries |

`src/Hotel.Game/` owns the safe C# game; `src/ui/` presents its facts and submits
semantic intents; `content/` holds authored definitions and runtime assets.
`art/` holds source assets and provenance. Engine supplies the host, browser shell,
rendering and generated bind entry under ignored build output.

Den project `rusty-hotel` owns tasks, progress, reviews and playtest records. Start
with its `project-entry` and `known-limitations` documents and the active task's
canonical context. Keep settled contracts and repeatable instructions here;
exact dependency versions belong in `Directory.Build.props`.
