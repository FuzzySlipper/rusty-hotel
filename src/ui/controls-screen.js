import { focusFirstButton, screenElement } from './screen.js';

/** The complete controls reference. The HUD only hints; this screen lists everything. */
export function mountControlsScreen(document) {
  const element = screenElement(document, `
    <section class="hotel-screen controls-screen" data-screen="controls" hidden role="dialog" aria-modal="true" aria-labelledby="controls-title">
      <header class="screen-heading"><div><span class="eyebrow">Finding your way</span><h2 id="controls-title">Controls</h2></div><button type="button" data-close>Back <kbd>Esc</kbd></button></header>
      <dl class="control-list"><dt>Walk & strafe</dt><dd>W A S D</dd><dt>Look around</dt><dd>Mouse</dd><dt>Attack</dt><dd>Left click / Ctrl</dd><dt>Pry bar / pistol</dt><dd>1 / 2</dd><dt>Load cartridges / restore checkpoint when downed</dt><dd>R</dd><dt>Use pockets 01–03</dt><dd>3 / 4 / 5</dd><dt>Call equipped spirit</dt><dd>Q</dd><dt>Use / read</dt><dd>E</dd><dt>Field case</dt><dd>I</dd><dt>Menu / close screen</dt><dd>Esc</dd></dl>
      <p>Click the hotel view to capture the mouse. Opening a screen releases it; returning to the hotel restores it.</p>
      <p class="live-note" data-lifecycle-status role="status"></p>
    </section>`);
  return { name: 'controls', element, enter() { focusFirstButton(element); }, leave() {} };
}
