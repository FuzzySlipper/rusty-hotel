import { focusFirstButton, screenElement } from './screen.js';

/** One authored text surface: a found notice or log, or the refuge ledger's receipt. */
export function mountReadingScreen(document, { name, eyebrow, closeLabel }) {
  const element = screenElement(document, `
    <section class="hotel-screen reading-screen" data-screen="${name}" hidden role="dialog" aria-modal="true" aria-labelledby="${name}-title">
      <header class="screen-heading"><div><span class="eyebrow">${eyebrow}</span><h2 id="${name}-title" data-title></h2></div><button type="button" data-close>${closeLabel} <kbd>Esc</kbd></button></header>
      <div class="reading-text" data-text></div><footer class="screen-footer" data-lifecycle-status role="status"></footer>
    </section>`);
  return {
    name,
    element,
    enter() { focusFirstButton(element); },
    leave() {},
    show(title, text) {
      element.querySelector('[data-title]').textContent = title || '';
      element.querySelector('[data-text]').textContent = text || '';
    }
  };
}
