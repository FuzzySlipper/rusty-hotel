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
