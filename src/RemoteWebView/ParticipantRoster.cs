using System.Collections.Generic;
using System.Text.Json;

namespace PeakSWC.RemoteWebView
{
    internal static class ParticipantRoster
    {
        /// <summary>
        /// Reads the relay's roster message (after "participants:"):
        /// [{"id":"...","owner":true,"primary":false,"name":"..."}, ...]. "name" is newer; older relays leave it out.
        /// </summary>
        public static IReadOnlyList<Participant> Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<Participant>();
            foreach (var p in doc.RootElement.EnumerateArray())
            {
                list.Add(new Participant(
                    p.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    p.TryGetProperty("owner", out var owner) && owner.ValueKind == JsonValueKind.True,
                    p.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True,
                    p.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? string.Empty : string.Empty));
            }
            return list;
        }
    }
}
