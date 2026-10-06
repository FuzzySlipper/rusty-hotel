# Hotel interface

This document owns the intended player surfaces and developer-access boundary.
The implemented HUD, field case, menu and developer console are described at the
end. Extend these curated surfaces as gameplay domains arrive.
[Design](design.md) owns the game direction.

## Player surfaces

Keep the hotel view dominant. Use restrained cream, tobacco/wood brown, muted
teal and burgundy accents, legible typography, and a few analog label/folio cues.
The richly patterned wallpaper belongs in the world; text backgrounds should
stay quiet. Do not reproduce the old four-person party portraits or large CRPG
combat slab. Final sizes and colors should be tuned against the rendered scene.

| Surface | Deliberate contents | Entry and return |
| --- | --- | --- |
| Exploration HUD | Small reticle/focus prompt; health and stamina at lower left; what the hands hold and ammo at lower right; equipped spirit and summon resource nearby; brief pickup/result notice | Default during play. No persistent list of action buttons, transcript, developer values, or menu column. |
| Field case | Focused Supplies, Worn and Spirits views; readable collection/slots, one selected entry's details, and only its relevant use/wear/equip action | Inventory control opens one bounded overlay; clear Back/Close returns to exploration. Show honest empty states as domains arrive. |
| Reading/inspection | One note, tape transcript or found object's content, with contextual navigation only where needed | Ordinary world use opens it; closing returns to the same situation. Do not turn it into a general command palette. |
| Refuge | The current return/checkpoint interaction and its result | Use the refuge fixture to open a small contextual surface. Inventory remains in the field case; no duplicate state editor. |
| Pause | Resume, controls/settings that actually work, and appropriate session actions | Pause control opens it; resume uses Engine lifecycle. In a debug-enabled session, one labeled Developer console entry may lead to the Engine console. |

These are a small set of product surfaces, not a generic screen framework.
Implement working HUD, pause and field-case navigation first. Reserve reading
and refuge designs, then populate them when their domains exist. Do not ship
fake inventory items, nonfunctional settings, or placeholder success actions
to fill out the UI. A mockup demonstrates layout only.

Opening a foreground surface must give it keyboard/pointer focus and suppress
movement, attacks, and summons from typing or clicking. Use the Engine's input
and lifecycle facilities; clear pending/held actions at transitions. Declare
and test whether each surface pauses simulation rather than accidentally letting
an overlay imply a pause. Closing restores the prior valid screen/focus, and
returns pointer lock through the supported host flow without an accidental
attack. Escape closes the active surface before opening another. Avoid stacked
inventory, pause, and console panels competing for input.

Provide visible keyboard focus, readable contrast, clear selection and disabled
reasons, and scalable layout at the supported desktop sizes. Keep essential HUD
facts and Close/Back controls in view. Scroll or paginate bounded collections and
long text when appropriate; do not make the game viewport an endlessly scrolling
document. Resource feedback must not rely on color alone.

## HUD budget

The exploration HUD is a fixed set of regions. A new fact joins one of them or
displaces something; adding a region is a design decision recorded here first.

