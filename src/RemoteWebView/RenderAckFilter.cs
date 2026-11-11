using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Keeps render-batch acknowledgements in the exact one-per-batch, in-order sequence Blazor's WebViewRenderer requires.
    /// <para>
    /// Every browser on a session (the controller and each read-only mirror) applies every render batch and acknowledges it,
    /// and the relay forwards only the current controller's messages. When control moves while a batch is in flight, the host
    /// can receive a batch's acknowledgement twice (from the old and the new controller) - WebViewRenderer then dequeues an
    /// empty queue and throws - or not at all (the old controller's ack was dropped), and the next one throws as unexpected.
    /// Either exception escapes on the UI dispatcher and takes the app down.
    /// </para>
    /// <para>
    /// This filter sits between the relay and WebViewManager.MessageReceived: it drops acknowledgements for batches that are
    /// already acknowledged (or were never sent) and fills a gap by acknowledging the skipped batches first, so the renderer
    /// always sees each batch acknowledged once, in order. All other messages pass through unchanged.
    /// </para>
    /// </summary>
    public sealed class RenderAckFilter
    {
        private const string Prefix = "__bwv:";
        private const string RenderBatch = Prefix + "[\"RenderBatch\",";
        private const string RenderCompleted = Prefix + "[\"OnRenderCompleted\",";
        private const string AttachPage = Prefix + "[\"AttachPage\",";

        private readonly object _gate = new();
        private long _lastSent;
        private long _lastAcked;

        /// <summary>Records an outgoing message (host to browsers). Call for every message the manager sends.</summary>
        public void OnSending(string message)
        {
            if (!message.StartsWith(RenderBatch, StringComparison.Ordinal) || !TryReadId(message, RenderBatch.Length, out var id))
                return;

            lock (_gate)
            {
                // A new page context numbers its batches from the start again.
                if (id <= _lastAcked) _lastAcked = id - 1;
                _lastSent = id;
            }
        }

        /// <summary>
        /// Returns the messages to hand to WebViewManager.MessageReceived for one incoming message (browser to host): usually just
        /// the message, nothing for a duplicate acknowledgement, or synthesized acknowledgements for skipped batches followed by it.
        /// </summary>
        public IReadOnlyList<string> OnReceived(string message)
        {
            if (message.StartsWith(AttachPage, StringComparison.Ordinal))
            {
                lock (_gate) { _lastSent = 0; _lastAcked = 0; } // a new page context starts its own batch sequence
                return new[] { message };
            }

            if (!message.StartsWith(RenderCompleted, StringComparison.Ordinal) || !TryReadId(message, RenderCompleted.Length, out var id))
                return new[] { message };

            lock (_gate)
            {
                if (id <= _lastAcked || id > _lastSent)
                    return Array.Empty<string>(); // already acknowledged, or not a batch we're waiting on

                if (id == _lastAcked + 1)
                {
                    _lastAcked = id;
                    return new[] { message };
                }

                // The acknowledgements for the batches in between were lost in a control handoff: acknowledge them first.
                var result = new List<string>((int)Math.Min(id - _lastAcked, 64));
                for (var missing = _lastAcked + 1; missing < id; missing++)
                    result.Add(Ack(missing));
                result.Add(message);
                _lastAcked = id;
                return result;
            }
        }

        private static string Ack(long batchId) =>
            RenderCompleted + batchId.ToString(CultureInfo.InvariantCulture) + ",null]";

        private static bool TryReadId(string message, int start, out long id)
        {
            id = 0;
            var i = start;
            while (i < message.Length && message[i] == ' ') i++;
            var digitsStart = i;
            while (i < message.Length && message[i] >= '0' && message[i] <= '9') i++;
            return i > digitsStart &&
                   long.TryParse(message.AsSpan(digitsStart, i - digitsStart), NumberStyles.None, CultureInfo.InvariantCulture, out id);
        }
    }
}
