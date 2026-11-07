// Remote components' frames (RemoteComponentFrameBase). A frame has an opaque origin (sandbox without allow-same-origin), so a message
// is matched to its frame by the window that sent it, never by origin. The only message accepted is the frame's content height.
const frames = new Map(); // iframe element -> .NET HeightSink
let listening = false;

function onMessage(e) {
    for (const [frame, sink] of frames) {
        if (frame.contentWindow !== e.source) continue;
        const d = e.data;
        if (d && typeof d === 'object' && d.type === 'rbwv-remote-height' && typeof d.height === 'number')
            sink.invokeMethodAsync('SetHeight', d.height).catch(() => { });
        return;
    }
}

// A read-only mirror of the app's page (RemoteWebView sets RemoteBlazor.isPrimary = false). It gets the same render batches and JS calls
// as the app's page, but must neither load the component's own page (the relay would take it as a reload and restart the component) nor
// size the frame: the RemoteWebView script points its frames at the mirror address, and the frame's height comes with the app's renders.
function isMirror() {
    return !!(window.RemoteBlazor && window.RemoteBlazor.isPrimary === false);
}

// The frame is rendered without src (data-rbwv-src holds it), so only the page that should load it does.
function load(frame) {
    const src = frame.getAttribute('data-rbwv-src');
    if (src && frame.getAttribute('src') !== src) frame.setAttribute('src', src);
}

export function watch(frame, sink) {
    if (!frame || isMirror()) return;
    load(frame);
    if (!listening) { window.addEventListener('message', onMessage); listening = true; }
    frames.set(frame, sink);
}

export function unwatch(frame) {
    frames.delete(frame);
}
