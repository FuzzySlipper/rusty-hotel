import { quickPocketViews } from './field-case.js';

/** The exploration HUD, inside the regions docs/ui.md budgets. It presents C# facts only. */
export function mountHud(document) {
  const element = document.createElement('div');
  element.className = 'exploration';
  element.innerHTML = `
    <header class="hotel-title"><span class="eyebrow">Hotel</span><h1>Endless</h1><span class="wing" data-location>Arriving…</span></header>
    <nav class="hud-nav" aria-label="Hotel navigation"><button type="button" data-open="case">Field case <kbd>I</kbd></button><button type="button" data-open="menu">Menu <kbd>Esc</kbd></button></nav>
    <div class="hurt-screen" hidden></div>
    <span class="reticle" aria-hidden="true"></span>
    <div class="focus-prompt" data-focus-prompt hidden></div>
    <div class="supply-notice" data-supply-notice hidden role="status"></div>
    <div class="travel-hint"><span>WASD · Walk</span><span>Mouse · Look</span><span>Click / Ctrl · Attack</span><span>1 / 2 · Weapon</span><span>R · Reload</span><span>3–5 · Quick supply</span><span>E · Use</span><span>Q · Spirit</span><span>I · Field case</span><span>Esc · Menu</span></div>
    <div class="hud-bottom">
      <div class="condition"><span class="eyebrow">On your own</span><span data-health></span></div>
      <div class="quick-access"><span class="eyebrow">Quick access</span><div class="quick-pockets" data-hud-pockets></div></div>
      <div class="held-status"><div><span class="eyebrow">Reserves</span><span data-resources></span></div><div><span class="eyebrow">Hands</span><span data-weapon>—</span><span class="eyebrow" data-combat-action></span></div><div><span class="eyebrow">Spirit</span><span data-spirit>—</span><span class="spirit-state" data-spirit-state></span></div></div>
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
      find('[data-spirit]').textContent = held.spirit;
      find('[data-spirit-state]').textContent = held.spiritStatus;
      find('[data-health]').textContent = `Health ${condition.health} / ${condition.maximumHealth}`;
      find('[data-resources]').textContent = `Ammo ${held.ammo} / ${held.maximumAmmo} · Summon ${held.summon} / ${held.maximumSummon}`;
      const notice = find('[data-supply-notice]');
      notice.textContent = facts.notice;
      notice.hidden = !facts.notice;
      find('[data-combat-action]').textContent = held.action;
      find('.hurt-screen').hidden = !condition.hurt;
      find('.reticle').classList.toggle('hit', !!held.hit);
      find('[data-hud-pockets]').replaceChildren(...quickPocketViews(document, facts.supplies));
      const prompt = find('[data-focus-prompt]');
      prompt.textContent = facts.focusPrompt || '';
      prompt.hidden = !facts.focusPrompt;
    }
  };
}