| Region | Holds |
| --- | --- |
| Top left | Location: product mark and current wing/room |
| Top right | Field case and Menu entries, only while the pointer is not captured |
| Centre | Reticle, hit marker, and the one Engine-selected focus prompt |
| Notice slot | One brief result notice, chosen by priority and expiring on admitted time; never a stack or log |
| Lower band | At most three clusters: condition (left: health and the investigator's active effects, each a small chip that appears while it lasts), quick access (centre), held status (right: reserves, hands, spirit) |
| Viewport edge | Transient damage accent only |

Every key the player sees comes from the one binding table, `content/input/bindings.json`.
That includes the Controls screen, the `<kbd>` shortcut badges, quick-pocket keys and
key names inside authored text (`{key.action}`). The DOM companion handles the screen
shortcuts (field case, menu, console) by the browser codes published with the HUD
facts, and never spells a key itself.

Prefer contextual over persistent. A control hint appears when it applies (a
focus prompt, a refusal, an early-play reminder) and then goes away. The complete
controls reference lives on the Controls screen, and full resource and inventory
detail lives in the field case. A cluster shows its current value and state, not
explanations. When a cluster outgrows its region, move the detail to the screen
that owns it rather than widening the band.

Each foreground screen is its own module in `src/ui/` with its own markup and
draw function. `main.js` owns only screen presence, navigation, focus and the
lifecycle flow. Labels shown to the player come from C# facts or authored
content, not from literals repeated in DOM templates.

## Developer console

The Engine command console is the standard home for developer and agent hooks.
It may expose inspection, scene/fixture setup, testing triggers and explicitly
identified cheats. Hotel owns the meaning of its commands and the choice to
show developer access. Engine owns the command catalog/generation, dispatch,
client, transport, history/transcript UI and console presentation.

Use the installed pair's opt-in live-debug panel with overlay or dock presentation
behind a clearly labeled Developer console entry/shortcut. Keep it closed and
unmounted until requested, with no debug traffic from the normal gameplay UI.
The host must explicitly enable the live-debug channel. A session without it
must still play normally; unavailable developer access must not pretend success.
Dispose the mounted panel and its owned requests on close/unmount. An in-flight
mount that resolves after closing must also be disposed.

Register product commands through the supported generated C# catalog. Small
adapters call the same domain owners as real interactions. Normal action commands
retain real range, eligibility, inventory, and target-revision rules. Commands
that intentionally change fixtures or bypass rules must be clearly named and
described as developer overrides (for example, a `hotel.dev.*` namespace).
Do not implement a parallel gameplay path or shadow state for automation.

Prefer clear command names, usage/help and honest success/failure results. Add
commands when a task needs them, not an upfront matrix for every future feature.
Do not add spawn/heal/give/summon/teleport buttons around the game to make tests
convenient. The one console entry is navigation to a developer tool, not a
growing collection of feature triggers.

### Packaged capability and donor pointers

The installed pair's `runtime-pack/share/live-debug-panel/index.d.ts` exposes
`mountLiveDebugPanel`, `enabled`, and `dispose`; its model declares `inline`,
`dock`, and `overlay` presentation. Resolve these from the selected package when
implementing; do not hardcode cache paths in build configuration. The paired
managed assembly contains the generated debug-command registration contracts.
Concrete signatures and bundling must be checked against the selected pin.

- CraftSurvive: `src/ui/developer.ts` shows lazy Engine panel/client mounting,
  failure handling, and disposal. Adapt the lifecycle; do not copy its surrounding
  inline details/metrics controls into Hotel's player UI.
- Doom: `csharp/LoadingBay.Game/LoadingBayProduct.cs` shows
  `IDebugCommandModuleSource.RegisterDebugCommands` and module registration.
- FpTester: `src/FpTester.Game/FpTesterProduct.cs` shows the Engine playtest and
  interaction debug modules. Borrow only the applicable hook semantics, not its
  lab telemetry or reset controls.

These are one-time consultation pointers under [reuse.md](reuse.md), not sibling
dependencies. Prefer packaged helpers to copying their implementation. A missing
safe capability is an upstream issue, not a reason to build a local console.

## Completion and evidence

For a player feature, name the intended surface and verify the full sequence:
enter it through normal play, perform the action, read the result, and return to
play. The feature is not player-complete merely because a console command or a
temporary button can trigger the underlying code.

A task explicitly scoped to a backend, command hook, or prototype may finish
within that scope. Report it as such and retain the ordinary player integration
in its named Den task. When a larger task promises both, leave that task open
until both exist. Console setup can assist reproduction, but record it separately
and prove actual acquisition/navigation/interaction normally when those are the
behavior under acceptance. Use honest labels such as command-tested,
UI-integrated, and ordinary-player verified.

UI review checks the purpose, visual hierarchy, entry/exit, focus behavior and
feedback of each new control. Adding tabs, accordions, or a new drawer around
an unrelated test-button list does not satisfy this contract.

## Current DOM companion

`src/ui/main.js` exports `mountProductUi`. It composes `hud.js` and one module per
foreground screen (`menu-screen.js`, `case-screen.js`, `controls-screen.js`,
`reading-screen.js` for both readings and the refuge ledger, `console-screen.js`).
Each module returns its element and `enter`/`leave` focus hooks, plus drawing
where it needs it. Adding a screen means a new module and one entry in `main.js`'s
screen list. Exploration shows a compact title and
wing label, reticle, quick pockets, hands and spirit status, plus a short opening reminder
of the basic controls that expires after the authored number of seconds of play. C#
`Interface/HotelHud.cs` publishes those facts through the Engine UI stream, along
with the supplies owner's health, ammunition and summon reserves. Brief pickup
feedback appears above the HUD and expires on admitted simulation time.

I opens the field case; Escape opens the pause menu or closes the current surface.
The menu offers Resume hotel, Field case and Controls. Supplies has a bounded
pocket grid, quick pockets, the case's pocket count and capacity load, and
selected-pocket details; a wearable item's action is Wear instead of Use. Worn
(`worn-view.js`) lists every equipment slot with what it holds; a selected slot
shows the worn item and its Take off action, which returns it to a pocket. Spirits (`pact-view.js`) has an honest
empty state until a spirit is freed, then one card per pact made; the selected
pact's details give Equip, or Let rest for the one in the pact slot, which Q calls. Real supply stacks populate the grid; selected details show their
description, quantity and any use refusal reason. `field-case.js` owns presentation
selection and tab navigation only. Use and Move stack are the selected supply's contextual actions. Move stack then
a destination supports keyboard activation; pointer dragging sends the same
move claim. Matching stacks merge and different supplies swap. Both submit
revisioned semantic actions through Engine paused delivery and redraw C# facts.
The first three pockets also appear on the HUD; 3–5 use them during exploration.
These are the same saved pockets, not a separate collection or assignment state.
Closing the case or changing tabs cancels unfinished move gestures. Do not route
player actions through live-debug or resume simulation just to deliver them.

One foreground screen is visible at a time. Tab focus stays inside it; closing a
child restores its preceding surface. Engine interface/gameplay mode transitions
handle pointer-lock release and input clear. All foreground screens, including
the opted-in console, request a real Engine pause through `context.lifecycle`.
Nested navigation retains pause; closing the final surface requests resume before
`focusGameplay` restores play through the host. The status says Paused only when
the Engine reports it. Pending transitions suppress duplicate navigation. A
failed request keeps its error visible; a running menu offers Pause hotel to retry.

Add `#developer=1` to the hosted page URL to opt into the menu's Developer console
entry and F2 shortcut. The host independently requires `--live-debug`. The
`developer.js` adapter lazily imports and mounts the packaged Engine dock inside
the bounded console screen, disposes it on close, and disposes stale async mounts.
It owns no parser, transport, polling or command state. The generated C# catalog
exposes `hotel.inspect` and the explicit developer excursion reset
`hotel.dev.return-to-entrance`, both over existing product owners. The latter
resets live player, supplies, combat, pact and route state to the initial excursion;
it does not replace the stored checkpoint. See [development.md](development.md).

`pause.js` owns only request lifetime and presentation errors. It subscribes to
the Engine lifecycle port rather than mirroring runtime state. A pause made
elsewhere opens the ordinary menu; external resume updates its status without
stealing foreground focus. A rejected or superseded resume leaves the screen
open. Disposal unsubscribes and prevents a late response from refocusing gameplay.
The ordinary lifecycle path does not require live-debug. Product pause/resume
callbacks clear held input; Engine owns admission, pending-input clear and time.

Keep only browser assets in `src/ui/`. The host admits every staged file by its
content type; documentation belongs under `docs/`.

Keep this lane to DOM presentation, accessibility, and semantic actions.
Player state lives in C#; input delivery, projection transport, the canvas,
and rendering belong to Engine. Dispose event listeners and subscriptions
when the host unmounts the UI.

### World focus and reading

The exploration HUD presents the Engine-selected action beside the reticle:
E to use, a move-closer hint, a latched door's far-side reason, or a locked door's prompt naming the key it needs.
A key is an ordinary find (E · Take key to the guest room); the route holds it for that floor, and it is not a
supplies pocket item. Occluded
objects do not advertise an actionable prompt. Using the refuge notice or a field log
opens one reading surface with authored title/text and Put down / Escape. It
uses the same real Engine pause and final resume/focus flow as the field case.
The UI only observes reading facts; it does not acquire items or own door state.
Using a supply find instead calls the C# supplies owner. Success removes the
world prop and publishes its carried stack; a full case leaves the find in place
and presents the refusal through the brief result notice.

### Combat controls and feedback

Left click or Ctrl uses the primary action of what the main hand holds, one per
press; R or right click its second action (the pry bar's shove, the pistol's
reload), or the off hand's when the held item has one action. X swaps the two
hands' items; there is no key per weapon, and changing what is held otherwise is a
Wear action in the field case. Windup, strike and recovery are shown by the actual
first-person pose of the held item and the Hands HUD state, which names the item in
hand and the one at your side. Swapping and another action cannot cancel the
committed action; an unmet cost or a cooldown is a brief notice. A quick key (3–5) uses its belt item through the
hands: the Hands state shows the use's own windup (unwrapping a dressing, reaching for a supply), and the item is
used as it lands. Q calls the equipped pact through its own slot. A hit marks the reticle and names the
resident in a short result notice; damage briefly accents the viewport border
and updates the supplies owner's health.

Resident tells are in the world: the porter raises its hammer and lights its eye
before striking; the Lamplighter charges its eye before firing along a committed
direction. Recovery changes their posture. These are sparse room encounters,
not a combat dashboard. At zero health the HUD offers R to restore the refuge checkpoint. Recovery
opens a focused result receipt before returning to play.
Opening a foreground screen still pauses the Engine and clears pending physical
actions. Normal combat never dispatches through the developer console.

### Spirit encounter, equipment and call

E lifts the bell in the linen room to free Hushwing. Its world presence disappears
from the pedestal, the notice names the pact and two welcome charges, and I →
Spirits shows one selected pact with its description and Equip Hushwing action.
The authoritative projection changes the card to Equipped and the compact HUD
to Hushwing. Let Hushwing rest unequips it; this is disabled during a manifestation
or defeat with the reason shown. Equip uses normal semantic claims while paused.
Closing returns to ordinary play with no queued summon.

Q calls toward the resident under the reticle. The creature's arrival interrupts
that resident; hushing holds it briefly, then its wings fold and it leaves. The
HUD names its phase and remaining shared reserve. An invalid target, missing
pact/equipped choice, active call, defeat or empty reserve gives a brief refusal
and spends nothing. Opening a foreground screen freezes the manifestation and
resident through Engine pause. No summon/equip debug panel is introduced.

### Stairs between floors

A flight of stairs is an ordinary world interaction with the same focus prompt: E · Climb the stairs, or E · Go back
down the stairs. The service stairs at the south end of the west wing's long service passage lead up; every generated
floor's stair landing has a flight up and a flight down. Using one changes floor at once, with no screen of its own:
the player arrives on the next floor's landing, or at the foot of the stairs below, carrying their supplies, pact and
what they wear and hold. The location label names the new floor's space. A floor that cannot be generated leaves the player where they
stand.

### Refuge return and recovery

The notebook on the refuge's west desk is the ordinary checkpoint interaction.
E records the return and opens a short refuge ledger receipt. It names secured
expedition finds and saved resources; its only navigation is Return to hotel /
Escape. A failed write displays Checkpoint not saved with the actual error and
retains the carried find. The pause menu reports checkpoint status without adding
save/load/reset controls. R while overwhelmed restores the full last checkpoint
and opens a recovery receipt. Both receipts share existing pause, focus and final
resume handling. Startup load failure is a real host product error, never a fake
successful new game. Developer return-to-entrance remains an unsaved fixture. R while overwhelmed on a generated
floor returns to the refuge's floor and its checkpoint.
