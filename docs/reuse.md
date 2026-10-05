# Reusing sibling product code

Sibling Rusty projects are one-time code and pattern donors. Hotel remains an
ordinary safe C# product with the packaged Engine as its runtime dependency.
No build, restore, content load, or gameplay path may require another checkout.

## Where to look

Paths below are relative to the named sibling repository, not build references.

| Donor | Useful starting points | Hotel adaptation |
| --- | --- | --- |
| `rusty-fptester` | `src/FpTester.Game/Player/PlayerController.cs`, `FpTesterProduct.cs`, `Scene/LabDefinition.cs`, `Interaction/CubeInteraction.cs` | Small composition of Engine FPS input, accepted character motion, camera samples, typed content, and `WorldInteraction`. Strip lab telemetry, dynamic-cube/tether rules, debug UI, and lab geometry. |
| `rusty-craftsurvive` | `src/CraftSurvive.Game/Modules/Player/PlayerBody.cs`, `PlayerCamera.cs`, `PlayerController.cs` | Body dimensions, controller tuning, camera placement and lifecycle examples. Do not import terrain streaming, world-origin rebasing, climbing, water, survival meters, or its local physical-input aggregation. |
| `rusty-doom` | `csharp/LoadingBay.Game/LoadingBayPlayerInput.cs`, `LoadingBayEngineServices.cs`, `LoadingBayCombat.cs`, `LoadingBayPickups.cs` | Engine FPS input and spatial-backed character/encounter examples. Replace E1M1 assumptions, Doom pacing, catalog identities, and scene-specific coordination. Use packaged interaction helpers instead of copying the older hand-assembled use-target flow. |
| `rusty-dungeon` | `src/DelveRpg.Kit/Inventory/InventoryStore.cs`, `src/DelveRpg.Host/Save/DelveSaveStore.cs`, `DelveProduct.cs` | Small slot/stack/consume operations and explicit save boundaries over Engine persistence. Strip enchantments, identification, growth/meta progression, and permadeath policy. Its movement is continuous over a tile world, but its local collision/movement solver is not a Hotel controller donor. |

These are consultation candidates, not compatibility promises or a record of
code already copied. The initial source survey and its exact revisions live in
Den at `[doc: rusty-hotel/bootstrap-donor-survey]`. Refresh the source before an
implementation decision; do not mistake an uncommitted working tree for its HEAD.

## Copying procedure

1. Find the smallest relevant flow: entry point, state owner, implementation,
   and meaningful caller. Read it rather than copying a class by name.
2. Check the installed package selected by `Directory.Build.props`. Prefer its
   safe helpers where they already express the mechanism. Different donor pins
   do not establish API compatibility with Hotel.
3. Record the source revision and paths for code actually copied. For authorized
   uncommitted source, preserve exact source hashes and identify the working-tree
   snapshot. Retain applicable attribution and license notices, including any
   upstream provenance; code reuse does not authorize importing third-party art.
4. Adapt into the existing Hotel domain owner. Keep one owner for player state,
   interaction outcomes, inventory, and world changes. Use Engine-admitted time
   and input; avoid importing a separate accumulator, clock, physical-input
   authority, renderer, collision solver, or persistence transport.
5. Verify compilation against Hotel's pin and the ordinary changed behavior.
   Test input clear/pause and resource lifetimes when touched. Build success
   alone does not establish movement feel or visible interaction.

Keep settled provenance for incorporated code here or beside the copied code.
Keep investigation logs, exact build results, screenshots, and task status in
Den. Do not change or clean donor repositories to make them easier to copy.

## Incorporated bootstrap flows

Hotel adapted the following small flows from the `rusty-fptester` working tree:

| Donor file | Hotel owner and retained behavior | SHA-256 of source snapshot |
| --- | --- | --- |
| `src/FpTester.Game/Player/PlayerController.cs` | `Player/HotelPlayer.cs`: Engine FPS input, controller receipt application, eye placement, camera sampling and input clear/reset | `f64dbf4b177158d0e4033840365c64b906396e59f262d59bf16a7e52f854c861` |
| `src/FpTester.Game/Scene/LabGeometry.cs` | `Scene/RoomGeometry.cs`: authored unit-box vertices/normals/triangles only | `af0c84c5841fd6a26da24cb03ab28fe47a9e1a29c6216bb6319e9207434530cf` |
| `src/FpTester.Game/Scene/LabDefinition.cs` | `Content/Authored.cs`: Engine content read and typed JSON definition pattern | `3d6b4d9bd46e205b88008c7ac2f2f3e3260f629ffc3b5d56a3e2c4e72a5ef530` |
| `src/FpTester.Game/FpTesterProduct.cs` | `HotelProduct.cs`: explicit lifecycle composition and Engine playtest module adapter | `1ce48144c06b3bc3dad3603db7b558965c83c977df8668ce040acdb8ae983282` |

