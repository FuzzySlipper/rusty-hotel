import test from 'node:test';
import assert from 'node:assert/strict';
import { createDeveloperConsole, developerAvailable } from '../../src/ui/developer.js';

function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
}

// Just the DOM ownership needed by the mount adapter, not a substitute browser test.
function host() {
  const children = [];
  return {
    children,
    ownerDocument: { createElement: () => ({
      textContent: '',
      remove() { const index = children.indexOf(this); if (index >= 0) children.splice(index, 1); },
      querySelector: () => null,
      setAttribute() {}
    }) },
    append: child => children.push(child)
  };
}

test('closed console does not load Engine or create a mount; closing during import stays closed', async () => {
  const root = host();
  const loading = deferred();
  let imports = 0, mounts = 0;
  const console = createDeveloperConsole(root, () => { imports++; return loading.promise; });
  assert.equal(imports, 0);
  assert.equal(root.children.length, 0);
  const opening = console.open();
  console.close();
  loading.resolve({ mountLiveDebugPanel() { mounts++; } });
  await opening;
  assert.equal(imports, 1);
  assert.equal(mounts, 0);
  assert.equal(root.children.length, 0);
});

test('late Engine mount is disposed without replacing a newer console', async () => {
  const root = host();
  const started = deferred(), mounting = deferred();
  let mounts = 0, oldDisposals = 0, currentDisposals = 0;
  const console = createDeveloperConsole(root, async () => ({
    mountLiveDebugPanel(element, options) {
      assert.deepEqual(options, { enabled: true, presentation: 'dock' });
      if (++mounts === 1) { started.resolve(); return mounting.promise; }
      element.textContent = 'new panel';
      return Promise.resolve({ dispose() { currentDisposals++; } });
    }
  }));
  const first = console.open();
  await started.promise;
  console.close();
  await console.open();
  mounting.resolve({ dispose() { oldDisposals++; } });
  await first;
  assert.equal(oldDisposals, 1);
  assert.equal(currentDisposals, 0);
  assert.equal(root.children.length, 1);
  assert.equal(root.children[0].textContent, 'new panel');
  console.dispose();
  console.dispose();
  assert.equal(currentDisposals, 1);
  assert.equal(root.children.length, 0);
});

test('failed import reports an honest error and can be closed', async () => {
  const root = host();
  const console = createDeveloperConsole(root, async () => { throw new Error('offline'); });
  await console.open();
  assert.match(root.children[0].textContent, /unavailable: offline/);
  console.close();
  assert.equal(root.children.length, 0);
});

test('the console is available only when the Engine debug catalog says so', async () => {
  const panel = catalog => async () => ({ createLiveDebugHttpTransport: () => ({ catalog }) });
  assert.equal(await developerAvailable(panel(async () => ({ available: true, commands: [] }))), true);
  assert.equal(await developerAvailable(panel(async () => ({ available: false, commands: [] }))), false);
  assert.equal(await developerAvailable(panel(async () => { throw new Error('offline'); })), false);
  assert.equal(await developerAvailable(async () => { throw new Error('no panel'); }), false);
});
