import { focusFirstButton, screenElement } from './screen.js';

/** The complete controls reference. The HUD only hints; this screen lists everything. */
export function mountControlsScreen(document) {
  const element = screenElement(document, `
    <section class="hotel-screen controls-screen" data-screen="controls" hidden role="dialog" aria-modal="true" aria-labelledby="controls-title">
      <header class="screen-heading"><div><span class="eyebrow">Finding your way</span><h2 id="controls-title">Controls</h2></div><button type="button" data-close>Back <kbd data-key="menu"></kbd></button></header>
      <dl class="control-list"></dl>
      <p>Click the hotel view to capture the mouse. Opening a screen releases it; returning to the hotel restores it.</p>
      <p class="live-note" data-lifecycle-status role="status"></p>
    </section>`);
  return {
    name: 'controls',
    element,
    enter() { focusFirstButton(element); },
    leave() {},
    // Rows come from the authored binding table; this screen only lays them out.
    draw(facts) {
      element.querySelector('.control-list').replaceChildren(...facts.controls.rows.flatMap(row => [
        Object.assign(document.createElement('dt'), { textContent: row.name }),
        Object.assign(document.createElement('dd'), { textContent: row.label })]));
    }
  };
}
