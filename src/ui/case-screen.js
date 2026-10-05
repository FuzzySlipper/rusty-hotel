import { mountFieldCase } from './field-case.js';
import { screenElement } from './screen.js';

/** Frame for the field case; the collection itself is field-case.js. */
export function mountCaseScreen(document, intents) {
  const element = screenElement(document, `
    <section class="hotel-screen case-screen" data-screen="case" hidden role="dialog" aria-modal="true" aria-labelledby="case-title">
      <header class="screen-heading"><div><span class="eyebrow">Personal effects</span><h2 id="case-title">Field case</h2></div><button type="button" data-close>Close <kbd>Esc</kbd></button></header>
      <div class="field-case"></div><footer class="screen-footer" data-lifecycle-status role="status"></footer>
    </section>`);
  const fieldCase = mountFieldCase(element.querySelector('.field-case'), intents);
  return {
    name: 'case',
    element,
    enter() { fieldCase.focus(); },
    leave() { fieldCase.close(); },
    draw(facts) { fieldCase.draw(facts); }
  };
}
