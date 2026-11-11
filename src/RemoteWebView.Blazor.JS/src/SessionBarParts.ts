// What the session bar shows for a given state: which parts, and the status text. No DOM here, so the built-in bar and
// an app's own markup (a template in the placeholder) follow exactly the same rules. See SessionControls.ts.

import { ControlState, Participant, VoiceState } from './RemoteControl';

/** The parts of the bar. In an app's template an element is one of these when it carries data-rbwv="<part>". */
export type BarPart =
    'dot' | 'status' | 'give' | 'take' | 'copy-link' |
    'restart' | 'restart-question' | 'restart-confirm' | 'restart-cancel' |
    'voice-status' | 'voice-act' | 'voice-mute' | 'voice-end';

export const BAR_PARTS: readonly BarPart[] = ['dot', 'status', 'give', 'take', 'copy-link',
    'restart', 'restart-question', 'restart-confirm', 'restart-cancel', 'voice-status', 'voice-act', 'voice-mute', 'voice-end'];

export interface BarOptions {
    /** A hand-off was asked for and the relay has not answered yet. */
    switching: boolean;
    /** "Restart the app?" is showing. */
    confirmingRestart: boolean;
    /** The app wants Copy link (owner only). */
    shareLink: boolean;
    /** The app wants Restart app. */
    restart: boolean;
    hostName: string;
    remoteName: string;
}

/** The status wording an app may replace (data-text-* on its status element). */
export interface StatusTexts {
    you: string;
    viewOnly: string;
    none: string;
    switching: string;
    restarting: string;
}

export const DEFAULT_STATUS_TEXTS: StatusTexts = {
    you: 'You have control',
    viewOnly: 'View only',
    none: 'No one has control',
    switching: 'Switching control…',
    restarting: 'Restarting…',
};

/** There is something for a bar to do: a session with an owner (control can be handed over) or a voice call. */
export function hasSomethingToShow(control: ControlState | null, voice: VoiceState | null): boolean {
    return !!control && (control.participants.some(p => p.owner) || !!voice);
}

/** The party this page hands control to: the remote user for the owner, the owner for everyone else. */
export function otherParty(control: ControlState): Participant | undefined {
    const me = control.myClientId;
    return control.isOwner
        ? control.participants.find(p => !p.owner && p.id !== me)
        : control.participants.find(p => p.owner && p.id !== me);
}

/** A read-only viewer: a remote connection that is not the one control is shared with (the first remote connection). */
export function isViewer(control: ControlState): boolean {
    if (control.isOwner || !control.participants.some(p => p.owner)) return false;
    const first = control.participants.find(p => !p.owner);
    return !!first && first.id !== control.myClientId;
}

/** How many are connected remotely, and the sentence for the dot's tooltip. */
export function remoteConnections(control: ControlState | null): { count: number; text: string } {
    const count = control
        ? control.participants.filter(p => control.participants.some(o => o.owner) ? !p.owner : p.id !== control.myClientId).length
        : 0;
    const text = control && !control.isOwner && control.participants.some(p => p.owner)
        ? (count <= 1 ? 'You are connected remotely' : `${count} people are connected remotely`)
        : count === 0 ? 'No one is connected remotely'
            : count === 1 ? 'Someone is connected remotely'
                : `${count} people are connected remotely`;
    return { count, text };
}

export function statusText(control: ControlState, options: BarOptions, texts: StatusTexts = DEFAULT_STATUS_TEXTS): string {
    if (control.restarting) return texts.restarting;
    if (options.switching) return texts.switching;
    if (control.iHaveControl) return texts.you;
    if (isViewer(control)) return texts.viewOnly;
    const controller = control.participants.find(p => p.primary);
    return controller ? `${controller.owner ? options.hostName : options.remoteName} has control` : texts.none;
}

/** The parts to show, in the built-in bar's order. Parts not listed are left out of the bar (never hidden with CSS). */
export function visibleParts(control: ControlState, voice: VoiceState | null, options: BarOptions): BarPart[] {
    const parts: BarPart[] = ['dot', 'status'];
    // Restarting: nothing can be used until the new copy is up and this page has loaded again.
    if (control.restarting) return parts;

    if (!options.switching) {
        // Give: only the current controller, and only when there is another party to hand off to.
        if (control.iHaveControl && otherParty(control)) parts.push('give');
        // Take: only the owner may reclaim, and only when they do not already hold control.
        if (control.isOwner && !control.iHaveControl) parts.push('take');
    }

    if (options.shareLink && control.isOwner && control.shareUrl) parts.push('copy-link');

    // The relay allows the controller and the owner to restart the app.
    if (options.restart && (control.iHaveControl || control.isOwner)) {
        if (options.confirmingRestart) parts.push('restart-question', 'restart-confirm', 'restart-cancel');
        else parts.push('restart');
    }

    if (voice) {
        if (voice.statusText) parts.push('voice-status');
        if (voice.canAct) parts.push('voice-act');
        if (voice.canMute) parts.push('voice-mute');
        if (voice.canEnd) parts.push('voice-end');
    }
    return parts;
}
