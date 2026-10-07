/**
 * An item's icon: its line drawing (icons/<item id>.svg, one for every item) as a mask, so it takes the colour of the
 * text around it. Decorative: the name and count beside it are what assistive technology reads.
 */
export function itemIcon(document, id) {
  const icon = document.createElement('span');
  icon.className = 'item-icon';
  icon.setAttribute('aria-hidden', 'true');
  icon.style.setProperty('--icon', `url("${new URL(`./icons/${id}.svg`, import.meta.url)}")`);
  return icon;
}
