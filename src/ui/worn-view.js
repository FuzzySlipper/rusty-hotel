import { itemIcon } from './item-icon.js';
/** The field case's Worn tab: equipment slots and what each holds. C# owns slots, eligibility and the stats they feed. */
export function mountWornView(document, claim) {
  const element = document.createElement('div');
  element.className = 'worn-view';
  element.hidden = true;
  element.innerHTML = `
    <div class="worn-slots" role="group" aria-label="Equipment slots"></div>
    <div class="worn-detail" aria-live="polite">
      <span class="eyebrow" data-worn-slot></span>
      <h3 data-worn-title></h3>
      <p data-worn-body></p>
      <button type="button" data-take-off>Take off</button>
      <p class="supply-result" data-worn-result role="status"></p>
    </div>`;
  const list = element.querySelector('.worn-slots');
  const takeOff = element.querySelector('[data-take-off]');
  let slots = [];
  let facts = {};
  let selected = 0;
  takeOff.addEventListener('click', () => {
    if (slots[selected]?.id) claim('takeOff', selected);
  });
  const select = index => {
    selected = index;
    for (const button of list.children) button.setAttribute('aria-pressed', String(Number(button.dataset.slot) === index));
    const slot = slots[index];
    element.querySelector('[data-worn-slot]').textContent = slot?.slot || '';
    element.querySelector('[data-worn-title]').replaceChildren(...(slot?.id ? [itemIcon(document, slot.id), ` ${slot.name}`] : ['—']));
    element.querySelector('[data-worn-body]').textContent = slot?.id ? slot.description : '';
    takeOff.disabled = !slot?.id;
    takeOff.textContent = slot?.id ? `Take off ${slot.name}` : 'Take off';
    element.querySelector('[data-worn-result]').textContent = facts.message || '';
  };
  return {
    element,
    draw(supplies) {
      facts = supplies;
      if (slots.length !== supplies.worn.length) {
        list.replaceChildren(...supplies.worn.map((_, index) => {
          const button = document.createElement('button');
          button.type = 'button';
          button.className = 'worn-slot';
          button.dataset.slot = index;
          button.addEventListener('click', () => select(index));
          return button;
        }));
      }
      slots = supplies.worn;
      for (const button of list.children) {
        const slot = slots[Number(button.dataset.slot)];
        button.replaceChildren(`${slot.slot} · `, ...(slot.id ? [itemIcon(document, slot.id), ` ${slot.name}`] : ['—']));
        button.classList.toggle('occupied', !!slot.id);
      }
      select(Math.min(selected, slots.length - 1));
    },
    focus() { list.children[selected]?.focus(); }
  };
}
