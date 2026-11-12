using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeakSWC.RemoteWebView
{
    public sealed class RemoteTicketPayload
    {
        [JsonPropertyName("oid")]
        public string Oid { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("groups")]
        public string[] Groups { get; set; } = [];

        [JsonPropertyName("tier")]
        public string Tier { get; set; } = "";

        [JsonPropertyName("exp")]
        public long Exp { get; set; }
    }

    /// <summary>
    /// <c>base64url(json).base64url(HMACSHA256(signingKey, jsonPart))</c>.
    /// The signature covers the first token segment, the base64url JSON.
    /// </summary>
    public static class RemoteTicketFormat
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static string Create(string signingKey, RemoteTicketPayload payload)
        {
            ArgumentException.ThrowIfNullOrEmpty(signingKey);
            ArgumentNullException.ThrowIfNull(payload);
            var jsonPart = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload, Json));
            var signature = Base64Url(Sign(signingKey, jsonPart));
            return jsonPart + "." + signature;
        }

        public static bool TryRead(string? token, string signingKey, DateTimeOffset utcNow, out RemoteTicketPayload? payload, out string? error)
        {
            payload = null;
            error = null;
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(signingKey))
            {
                error = "missing_ticket";
                return false;
            }

            var parts = token.Trim().Split('.');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                error = "malformed_ticket";
                return false;
            }

            byte[] given;
            byte[] body;
            try
            {
                given = FromBase64Url(parts[1]);
                body = FromBase64Url(parts[0]);
            }
            catch (FormatException)
            {
                error = "malformed_ticket";
                return false;
            }

            var expected = Sign(signingKey, parts[0]);
            if (given.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(expected, given))
            {
                error = "invalid_signature";
                return false;
            }

            RemoteTicketPayload? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<RemoteTicketPayload>(body, Json);
            }
            catch (JsonException)
            {
                error = "malformed_ticket";
                return false;
            }

            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Oid))
            {
                error = "malformed_ticket";
                return false;
            }

            if (parsed.Exp <= utcNow.ToUnixTimeSeconds())
            {
                error = "expired";
                return false;
            }

            parsed.Groups ??= [];
            payload = parsed;
            return true;
        }

        public static bool AllowsGroup(RemoteTicketPayload payload, string? group) =>
            !string.IsNullOrEmpty(group) && payload.Groups.Contains(group, StringComparer.Ordinal);

        private static byte[] Sign(string signingKey, string jsonPart) =>
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(jsonPart));

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromBase64Url(string text)
        {
            var padded = text.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
