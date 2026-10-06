# Hotel Endless agent guidance

Hotel Endless is a first-person exploration and scavenging game in an impossible
1970s hotel, with brief demon summons. The implementation provides an authored excursion with scavenging,
combat, one spirit pact, a return shortcut and a whole-refuge checkpoint. Read [docs/design.md](docs/design.md) for product
direction and [docs/reuse.md](docs/reuse.md) before borrowing sibling code.
Den project `rusty-hotel` owns tasks and progress. Keep the product small and
explicit; proposed game systems are not implemented owners yet.

> The product decides. The Engine guarantees.

## Start here

Read [README.md](README.md) for setup and commands and
[docs/architecture.md](docs/architecture.md) for the current owners. Before
changing the Engine boundary, read the Engine's
[C# SDK guide](https://github.com/FuzzySlipper/rusty-engine/blob/main/docs/csharp-sdk.md)
and architecture. `rusty --help` is the workflow reference. Ordinary builds
consume the pinned package; verify capabilities against that pin (its release
notes and API surface) rather than against Engine source at another revision.

The user request and owning task define scope and acceptance. If work is tied
to Den, resolve that project's live guidance, task, and dependencies. Report
failed reads; do not invent task state. Continue independently authorized work
and pause only decisions that need unavailable authority.

## Ownership and source

- `src/Hotel.Game/` owns Hotel composition, player policy, scene definitions,
  semantic input interpretation, and UI facts. `Player/HotelPlayer.cs` owns the
  player body and camera; `Scene/HotelScene.cs` owns scene resources and paired
  door appearance/collision poses. `Route/HotelRoute.cs` owns door/latch state,
  readings and the Engine world-interaction adapter. Extend that owner for world
  actions; do not add a second ray/focus or interaction registry.
  `Mechanics/` owns the stat vocabulary and each actor's Engine stats (`ActorStats`):
  attributes, derived stats, tracks and damage kinds with resistances, and the
  actor's effects (`ActorEffects`) over the Engine effects component. Supplies spends
  the investigator's tracks and Combat the residents'; do not keep a resource as a
  plain number beside them. A timed condition (a heal over time, a hold, a slow, a
  carried light) is an effect in `mechanics/effects.json`, advanced on admitted
  time; do not add a separate timer for one.
  `Supplies/HotelSupplies.cs` owns carried stacks, collected finds and resource
  values; `Supplies/FieldCase.cs` holds them as the Engine inventory and equipment
  (pockets, worn slots, capacity), and worn items feed the stats through it. UI and developer actions share its capacity, eligibility and revision
  checks; room entry and case navigation must not reset it. Route inventory claims
  through HandleIntents during both running and paused admission. Quick access
  mirrors the first three saved pockets; never add a second inventory in the DOM.
  `Actions/` owns the action catalog and the one action pipeline every hand and
  resident uses (timing, Engine-query deliveries, damage and contributions); weapons
  are held items granting actions, and input never names a weapon.
  `Residents/` owns resident kinds composed from typed parts (look, faction,
  perception, movement, action choices), their senses and conduct; a new kind from
  existing parts is a content edit.
  `Combat/HotelCombat.cs` owns encounter policy over the pipeline and residents;
  `Combat/CombatView.cs` supplies poses to the existing scene snapshot. Keep
  resource mutations in Supplies, physical input in Engine FPS, and hits/body
  collision in Engine spatial services. Do not add a combat loop or local ray solver.
  `Loot/LootDefinition.cs` owns qualities, affixes, tables and item generation; a
  generated item's resolved roll travels with its stack, and searches (remains and
  containers) give through Supplies once, keyed by the run seed. Do not re-roll a
  placed or carried item.
  `Progression/` owns levels, skills grown by use and what relics and tomes teach;
  Supplies holds the investigator's `InvestigatorGrowth`, whose growth is Engine
  sources on its stats. Do not add a level-up or allocation screen or store grown
  values as bases; growth is told on the refuge receipt.
  `Spirits/HotelSpirit.cs` owns the roster's pacts, the pact slot and the visit;
  `Spirits/SpiritView.cs` contributes the articulated creature to the same scene.
  Equip claims use the Engine paused-intent callback and the same domain rule as
  running claims; summons use admitted physical Q input. Keep charges in Supplies
  and enemy interruption in Combat. Never add follower AI or a second clock.
  `Expedition/HotelExpedition.cs` owns the refuge checkpoint across those domains.
  Extend their capture/validate/restore methods when adding meaningful save state;
  persist one complete value through Engine ProductStateStore. Decode/validate
  before restore, settle deposits only after successful save, and never replace
  a present invalid save with initial state. Pause/quit must not save an excursion.
  `Interface/HotelHud.cs` publishes UI facts; `Interface/HotelDebugCommands.cs`
  adapts the generated command catalog to the existing owners.
  Organize additions by product domain;
  keep the product entry focused on explicit composition and lifecycle.
