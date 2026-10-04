# Hotel Endless agent guidance

Hotel Endless is a first-person exploration and scavenging game in an impossible
1970s hotel, with brief demon summons. The implementation currently provides a
walkable, dressed first-person hotel section. Read [docs/design.md](docs/design.md) for product
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
  `Supplies/HotelSupplies.cs` owns carried stacks, collected finds and resource
  values. UI and developer actions share its capacity, eligibility and revision
  checks; room entry and case navigation must not reset it. Route inventory claims
  through HandleIntents during both running and paused admission. Quick access
  mirrors the first three saved pockets; never add a second inventory in the DOM.
  `Combat/HotelCombat.cs` owns weapon commitment, damage and resident behavior;
  `Combat/CombatView.cs` supplies poses to the existing scene snapshot. Keep
  resource mutations in Supplies, physical input in Engine FPS, and hits/body
  collision in Engine spatial services. Do not add a combat loop or local ray solver.
  `Spirits/HotelSpirit.cs` owns the one pact, equipped choice and manifestation;
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
constants beside their algorithm; give meaningful identities names. Put
adjustable gameplay values and authored definitions in domain-owned content
when the product needs tuning, rather than hiding them in call sites.

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
