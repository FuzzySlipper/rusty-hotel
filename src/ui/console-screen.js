import { createDeveloperConsole } from './developer.js';
import { focusFirstButton, screenElement } from './screen.js';

/** Developer access only: hosts the packaged Engine console while this screen is open. */
export function mountConsoleScreen(document) {
  const element = screenElement(document, `
    <section class="hotel-screen console-screen" data-screen="console" hidden role="dialog" aria-modal="true" aria-labelledby="console-title">
      <header class="screen-heading"><div><span class="eyebrow">Developer access</span><h2 id="console-title">Command console</h2></div><button type="button" data-close>Back <kbd>Esc</kbd></button></header>
      <p class="console-help">Engine console · Try <code>hotel.inspect</code>. <code>hotel.dev.return-to-entrance</code> resets the route and returns you to the entrance.</p>
      <div class="console-host"></div><footer class="screen-footer" data-lifecycle-status role="status"></footer>
    </section>`);
  const developer = createDeveloperConsole(element.querySelector('.console-host'));
  return {
    name: 'console',
    element,
    enter() { focusFirstButton(element); void developer.open(); },
    leave() { developer.close(); },
    dispose() { developer.dispose(); }
  };
}