- `Rusty.Engine` owns named Engine mechanisms: lifecycle/update admission,
  input delivery, rendering/resources, spatial queries, content delivery,
  persistence primitives, and host integration. Search the safe SDK and
  existing product owners before adding a mechanism.
- `src/ui/` is a DOM companion. It observes Engine projections and submits
  semantic intents. Gameplay state, game rendering, canvas, transport, and
  scheduling stay with their C#/Engine owners.
  `main.js` owns the one foreground-screen/focus flow; `field-case.js` owns
  collection presentation; `developer.js` owns the optional Engine panel lifetime.
  `pause.js` requests/observes the Engine lifecycle port. Foreground screens pause
  simulation; nested navigation retains pause and the final exit resumes before
  restoring gameplay focus. Never infer success from a clicked button.
  Player inventory actions require Engine-owned delivery while paused. Do not
  temporarily resume gameplay or use the developer transport to deliver them.
- `content/` holds product-authored data. Interpret it in typed C# through
  Engine content services. Keep authored definitions, live state, and transient
  presentation distinct.
- The SDK generates the bind entry point and interop under ignored `obj/` output.
  Product code stays safe C#: no handwritten ABI/PInvoke, exports, raw native
  access, downstream Rust, or checked-in composition projects.

There is one Engine-admitted update path. Use its time/input facts; do not add
another loop, clock, scheduler, renderer, or state authority downstream.

## Player UI and developer access

Read [docs/ui.md](docs/ui.md) before adding any player-facing interaction or
developer hook. Establish and extend the curated Hotel interface early.

- Give every player-facing feature a deliberate home: an ordinary world
  interaction, the compact HUD, a focused inventory/spirit screen, contextual
  reading/refuge UI, or the pause menu. Design its entry, action, feedback, and
  return to play. Extend the relevant screen instead of appending controls.
- Do not complete tasks by adding a button to a long scrolling panel, generic
  action list, debug dashboard, or pile of collapsible sections. Hiding that
  panel in a tab does not make it a designed player flow.
- The exploration HUD has a fixed budget (docs/ui.md "HUD budget"). A new fact
  joins an existing cluster or replaces something; it does not add a
  persistent element. Prefer contextual facts that appear when relevant and
  expire. One notice slot, chosen by priority, never stacked toasts. Full
  references (all controls, all resources) live on their screens, not the HUD.
- Each foreground screen is its own `src/ui/` module with its own markup;
  `main.js` composes navigation and focus only. Do not grow one template
  literal or one draw function across screens.
- Use the opt-in **Engine-owned command console and generated command catalog**
  for agent hooks, inspection, fixture setup, cheats, and temporary feature
  triggers. Reuse its panel/client/transport and C# registration; do not build
  a Hotel console, command parser, transport, or per-feature test buttons.
- Keep the console out of the normal HUD, clearly labeled as developer access,
  and closed/inert until opted into. All command mutations go through existing
  C# domain owners. Distinguish normal semantic actions from explicitly named
  developer overrides; do not quietly bypass ordinary eligibility checks.
- Console support is legitimate scoped deliverable work. If a task promises a
  player feature, console-only access is an intermediate result, not completion.
  Keep its ordinary UI/world integration in the active task or an explicit Den
  follow-up; do not silently reduce the parent acceptance criteria.