These files were uncommitted/untracked in the donor; its HEAD
`d2283b12959ff9dba886e0c4325eb0b10f9614d6` does **not** identify that
implementation. The hashes identify the consulted source. Hotel owns the adapted
copies; no sibling is a build dependency. No lab telemetry, cube physics,
jump/crouch/sprint policy, ramps or game assets were copied. Hotel's room layout,
walking pace, eye height, palette and controls overlay are authored here.

The movement/input/camera APIs were verified against Hotel's installed package
and compiled without changing its pin. CraftSurvive's body/camera and Doom's FPS
input were consulted as cross-checks; neither became a dependency or an
additional state/input owner. Runtime evidence belongs in the Den task record.

## Incorporated interface patterns

The field case and HUD adapt CraftSurvive's inventory presentation and console
lifetime patterns from revision `1186a92ee504f5980cd487d1244ff321dc2ea45d`.
The consulted files were unchanged by concurrent terrain work in that checkout.

| Donor file | Hotel adaptation | SHA-256 of source snapshot |
| --- | --- | --- |
| `src/ui/developer.ts` | `src/ui/developer.js`: lazy Engine panel mount and disposal, with Hotel session identity guarding late async completion | `ff56ebab9753e445eb0f7dac406539659d95a607c0fb1edddfab94cf2a105d67` |
| `src/ui/overlay.ts` | Exploration HUD: quiet grouped facts, omit absent resource values | `646ae0abc593b92390da07d6394f01b9c7533d2f2b1f6bcf7c227a557d76ede1` |
| `src/ui/screens.ts` | `src/ui/field-case.js`: focused collection, selected details and quick-pocket layout | `22f6e62cadb2c70cbe4a09024eac8a75a7b081421f0dd95529f2c4ce13358a8d` |
| `src/CraftSurvive.Game/Modules/World/ProductUiProjection.cs` | `Interface/HotelHud.cs`: flat typed `UiValue` projection encoding using the pinned safe SDK | `67975e63787cdea47d126e0d9750e036f4db02891aeb202ba83571ad50321811` |

Hotel's C# facts and authored interface capacities remain authoritative. Hotel
uses Engine focus/mode transitions and a single foreground navigation owner.
Crafting, hunger/thirst, placeholder equipment, journal, telemetry and the donor's
expanding menu were not incorporated. The packaged Engine panel supplies the
console catalog, dispatch, transport and transcript; its implementation is not
copied.

