/** The field case's Spirits tab: the pacts made, and the one in the pact slot. C# owns pacts, eligibility and calls. */
export function mountPactView(document, intents) {
  const element = document.createElement('div');
  element.className = 'pact-view';
  element.hidden = true;
  element.innerHTML = `
    <div class="spirit-empty" hidden><span class="pact-mark" aria-hidden="true">◇</span><h3>No pact made</h3><p>No spirits accompany you.</p></div>
    <div class="pact-list" role="group" aria-label="Pacts"></div>
    <div class="pact-detail" aria-live="polite" hidden>
      <span class="eyebrow" data-pact-state></span>
      <h3 data-pact-name></h3>
      <p data-pact-description></p>
      <button type="button" data-equip-pact></button>
      <p class="spirit-result" data-pact-result role="status"></p>
    </div>`;
  const list = element.querySelector('.pact-list');
  const detail = element.querySelector('.pact-detail');
  const equip = element.querySelector('[data-equip-pact]');
  const result = element.querySelector('[data-pact-result]');
  let facts = { pacts: [] };
  let selected = 0;
  equip.addEventListener('click', () => {
    const pact = facts.pacts[selected];
    if (!intents || !pact || facts.equipReason) return;
    try {
      intents.claim('hotel.spirit.equip', { kind: 'product-payload', contract: 'hotel.spirit.equip.v2',
        data: { spirit: facts.equipped === pact.id ? null : pact.id, revision: facts.revision } });
    } catch { result.textContent = 'The choice could not be sent. Close and reopen the field case.'; }
  });
  const select = index => {
    selected = index;
    const pact = facts.pacts[index];
    for (const button of list.children) button.setAttribute('aria-pressed', String(Number(button.dataset.pact) === index));
    detail.hidden = !pact;
    if (!pact) return;
    const equipped = facts.equipped === pact.id;
    element.querySelector('[data-pact-state]').textContent = equipped ? 'In the pact slot' : 'Pact made';
    element.querySelector('[data-pact-name]').textContent = pact.name;
    element.querySelector('[data-pact-description]').textContent = pact.description;
    equip.textContent = equipped ? `Let ${pact.name} rest` : `Equip ${pact.name}`;
    equip.disabled = !intents || !!facts.equipReason;
    result.textContent = facts.equipReason || facts.message || '';
  };
  return {
    element,
    count: spirit => spirit.pacts.length,
    draw(spirit) {
      facts = spirit;
      element.querySelector('.spirit-empty').hidden = facts.pacts.length > 0;
      list.replaceChildren(...facts.pacts.map((pact, index) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'spirit-card';
        button.dataset.pact = index;
        button.innerHTML = '<span class="moth-emblem" aria-hidden="true">⋈</span><span></span><small></small>';
        button.children[1].textContent = pact.name;
        button.children[2].textContent = facts.equipped === pact.id ? 'Equipped' : 'Pact made';
        button.addEventListener('click', () => select(index));
        return button;
      }));
      select(Math.min(selected, Math.max(0, facts.pacts.length - 1)));
    },
    focus() { list.children[selected]?.focus(); }
  };
}
