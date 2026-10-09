import { focusFirstButton, screenElement } from './screen.js';

/** Pause menu: resume, the other screens, leaving for the title menu, and checkpoint/lifecycle status. */
export function mountMenuScreen(document, { developerEnabled, intents }) {
  const element = screenElement(document, `
    <section class="hotel-screen menu-screen" data-screen="menu" hidden role="dialog" aria-modal="true" aria-labelledby="menu-title">
      <header class="screen-heading"><div><span class="eyebrow">Hotel Endless</span><h2 id="menu-title">Menu</h2></div></header>
      <div class="menu-actions"><button type="button" data-return>Resume hotel</button><button type="button" data-pause hidden>Pause hotel</button><button type="button" data-open="case">Field case <kbd data-key="fieldCase"></kbd></button><button type="button" data-open="controls">Controls</button><button type="button" data-leave-title>Title menu</button><button type="button" data-open="console" data-developer hidden>Developer console <kbd data-key="console"></kbd></button></div>
      <p class="live-note" data-checkpoint-status></p>
      <p class="live-note" data-lifecycle-status role="status"></p>
    </section>`);
  element.querySelector('[data-developer]').hidden = !developerEnabled;
  // Leaving for the title menu keeps nothing since the last refuge checkpoint; C# opens the menu and main.js shows it.
  element.querySelector('[data-leave-title]').addEventListener('click', () => {
    try { intents.claim('hotel.title', { kind: 'product-payload', contract: 'hotel.title.v1', data: { choice: 'leave' } }); } catch { }
  });
  return {
    name: 'menu',
    element,
    enter() { focusFirstButton(element); },
    leave() {},
    draw(facts) {
      element.querySelector('[data-checkpoint-status]').textContent = facts.refuge.checkpointStatus;
    },
    drawLifecycle({ state, pending }) {
      element.querySelector('#menu-title').textContent = state === 'paused' ? 'Paused' : 'Menu';
      element.querySelector('[data-return]').textContent = state === 'paused' ? 'Resume hotel' : 'Return to hotel';
      element.querySelector('[data-pause]').hidden = state !== 'running' || !!pending;
    }
  };
}
