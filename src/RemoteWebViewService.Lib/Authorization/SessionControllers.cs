using System;
using System.Collections.Generic;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// The signed-in users who may be given control of a session. Empty (the usual case) means no limit beyond the
    /// session's own access rule. A session started through AgentAdmin.StartSession starts with the user who asked for
    /// it; anyone that user (or another listed user) hands control to is added, so a deliberate hand-off keeps working.
    /// Anyone else in the session's group can still watch.
    /// </summary>
    public sealed class SessionControllers
    {
        private readonly HashSet<string> users = new(StringComparer.Ordinal);
        private readonly object gate = new();

        /// <summary>True once a user has been named: from then on only listed users may be given control.</summary>
        public bool IsLimited
        {
            get { lock (gate) return users.Count > 0; }
        }

        public void Add(string? userId)
        {
            if (string.IsNullOrEmpty(userId))
                return;
            lock (gate)
                users.Add(userId);
        }

        /// <summary>Whether a connection signed in as <paramref name="userId"/> may be given control.</summary>
        public bool Allows(string? userId)
        {
            lock (gate)
                return users.Count == 0 || (!string.IsNullOrEmpty(userId) && users.Contains(userId));
        }

        /// <summary>Whether <paramref name="userId"/> is listed by name (false when the list is empty).</summary>
        public bool Lists(string? userId)
        {
            if (string.IsNullOrEmpty(userId))
                return false;
            lock (gate)
                return users.Contains(userId);
        }
    }
}
