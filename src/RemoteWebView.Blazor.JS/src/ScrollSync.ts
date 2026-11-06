// Scrolling is browser state and is not included in Blazor render batches.
export const scrollMessagePrefix = 'mirror-scroll:';

interface ScrollPosition {
    frames: number[][];
    path: number[];
    tag: string;
    id: string;
    left: number;
    top: number;
}

function elementPath(element: Element): number[] {
    const path: number[] = [];
    let current = element;
    while (current.parentElement) {
        path.unshift(Array.prototype.indexOf.call(current.parentElement.children, current));
        current = current.parentElement;
    }
    return path;
}

function resolveElement(doc: Document, path: number[]): Element | undefined {
    let element: Element | undefined = doc.documentElement;
    for (const index of path) element = element?.children[index];
    return element;
}

export function initializeScrollSync(send: (message: string) => void): void {
    const targets = new Map<Element, HTMLIFrameElement[]>();
    const watched = new WeakSet<Document>();
    const watchedFrames = new WeakSet<HTMLIFrameElement>();
    let scheduled = false;

    function watch(doc: Document, frames: HTMLIFrameElement[]): void {
        if (watched.has(doc)) return;
        watched.add(doc);
        doc.addEventListener('scroll', event => {
            const target = (event.target === doc ? doc.scrollingElement : event.target) as Element | null;
            // instanceof Element would reject elements from an iframe's realm.
            if (target?.nodeType !== 1) return;
            targets.set(target, frames);
            if (scheduled) return;
            scheduled = true;
            window.setTimeout(() => {
                scheduled = false;
                const positions: ScrollPosition[] = [];
                for (const [element, chain] of targets) {
                    if (!element.isConnected || chain.some(frame => !frame.isConnected)) {
                        targets.delete(element); continue;
                    }
                    positions.push({ frames: chain.map(elementPath), path: elementPath(element),
                        tag: element.tagName, id: element.id, left: element.scrollLeft, top: element.scrollTop });
                }
                send(scrollMessagePrefix + JSON.stringify(positions));
            }, 50);
        }, true); // scroll does not bubble; nested overflow panes need capture.

        function findFrames(): void {
            doc.querySelectorAll<HTMLIFrameElement>('iframe').forEach(frame => {
                if (watchedFrames.has(frame)) return;
                watchedFrames.add(frame);
                const attach = () => {
                    // Cross-origin frames are intentionally inaccessible.
                    try { if (frame.contentDocument) watch(frame.contentDocument, [...frames, frame]); }
                    catch { /* Browser origin policy applies. */ }
                };
                frame.addEventListener('load', attach);
                attach();
            });
        }
        findFrames();
        new MutationObserver(findFrames).observe(doc, { childList: true, subtree: true });
    }
    watch(document, []);
}

function documentInFrameChain(start: Document, frames: number[][]): Document | null {
    let doc: Document | null = start;
    for (const framePath of frames) {
        const frame = doc && resolveElement(doc, framePath);
        if (frame?.tagName !== 'IFRAME') return null;
        try { doc = (frame as HTMLIFrameElement).contentDocument; }
        catch { return null; }
        if (!doc) return null;
    }
    return doc;
}

export function receiveScroll(message: string): boolean {
    if (!message.startsWith(scrollMessagePrefix)) return false;
    if (window.RemoteBlazor.isPrimary) return true;
    const positions: ScrollPosition[] = JSON.parse(message.substring(scrollMessagePrefix.length));
    for (const position of positions) {
        const doc = documentInFrameChain(document, position.frames);
        if (!doc) continue;
        const element = resolveElement(doc, position.path);
        if (element?.tagName !== position.tag || element.id !== position.id) continue;
        // Assign offsets directly so CSS smooth scrolling cannot delay the mirror.
        element.scrollLeft = position.left;
        element.scrollTop = position.top;
    }
    return true;
}