- Verify player-facing claims with the console closed and ordinary controls.
  Record command setup/testing separately from the visible player path. Check
  input focus, pointer-lock handoff, close/resume, and relevant viewport sizes.
  Long notes, inventory collections, and console transcripts may scroll inside
  their designed surfaces; the prohibition is on accumulating unrelated controls.

## Product style

Prefer ordinary readable C#, explicit composition, direct methods, and one
clear mutable owner per domain. Keep operations thin: read, decide, apply,
publish. Use typed boundaries where they help; do not introduce a framework,
reflection discovery, generic bus, or service locator for hypothetical needs.

Use nullable types, file-scoped namespaces, and `internal`/`sealed` defaults
where the public product contract does not require otherwise. Keep structural
constants beside their algorithm; give meaningful identities names. Gameplay
values and authored definitions live in domain-owned content, not call sites;
see the next section.

## Content and code organization

Hotel Endless is content-focused. Organize ahead of need: agents and people
extend the shape they find, so an ad hoc catch-all becomes the pattern every
later change follows. Put an addition where its future siblings will live. If
the file, class or module you are extending is already a catch-all, split it
along domain lines first, as its own change, rather than adding to it. Do not
defer organization until "features demand it". Organization means folders,
files, typed records and named owners, not a framework, generic registry, rule
engine or plugin system.

- **Authored data is a domain-organized tree.** `content/` has directories and
  files named for what they hold (supplies items, weapons, resident kinds,
  spirits, readings, interface text, excursion geometry and placements). A
  product-named or catch-all file (`hotel.json`, `data.json`, `config.json`)
  is a smell. Keep reusable definitions (an item, weapon, resident kind or
  spirit) apart from where one excursion places them. Keep geometry apart from
  tuning. Each domain loads and validates its own typed records; do not funnel
  all content through one record that every owner reads.
- **No gameplay prose in C# or JS.** Names, prompts, notices, refusal reasons,
  receipts, phase labels and control labels belong in their domain's content
  (`messages.json`, or the definition they describe) as templates filled from
  definition values. Never repeat a definition's name or number inside a
  literal (`"Hushwing"`, `"six paces"`, `"pry bar"`). Code composes text; it
  does not author it. A screen module's fixed chrome (its title, button
  captions, empty-state copy) may live in its markup, and so may
  developer-console output.
- **Gameplay values are tuning.** Timings, notice and flash durations, ranges,
  offsets, damage, costs and limits go in content. A C# `const` is not tuning
  support. Missing or invalid authored values fail validation naming the file
  and field; no silent defaults.
- **Variants are typed.** A behavior, kind or mode is an enum or typed record in
  its definition, dispatched by the owning domain. Do not compare strings in
  views.
