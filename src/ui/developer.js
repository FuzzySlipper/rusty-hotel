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

// The Hotel overlay sets the shell shows in the player view while open (C# HeldInspection): the stance guide and the
// hand fit's contact probes. Each draws only once C# publishes it.
const PLAYER_SETS = ['held.guide', 'hands.contact'];

/**
 * The Engine developer shell, docked at the left beside play (which keeps running): the console, the first-person
 * workbench (the tunables panel over Hotel's page, HotelWorkbench) and two inspection views of the held assembly, the
 * main grip from the side and the support grip from underneath (C# inspection targets). Closed, it holds no panel and
 * sends nothing. However it closes (its own Close, the key, Escape), the views close, the hands return to what they
 * hold and `onClose` returns input to play.
 */
export function createDeveloperShell(host, { viewport = null, onClose = () => {}, loadPanel = () => import('@rusty-engine/live-debug') } = {}) {
  let current = null;
  const close = () => {
    const previous = current;
    current = null;
    if (!previous) return;
    previous.observer?.disconnect();
    previous.shell?.close();
    if (previous.transport) {
      for (const set of PLAYER_SETS) void previous.transport.execute(`engine.inspect.overlay hide set ${set}`).catch(() => {});
      void previous.transport.execute('hotel.dev.motion.off').catch(() => {});
    }
    previous.error?.remove();
    onClose();
  };
  return {
    get isOpen() { return current !== null; },
    async open() {
      if (current) return;
      const session = { shell: null, transport: null, observer: null, error: null };
      current = session;
      try {
        const { mountDeveloperShell, createLiveDebugHttpTransport } = await loadPanel();
        if (current !== session) return;
        session.transport = createLiveDebugHttpTransport();
        session.shell = mountDeveloperShell(host, {
          side: 'left',
          presentation: 'dock',
          transport: session.transport,
          panels: [
            ...(viewport ? [
              { kind: 'view', title: 'Main grip', options: { viewport, id: 'side', subject: 'target main-grip', framings: ['side', 'front', 'top', 'close'] } },
              { kind: 'view', title: 'Support grip', options: { viewport, id: 'grip', subject: 'target off-grip', framings: ['underside', 'side', 'close'] } }
            ] : []),
            { kind: 'tunables', title: 'Workbench' },
            { kind: 'console' }
          ]
        });
        await session.shell.open();
        if (current !== session) { session.shell.close(); return; }
        // The shell's own Close removes its element without telling the product: closing follows it.
        session.observer = new host.ownerDocument.defaultView.MutationObserver(() => { if (current === session && !session.shell.isOpen) close(); });
        session.observer.observe(host, { childList: true });
        for (const set of PLAYER_SETS) void session.transport.execute(`engine.inspect.overlay show set ${set}`).catch(() => {});
      } catch (error) {
        if (current !== session) return;
        session.error = host.ownerDocument.createElement('p');
        session.error.className = 'developer-error';
        session.error.setAttribute('role', 'alert');
        session.error.textContent = `Developer shell unavailable: ${error instanceof Error ? error.message : 'could not load the Engine panel'}`;
        host.append(session.error);
      }
    },
    close,
    dispose: close
  };
}
