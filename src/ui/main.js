import { mountHud } from './hud.js';
import { mountMenuScreen } from './menu-screen.js';
import { mountCaseScreen } from './case-screen.js';
import { mountControlsScreen } from './controls-screen.js';
import { mountReadingScreen } from './reading-screen.js';
import { mountConsoleScreen } from './console-screen.js';
import { createPauseFlow } from './pause.js';

/**
 * Composition, navigation and focus only. Each screen module owns its markup and drawing;
 * Engine owns gameplay input, lifecycle and console machinery.
 */
export function mountProductUi(root, context) {
  const document = root.ownerDocument;
  const layer = document.createElement('section');
  layer.className = 'hotel-ui';
  layer.setAttribute('aria-label', 'Hotel Endless');
  layer.innerHTML = `<link rel="stylesheet" href="${new URL('./hotel.css', import.meta.url)}">`;
  // Product opt-in controls access; the Engine host separately requires --live-debug.
  const developerEnabled = new URLSearchParams(document.defaultView.location.hash.slice(1)).get('developer') === '1';
  const hud = mountHud(document);
  const menu = mountMenuScreen(document, { developerEnabled });
  const reading = mountReadingScreen(document, { name: 'reading', eyebrow: 'Found in the hotel', closeLabel: 'Put down' });
  const refuge = mountReadingScreen(document, { name: 'refuge', eyebrow: 'Refuge ledger', closeLabel: 'Return to hotel' });
  const fieldCase = mountCaseScreen(document, context.intents);
  const screens = Object.fromEntries([menu, fieldCase, mountControlsScreen(document), reading, refuge, mountConsoleScreen(document)]
    .map(view => [view.name, view]));
  const foreground = document.createElement('div');
  foreground.className = 'foreground';
  foreground.hidden = true;
  foreground.setAttribute('data-rusty-ui-interactive', '');
  foreground.append(...Object.values(screens).map(view => view.element));
  layer.append(hud.element, foreground);
  root.append(layer);
  let screen = null;
  let returnScreen = null;
  let readingSequence = -1;
  let refugeSequence = -1;
  let disposed = false;
  let pause;

  const syncPointer = () => hud.setNavigationHidden(screen !== null || document.pointerLockElement !== null);
  const present = (next, parent = null) => {
    if (next === 'console' && !developerEnabled) return;
    for (const view of Object.values(screens)) view.leave();
    screen = next;
    returnScreen = parent;
    foreground.hidden = next === null;
    hud.element.hidden = next !== null;
    for (const view of Object.values(screens)) view.element.hidden = view.name !== next;
    context.ui.setInteractionMode(next === null ? 'gameplay' : 'interface');
    syncPointer();
    if (next === null) context.ui.focusGameplay();
    else screens[next].enter();
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
      const view = screens[screen].element;
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
    menu.drawLifecycle({ state, pending });
    for (const button of layer.querySelectorAll('[data-open],[data-close],[data-return],[data-pause]')) button.disabled = !!pending;
  };
  const draw = envelope => {
    if (disposed || envelope?.stream !== 'rusty-hotel' || envelope.contract !== 'rusty.hotel.hud') return;
    const facts = envelope.value;
    hud.draw(facts);
    menu.draw(facts);
    fieldCase.draw(facts);
    if (readingSequence !== facts.readingSequence) {
      readingSequence = facts.readingSequence;
      reading.show(facts.readingTitle, facts.readingText);
      if (facts.readingTitle) void show('reading', screen === null ? null : { screen, back: returnScreen });
    }
    if (refugeSequence !== facts.refugeSequence) {
      refugeSequence = facts.refugeSequence;
      refuge.show(facts.refugeTitle, facts.refugeText);
      if (facts.refugeTitle) void show('refuge');
    }
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
    for (const view of Object.values(screens)) view.dispose?.();
    layer.removeEventListener('click', onClick);
    document.removeEventListener('keydown', onKey, true);
    document.removeEventListener('pointerlockchange', syncPointer);
    layer.remove();
  } };
}