The completed supplies interaction adapts `slots.ts` as recorded under
[Supplies interaction](#supplies-interaction). Quick access mirrors the first
three saved pockets; it has no independent assignment or inventory state.

## Incorporated room presentation patterns

Hotel consulted CraftSurvive revision
`1186a92ee504f5980cd487d1244ff321dc2ea45d` and Rifles revision
`70c9cee3473bff9672ec4d4080baa7ad78960d71` for the small safe-SDK flows below.
The room composition, UV scale, material palette and sound choices are authored
in Hotel. No donor artwork was copied.

| Donor file | Retained pattern and Hotel owner |
| --- | --- |
| `rusty-craftsurvive/src/CraftSurvive.Game/Modules/Manipulation/LampLights.cs` | Retained point-light descriptors and disposal in `HotelScene`; no block/light pool or nearest-light policy |
| `rusty-craftsurvive/src/CraftSurvive.Game/Modules/Sky/DayNightSky.cs` | Engine ambient-light descriptor; no sky/time simulation |
| `rusty-craftsurvive/src/CraftSurvive.Game/Modules/Audio/SoundPlayer.cs` | Engine content clip admission and voice descriptors in `HotelAmbience`; no cue bus, module framework or swallowed playback errors |
| `rusty-rifles/src/Rifles.Game/Content/GeneratedArt.cs` | Texture content references and linear/repeat admission in `HotelScene`; no donor catalog framework |
| `rusty-rifles/src/Rifles.Game/Dungeon/DungeonMaterials.cs` | Material/texture relationships; Hotel keeps its own authored definitions |
| `rusty-rifles/src/Rifles.Game/Presentation/MusketeerArt.cs` | Safe Engine Animation GLB resource/appearance path for static props; no rig, animation instance or attachment state |

These APIs were checked against Hotel's installed package and compiled there.
Hotel uses `OpenAnimatedMeshFromContent` for GLB, including its unrigged prop;
`CreateStaticMeshFromContentReference` admits a different mesh format. Recheck
these content contracts against the selected package when changing that path.
Engine retains PNG/GLB/WAV decoding, graphics/audio resources, camera listening,
light rendering, collision and scheduling. Hotel supplies authored boxes and
world-metre UVs, not a renderer or spatial solver. Offline Blender conversion
and PCM authoring produce ordinary content files and are outside the runtime.

## Incorporated floor generation patterns

| Donor file | Revision | Hotel adaptation | SHA-256 of source |
| --- | --- | --- | --- |
| `rusty-rifles/src/Rifles.Procgen/CanonicalIdentity.cs` | `9964b33a8cf51cdc3dd92c9f651f1837e9e9b6bd` | `Floors/CanonicalText.cs`: quoted, JSON-escaped canonical lines hashed with SHA-256, independent of serializer and insertion order. Hotel writes lines per stage instead of hashing one candidate type | `ec8cbf83c8bd3154cf05625319b654c1d5a6e920571440c88b4919abb506cd72` |
| `rusty-craftsurvive/src/CraftSurvive.Game/Modules/WorldGen/TerrainGeneratorContract.cs` | `207b19a55db470eb605741dab392a8355ac96295` | `Floors/FloorDraws.cs`: Engine `DrawKeyed` as the only draw source, the version spread into the seed, purpose scopes, and wide-range one-in-N. Hotel adds stage scopes, depth/shift key prefixes and weighted choice; no noise seed or test-only draw source | `cb887122ede87742c4b96bfc40556ac97777a4ff75877042cd02dc89a2c02a97` |

Both were committed and unchanged at those revisions. Checks use the same Engine draws through `EngineTestHost`.

## Mounted UI lifecycle

The Engine's `fixtures/csharp-controller-interaction/product-ui/main.js` and
`docs/csharp-product-project.md` pause/resume flow at reviewed source
`816cf212daf67327d7431686e849285de094dd5a` supplies the supported pattern:
observe `context.lifecycle`, request pause/resume there, and restore gameplay
mode/focus only after resume. The installed pair's public UI types confirm the
contract. Hotel adapts it into its existing single-screen navigation rather than
copying the fixture's floating button. `pause.js` adds only pending-request,
failure and disposal handling. No Engine transport, runtime state owner or clock
is copied.

## Incorporated world-interaction adapter

`rusty-fptester/src/FpTester.Game/Interaction/CubeInteraction.cs` (source SHA-256
`0470e04016ca2d83fd3fd7b7b2210afd68108dca85dc12b6f0f740192d6299d7`)
supplies the `IWorldInteractionScene` / `WorldInteraction` / Engine visibility
composition pattern used by `Route/HotelRoute.cs`. The already recorded
`FpTesterProduct.cs` snapshot supplies pending-use admission across zero-step
input batches. Hotel retains its existing lifecycle and player owners; it does
not copy donor pause flags, dynamics, grip/tether state, throws or pickup policy.
The installed SDK owns focus selection, reach/angular validation, stale-target
rejection and visibility casts. Hotel owns the authored doors, far-side latch,
reading contents and room labels. Debug and ordinary E use share that owner.

## Incorporated supplies rules

Hotel consulted the following Dungeon working-tree snapshots directly; these
hashes identify the exact sources independently of concurrent donor changes.

| Donor file | Retained behavior | SHA-256 of source snapshot |
| --- | --- | --- |
| `rusty-dungeon/src/DelveRpg.Kit/Inventory/InventoryStore.cs` | Nullable flat slots, merge existing stacks before free slots, clear a consumed last item, swap pockets | `c4b677cc42b05bc96c39d5427f5e8b97c53cd55ff72c878566b838c1a5a3cdc2` |
| `rusty-dungeon/src/DelveRpg.Kit/Session/RunSession.Actions.cs` | Remove a world find only after inventory admission succeeds; clamp healing | `add561300e4b9b816e3c6772a96f1fb0c7c6091679b0b7bdd8dc90a7862ecddd` |

`Supplies/HotelSupplies.cs` adapts these policies over the pinned safe SDK's
`InventoryStore` and `Track`. The donor's quantity storage and numeric resource
mutation are not copied: Hotel pockets hold Engine stack identities, and Engine
performs grants, consumption, merges, splits and bounded resource changes.
Hotel owns authored stack limits, whole-find admission, full-resource refusal
and stale-selection checks. `HotelRoute` keeps the existing Engine interaction adapter; no grid,
equipment, affix, durability, progression or donor session framework was copied.
Engine retains world reach/visibility and action admission; Hotel owns capacity,
item effects and collected-find state. The DOM only projects these facts.

## Incorporated combat API patterns

Hotel consulted Doom's working-tree files directly. These are API and settlement examples, not pacing or enemy-design
references; no Doom content or assets were copied.

| Donor file under `rusty-doom/csharp/LoadingBay.Game/` | Hotel adaptation | SHA-256 |
| --- | --- | --- |
| `LoadingBayCombat.cs` | Refuse unavailable actions, settle an accepted shot's ammunition once, apply bounded health changes | `394b13949d2d20d68fa7270f644ff5d136b3381e42298c60bdc94432ffa742bc` |
| `LoadingBayEngineServices.cs` | Engine ray casts combine live entity hitboxes with world collision; character proposals consume real obstacles and accepted receipts | `821d0a4faacbdea8f9677589be8e46748453a82b2b259c396758fe788be773a7` |
| `LoadingBayPlayerInput.cs` | Attack presses read from the existing Engine FPS physical state | `678fb3f80467fe147d10396665c4d95af5a13bc90ad7149645be72c05e2e47f3` |
| `LoadingBayPickups.cs` | Consulted resource eligibility and existing owner settlement; Hotel retains its own explicit E pickup flow | `e947b8936a7110d5b99edaeace39e37deb64c5037efde91519bab9c8c8f549f1` |

Hotel's `HotelCombat` uses its existing supplies owner, real scene identities,
Engine Track, spatial casts and character controller. Its explicit windup,
locked direction, recovery, standoff and short sight-limited approach are Hotel
policy. No E1M1 catalogs, density, random damage, projectiles, drops, armor,
entity remapping, session framework or donor navigation were imported. The two
resident silhouettes and weapon poses are authored here using the existing
material palette and Engine mesh resources. No sibling is a build dependency.

## Spirit interface and presentation

The spirit's pact, bell-headed moth, intervention and phases are Hotel-authored.
They extend the existing route, supplies, combat, scene and HUD owners. No donor
summoning system, follower/pathfinding state or timer was copied.

CraftSurvive's semantic UI flow supplies a consultation pattern. `src/ui/screens.ts` → `PlayerActionModule.Update` →
`PlayerAction.Parse` showed the claim/contract/owner/projection boundary. Hotel
adapts that boundary with one typed equip choice and pact revision; it rejects
the donor's broad action catalog, survival/build rules and UI timer machinery.
The Engine's matching `csharp-controller-interaction` fixture and lifecycle guide
establish the paused-intent callback. No fixture panel or source checkout is a
dependency. Rendering uses the existing Engine mesh/appearance/snapshot path;
spatial targeting extends the already-consulted Doom-style Engine ray flow.

| Consulted CraftSurvive source | SHA-256 |
| --- | --- |
| `src/ui/screens.ts` | `22f6e62cadb2c70cbe4a09024eac8a75a7b081421f0dd95529f2c4ce13358a8d` |
| `src/CraftSurvive.Game/Modules/Actions/PlayerActionModule.cs` | `eaf735ff279a3a26c08757381d2059b01952e5e3b2f5ee29cde3e8cb09756a61` |
| `src/CraftSurvive.Game/Modules/Actions/PlayerAction.cs` | `f55f0a501523b91fe2063e7ae58c8c899ea7734f3bc8a3d68c46d92f389a6bcd` |

## Refuge persistence

Dungeon supplies the persistence consultation pattern. `DelveSaveStore` and `DelveProduct.Start`, level-change saving,
`Pause`, `Restart`, `Shutdown` and `FinishRun` establish the explicit caller →
ProductStateStore / source-generated JsonProductStateCodec boundary. Hotel adapts
that small boundary into one whole refuge checkpoint. It rejects Dungeon's two
run/meta records, level-change/pause/exit writes, stale-save deletion and
permadeath policy. Hotel saves only initial state and explicit refuge returns;
present invalid data reports an error. Exact matching Engine store/codec and SDK
contracts were checked against the installed pair and exercised in EngineTestHost.

| Consulted Dungeon source | SHA-256 |
| --- | --- |
| `src/DelveRpg.Host/Save/DelveSaveStore.cs` | `9ccf87dfca7ca07c1f64288f1069369a46e80463dad9d841f5ea973e14213d53` |
| `src/DelveRpg.Host/DelveProduct.cs` | `b3a647907909ecc12fe31e0dd058e48036863e2f0f3db12a28e4e31acd7b1311` |

Domain capture/restore extends Hotel's existing owners. No donor session,
serializer framework, raw persistence handles, file transport or sibling build
dependency is copied.

## Supplies interaction

CraftSurvive `slots.ts` (SHA-256
`636906660aa93ad39945b0926cfb49b1567592540c51096a374251858b07d99d`)
and the `screens.ts` → `PlayerActionModule` → `PlayerAction` snapshots above
supply the consulted interaction pattern.
Hotel adapts the pointer threshold, projected slot contents and semantic move/use
boundary. It retains its existing Engine ledger and revision-checked supplies
owner, adds a keyboard destination alternative, and uses the first three case
pockets as quick access. Half-stack splitting, equipment placeholders, crafting
and donor framework are excluded. Hotel's existing paused spirit-claim route
supplies the lifecycle pattern, verified against the installed Engine pair's
matching lifecycle guide. There is no sibling dependency or local transport.
