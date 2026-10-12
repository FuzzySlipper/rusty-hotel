/**
 * Whether the Engine's debug service runs (a host started with --live-debug), asked once through the Engine's own
 * client. Works the same in a streamed page and a window, which has no address to add an opt-in to.
 */
export async function developerAvailable(loadPanel = () => import('@rusty-engine/live-debug')) {
  try {
    const { createLiveDebugHttpTransport } = await loadPanel();
    return (await createLiveDebugHttpTransport().catalog()).available === true;
  } catch {
    return false;
  }
}

/** One opt-in Engine panel. No transport, catalog or diagnostics exists while closed. */
export function createDeveloperConsole(host, loadPanel = () => import('@rusty-engine/live-debug')) {
  let current = null;
  const close = () => {
    const previous = current;
    current = null;
    previous?.mount?.dispose();
    previous?.element.remove();
  };
  return {
    async open() {
      close();
      const element = host.ownerDocument.createElement('div');
      const session = { element, mount: null };
      current = session;
      element.textContent = 'Opening developer console…';
      host.append(element);
      try {
        const { mountLiveDebugPanel } = await loadPanel();
        if (current !== session) return;
        element.textContent = '';
        const mounted = await mountLiveDebugPanel(element, { enabled: true, presentation: 'dock' });
        if (current !== session) { mounted.dispose(); return; }
        session.mount = mounted;
        element.querySelector('input')?.focus();
      } catch (error) {
        if (current !== session) return;
        element.textContent = `Developer console unavailable: ${error instanceof Error ? error.message : 'could not load Engine panel'}`;
        element.setAttribute('role', 'alert');
      }
    },
    close,
    dispose: close
  };
}

/**
 * The first-person workbench: the Engine tunables panel over Hotel's tunables page (HotelWorkbench), with Close.
 * Closed, it holds no panel and sends nothing; closing returns the hands to what they hold.
 */
export function createWorkbench(host, loadPanel = () => import('@rusty-engine/live-debug')) {
  let current = null;
  const close = () => {
    const previous = current;
    current = null;
    if (!previous) return;
    previous.mount?.dispose();
    previous.element.remove();
    void previous.transport?.execute('hotel.dev.motion.off').catch(() => {});
  };
  return {
    get isOpen() { return current !== null; },
    async open() {
      close();
      const element = host.ownerDocument.createElement('aside');
      element.className = 'workbench';
      element.setAttribute('aria-label', 'First-person workbench');
      element.innerHTML = '<header><button type="button" data-workbench-close>Close</button></header><div class="workbench-panel">Opening workbench…</div>';
      const session = { element, mount: null, transport: null };
      current = session;
      host.append(element);
      try {
        const { mountTunablesPanel, createLiveDebugHttpTransport } = await loadPanel();
        if (current !== session) return;
        session.transport = createLiveDebugHttpTransport();
        const panel = element.querySelector('.workbench-panel');
        panel.textContent = '';
        session.mount = mountTunablesPanel(panel, { transport: session.transport });
      } catch (error) {
        if (current !== session) return;
        element.querySelector('.workbench-panel').textContent =
          `Workbench unavailable: ${error instanceof Error ? error.message : 'could not load the Engine panel'}`;
        element.setAttribute('role', 'alert');
      }
    },
    close,
    dispose: close
  };
}
