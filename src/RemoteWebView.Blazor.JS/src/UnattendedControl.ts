// Unattended sessions (?control=remote on the owner's page): the remote user gets control on joining, with nobody at
// the machine handing it over. Once per remote user, so the owner can take control back and keep it.
//
// "Once" means once it has happened. A request can miss: the call fails, or the relay turns it down because the
// remote user's connection is not fully attached yet. So a remote user counts as served only when the roster has shown
// them in control; until then the request is repeated a few times.

import type { ControlState } from './RemoteControl';

export interface UnattendedControlOptions {
    subscribe: (callback: (state: ControlState) => void) => unknown;
    requestPrimary: (targetClientId: string) => void;
    retryMs?: number;
    maxAttempts?: number;
}

export function giveControlToWhoeverJoins(options: UnattendedControlOptions) {
    const retryMs = options.retryMs ?? 3000;
    const maxAttempts = options.maxAttempts ?? 5;
    const served = new Set<string>();           // has been seen in control
    const attempts = new Map<string, number>(); // requests made so far, per remote user
    let latest: ControlState | null = null;
    let retryScheduled = false;

    const consider = () => {
        const state = latest;
        if (!state) return;

        // Whoever the roster shows in control has had their turn, however they got it.
        for (const p of state.participants) {
            if (p.primary && !p.owner && p.id !== state.myClientId) served.add(p.id);
        }

        if (!state.iHaveControl) return;
        const remoteUser = state.participants.find(p => !p.owner && p.id !== state.myClientId);
        if (!remoteUser || served.has(remoteUser.id)) return;

        const made = attempts.get(remoteUser.id) ?? 0;
        if (made >= maxAttempts || retryScheduled) return;
        attempts.set(remoteUser.id, made + 1);
        options.requestPrimary(remoteUser.id);

        // Look again after a while: if the roster still shows this page in control, the request did not take.
        retryScheduled = true;
        setTimeout(() => { retryScheduled = false; consider(); }, retryMs);
    };

    options.subscribe(state => { latest = state; consider(); });
}
