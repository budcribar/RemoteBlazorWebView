// Remote component frames (PeakSWC.RemoteBlazorWebView.RemoteComponents) on a read-only mirror page.
// A frame is rendered without src: data-rbwv-src holds the component's own page, which the app's page loads (remotecomponents.js), and
// data-rbwv-mirror-src the relay's read-only view of it. A mirror gets the same render batches as the app's page; if it loaded
// data-rbwv-src too, the relay would take that as a reload and the component's process would restart. So a mirror points each frame
// at its mirror address, or leaves it empty when there is none.
export const sourceAttribute = 'data-rbwv-src';
export const mirrorSourceAttribute = 'data-rbwv-mirror-src';

export function mirrorSourceOf(frame: Element): string | null {
    return frame.getAttribute(mirrorSourceAttribute);
}

function point(frame: Element) {
    if (frame.tagName !== 'IFRAME' || !frame.hasAttribute(sourceAttribute)) return;
    const mirror = mirrorSourceOf(frame);
    if (mirror) {
        if (frame.getAttribute('src') !== mirror) frame.setAttribute('src', mirror);
    } else if (frame.hasAttribute('src')) {
        frame.removeAttribute('src');
    }
}

function pointAll(root: ParentNode) {
    root.querySelectorAll(`iframe[${sourceAttribute}]`).forEach(point);
}

let observer: MutationObserver | null = null;

export function watchMirrorFrames(root: Element = document.documentElement) {
    if (observer) return;
    pointAll(root);
    observer = new MutationObserver(records => {
        for (const r of records) {
            if (r.type === 'attributes') {
                point(r.target as Element);
            } else {
                r.addedNodes.forEach(n => {
                    if (n.nodeType !== Node.ELEMENT_NODE) return;
                    point(n as Element);
                    pointAll(n as Element);
                });
            }
        }
    });
    observer.observe(root, { subtree: true, childList: true, attributes: true, attributeFilter: [sourceAttribute, mirrorSourceAttribute, 'src'] });
}
