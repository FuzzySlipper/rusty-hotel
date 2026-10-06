import { quickPocketViews } from './field-case.js';

/** The exploration HUD, inside the regions docs/ui.md budgets. It presents C# facts only. */
export function mountHud(document) {
  const element = document.createElement('div');
  element.className = 'exploration';
  element.innerHTML = `
    <header class="hotel-title"><span class="eyebrow">Hotel</span><h1>Endless</h1><span class="wing" data-location>Arriving…</span></header>
    <nav class="hud-nav" aria-label="Hotel navigation"><button type="button" data-open="case">Field case <kbd data-key="fieldCase"></kbd></button><button type="button" data-open="menu">Menu <kbd data-key="menu"></kbd></button></nav>
    <div class="hurt-screen" hidden></div>
    <span class="reticle" aria-hidden="true"></span>
    <div class="focus-prompt" data-focus-prompt hidden></div>
    <div class="supply-notice" data-supply-notice hidden role="status"></div>
    <div class="travel-hint" data-hint hidden></div>
    <div class="hud-bottom">
      <div class="condition"><span class="eyebrow">On your own</span><span data-health></span><div class="effects" data-effects></div></div>
      <div class="quick-access"><span class="eyebrow">Quick access</span><div class="quick-pockets" data-hud-pockets></div></div>
      <div class="held-status"><div><span class="eyebrow">Reserves</span><span data-resources></span></div><div><span class="eyebrow">Hands</span><span data-weapon>—</span><span class="spirit-state" data-off-hand></span><span class="eyebrow" data-combat-action></span></div><div><span class="eyebrow">Spirit</span><span data-spirit>—</span><span class="spirit-state" data-spirit-state></span></div></div>
    </div>`;
  const find = selector => element.querySelector(selector);
  return {
    element,
    // The Field case / Menu entries show only while the pointer is free and no screen is open.
    setNavigationHidden(hidden) { find('.hud-nav').hidden = hidden; },
    draw(facts) {
      const { condition, held } = facts;
      find('[data-location]').textContent = facts.location;
      find('[data-weapon]').textContent = held.weapon;
      find('[data-off-hand]').textContent = held.offHand;
      find('[data-spirit]').textContent = held.spirit;
      find('[data-spirit-state]').textContent = held.spiritStatus;
      find('[data-health]').textContent = `Health ${condition.health} / ${condition.maximumHealth} · Stamina ${condition.stamina} / ${condition.maximumStamina}`;
      find('[data-resources]').textContent = `Ammo ${held.ammo} / ${held.maximumAmmo} · Summon ${held.summon} / ${held.maximumSummon}`;
      const notice = find('[data-supply-notice]');
      notice.textContent = facts.notice;
      notice.hidden = !facts.notice;
      find('[data-combat-action]').textContent = held.action;
      find('.hurt-screen').hidden = !condition.hurt;
      find('[data-effects]').replaceChildren(...condition.effects.map(effect => effectView(document, effect)));
      find('.reticle').classList.toggle('hit', !!held.hit);
      find('[data-hud-pockets]').replaceChildren(...quickPocketViews(document, facts.supplies));
      // The opening reminder only; afterwards controls are hinted where they apply.
      const hint = find('[data-hint]');
      hint.replaceChildren(...facts.controls.hint.map(line => Object.assign(document.createElement('span'), { textContent: line })));
      hint.hidden = facts.controls.hint.length === 0;
      const prompt = find('[data-focus-prompt]');
      prompt.textContent = facts.focusPrompt || '';
      prompt.hidden = !facts.focusPrompt;
    }
  };
}

// One active effect: its mark, name, state (stacks, ward, reveal) and time left, all as the HUD facts word them.
function effectView(document, effect) {
  const chip = document.createElement('span');
  chip.className = 'effect';
  chip.dataset.effect = effect.id;
  chip.textContent = [effect.mark, effect.name, effect.state, '·', effect.time].filter(Boolean).join(' ');
  return chip;
}
