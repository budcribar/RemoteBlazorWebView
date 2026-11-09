// Bridges the headless window.RemoteControl and window.RemoteVoice globals (published by the RemoteBlazorWebView
// client JS) to the SessionControlBar Blazor component. The component owns the UI; this only forwards state and actions.

export function init(dotNetRef) {
    let control = null;
    let voice = null;
    const unsubscribers = [];

    const push = () => { dotNetRef.invokeMethodAsync('OnState', control, voice); };

    const bindControl = () => {
        if (!window.RemoteControl) return;
        control = window.RemoteControl.getState();
        unsubscribers.push(window.RemoteControl.subscribe(s => { control = s; push(); }));
    };
    const bindVoice = () => {
        if (!window.RemoteVoice) return;
        // VoiceClient's headless subscribe does not return an unsubscribe; it lives for the page.
        window.RemoteVoice.subscribe(s => { voice = s; push(); });
    };

    if (window.RemoteControl) bindControl();
    else window.addEventListener('remotecontrolready', () => { bindControl(); push(); }, { once: true });

    if (window.RemoteVoice) bindVoice();
    else window.addEventListener('remotevoiceready', () => { bindVoice(); push(); }, { once: true });

    push();

    return {
        dispose: () => { unsubscribers.forEach(u => { try { if (u) u(); } catch (e) { /* ignore */ } }); }
    };
}

export function requestPrimary(target) {
    if (window.RemoteControl) window.RemoteControl.requestPrimary(target);
}

export function copyShareLink() {
    const url = window.RemoteControl ? window.RemoteControl.getState().shareUrl : null;
    if (url && navigator.clipboard) navigator.clipboard.writeText(url);
    return url || null;
}

export function voiceAct()  { if (window.RemoteVoice) window.RemoteVoice.act(); }
export function voiceMute() { if (window.RemoteVoice) window.RemoteVoice.toggleMute(); }
export function voiceEnd()  { if (window.RemoteVoice) window.RemoteVoice.hangup(); }
