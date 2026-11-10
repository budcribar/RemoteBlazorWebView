// What a page does when its connection to the session is lost without warning (not a restart, which announces itself).
// The page would otherwise stay on screen looking alive, with nothing on it working and nothing saying why.
//
// A read-only /mirror page can come back: the session keeps everything the app has sent, so loading the page again
// rebuilds it, the way a late joiner's page is built. It waits until the session answers and then reloads. The app's
// own page cannot: loading it again counts as a refresh, which rebuilds the page for everyone and restarts an app that
// restarts on refresh. That page only says what happened.

export interface ConnectionLostOptions {
    /** A page that can be loaded again without disturbing the session: a /mirror page in a browser. */
    canReturn: boolean;
    /** The address to wait for and load again. */
    address: string;
    reload: () => void;
    /** True when the address answers, which a /mirror address does only while its session is running. */
    probe?: (address: string) => Promise<boolean>;
    firstTryMs?: number;
    retryMs?: number;
    giveUpMs?: number;
}

export const NOTICE_ID = 'rbwv-connection-lost';
export const TEXT_WAITING = 'Connection lost. Waiting for the session to come back…';
export const TEXT_ENDED = 'The session has ended.';
export const TEXT_LOST = 'Connection lost. The session has ended or cannot be reached.';

let shown = false;

async function answers(address: string): Promise<boolean> {
    try {
        const response = await fetch(address, { cache: 'no-store', credentials: 'same-origin' });
        return response.ok;
    } catch {
        return false; // not reachable right now
    }
}

function notice(text: string) {
    let element = document.getElementById(NOTICE_ID);
    if (!element) {
        element = document.createElement('div');
        element.id = NOTICE_ID;
        element.setAttribute('role', 'status');
        element.setAttribute('aria-live', 'polite');
        const style = element.style;
        style.position = 'fixed';
        style.top = '0';
        style.left = '0';
        style.right = '0';
        // Above the page, the read-only overlay and the session bar: nothing under it works any more.
        style.zIndex = '2147483647';
        style.padding = '10px 16px';
        style.background = '#fff4ce';
        style.color = '#1b1b1b';
        style.borderBottom = '1px solid #d6b656';
        style.font = '14px/1.4 system-ui, -apple-system, "Segoe UI", sans-serif';
        style.textAlign = 'center';
        document.body.appendChild(element);
    }
    element.textContent = text;
}

/** Call when the page's connection to the session closes unexpectedly. Later calls do nothing. */
export function connectionLost(options: ConnectionLostOptions) {
    if (shown) return;
    shown = true;

    if (!options.canReturn) {
        notice(TEXT_LOST);
        return;
    }

    notice(TEXT_WAITING);
    const probe = options.probe ?? answers;
    const retryMs = options.retryMs ?? 2000;
    const giveUpMs = options.giveUpMs ?? 120000;
    const started = Date.now();

    const attempt = async () => {
        if (await probe(options.address)) { options.reload(); return; }
        if (Date.now() - started > giveUpMs) { notice(TEXT_ENDED); return; }
        setTimeout(attempt, retryMs);
    };
    setTimeout(attempt, options.firstTryMs ?? 2000);
}
