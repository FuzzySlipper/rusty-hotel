/** Small helpers every foreground screen module shares. Navigation itself stays in main.js. */
export function screenElement(document, markup) {
  const holder = document.createElement('div');
  holder.innerHTML = markup.trim();
  return holder.firstElementChild;
}

export function focusFirstButton(element) {
  element.querySelector('button')?.focus();
}
