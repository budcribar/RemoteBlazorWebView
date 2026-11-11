// Remote components' frames (RemoteComponentFrameBase). A frame has an opaque origin (sandbox without allow-same-origin), so a message
// is matched to its frame by the window that sent it, never by origin. The only message accepted is the frame's content height.
const frames = new Map(); // iframe element -> .NET HeightSink
let listening = false;

function onMessage(e) {
    // Only the page in control reports the height (the relay drops calls from the others anyway).
    if (window.RemoteBlazor && window.RemoteBlazor.isPrimary === false) return;
    for (const [frame, sink] of frames) {
        if (frame.contentWindow !== e.source) continue;
        const d = e.data;
        if (d && typeof d === 'object' && d.type === 'rbwv-remote-height' && typeof d.height === 'number')
            sink.invokeMethodAsync('SetHeight', d.height).catch(() => { });
        return;
    }
}

// A page opened as a read-only /mirror page. It gets the same render batches and JS calls as the app's page, but must not load the
// component's own page (the relay would take it as a reload and restart the component): the RemoteWebView script points its frames at
// the mirror address. This is about which page it is, not who has control: control moves between pages, and the app's page keeps
// the component's own page (and so keeps it running) even while another page is in control.
function isMirror() {
    const rb = window.RemoteBlazor;
    if (!rb) return false;
    return typeof rb.isMirrorPage === 'boolean' ? rb.isMirrorPage : rb.isPrimary === false;
}

// The frame is rendered without src (data-rbwv-src holds it), so only the page that should load it does.
function load(frame) {
    let src = frame.getAttribute('data-rbwv-src');
    if (!src) return;
    // This page's identity in the component's session (set by the RemoteWebView script), so the component's control can follow the
    // app's control from browser to browser. A fragment, so it is never sent to the relay.
    const hash = window.RemoteBlazor && window.RemoteBlazor.frameHash;
    if (hash && !src.includes('#')) src += hash;
    if (frame.getAttribute('src') !== src) frame.setAttribute('src', src);
}

export function watch(frame, sink) {
    if (!frame) return;
    if (!isMirror()) load(frame);
    if (!listening) { window.addEventListener('message', onMessage); listening = true; }
    frames.set(frame, sink);
}

export function unwatch(frame) {
    frames.delete(frame);
}
