import { itemIcon } from './item-icon.js';

/**
 * The field case's worn column: each equipment slot and what it holds. Choosing a slot shows it in the case's detail
 * panel; dragging a worn item out is reported to the case, which decides where it was released. C# owns slots,
 * eligibility and the stats they feed.
 */
export function mountWornSlots(document, { choose, drag }) {
  const element = document.createElement('div');
  element.className = 'worn-slots';
  element.setAttribute('role', 'group');
  element.setAttribute('aria-label', 'Worn');
  let slots = [];
  const draw = (worn, selected) => {
    if (slots.length !== worn.length) {
      element.replaceChildren(...worn.map((_, index) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'worn-slot';
        button.dataset.slot = index;
        button.addEventListener('click', () => choose(index));
        drag(button, () => (slots[index]?.id ? index : null));
        return button;
      }));
    }
    slots = worn;
    for (const button of element.children) {
      const index = Number(button.dataset.slot);
      const slot = slots[index];
      const name = document.createElement('span');
      name.className = 'worn-slot-name';
      name.textContent = slot.slot;
      const holds = document.createElement('span');
      holds.className = 'worn-slot-item';
      holds.replaceChildren(...(slot.id ? [itemIcon(document, slot.id, slot.mark), ` ${slot.name}`] : ['—']));
      button.replaceChildren(name, holds);
      button.classList.toggle('occupied', !!slot.id);
      button.setAttribute('aria-pressed', String(index === selected));
      button.setAttribute('aria-label', `${slot.slot}, ${slot.id ? slot.name : 'empty'}`);
    }
  };
  return { element, draw, focus(index) { element.children[index]?.focus(); } };
}
