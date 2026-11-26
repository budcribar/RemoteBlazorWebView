using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// What proves a browser request comes from the connection it names. A connection's clientId is public: the roster
    /// sends every clientId to every browser in the session (pages need them to hand control over), so the id alone
    /// proves nothing. Each page therefore also picks a secret key that it sends with its own requests and that the
    /// relay never sends to anyone. The first key seen for a clientId is that connection's key; a request naming the
    /// clientId with any other key is not from it. A page that sends no key (an app built before keys existed) is held
    /// to the empty key, so it works as before and is protected only against a request that does carry a key.
    /// </summary>
    public sealed class ClientKeys
    {
        private sealed class Entry(string key)
        {
            public string Key { get; } = key;
            public int Streams { get; set; }
        }

        private readonly object gate = new();
        private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

        /// <summary>
        /// The clientId's first request, when it is not its stream: the page that claims control sends its first
        /// message before its stream opens. False when the clientId is already known with another key.
        /// </summary>
        public bool Bind(string clientId, string? key)
        {
            lock (gate)
                return Same(Known(clientId, key).Key, key);
        }

        /// <summary>A stream opens for the clientId. False (and nothing is recorded) when the clientId is known with another key.</summary>
        public bool Attach(string clientId, string? key)
        {
            lock (gate)
            {
                var entry = Known(clientId, key);
                if (!Same(entry.Key, key))
                    return false;
                entry.Streams++;
                return true;
            }
        }

        /// <summary>
        /// A stream that Attach accepted has closed. The key is kept while the clientId still has a stream, and when
        /// <paramref name="keep"/> is set: the session still names this clientId (it has control or is the owner), and
        /// forgetting its key would let anyone take the name over.
        /// </summary>
        public void Detach(string clientId, bool keep)
        {
            lock (gate)
            {
                if (!entries.TryGetValue(clientId ?? string.Empty, out var entry))
                    return;
                if (entry.Streams > 0)
                    entry.Streams--;
                if (entry.Streams == 0 && !keep)
                    entries.Remove(clientId ?? string.Empty);
            }
        }

        /// <summary>The request carries the key of a clientId the relay already knows. An unknown clientId never matches.</summary>
        public bool Matches(string clientId, string? key)
        {
            lock (gate)
                return entries.TryGetValue(clientId ?? string.Empty, out var entry) && Same(entry.Key, key);
        }

        private Entry Known(string clientId, string? key)
        {
            clientId ??= string.Empty;
            if (!entries.TryGetValue(clientId, out var entry))
                entries[clientId] = entry = new Entry(key ?? string.Empty);
            return entry;
        }

        private static bool Same(string known, string? given) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(known), Encoding.UTF8.GetBytes(given ?? string.Empty));
    }
}
