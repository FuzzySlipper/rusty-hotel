import { mountWornView } from './worn-view.js';

/** Projection-owned pockets and selection; C# owns inventory, equipment and resource rules. */
export function mountFieldCase(host, intents) {
  const document = host.ownerDocument;
  host.innerHTML = `
    <div class="case-tabs" role="tablist" aria-label="Field case collection">
      <button type="button" id="supplies-tab" role="tab" aria-controls="case-collection" aria-selected="true" data-tab="supplies">Supplies <span data-count="supplies">0</span></button>
      <button type="button" id="worn-tab" role="tab" aria-controls="case-collection" aria-selected="false" tabindex="-1" data-tab="worn">Worn <span data-count="worn">0</span></button>
      <button type="button" id="spirits-tab" role="tab" aria-controls="case-collection" aria-selected="false" tabindex="-1" data-tab="spirits">Spirits <span data-count="spirits">0</span></button>
    </div>
    <div id="case-collection" class="case-collection" role="tabpanel" aria-labelledby="supplies-tab">
      <div class="case-contents">
        <div class="collection-heading"><h3 data-collection-heading>Carried supplies</h3><span class="eyebrow" data-load></span></div>
        <div class="pocket-grid" role="group" aria-label="Supply pockets"></div>
        <div class="spirit-empty" hidden><span class="pact-mark" aria-hidden="true">◇</span><h3>No pact made</h3><p>No spirits accompany you.</p></div>
        <button type="button" class="spirit-card" data-spirit-card hidden aria-pressed="true"><span class="moth-emblem" aria-hidden="true">⋈</span><span data-spirit-name></span><small data-spirit-equipped></small></button>
        <section class="case-quick"><h3>Quick access</h3><div class="quick-pockets" aria-label="Quick access pockets"></div><p class="muted" data-quick-note></p></section>
      </div>
      <aside class="item-detail" aria-live="polite"><span class="eyebrow" data-detail-number>Pocket 01</span>
        <div class="empty-emblem" aria-hidden="true">—</div><h3 data-detail-title>Empty pocket</h3><p data-detail-body>No supplies carried.</p><div class="supply-actions"><button type="button" data-use-supply>Use supply</button><button type="button" data-move-supply>Move stack</button></div><p class="supply-result" data-supply-result role="status"></p><button type="button" data-equip-spirit hidden>Equip spirit</button><p class="spirit-result" data-spirit-result role="status" hidden></p>
      </aside>
    </div>`;
  const tabs = [...host.querySelectorAll('[data-tab]')];
  const worn = mountWornView(document, (action, from) => claim(action, from));
  host.querySelector('.case-contents').append(worn.element);
  const grid = host.querySelector('.pocket-grid');
  const quick = host.querySelector('.quick-pockets');
  let active = 'supplies';
  let pockets = 0;
  let selected = 0;
  let items = [];
  let supplyFacts = {};
  let spiritFacts = {};
  let moving = null;
  let dragging = null;
  const use = host.querySelector('[data-use-supply]');
  const move = host.querySelector('[data-move-supply]');
  const supplyResult = host.querySelector('[data-supply-result]');
  const claim = (action, from, to, revision = supplyFacts.revision) => {
    try { intents.claim('hotel.supplies', { kind: 'product-payload', contract: 'hotel.supplies.v1',
      data: { action, from, ...(to === undefined ? {} : { to }), revision } }); }
    catch { supplyResult.textContent = 'The choice could not be sent. Close and reopen the field case.'; }
  };
  const cancel = () => { moving = dragging = null; grid.classList.remove('moving'); };
  // One action button: a worn item is worn, anything else is used, each under its own C# eligibility.
  use.addEventListener('click', () => {
    const item = items[selected];
    if (!intents || !item?.id) return;
    if (item.wearable) { if (!item.wearReason) claim('wear', selected); }
    else if (!item.useReason) claim('use', selected);
  });
  move.addEventListener('click', () => {
    if (moving) { cancel(); selectPocket(selected); return; }
    if (!intents || !items[selected]?.id) return;
    moving = { from: selected, revision: supplyFacts.revision };
    grid.classList.add('moving');
    move.textContent = 'Cancel move';
    supplyResult.textContent = 'Choose a destination pocket. Matching stacks merge; different supplies swap.';
    grid.children[selected].focus();
  });
  const choose = index => {
    if (moving) {
      const request = moving;
      cancel();
      if (request.from !== index) claim('move', request.from, index, request.revision);
    }
    selectPocket(index);
  };
  const equip = host.querySelector('[data-equip-spirit]');
  const result = host.querySelector('[data-spirit-result]');
  equip.addEventListener('click', () => {
    if (!intents || !spiritFacts.acquired || spiritFacts.equipReason) return;
    try {
      intents.claim('hotel.spirit.equip', { kind: 'product-payload', contract: 'hotel.spirit.equip.v1',
        data: { equipped: !spiritFacts.equipped, revision: spiritFacts.revision } });
    } catch { result.textContent = 'The choice could not be sent. Close and reopen the field case.'; }
  });
  const selectPocket = index => {
    selected = index;
    for (const button of grid.children) button.setAttribute('aria-pressed', String(Number(button.dataset.pocket) === index));
    host.querySelector('[data-detail-number]').textContent = `Pocket ${String(index + 1).padStart(2, '0')}`;
    const item = items[index];
    use.disabled = !intents || !item?.id || !!(item.wearable ? item.wearReason : item.useReason);
    use.textContent = item?.id ? `${item.wearable ? 'Wear' : 'Use'} ${item.name}` : 'Use supply';
    move.disabled = !intents || !item?.id;
    move.textContent = moving ? 'Cancel move' : 'Move stack';
    supplyResult.textContent = moving ? 'Choose a destination pocket. Matching stacks merge; different supplies swap.' : supplyFacts.message || '';
    host.querySelector('.empty-emblem').textContent = item?.mark || '—';
    host.querySelector('[data-detail-title]').textContent = item?.name || 'Empty pocket';
    host.querySelector('[data-detail-body]').textContent = item?.id
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
    grid.hidden = tab !== 'supplies';
    worn.element.hidden = tab !== 'worn';
    host.querySelector('.item-detail').hidden = tab === 'worn';
    host.querySelector('.supply-actions').hidden = tab !== 'supplies';
    supplyResult.hidden = tab !== 'supplies';
    host.querySelector('.case-quick').hidden = tab !== 'supplies';
    host.querySelector('.spirit-empty').hidden = tab !== 'spirits' || !!spiritFacts.acquired;
    host.querySelector('[data-spirit-card]').hidden = tab !== 'spirits' || !spiritFacts.acquired;
    equip.hidden = tab !== 'spirits' || !spiritFacts.acquired;
    result.hidden = tab !== 'spirits';
    host.querySelector('[data-collection-heading]').textContent = { supplies: 'Carried supplies', worn: 'Worn', spirits: 'Companions' }[tab];
    host.querySelector('[data-load]').hidden = tab === 'spirits';
    host.querySelector('[data-detail-title]').textContent = tab === 'supplies' ? 'Empty pocket' : 'No spirit selected';
    host.querySelector('[data-detail-body]').textContent = tab === 'supplies' ? 'No supplies carried.' : 'Your field case holds no pacts.';
    if (tab === 'supplies') selectPocket(selected);
    else if (tab === 'worn') worn.draw(supplyFacts);
    else {
      host.querySelector('[data-detail-number]').textContent = 'Pacts';
      host.querySelector('.empty-emblem').textContent = spiritFacts.acquired ? '⋈' : '◇';
      if (spiritFacts.acquired) {
        host.querySelector('[data-detail-title]').textContent = spiritFacts.name;
        host.querySelector('[data-detail-body]').textContent = spiritFacts.description;
      }
      host.querySelector('[data-spirit-name]').textContent = spiritFacts.name || '';
      host.querySelector('[data-spirit-equipped]').textContent = spiritFacts.equipped ? 'Equipped' : 'Pact made';
      equip.textContent = spiritFacts.equipped ? `Let ${spiritFacts.name} rest` : `Equip ${spiritFacts.name}`;
      equip.disabled = !intents || !!spiritFacts.equipReason;
      result.textContent = spiritFacts.equipReason || spiritFacts.message || '';

    }
  };
  host.querySelector('[data-spirit-card]').addEventListener('click', () => showTab('spirits'));
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
      host.querySelector('[data-count="supplies"]').textContent = supplyFacts.occupied;
      host.querySelector('[data-count="spirits"]').textContent = spiritFacts.acquired ? 1 : 0;
      host.querySelector('[data-count="worn"]').textContent = supplyFacts.worn.filter(slot => slot.id).length;
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
          button.addEventListener('click', () => choose(index));
          button.addEventListener('pointerdown', event => {
            if (event.button !== 0 || !intents || !items[index]?.id || moving) return;
            dragging = { from: index, revision: supplyFacts.revision, x: event.clientX, y: event.clientY, moved: false };
            button.setPointerCapture(event.pointerId);
          });
          button.addEventListener('pointermove', event => {
            if (!dragging) return;
            if (Math.hypot(event.clientX - dragging.x, event.clientY - dragging.y) >= 4) {
              dragging.moved = true;
              grid.classList.add('moving');
              supplyResult.textContent = `Moving ${items[dragging.from]?.name || 'stack'} · release over another pocket.`;
            }
          });
          button.addEventListener('pointerup', event => {
            const request = dragging;
            if (!request) return;
            cancel();
            if (!request.moved) return;
            const target = document.elementFromPoint(event.clientX, event.clientY)?.closest('[data-pocket]');
            if (target && grid.contains(target) && Number(target.dataset.pocket) !== request.from) {
              claim('move', request.from, Number(target.dataset.pocket), request.revision);
              selectPocket(Number(target.dataset.pocket));
            } else selectPocket(selected);
          });
          button.addEventListener('pointercancel', () => { cancel(); selectPocket(selected); });
          return button;
        }));
        selected = 0;
      }
      quick.replaceChildren(...quickPocketViews(document, supplyFacts));
      host.querySelector('[data-quick-note]').textContent = supplyFacts.quickNote;
      for (const button of grid.children) {
        const item = items[Number(button.dataset.pocket)];
        button.classList.toggle('occupied', !!item?.id);
        button.setAttribute('aria-label', `Pocket ${Number(button.dataset.pocket) + 1}, ${item?.id ? `${item.name}, ${item.count}` : 'empty'}`);
        button.querySelector('[aria-hidden]').textContent = item?.id ? `${item.mark} ×${item.count}` : '·';
      }
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
    contents.textContent = item?.id ? `${item.mark} ×${item.count}` : '—';
    pocket.append(key, contents);
    return pocket;
  });
}
