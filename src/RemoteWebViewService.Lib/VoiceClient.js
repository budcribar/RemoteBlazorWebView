(() => {
    const script = document.currentScript;
    const session = script.dataset.session;
    const host = script.dataset.host === 'true';
    const controllerKey = script.dataset.key;
    // Set when the WPF host injects this client into the owner's main surface (in-bubble voice). Only the local host
    // can furnish it, so it is never served over the network. Absent on the bare host popup, which uses chrome.webview.
    const hostKey = script.dataset.hostKey;
    script.removeAttribute('data-key');
    script.removeAttribute('data-host-key');
    // When the control bubble is on the page it renders the voice controls; keep all the call logic here but hide this
    // panel and expose a small API (window.RemoteVoice) for the bubble to drive. The standalone panel is the fallback
    // (e.g. the bare host popup) when there is no bubble.
    const headless = window.RemoteVoiceHeadless === true;
    const subscribers = [];
    const panel = document.createElement('section');
    panel.setAttribute('aria-label', 'Remote voice');
    panel.style.cssText = host
        ? 'font:16px system-ui;padding:12px'
        : 'position:fixed;top:12px;right:12px;z-index:9999;padding:8px;color:#111;background:#fff;border:1px solid #888;border-radius:6px;font:14px system-ui';
    const status = document.createElement('span');
    status.setAttribute('role', 'status');
    const action = document.createElement('button');
    action.type = 'button';
    const mute = document.createElement('button');
    mute.type = 'button';
    const end = document.createElement('button');
    end.type = 'button';
    const audio = document.createElement('audio');
    audio.autoplay = true;
    panel.append(status, ' ', action, ' ', mute, ' ', end, audio);
    document.body.append(panel);
    // Keep the panel in the DOM so <audio> can play, but hidden when the bubble owns the controls.
    if (headless) panel.style.display = 'none';

    let socket;
    let iceServers;
    let peer;
    let mic;
    let candidates = [];
    let state = 'offline';
    let generation = 0;
    let callId;
    let connectionTimeout;

    function display(next, text) {
        state = next;
        status.textContent = text;
        action.hidden = !(next === 'ringing' || (next === 'idle' && !host));
        action.textContent = host ? 'Accept' : 'Voice call';
        end.hidden = !['ringing', 'calling', 'connecting', 'active'].includes(next);
        end.textContent = next === 'ringing' ? 'Decline' : 'Hang up';
        mute.hidden = !mic;
        mute.textContent = mic?.getAudioTracks()[0]?.enabled === false ? 'Unmute' : 'Mute';
        notify();
    }

    function snapshot() {
        return {
            state,
            statusText: status.textContent,
            actLabel: action.textContent, canAct: !action.hidden,
            endLabel: end.textContent, canEnd: !end.hidden,
            muteLabel: mute.textContent, canMute: !mute.hidden,
        };
    }

    function notify() {
        if (headless) for (const cb of subscribers) cb(snapshot());
    }

    function send(type, payload = {}) {
        if (socket?.readyState === WebSocket.OPEN)
            socket.send(JSON.stringify({ type, callId, ...payload }));
    }

    function stop(next, text) {
        generation++;
        callId = undefined;
        clearTimeout(connectionTimeout);
        if (peer) {
            peer.onconnectionstatechange = null;
            peer.close();
        }
        peer = undefined;
        mic?.getTracks().forEach(track => track.stop());
        mic = undefined;
        audio.srcObject = null;
        audio.controls = false;
        candidates = [];
        display(next, text);
    }

    function fail(text) {
        send('end');
        stop('idle', text);
    }

    async function openPeer(call) {
        const stream = await navigator.mediaDevices.getUserMedia({
            audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true }
        });
        if (call !== generation) {
            stream.getTracks().forEach(track => track.stop());
            return;
        }
        mic = stream;
        const connection = new RTCPeerConnection({ iceServers });
        peer = connection;
        stream.getTracks().forEach(track => connection.addTrack(track, stream));
        connection.onicecandidate = event => {
            if (event.candidate && connection === peer)
                send('ice', { candidate: event.candidate.toJSON() });
        };
        connection.ontrack = event => {
            if (connection !== peer) return;
            audio.srcObject = event.streams[0];
            audio.play().catch(() => {
                if (connection === peer) audio.controls = true;
            });
        };
        connection.onconnectionstatechange = () => {
            if (connection !== peer) return;
            if (connection.connectionState === 'connected') {
                clearTimeout(connectionTimeout);
                display('active', 'Voice connected');
            } else if (['failed', 'disconnected', 'closed'].includes(connection.connectionState)) {
                fail('Voice connection lost');
            }
        };
        display('connecting', 'Connecting voice…');
        return connection;
    }

    function connect(key) {
        if (socket) return;
        socket = new WebSocket(`wss://${location.host}/voice/${session}/signal`);
        socket.onopen = () => socket.send(key);
        socket.onclose = () => stop('offline', 'Voice disconnected');
        socket.onerror = () => stop('offline', 'Voice unavailable');
        // Keep signaling ordered even when a browser API resolves asynchronously.
        let queue = Promise.resolve();
        socket.onmessage = event => {
            const call = generation;
            queue = queue.then(() => receive(JSON.parse(event.data), call)).catch(() => {
                if (call === generation) fail('Could not connect voice');
            });
        };
    }

    async function receive(message, call) {
        if (message.type === 'ready') {
            iceServers = message.iceServers;
            display('idle', host ? 'Waiting for a voice call' : '');
            return;
        }
        if (message.type === 'invite' && host && state === 'idle') {
            callId = message.callId;
            display('ringing', 'The remote technician is requesting a voice call.');
            return;
        }
        if (message.callId !== callId) return;
        if (message.type === 'end' || message.type === 'unavailable') {
            stop('idle', message.type === 'end' ? 'Voice call ended' : 'Other participant unavailable');
            return;
        }
        if (call !== generation) return;
        if (message.type === 'accept' && !host && state === 'calling') {
            display('connecting', 'Connecting voice…');
            // Run permission acquisition independently so an end message can cancel it.
            startOffer(call);
        } else if (message.type === 'ice' && ['connecting', 'active'].includes(state)) {
            if (peer?.remoteDescription) await peer.addIceCandidate(message.candidate);
            else if (candidates.length < 128) candidates.push(message.candidate);
        } else if ((message.type === 'offer' && host) || (message.type === 'answer' && !host)) {
            const connection = peer;
            if (!connection) return;
            await connection.setRemoteDescription(message.description);
            for (const candidate of candidates.splice(0))
                await connection.addIceCandidate(candidate);
            if (host && call === generation) {
                await connection.setLocalDescription(await connection.createAnswer());
                if (call === generation)
                    send('answer', { description: connection.localDescription.toJSON() });
            }
        }
    }

    async function startOffer(call) {
        try {
            const connection = await openPeer(call);
            if (!connection) return;
            await connection.setLocalDescription(await connection.createOffer());
            if (call === generation)
                send('offer', { description: connection.localDescription.toJSON() });
        } catch {
            if (call === generation) fail('Microphone or voice connection unavailable');
        }
    }

    action.onclick = async () => {
        const call = ++generation;
        connectionTimeout = setTimeout(() => fail('Voice call timed out'), 45000);
        if (!host) {
            callId = crypto.randomUUID();
            display('calling', 'Waiting for the person at the PC…');
            send('invite');
            return;
        }
        display('connecting', 'Opening microphone…');
        try {
            if (await openPeer(call))
                send('accept');
        } catch {
            if (call === generation) fail('Microphone unavailable');
        }
    };
    end.onclick = () => {
        send('end');
        stop('idle', 'Voice call ended');
    };
    mute.onclick = () => {
        const track = mic?.getAudioTracks()[0];
        if (track) track.enabled = !track.enabled;
        display(state, status.textContent);
    };
    addEventListener('pagehide', () => {
        stop('offline', 'Voice disconnected');
        socket?.close();
    });

    if (headless) {
        window.RemoteVoice = {
            subscribe(cb) { subscribers.push(cb); cb(snapshot()); },
            act() { if (action.onclick) action.onclick(); },
            hangup() { if (end.onclick) end.onclick(); },
            toggleMute() { if (mute.onclick) mute.onclick(); },
        };
        window.dispatchEvent(new Event('remotevoiceready'));
    }

    display('offline', 'Connecting voice controls…');
    if (host) {
        // In-surface (bubble) voice gets the key from the injected script; the standalone popup gets it postMessage'd.
        if (hostKey) connect(hostKey);
        else window.chrome.webview.addEventListener('message', event => connect(event.data));
    } else {
        connect(controllerKey);
    }
})();
