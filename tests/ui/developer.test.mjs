import test from 'node:test';
import assert from 'node:assert/strict';
import { createDeveloperShell, developerAvailable } from '../../src/ui/developer.js';

function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
}

// Just the DOM ownership needed by the shell adapter, not a substitute browser test.
function host() {
  const children = [];
  const observers = [];
  class MutationObserver {
    constructor(callback) { this.callback = callback; }
    observe() { observers.push(this); }
    disconnect() { const index = observers.indexOf(this); if (index >= 0) observers.splice(index, 1); }
  }
  return {
    children,
    mutate: () => { for (const observer of [...observers]) observer.callback([]); },
    ownerDocument: {
      defaultView: { MutationObserver },
      createElement: () => ({ textContent: '', className: '', remove() { const index = children.indexOf(this); if (index >= 0) children.splice(index, 1); }, setAttribute() {} })
    },
    append: child => children.push(child)
  };
}

// A stand-in Engine shell: records what it was given, and opens and closes as the Engine's does.
function engine(sent = []) {
  const shells = [];
  return {
    shells,
    sent,
    load: async () => ({
      createLiveDebugHttpTransport: () => ({ execute: async command => { sent.push(command); return {}; } }),
      mountDeveloperShell(element, options) {
        const shell = { options, isOpen: false, open: async () => { shell.isOpen = true; }, close() { shell.isOpen = false; } };
        shells.push(shell);
        return shell;
      }
    })
  };
}

test('closed shell does not load Engine; closing during import stays closed', async () => {
  const root = host();
  const loading = deferred();
  let imports = 0, mounts = 0, closes = 0;
  const shell = createDeveloperShell(root, { onClose: () => closes++, loadPanel: () => { imports++; return loading.promise; } });
  assert.equal(imports, 0);
  const opening = shell.open();
  shell.close();
  loading.resolve({ createLiveDebugHttpTransport: () => ({}), mountDeveloperShell() { mounts++; } });
  await opening;
  assert.equal(imports, 1);
  assert.equal(mounts, 0);
  assert.equal(shell.isOpen, false);
  assert.equal(closes, 1);
});

test('the shell docks the console, the workbench and both grip views, and shows the Hotel overlay sets', async () => {
  const root = host();
  const stand = engine();
  const viewport = {};
  const shell = createDeveloperShell(root, { viewport, loadPanel: stand.load });
  await shell.open();
  const { options } = stand.shells[0];
  assert.equal(options.side, 'left');
  assert.deepEqual(options.panels.map(panel => panel.kind), ['view', 'view', 'tunables', 'console']);
  assert.deepEqual(options.panels.slice(0, 2).map(panel => [panel.options.viewport, panel.options.subject]),
    [[viewport, 'target main-grip'], [viewport, 'target off-grip']]);
  assert.ok(stand.sent.includes('engine.inspect.overlay show set held.guide'));
  assert.ok(stand.sent.includes('engine.inspect.overlay show set hands.contact'));
});

test("closing by the shell's own Close returns the hands and input to play", async () => {
  const root = host();
  const stand = engine();
  let closes = 0;
  const shell = createDeveloperShell(root, { loadPanel: stand.load, onClose: () => closes++ });
  await shell.open();
  stand.shells[0].close();
  root.mutate();
  assert.equal(shell.isOpen, false);
  assert.equal(closes, 1);
  assert.ok(stand.sent.includes('hotel.dev.motion.off'));
  assert.ok(stand.sent.includes('engine.inspect.overlay hide set held.guide'));
  shell.dispose();
  assert.equal(closes, 1);
});

test('failed import reports an honest error and can be closed', async () => {
  const root = host();
  const shell = createDeveloperShell(root, { loadPanel: async () => { throw new Error('offline'); } });
  await shell.open();
  assert.match(root.children[0].textContent, /unavailable: offline/);
  shell.close();
  assert.equal(root.children.length, 0);
});

test('the shell is available only when the Engine debug catalog says so', async () => {
  const panel = catalog => async () => ({ createLiveDebugHttpTransport: () => ({ catalog }) });
  assert.equal(await developerAvailable(panel(async () => ({ available: true, commands: [] }))), true);
  assert.equal(await developerAvailable(panel(async () => ({ available: false, commands: [] }))), false);
  assert.equal(await developerAvailable(panel(async () => { throw new Error('offline'); })), false);
  assert.equal(await developerAvailable(async () => { throw new Error('no panel'); }), false);
});
