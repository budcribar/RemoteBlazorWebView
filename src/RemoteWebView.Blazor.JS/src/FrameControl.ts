// Control of remote component frames follows control of the page they are on.
//
// A remote component (PeakSWC.RemoteBlazorWebView.RemoteComponents) is its own relay session, shown in an iframe: the
// app's page frames the component's own page, every /mirror page frames the component's read-only mirror. When control
// of the app moves to another browser, the component must become usable there and read-only here.
//
// Browsers cannot talk to each other, so the hand-off needs no lookup: a frame's connection id in the component's
// session is derived from its parent page's connection id in the app's session (panelClientId). Every page knows who
// controls the app (the relay's roster), so it can tell its frames which connection should control the component, and
// the frame that currently holds the component's control gives it to that connection. The relay already lets the
// current holder give control away; nothing else is needed from it.
//
// Parent -> frame:  { type: 'rbwv-parent-control', primaryId }   (primaryId is the derived id, never the app's own ids)
// Frame -> parent:  { type: 'rbwv-control-query' }               (asked when the frame starts)
// Frames are sandboxed with an opaque origin, so messages are matched by window (parent / contentWindow), not origin.

const frameSelector = 'iframe[data-rbwv-src]';
const idPattern = /^[0-9a-f]{32}$/;

const derived = new Map<string, string>();

/** The connection id a frame on the page with app connection id `mainClientId` uses in the component's session. */
export async function panelClientId(mainClientId: string): Promise<string> {
    let id = derived.get(mainClientId);
    if (!id) {
        const bytes = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode('rbwv-frame:' + mainClientId)));
        id = Array.from(bytes.slice(0, 16), b => b.toString(16).padStart(2, '0')).join('');
        derived.set(mainClientId, id);
    }
    return id;
}

/** True when this page is itself framed (a remote component's page or its mirror). */
export function isFramed(): boolean {
    try { return window.parent !== window; } catch { return true; }
}

/** What the parent page passed this frame in its address fragment: its connection id, and whether the parent is the owner's surface. */
export function readFrameIdentity(): { clientId: string | null, owner: boolean } {
    if (!isFramed()) return { clientId: null, owner: false };
    const p = new URLSearchParams(window.location.hash.replace(/^#/, ''));
    const id = p.get('rbwvFrameId');
    return { clientId: id && idPattern.test(id) ? id : null, owner: p.get('rbwvOwner') === '1' };
}

/** Adds this page's frame identity to a component address (the fragment is never sent to the relay). */
export function withFrameIdentity(url: string): string {
    const hash = window.RemoteBlazor?.frameHash;
    return hash && !url.includes('#') ? url + hash : url;
}

// ---- Parent side: tell this page's frames who should control their component ----

let desiredForFrames: string | null = null;

function postToFrames() {
    if (!desiredForFrames) return;
    document.querySelectorAll(frameSelector).forEach(f =>
        (f as HTMLIFrameElement).contentWindow?.postMessage({ type: 'rbwv-parent-control', primaryId: desiredForFrames }, '*'));
}

/** Called once on every page. `onIdentityReady` runs when this page's own frame identity (RemoteBlazor.frameHash) is known. */
export function initParentFrameControl(onIdentityReady: () => void) {
    void panelClientId(window.RemoteBlazor.clientId).then(id => {
        window.RemoteBlazor.frameHash = `#rbwvFrameId=${id}${window.RemoteBlazor.isOwner ? '&rbwvOwner=1' : ''}`;
        onIdentityReady();
    });

    // Working out a frame identity takes a moment (it is a hash), so two control changes close together can finish
    // out of order. Only the answer for the latest change is used, or the frames would be left following the earlier one.
    let latestChange = 0;
    window.RemoteControl?.subscribe(state => {
        const primary = state.participants.find(p => p.primary);
        if (!primary) return;
        const change = ++latestChange;
        void panelClientId(primary.id).then(id => {
            if (change !== latestChange) return;
            desiredForFrames = id;
            postToFrames();
        });
    });

    window.addEventListener('message', e => {
        const d = e.data;
        if (!d || typeof d !== 'object' || d.type !== 'rbwv-control-query' || !desiredForFrames) return;
        for (const f of Array.from(document.querySelectorAll(frameSelector))) {
            if ((f as HTMLIFrameElement).contentWindow === e.source) {
                (e.source as Window).postMessage({ type: 'rbwv-parent-control', primaryId: desiredForFrames }, '*');
                return;
            }
        }
    });
}

// ---- Frame side: give the component's control to the connection the parent names ----

let desired: string | null = null;
let askedFor: string | null = null;
let askedAt = 0;

function evaluate() {
    const control = window.RemoteControl;
    if (!desired || !control) return;
    const s = control.getState();
    if (!s.iHaveControl || desired === s.myClientId) return;
    if (!s.participants.some(p => p.id === desired)) return; // that browser's frame has not attached yet; the roster will say when
    const now = Date.now();
    if (askedFor === desired && now - askedAt < 3000) return;
    askedFor = desired;
    askedAt = now;
    control.requestPrimary(desired);
}

/** Called once on a framed page. */
export function initFrameControl() {
    if (!isFramed()) return;
    window.addEventListener('message', e => {
        if (e.source !== window.parent) return;
        const d = e.data;
        if (!d || typeof d !== 'object' || d.type !== 'rbwv-parent-control' || typeof d.primaryId !== 'string' || !idPattern.test(d.primaryId)) return;
        desired = d.primaryId;
        evaluate();
    });
    window.RemoteControl?.subscribe(() => evaluate());
    window.parent.postMessage({ type: 'rbwv-control-query' }, '*');
}
