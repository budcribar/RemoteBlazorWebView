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

export function watch(frame, sink) {
    if (!frame) return;
    if (!listening) { window.addEventListener('message', onMessage); listening = true; }
    frames.set(frame, sink);
}

export function unwatch(frame) {
    frames.delete(frame);
}
