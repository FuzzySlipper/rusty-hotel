import { itemIcon } from './item-icon.js';
import { mountWornSlots } from './worn-view.js';
import { mountPactView } from './pact-view.js';

/**
 * Projection-owned pockets, worn slots and selection; C# owns inventory, equipment and resource rules. The Case view
 * is one screen: what is worn on the left, the pockets in the middle, the chosen pocket or slot in the detail panel.
 * Stacks are dragged between pockets, onto the worn column to wear them, or out of the case to drop them; a worn item
 * dragged onto the pockets is taken off. Every drag and button claims the same revisioned supplies intent.
 */
export function mountFieldCase(host, intents) {
  const document = host.ownerDocument;
  host.innerHTML = `
    <div class="case-tabs" role="tablist" aria-label="Field case collection">
      <button type="button" id="case-tab" role="tab" aria-controls="case-collection" aria-selected="true" data-tab="case">Case <span data-count="case">0</span></button>
      <button type="button" id="spirits-tab" role="tab" aria-controls="case-collection" aria-selected="false" tabindex="-1" data-tab="spirits">Spirits <span data-count="spirits">0</span></button>
    </div>
    <div id="case-collection" class="case-collection" role="tabpanel" aria-labelledby="case-tab">
      <section class="case-worn"><h3>Worn</h3></section>
      <div class="case-contents">
        <div class="collection-heading"><h3 data-collection-heading>Carried supplies</h3><span class="eyebrow" data-load></span></div>
        <div class="pocket-grid" role="group" aria-label="Supply pockets"></div>
        <section class="case-quick"><h3>Quick access</h3><div class="quick-pockets" aria-label="Quick access pockets"></div><p class="muted" data-quick-note></p></section>
      </div>
      <aside class="item-detail" aria-live="polite"><span class="eyebrow" data-detail-number>Pocket 01</span>
        <div class="empty-emblem" aria-hidden="true">—</div><h3 data-detail-title>Empty pocket</h3><p data-detail-body>No supplies carried.</p><div class="supply-actions"><button type="button" data-use-supply>Use supply</button><button type="button" data-move-supply>Move stack</button><button type="button" data-drop-supply>Drop</button></div><p class="supply-result" data-supply-result role="status"></p>
      </aside>
    </div>`;
  const tabs = [...host.querySelectorAll('[data-tab]')];
  const grid = host.querySelector('.pocket-grid');
  const quick = host.querySelector('.quick-pockets');
  const wornColumn = host.querySelector('.case-worn');
  const pacts = mountPactView(document, intents);
  host.querySelector('.case-collection').append(pacts.element);
  let active = 'case';
  let pockets = 0;
  // What the detail panel shows: a pocket or a worn slot, by index.
  let chosen = { kind: 'pocket', index: 0 };
  let items = [];
  let supplyFacts = {};
  let spiritFacts = {};
  let moving = null;
  let dragging = null;
  const use = host.querySelector('[data-use-supply]');
  const move = host.querySelector('[data-move-supply]');
  const drop = host.querySelector('[data-drop-supply]');
  const supplyResult = host.querySelector('[data-supply-result]');
  const claim = (action, from, to, revision = supplyFacts.revision) => {
    try { intents.claim('hotel.supplies', { kind: 'product-payload', contract: 'hotel.supplies.v1',
      data: { action, from, ...(to === undefined ? {} : { to }), revision } }); }
    catch { supplyResult.textContent = 'The choice could not be sent. Close and reopen the field case.'; }
  };
  const cancel = () => { moving = dragging = null; grid.classList.remove('moving'); wornColumn.classList.remove('moving'); };

  // A drag from a pocket or a worn slot: past a few pixels it is a drag, and where it is released decides the claim.
  const draggable = (button, kind, from) => {
    button.addEventListener('pointerdown', event => {
      const index = from();
      if (event.button !== 0 || !intents || index === null || moving) return;
      dragging = { kind, from: index, revision: supplyFacts.revision, x: event.clientX, y: event.clientY, moved: false };
      button.setPointerCapture(event.pointerId);
    });
    button.addEventListener('pointermove', event => {
      if (!dragging || dragging.moved || Math.hypot(event.clientX - dragging.x, event.clientY - dragging.y) < 4) return;
      dragging.moved = true;
      if (dragging.kind === 'pocket') {
        grid.classList.add('moving');
        wornColumn.classList.toggle('moving', !!items[dragging.from]?.wearable);
        supplyResult.textContent = `Moving ${items[dragging.from]?.name || 'stack'} · release over another pocket${items[dragging.from]?.wearable ? ', on what you wear to wear it,' : ''} or outside the case to drop it.`;
      } else {
        grid.classList.add('moving');
        supplyResult.textContent = `Moving ${supplyFacts.worn[dragging.from]?.name || 'it'} · release over the pockets to take it off.`;
      }
    });
    button.addEventListener('pointerup', event => {
      const request = dragging;
      if (!request) return;
      cancel();
      if (!request.moved) return;
      const under = document.elementFromPoint(event.clientX, event.clientY);
      const pocket = under?.closest('[data-pocket]');
      const inGrid = !!under && grid.contains(under), onWorn = !!under && wornColumn.contains(under);
      if (request.kind === 'worn') {
        if (inGrid) claim('takeOff', request.from, undefined, request.revision);
        show(chosen);
      } else if (pocket && inGrid && Number(pocket.dataset.pocket) !== request.from) {
        claim('move', request.from, Number(pocket.dataset.pocket), request.revision);
        show({ kind: 'pocket', index: Number(pocket.dataset.pocket) });
      } else if (onWorn) {
        // Dropped on a slot, it is worn there (C# refuses a slot that does not take it); on the column, where it fits.
        const slot = under.closest('[data-slot]');
        if (items[request.from]?.wearable) claim('wear', request.from, slot ? Number(slot.dataset.slot) : undefined, request.revision);
        show({ kind: 'pocket', index: request.from });
      } else if (!inGrid && !host.querySelector('.case-collection').contains(under)) {
        // Released outside the case: the stack is left on the floor.
        claim('drop', request.from, undefined, request.revision);
        show({ kind: 'pocket', index: request.from });
      } else show(chosen);
    });
    button.addEventListener('pointercancel', () => { cancel(); show(chosen); });
  };
  const worn = mountWornSlots(document, {
    choose: index => { cancel(); show({ kind: 'worn', index }); },
    drag: (button, from) => draggable(button, 'worn', from)
  });
  wornColumn.append(worn.element);

  // One action button: a worn slot's item is taken off; a pocket's is worn if wearable, otherwise used.
  use.addEventListener('click', () => {
    if (!intents) return;
    if (chosen.kind === 'worn') { if (supplyFacts.worn[chosen.index]?.id) claim('takeOff', chosen.index); return; }
    const item = items[chosen.index];
    if (!item?.id) return;
    if (item.wearable) { if (!item.wearReason) claim('wear', chosen.index); }
    else if (!item.useReason) claim('use', chosen.index);
  });
  // Dropping leaves the whole stack on the floor as a bag; C# refuses expedition finds and a full floor.
  drop.addEventListener('click', () => {
    if (!intents || chosen.kind !== 'pocket' || !items[chosen.index]?.id || moving) return;
    claim('drop', chosen.index);
  });
  move.addEventListener('click', () => {
    if (moving) { cancel(); show(chosen); return; }
    if (!intents || chosen.kind !== 'pocket' || !items[chosen.index]?.id) return;
    moving = { from: chosen.index, revision: supplyFacts.revision };
    grid.classList.add('moving');
    move.textContent = 'Cancel move';
    supplyResult.textContent = 'Choose a destination pocket. Matching stacks merge; different supplies swap.';
    grid.children[chosen.index].focus();
  });
  const choosePocket = index => {
    if (moving) {
      const request = moving;
      cancel();
      if (request.from !== index) claim('move', request.from, index, request.revision);
    }
    show({ kind: 'pocket', index });
  };

  const show = next => {
    chosen = next;
    for (const button of grid.children) button.setAttribute('aria-pressed', String(next.kind === 'pocket' && Number(button.dataset.pocket) === next.index));
    worn.draw(supplyFacts.worn || [], next.kind === 'worn' ? next.index : -1);
    const emblem = host.querySelector('.empty-emblem');
    const title = host.querySelector('[data-detail-title]');
    const body = host.querySelector('[data-detail-body]');
    const result = moving ? 'Choose a destination pocket. Matching stacks merge; different supplies swap.' : supplyFacts.message || '';
    if (next.kind === 'worn') {
      const slot = supplyFacts.worn?.[next.index];
      host.querySelector('[data-detail-number]').textContent = slot?.slot || '';
      emblem.replaceChildren(slot?.id ? itemIcon(document, slot.id, slot.mark) : '—');
      title.textContent = slot?.id ? slot.name : 'Nothing worn';
      body.textContent = slot?.id ? slot.description : 'Wear something from a pocket, or drag it here.';
      use.disabled = !intents || !slot?.id;
      use.textContent = slot?.id ? `Take off ${slot.name}` : 'Take off';
      move.hidden = drop.hidden = true;
      supplyResult.textContent = result;
      return;
    }
    const item = items[next.index];
    move.hidden = drop.hidden = false;
    host.querySelector('[data-detail-number]').textContent = `Pocket ${String(next.index + 1).padStart(2, '0')}`;
    use.disabled = !intents || !item?.id || !!(item.wearable ? item.wearReason : item.useReason);
    use.textContent = item?.id ? `${item.wearable ? 'Wear' : 'Use'} ${item.name}` : 'Use supply';
    move.disabled = !intents || !item?.id;
    drop.disabled = !intents || !item?.id || !!moving;
    drop.textContent = item?.id ? `Drop ${item.name}` : 'Drop';
    move.textContent = moving ? 'Cancel move' : 'Move stack';
    supplyResult.textContent = result;
    emblem.replaceChildren(item?.id ? itemIcon(document, item.id, item.mark) : '—');
    title.textContent = item?.name || 'Empty pocket';
    body.textContent = item?.id
      ? `${item.description}${item.stackLimit > 1 ? `\n\n${item.count} / ${item.stackLimit} in this stack.` : ''}${!item.wearable && item.useReason ? ` ${item.useReason}` : ''}`
      : 'Nothing in this pocket.';
  };
  const showTab = tab => {
    if (active !== tab) cancel();
    active = tab;
    host.dataset.collection = tab;
    for (const button of tabs) {
      button.setAttribute('aria-selected', String(button.dataset.tab === tab));
      button.tabIndex = button.dataset.tab === tab ? 0 : -1;
    }
    host.querySelector('[role="tabpanel"]').setAttribute('aria-labelledby', `${tab}-tab`);
    for (const part of host.querySelectorAll('.case-worn, .case-contents, .item-detail')) part.hidden = tab !== 'case';
    pacts.element.hidden = tab !== 'spirits';
    if (tab === 'case') show(chosen);
    else pacts.draw(spiritFacts);
  };
  for (const button of tabs) {
    button.addEventListener('click', () => showTab(button.dataset.tab));
    button.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      const at = tabs.indexOf(button);
      const next = event.key === 'Home' ? tabs[0] : event.key === 'End' ? tabs[tabs.length - 1]
        : tabs[(at + (event.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length];
      showTab(next.dataset.tab);
      next.focus();
    });
  }
  return {
    draw(facts) {
      supplyFacts = facts.supplies;
      spiritFacts = facts.spirit;
      items = supplyFacts.pockets;
      host.querySelector('[data-count="case"]').textContent = supplyFacts.occupied + supplyFacts.worn.filter(slot => slot.id).length;
      host.querySelector('[data-count="spirits"]').textContent = pacts.count(spiritFacts);
      host.querySelector('[data-load]').textContent = [`${supplyFacts.occupied} / ${supplyFacts.capacity} pockets`, ...supplyFacts.load].join(' · ');
      if (pockets !== supplyFacts.capacity) {
        pockets = supplyFacts.capacity;
        grid.replaceChildren(...Array.from({ length: pockets }, (_, index) => {
          const button = document.createElement('button');
          button.type = 'button';
          button.className = 'pocket';
          button.dataset.pocket = index;
          button.setAttribute('aria-label', `Pocket ${index + 1}, empty`);
          button.innerHTML = `<span class="pocket-number">${String(index + 1).padStart(2, '0')}</span><span aria-hidden="true">·</span>`;
          button.addEventListener('click', () => choosePocket(index));
          draggable(button, 'pocket', () => (items[index]?.id ? index : null));
          return button;
        }));
        chosen = { kind: 'pocket', index: 0 };
      }
      quick.replaceChildren(...quickPocketViews(document, supplyFacts));
      host.querySelector('[data-quick-note]').textContent = supplyFacts.quickNote;
      for (const button of grid.children) {
        const item = items[Number(button.dataset.pocket)];
        button.classList.toggle('occupied', !!item?.id);
        button.setAttribute('aria-label', `Pocket ${Number(button.dataset.pocket) + 1}, ${item?.id ? `${item.name}, ${item.count}` : 'empty'}`);
        button.querySelector('[aria-hidden]').replaceChildren(...(item?.id ? [itemIcon(document, item.id, item.mark), `×${item.count}`] : ['·']));
      }
      if (chosen.kind === 'worn' && chosen.index >= supplyFacts.worn.length) chosen = { kind: 'pocket', index: 0 };
      showTab(active);
    },
    close() { cancel(); },
    focus() { tabs.find(button => button.dataset.tab === active).focus(); }
  };
}

/** These mirror the first case pockets; they do not own another inventory. */
export function quickPocketViews(document, supplies) {
  return Array.from({ length: supplies.quickPockets }, (_, index) => {
    const item = supplies.pockets[index];
    const pocket = document.createElement('span');
    pocket.className = 'quick-pocket';
    const label = supplies.quickKeys[index];
    pocket.setAttribute('aria-label', `Key ${label}, ${item?.id ? `${item.name}, ${item.count}` : 'empty'}`);
    const key = document.createElement('small');
    key.textContent = label;
    const contents = document.createElement('span');
    contents.replaceChildren(...(item?.id ? [itemIcon(document, item.id, item.mark), `×${item.count}`] : ['—']));
    pocket.append(key, contents);
    return pocket;
  });
}
