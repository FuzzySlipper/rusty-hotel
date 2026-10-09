import { focusFirstButton, screenElement } from './screen.js';

/**
 * Title menu: continue the saved expedition, begin a new one, delete an unreadable or unwanted save, options, quit.
 * Each choice is claimed through the paused hotel.title intent; C# decides and the facts say what happened. Replacing or
 * deleting a save asks first.
 */
export function mountTitleScreen(document, intents) {
  const element = screenElement(document, `
    <section class="hotel-screen title-screen" data-screen="title" hidden role="dialog" aria-modal="true" aria-labelledby="title-name">
      <header class="title-heading"><span class="eyebrow">Hotel</span><h2 id="title-name">Endless</h2></header>
      <p class="title-save" data-title-message role="status"></p>
      <div class="menu-actions" data-title-actions>
        <button type="button" data-choice="continue">Continue</button>
        <button type="button" data-choice="new">New expedition</button>
        <button type="button" data-choice="delete">Delete saved expedition</button>
        <button type="button" data-open="controls">Options</button>
      </div>
      <div class="title-confirm" data-confirm hidden>
        <p data-confirm-text></p>
        <div class="menu-actions"><button type="button" data-confirm-yes>Confirm</button><button type="button" data-confirm-no>Cancel</button></div>
      </div>
    </section>`);
  const message = element.querySelector('[data-title-message]');
  const actions = element.querySelector('[data-title-actions]');
  const confirmBox = element.querySelector('[data-confirm]');
  const confirmText = element.querySelector('[data-confirm-text]');
  let facts = null;
  let asking = null;
  const claim = (choice, confirm = false) => {
    try { intents.claim('hotel.title', { kind: 'product-payload', contract: 'hotel.title.v1', data: { choice, confirm } }); }
    catch { message.textContent = 'The choice could not be sent. Please try again.'; }
  };
  const ask = choice => {
    asking = choice;
    confirmText.textContent = choice === 'delete' ? facts.deleteWarning : facts.replaceWarning;
    actions.hidden = true;
    confirmBox.hidden = false;
    element.querySelector('[data-confirm-no]').focus();
  };
  const settle = () => { asking = null; actions.hidden = false; confirmBox.hidden = true; };
  element.addEventListener('click', event => {
    const button = event.target.closest('button');
    if (!button) return;
    if (button.dataset.choice === 'new' && facts?.saved) ask('new');
    else if (button.dataset.choice === 'delete') ask('delete');
    else if (button.dataset.choice) claim(button.dataset.choice);
    else if (button.hasAttribute('data-confirm-yes')) { claim(asking, true); settle(); }
    else if (button.hasAttribute('data-confirm-no')) { settle(); focusFirstButton(actions); }
  });
  return {
    name: 'title',
    element,
    enter() { settle(); focusFirstButton(actions); },
    leave() { settle(); },
    draw(all) {
      facts = all.title;
      message.textContent = facts.message;
      element.querySelector('[data-choice="continue"]').hidden = !facts.canContinue;
      element.querySelector('[data-choice="delete"]').hidden = !facts.saved;
      if (!element.hidden && !element.contains(document.activeElement)) focusFirstButton(actions);
    }
  };
}
