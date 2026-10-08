/**
 * An item's icon: its line drawing (icons/<item id>.svg) as a mask, so it takes the colour of the text around it. Until
 * the drawing has loaded, or if it cannot, the item's authored mark stands in its place. Decorative: the name and count
 * beside it are what assistive technology reads.
 */
const loaded = new Map();

export function itemIcon(document, id, mark) {
  const icon = document.createElement('span');
  icon.className = 'item-icon';
  icon.setAttribute('aria-hidden', 'true');
  const url = new URL(`./icons/${id}.svg`, import.meta.url).href;
  const draw = () => { icon.textContent = ''; icon.style.setProperty('--icon', `url("${url}")`); icon.classList.add('drawn'); };
  if (loaded.get(url) === true) { draw(); return icon; }
  icon.textContent = mark || '·';
  if (loaded.get(url) === false) return icon;
  const probe = new Image();
  probe.onload = () => { loaded.set(url, true); draw(); };
  probe.onerror = () => loaded.set(url, false);
  probe.src = url;
  return icon;
}