- **One table per vocabulary.** Key bindings and their labels, interactable
  kinds, HUD facts and saved fields each have one declaration that every
  consumer (C# input, HUD, UI, playtest actions) reads. A feature should not
  have to be added in several places that must be kept in step by hand.
- **Size signals prompt a split.** A C# owner growing past roughly 300 lines, a
  method interleaving several domains, a JS module holding more than one
  screen, or a JSON file spanning several domains is the cue to divide it
  before extending. These are prompts to look, not gates.

Trust first-party runtime state and Engine-admitted data. Preserve concrete
eligibility rules, current-data errors, and resource lifetime/disposal. Do not
add repeated hashing, compatibility layers, whole-state rollback, or validation
ceremony without a task-owned failure it prevents. Save meaningful values at
explicit save boundaries; native handles and presentation resources are not
product save state.

## Engine dependencies and gaps

`Directory.Build.props` owns the exact SDK/runtime pin. Install it with
`rusty install`; deliberately advance it with `rusty update`, read the release
notes it lists, then run the focused checks. `rusty status` reports the pin,
installation and missing prerequisites. Keep exact
versions in executable configuration and evidence, not duplicated in prose.
Normal development uses the matched runtime pack through `rusty dev`.
NativeAOT is an explicit fidelity/release check. Do not make an adjacent
Engine checkout a build dependency or modify it as part of downstream work.

The product builds and runs on Windows and Linux. Anything `rusty dev` or
`rusty build` runs (a UI build command, an `Exec`, a step the README puts
before `rusty dev`) must work under both `cmd.exe` and `/bin/sh`: one
`npm`/`pnpm`/`node` invocation with quoted paths, MSBuild `Copy`/`MakeDir`
for files, no `bash`, shell utilities or absolute machine paths. Generated
output stays under `obj/` or an ignored directory. Linux-only tooling is named
as such and kept off that path.

If a required mechanism is missing, verify the safe API, name the blocked
behavior and upstream owner, and file/link one narrow Engine request when
that is authorized. Distinguish a missing mechanism or binding from a helper
or documentation gap. Stop that dependent slice; continue independent work.
Do not conceal the gap with a local substitute, fake success, or proof-only path.

## Durable documentation

Repository Markdown is for settled, permanently useful information: product
intent, implemented ownership, contracts, authoring recipes, repeatable commands,
and incorporated code/asset provenance. Read [docs/authoring.md](docs/authoring.md)
when changing content or save identities and [docs/development.md](docs/development.md)
for checks and developer access.

Den owns campaign plans, task status, handoffs, reviews, investigations, dated
measurements and playtest timelines/captures. Do not append a session diary or
milestone checklist to repository docs. Update the canonical document rather
than creating another competing overview. Preserve historical records in Den
and verify the stored content before removing their repository copies. Retain
original art, editable sources, prompts, hashes and final use constraints here;
asset critique rounds and execution incidents belong in Den.

Never book the same fact in both places. Repository files, this one included,
describe what the product is and the rules for changing it. They do not say
what is planned, in flight, next or left over: no Den task IDs, campaign or
phase status, "current state" or "remaining work" sections, roadmaps,
not-yet-implemented lists, or TODO/FIXME comments that carry a plan. Design
may state the intended direction; whether a part of it exists is visible from
the code and from architecture's implemented owners. When a pending fact
seems to need a home in the repo, it belongs in a Den task, document, board
post or `known-limitations` entry. Point from Den to the repo, not back.

When a change settles a durable rule, update its owning document in the same
work. Keep the README an entry point, architecture the owner/data-flow map,
design the product direction, and UI the player-flow contract. Record temporary
blockers and evidence limits in Den's `known-limitations` instead of freezing
them into build instructions. Do not claim an ephemeral capture path or link
has archived its bytes; report retention limits separately from durable text.

### Work longer than a turn

Architecture and organization work often outlasts one session. Continuity
lives in Den, not in a smaller scope or a repo note. Shape a long effort as a
Den campaign whose children each land complete and leave the repo coherent.
When you stop or narrow a task, move every deferred requirement into a named
receiving task (its behavior and verification, existing or new) and point the
source task at it; a remark that the scope shrank does not carry the
requirement. Write the handoff on the task thread: what changed, checks run,
what the next agent must know. Do not shrink acceptance to fit a turn, and do
not leave half-moved structure (two content layouts, an old and a new path)
without a Den task that owns finishing it.

## Review and evidence

Use [docs/agent-review/README.md](docs/agent-review/README.md). Every change gets
an Engine-reuse and existing-product-reuse check; trivial changes may record
that no mechanism is affected. Assign bounded independent lanes when review
agents are requested or the task's review workflow calls for them. Keep the
same reviewer for fix rounds and reconcile source-backed findings against the
original task. Review is not an extra user-approval gate.

`rusty build` builds the default project `Directory.Build.props` names
and stages the ordinary CoreCLR product; `--aot` additionally publishes NativeAOT. Use focused
semantic or interaction evidence only when it answers the changed behavior;
do not add broad test gates to this small product. Distinguish build/staging,
host launch, and visible interaction claims. Repeat passed checks only after
material changes or an unresolved failure.

Preserve unrelated edits. Keep generated output and installed artifacts
ignored. Do not reset, force-push, or change adjacent repositories. Report what
changed, relevant checks, and concrete limitations. Commit/push when requested
or authorized by the active task; a review packet does not authorize publishing.
