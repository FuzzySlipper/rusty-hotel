/**
 * A developer workbench panel, generic over the product (contract `tunables.v1`): it asks the product, through the
 * Engine live-debug client, to describe its controls, then draws choosers, a key timeline with playback, and groups of
 * sliders, toggles and choices, sending each change back as the console command the description names. Nothing here knows what the values
 * mean; the product's owners apply them. Closed, it holds no client and sends nothing.
 */
export function createWorkbench(host, loadClient = () => import('@rusty-engine/live-debug')) {
  const document = host.ownerDocument;
  let session = null;

  const fill = (template, values) => template.replace(/\{(\w+)\}/g, (_, name) => String(values[name] ?? ''));

  function close() {
    const previous = session;
    session = null;
    if (!previous) return;
    if (previous.view?.commands?.close) void previous.send(previous.view.commands.close).catch(() => {});
    previous.element.remove();
  }

  async function open() {
    close();
    const element = document.createElement('aside');
    element.className = 'workbench';
    element.setAttribute('aria-label', 'Developer workbench');
    element.textContent = 'Opening workbench…';
    host.append(element);
    const current = { element, view: null, client: null, send: null };
    session = current;
    try {
      const { createLiveDebugHttpTransport } = await loadClient();
      if (session !== current) return;
      current.client = createLiveDebugHttpTransport();
      current.send = async command => {
        const result = await current.client.execute(command);
        if (!result.succeeded) throw new Error(result.message);
        return result.message;
      };
      const described = JSON.parse(await current.send('hotel.dev.workbench.describe'));
      if (session !== current) return;
      render(current, described);
    } catch (error) {
      if (session !== current) return;
      element.textContent = `Workbench unavailable: ${error instanceof Error ? error.message : error}`;
      element.setAttribute('role', 'alert');
    }
  }

  function render(current, view) {
    current.view = view;
    const { element } = current;
    // Drawing again keeps where the panel was scrolled and which groups were folded.
    const scrolled = element.scrollTop;
    current.folded ??= new Set();
    element.textContent = '';
    element.removeAttribute('role');
    const status = document.createElement('p');
    status.className = 'workbench-status';
    status.setAttribute('role', 'status');
    const report = (message, failed = false) => { status.textContent = message; status.dataset.failed = failed ? 'true' : 'false'; };
    const run = async (command, then) => {
      try {
        const message = await current.send(command);
        if (session === current) then?.(message);
      } catch (error) {
        if (session === current) report(error instanceof Error ? error.message : String(error), true);
      }
    };
    const redescribe = message => render(current, JSON.parse(message));
    const describeAgain = () => void run(view.commands.describe, redescribe);

    const header = document.createElement('header');
    header.innerHTML = `<h2></h2><button type="button" data-workbench-close>Close</button>`;
    header.querySelector('h2').textContent = view.title;
    element.append(header);

    // Choosers: changing one selects with every chooser's value and redraws from the reply.
    const fields = document.createElement('div');
    fields.className = 'workbench-fields';
    for (const field of view.fields) {
      const label = document.createElement('label');
      label.textContent = field.label;
      const select = document.createElement('select');
      select.dataset.field = field.id;
      for (const option of field.options) select.append(new Option(option, option, false, option === field.value));
      select.addEventListener('change', () => {
        const values = Object.fromEntries([...fields.querySelectorAll('select')].map(s => [s.dataset.field, s.value]));
        void run(fill(view.commands.select, values), redescribe);
      });
      label.append(select);
      fields.append(label);
    }
    element.append(fields);

    // Timeline: keys to hold exactly, a scrubber that shows any moment, and playback at a speed.
    const timeline = document.createElement('section');
    timeline.className = 'workbench-timeline';
    const keys = document.createElement('div');
    keys.className = 'workbench-keys';
    const key = (index, text) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = text;
      button.setAttribute('aria-pressed', String(view.key === index));
      button.addEventListener('click', () => void run(fill(view.commands.key, { key: index }),
        () => void run(view.commands.describe, redescribe)));
      keys.append(button);
    };
    key(-1, 'rest');
    for (const k of view.keys) key(k.index, `${k.label} · ${k.time.toFixed(2)} s`);
    const scrub = document.createElement('input');
    scrub.type = 'range';
    scrub.min = '0';
    scrub.max = String(view.duration);
    scrub.step = '0.01';
    scrub.value = '0';
    scrub.setAttribute('aria-label', 'Moment of the action (seconds)');
    const moment = document.createElement('output');
    moment.textContent = '0.00 s';
    scrub.addEventListener('input', throttle(() => {
      moment.textContent = `${Number(scrub.value).toFixed(2)} s`;
      return run(fill(view.commands.at, { seconds: scrub.value }));
    }));
    const play = document.createElement('div');
    play.className = 'workbench-play';
    for (const speed of [0.25, 0.5, 1]) {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = `▶ ${speed}×`;
      button.addEventListener('click', () => void run(fill(view.commands.play, { speed }), message => report(message)));
      play.append(button);
    }
    timeline.append(keys, scrub, moment, play);
    element.append(timeline);

    // Groups of controls; each change is sent as the set command and shows the value the product kept.
    for (const group of view.groups) {
      const details = document.createElement('details');
      details.open = !current.folded.has(group.id);
      details.addEventListener('toggle', () => { if (details.open) current.folded.delete(group.id); else current.folded.add(group.id); });
      details.className = 'workbench-group';
      const summary = document.createElement('summary');
      summary.textContent = group.title;
      details.append(summary);
      for (const control of group.controls) details.append(controlRow(control, view, run, describeAgain));
      element.append(details);
    }

    // What has changed, as JSON to paste into each content file.
    const changes = document.createElement('section');
    changes.className = 'workbench-changes';
    const show = document.createElement('button');
    show.type = 'button';
    show.textContent = 'Changed values';
    const list = document.createElement('div');
    show.addEventListener('click', () => void run(view.commands.changes, message => {
      list.textContent = '';
      const files = JSON.parse(message).files;
      if (Object.keys(files).length === 0) list.textContent = 'Nothing has changed since the content loaded.';
      for (const [file, lines] of Object.entries(files)) {
        const block = document.createElement('div');
        const title = document.createElement('h3');
        title.textContent = `content/${file}`;
        const pre = document.createElement('pre');
        pre.textContent = lines.join('\n');
        const copy = document.createElement('button');
        copy.type = 'button';
        copy.textContent = 'Copy';
        copy.addEventListener('click', () => navigator.clipboard?.writeText(pre.textContent).then(
          () => report(`Copied the ${file} changes.`), () => report('The clipboard refused; select the text instead.', true)));
        block.append(title, copy, pre);
        list.append(block);
      }
    }));
    changes.append(show, list);
    element.append(changes, status);
    // The product's notice (keys out of time order, say) stays up until it clears.
    if (view.notice) report(view.notice, true);
    element.scrollTop = scrolled;
  }

  function controlRow(control, view, run, describeAgain) {
    const row = document.createElement('label');
    row.className = 'workbench-control';
    const name = document.createElement('span');
    name.textContent = control.label;
    row.append(name);
    // A control that reshapes the description (a key's moment) is described again once the product has kept it.
    const after = control.reshapes ? describeAgain : undefined;
    if (control.kind === 'choice') {
      const select = document.createElement('select');
      control.options.forEach((option, index) => select.append(new Option(option, String(index), false, index === control.value)));
      select.addEventListener('change', () => void run(fill(view.commands.set, { id: control.id, value: select.value }), after));
      row.append(select);
      return row;
    }
    if (control.kind === 'toggle') {
      const box = document.createElement('input');
      box.type = 'checkbox';
      box.checked = control.value >= 0.5;
      box.addEventListener('change', () => void run(fill(view.commands.set, { id: control.id, value: box.checked ? 1 : 0 })));
      row.append(box);
      return row;
    }
    const slider = document.createElement('input');
    slider.type = 'range';
    const number = document.createElement('input');
    number.type = 'number';
    for (const input of [slider, number]) {
      input.min = String(control.min);
      input.max = String(control.max);
      input.step = String(control.step);
      input.value = String(control.value);
    }
    const send = value => run(fill(view.commands.set, { id: control.id, value }), kept => {
      slider.value = kept;
      number.value = kept;
    });
    // Sliders that reshape the description are described again when the drag ends, not on every step.
    if (after) slider.addEventListener('change', after);
    slider.addEventListener('input', throttle(() => { number.value = slider.value; return send(slider.value); }));
    number.addEventListener('change', () => { slider.value = number.value; void send(number.value).then(after); });
    row.append(slider, number);
    return row;
  }

  return {
    get isOpen() { return session !== null; },
    open,
    close,
    dispose: close
  };
}

// Sends at most one request at a time per control, then the latest value once it returns.
function throttle(sendLatest) {
  let busy = false;
  let again = false;
  return async function pump() {
    if (busy) { again = true; return; }
    busy = true;
    try { await sendLatest(); } finally {
      busy = false;
      if (again) { again = false; void pump(); }
    }
  };
}
