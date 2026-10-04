import { mountFieldCase, quickPocketViews } from './field-case.js';
import { createDeveloperConsole } from './developer.js';
import { createPauseFlow } from './pause.js';

/** Presentation and navigation only. Engine owns gameplay input, lifecycle and console machinery. */
export function mountProductUi(root, context) {
  const document = root.ownerDocument;
  const layer = document.createElement('section');
  layer.className = 'hotel-ui';
  layer.setAttribute('aria-label', 'Hotel Endless');
  layer.innerHTML = `
    <link rel="stylesheet" href="${new URL('./hotel.css', import.meta.url)}">
    <div class="exploration">
      <header class="hotel-title"><span class="eyebrow">Hotel</span><h1>Endless</h1><span class="wing" data-location>Arriving…</span></header>
      <nav class="hud-nav" aria-label="Hotel navigation"><button type="button" data-open="case">Field case <kbd>I</kbd></button><button type="button" data-open="menu">Menu <kbd>Esc</kbd></button></nav>
      <div class="hurt-screen" hidden></div>
      <span class="reticle" aria-hidden="true"></span>
      <div class="focus-prompt" data-focus-prompt hidden></div>
      <div class="supply-notice" data-supply-notice hidden role="status"></div>
      <div class="travel-hint"><span>WASD · Walk</span><span>Mouse · Look</span><span>Click / Ctrl · Attack</span><span>1 / 2 · Weapon</span><span>R · Reload</span><span>3–5 · Quick supply</span><span>E · Use</span><span>Q · Spirit</span><span>I · Field case</span><span>Esc · Menu</span></div>
      <div class="hud-bottom">
        <div class="condition"><span class="eyebrow">On your own</span><span data-health></span></div>
        <div class="quick-access"><span class="eyebrow">Quick access</span><div class="quick-pockets" data-hud-pockets></div></div>
        <div class="held-status"><div><span class="eyebrow">Reserves</span><span data-resources></span></div><div><span class="eyebrow">Hands</span><span data-weapon>—</span><span class="eyebrow" data-combat-action></span></div><div><span class="eyebrow">Spirit</span><span data-spirit>—</span><span class="spirit-state" data-spirit-state></span></div></div>
      </div>
    </div>
    <div class="foreground" hidden data-rusty-ui-interactive>
      <section class="hotel-screen menu-screen" data-screen="menu" hidden role="dialog" aria-modal="true" aria-labelledby="menu-title">
        <header class="screen-heading"><div><span class="eyebrow">Hotel Endless</span><h2 id="menu-title">Menu</h2></div></header>
        <div class="menu-actions"><button type="button" data-return>Resume hotel</button><button type="button" data-pause hidden>Pause hotel</button><button type="button" data-open="case">Field case <kbd>I</kbd></button><button type="button" data-open="controls">Controls</button><button type="button" data-open="console" data-developer hidden>Developer console <kbd>F2</kbd></button></div>
        <p class="live-note" data-checkpoint-status></p>
        <p class="live-note" data-lifecycle-status role="status"></p>
      </section>
      <section class="hotel-screen case-screen" data-screen="case" hidden role="dialog" aria-modal="true" aria-labelledby="case-title">
        <header class="screen-heading"><div><span class="eyebrow">Personal effects</span><h2 id="case-title">Field case</h2></div><button type="button" data-close>Close <kbd>Esc</kbd></button></header>
        <div class="field-case"></div><footer class="screen-footer" data-lifecycle-status role="status"></footer>
      </section>
      <section class="hotel-screen controls-screen" data-screen="controls" hidden role="dialog" aria-modal="true" aria-labelledby="controls-title">
        <header class="screen-heading"><div><span class="eyebrow">Finding your way</span><h2 id="controls-title">Controls</h2></div><button type="button" data-close>Back <kbd>Esc</kbd></button></header>
        <dl class="control-list"><dt>Walk & strafe</dt><dd>W A S D</dd><dt>Look around</dt><dd>Mouse</dd><dt>Attack</dt><dd>Left click / Ctrl</dd><dt>Pry bar / pistol</dt><dd>1 / 2</dd><dt>Load cartridges / restore checkpoint when downed</dt><dd>R</dd><dt>Use pockets 01–03</dt><dd>3 / 4 / 5</dd><dt>Call equipped spirit</dt><dd>Q</dd><dt>Use / read</dt><dd>E</dd><dt>Field case</dt><dd>I</dd><dt>Menu / close screen</dt><dd>Esc</dd></dl>
        <p>Click the hotel view to capture the mouse. Opening a screen releases it; returning to the hotel restores it.</p>
        <p class="live-note" data-lifecycle-status role="status"></p>
      </section>
      <section class="hotel-screen reading-screen" data-screen="reading" hidden role="dialog" aria-modal="true" aria-labelledby="reading-title">
        <header class="screen-heading"><div><span class="eyebrow">Found in the hotel</span><h2 id="reading-title" data-reading-title></h2></div><button type="button" data-close>Put down <kbd>Esc</kbd></button></header>
        <div class="reading-text" data-reading-text></div><footer class="screen-footer" data-lifecycle-status role="status"></footer>
      </section>
      <section class="hotel-screen reading-screen" data-screen="refuge" hidden role="dialog" aria-modal="true" aria-labelledby="refuge-title">
        <header class="screen-heading"><div><span class="eyebrow">Refuge ledger</span><h2 id="refuge-title" data-refuge-title></h2></div><button type="button" data-close>Return to hotel <kbd>Esc</kbd></button></header>
        <div class="reading-text" data-refuge-text></div><footer class="screen-footer" data-lifecycle-status role="status"></footer>
      </section>
      <section class="hotel-screen console-screen" data-screen="console" hidden role="dialog" aria-modal="true" aria-labelledby="console-title">
        <header class="screen-heading"><div><span class="eyebrow">Developer access</span><h2 id="console-title">Command console</h2></div><button type="button" data-close>Back <kbd>Esc</kbd></button></header>
        <p class="console-help">Engine console · Try <code>hotel.inspect</code>. <code>hotel.dev.return-to-entrance</code> resets the route and returns you to the entrance.</p>
        <div class="console-host"></div><footer class="screen-footer" data-lifecycle-status role="status"></footer>
      </section>
    </div>`;
  root.append(layer);
  const exploration = layer.querySelector('.exploration');
  const foreground = layer.querySelector('.foreground');
  const screens = [...layer.querySelectorAll('[data-screen]')];
  const fieldCase = mountFieldCase(layer.querySelector('.field-case'), context.intents);
  const developer = createDeveloperConsole(layer.querySelector('.console-host'));
  // Product opt-in controls access; the Engine host separately requires --live-debug.
  const developerEnabled = new URLSearchParams(document.defaultView.location.hash.slice(1)).get('developer') === '1';
  layer.querySelector('[data-developer]').hidden = !developerEnabled;
  let screen = null;
  let returnScreen = null;
  let readingSequence = -1;
  let refugeSequence = -1;
  let disposed = false;
  let pause;

  const syncPointer = () => {
    layer.querySelector('.hud-nav').hidden = screen !== null || document.pointerLockElement !== null;
  };
  const present = (next, parent = null) => {
    if (next === 'console' && !developerEnabled) return;
    fieldCase.close();
    developer.close();
    screen = next;
    returnScreen = parent;
    foreground.hidden = next === null;
    exploration.hidden = next !== null;
    for (const view of screens) view.hidden = view.dataset.screen !== next;
    context.ui.setInteractionMode(next === null ? 'gameplay' : 'interface');
    syncPointer();
    if (next === null) context.ui.focusGameplay();
    else if (next === 'case') fieldCase.focus();
    else {
      layer.querySelector(`[data-screen="${next}"] button`).focus();
      if (next === 'console') void developer.open();
    }
  };
  const show = async (next, parent = null) => {
    if (disposed || pause.snapshot().pending || (next === 'console' && !developerEnabled)) return;
    if (next === null) {
      if (await pause.resume()) {
        if (!disposed) present(null);
      }
    } else {
      present(next, parent);
      await pause.pause();
    }
  };
  const close = () => show(returnScreen?.screen ?? null, returnScreen?.back ?? null);
  const onClick = event => {
    const button = event.target.closest('button');
    if (!button || !layer.contains(button)) return;
    if (button.hasAttribute('data-return')) show(null);
    else if (button.hasAttribute('data-pause')) void pause.pause();
    else if (button.hasAttribute('data-close')) close();
    else if (button.dataset.open) show(button.dataset.open, screen === null ? null : { screen, back: returnScreen });
  };
  const onKey = event => {
    if (pause.snapshot().pending && ['Escape', 'KeyI', 'F2'].includes(event.code)) {
      event.preventDefault(); event.stopPropagation(); return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      if (event.repeat) return;
      if (screen !== null) close(); else show('menu');
      return;
    }
    const editing = event.target instanceof Element && event.target.closest('input,textarea,select,[contenteditable="true"]');
    if (!editing && !event.ctrlKey && !event.altKey && !event.metaKey) {
      if (event.code === 'KeyI' && (screen === null || screen === 'case' || screen === 'menu')) {
        event.preventDefault();
        event.stopPropagation();
        if (!event.repeat) { if (screen === 'case') close(); else show('case', screen === null ? null : { screen, back: returnScreen }); }
      } else if (event.code === 'F2' && developerEnabled) {
        event.preventDefault();
        event.stopPropagation();
        if (!event.repeat) { if (screen === 'console') close(); else show('console', screen === null ? null : { screen, back: returnScreen }); }
      }
    }
    if (event.key === 'Tab' && screen !== null) {
      const view = layer.querySelector(`[data-screen="${screen}"]`);
      const focusable = [...view.querySelectorAll('button,input,textarea,select,a[href],[tabindex]')]
        .filter(node => !node.disabled && node.tabIndex >= 0 && node.getClientRects().length > 0);
      const first = focusable[0], last = focusable.at(-1);
      if (event.shiftKey && (document.activeElement === first || !view.contains(document.activeElement))) {
        event.preventDefault(); last?.focus();
      } else if (!event.shiftKey && (document.activeElement === last || !view.contains(document.activeElement))) {
        event.preventDefault(); first?.focus();
      }
    }
  };
  const drawLifecycle = ({ state, pending, error }) => {
    if (disposed) return;
    // External Engine pauses also get the ordinary menu, never a frozen bare HUD.
    if (state === 'paused' && screen === null) present('menu');
    const message = pending ? (pending === 'paused' ? 'Pausing…' : 'Resuming…')
      : state === 'paused' ? 'Hotel paused.' : state === 'running' ? 'The hotel is running.' : 'Hotel unavailable.';
    for (const note of layer.querySelectorAll('[data-lifecycle-status]')) note.textContent = error ? `${error} ${message}` : message;
    layer.querySelector('#menu-title').textContent = state === 'paused' ? 'Paused' : 'Menu';
    layer.querySelector('[data-return]').textContent = state === 'paused' ? 'Resume hotel' : 'Return to hotel';
    layer.querySelector('[data-pause]').hidden = state !== 'running' || !!pending;
    for (const button of layer.querySelectorAll('[data-open],[data-close],[data-return],[data-pause]')) button.disabled = !!pending;
  };
  const draw = envelope => {
    if (disposed || envelope?.stream !== 'rusty-hotel' || envelope.contract !== 'rusty.hotel.hud') return;
    const facts = envelope.value;
    layer.querySelector('[data-location]').textContent = facts.location;
    layer.querySelector('[data-weapon]').textContent = facts.weapon;
    layer.querySelector('[data-spirit]').textContent = facts.spirit;
    layer.querySelector('[data-spirit-state]').textContent = facts.spiritStatus || '';
    layer.querySelector('[data-health]').textContent = `Health ${facts.health} / ${facts.maximumHealth}`;
    layer.querySelector('[data-resources]').textContent = `Ammo ${facts.ammo} / ${facts.maximumAmmo} · Summon ${facts.summon} / ${facts.maximumSummon}`;
    const notice = layer.querySelector('[data-supply-notice]');
    notice.textContent = facts.spiritNotice || facts.combatNotice || facts.supplyNotice || '';
    notice.hidden = !notice.textContent;
    layer.querySelector('[data-combat-action]').textContent = facts.combatAction || '';
    layer.querySelector('.hurt-screen').hidden = !facts.hurt;
    layer.querySelector('.reticle').classList.toggle('hit', !!facts.hit);
    layer.querySelector('[data-hud-pockets]').replaceChildren(...quickPocketViews(document, facts));
    const prompt = layer.querySelector('[data-focus-prompt]');
    prompt.textContent = facts.focusPrompt || '';
    prompt.hidden = !facts.focusPrompt;
    if (readingSequence !== facts.readingSequence) {
      readingSequence = facts.readingSequence;
      layer.querySelector('[data-reading-title]').textContent = facts.readingTitle || '';
      layer.querySelector('[data-reading-text]').textContent = facts.readingText || '';
      if (facts.readingTitle) void show('reading', screen === null ? null : { screen, back: returnScreen });
    }
    layer.querySelector('[data-checkpoint-status]').textContent = facts.checkpointStatus || '';
    if (refugeSequence !== facts.refugeSequence) {
      refugeSequence = facts.refugeSequence;
      layer.querySelector('[data-refuge-title]').textContent = facts.refugeTitle || '';
      layer.querySelector('[data-refuge-text]').textContent = facts.refugeText || '';
      if (facts.refugeTitle) void show('refuge');
    }
    fieldCase.draw(facts);
  };
  pause = createPauseFlow(context.lifecycle, drawLifecycle);
  drawLifecycle(pause.snapshot());
  layer.addEventListener('click', onClick);
  document.addEventListener('keydown', onKey, true);
  document.addEventListener('pointerlockchange', syncPointer);
  const unsubscribe = context.projection?.subscribe(draw);
  draw(context.projection?.current());
  syncPointer();
  return { dispose() {
    disposed = true;
    unsubscribe?.();
    pause.dispose();
    developer.dispose();
    layer.removeEventListener('click', onClick);
    document.removeEventListener('keydown', onKey, true);
    document.removeEventListener('pointerlockchange', syncPointer);
    layer.remove();
  } };
}
