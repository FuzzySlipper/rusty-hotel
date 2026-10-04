import test from 'node:test';
import assert from 'node:assert/strict';
import { createPauseFlow } from '../../src/ui/pause.js';

function fixture() {
  let state = 'running', listener, finish;
  let requests = 0, unsubscribed = false;
  const port = {
    state: () => state,
    subscribe(fn) { listener = fn; return () => { unsubscribed = true; listener = undefined; }; },
    pause() { requests++; return new Promise(resolve => { finish = resolve; }); },
    resume() { requests++; return new Promise(resolve => { finish = resolve; }); }
  };
  return { port, get requests() { return requests; }, get unsubscribed() { return unsubscribed; },
    publish(next) { state = next; listener?.(next); },
    answer(result) { finish(result); } };
}

test('opening does not claim pause before Engine acceptance; duplicate transition is not sent', async () => {
  const host = fixture(), changes = [];
  const flow = createPauseFlow(host.port, value => changes.push(value));
  const request = flow.pause();
  assert.equal(flow.snapshot().state, 'running');
  assert.equal(flow.snapshot().pending, 'paused');
  assert.equal(await flow.resume(), false);
  assert.equal(host.requests, 1);
  host.publish('paused'); host.answer({ accepted: true, state: 'paused' });
  assert.equal(await request, true);
  assert.deepEqual(changes.at(-1), { state: 'paused', pending: null, error: '' });
  flow.dispose();
});

test('rejected or superseded resume cannot authorize returning to gameplay', async () => {
  const host = fixture(); host.publish('paused');
  const flow = createPauseFlow(host.port, () => {});
  const rejected = flow.resume();
  host.answer({ accepted: false, state: 'paused', diagnostic: 'Runtime replaced' });
  assert.equal(await rejected, false);
  assert.equal(flow.snapshot().error, 'Runtime replaced');
  const superseded = flow.resume();
  host.publish('running'); host.publish('paused');
  host.answer({ accepted: true, state: 'running' });
  assert.equal(await superseded, false);
  assert.equal(flow.snapshot().state, 'paused');
  flow.dispose();
});

test('disposing while resume is pending suppresses late view updates and focus permission', async () => {
  const host = fixture(); host.publish('paused');
  let changes = 0;
  const flow = createPauseFlow(host.port, () => changes++);
  const request = flow.resume();
  flow.dispose();
  const before = changes;
  host.publish('running'); host.answer({ accepted: true, state: 'running' });
  assert.equal(await request, false);
  assert.equal(changes, before);
  assert.equal(host.unsubscribed, true);
});

test('host rejection is visible and leaves the actual state unchanged', async () => {
  const host = fixture();
  host.port.pause = async () => { throw new Error('Host disposed'); };
  const flow = createPauseFlow(host.port, () => {});
  assert.equal(await flow.pause(), false);
  assert.equal(flow.snapshot().state, 'running');
  assert.match(flow.snapshot().error, /Host disposed/);
  assert.equal(flow.snapshot().pending, null);
  flow.dispose();
});
