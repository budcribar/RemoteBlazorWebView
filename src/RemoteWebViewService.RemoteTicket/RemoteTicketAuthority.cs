using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PeakSWC.RemoteWebView
{
    public sealed class RemoteTicketGroupInfo
    {
        [JsonPropertyName("group")]
        public string Group { get; set; } = "";

        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("planTier")]
        public string PlanTier { get; set; } = "";

        [JsonPropertyName("maxConcurrentSessions")]
        public int MaxConcurrentSessions { get; set; }
    }

    public sealed class RemoteTicketAuthorizeResult
    {
        [JsonPropertyName("accepted")]
        public bool Accepted { get; set; }

        [JsonPropertyName("group")]
        public string Group { get; set; } = "";

        [JsonPropertyName("denialReason")]
        public string? DenialReason { get; set; }
    }

    /// <summary>A test can register this handler; otherwise the relay uses the default sockets handler.</summary>
    public abstract class RemoteTicketAuthorityHandler : HttpMessageHandler
    {
    }

    public sealed class RemoteTicketAuthority(HttpClient http, string secret)
    {
        public const string SecretHeader = "X-Relay-Secret";

        public async Task<RemoteTicketGroupLookup> GetGroupAsync(string group, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "api/relay/groups/" + Uri.EscapeDataString(group));
                request.Headers.TryAddWithoutValidation(SecretHeader, secret);
                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return RemoteTicketGroupLookup.Unknown();
                if (!response.IsSuccessStatusCode)
                    return RemoteTicketGroupLookup.Unreachable();
                var body = await response.Content.ReadFromJsonAsync<RemoteTicketGroupInfo>(cancellationToken).ConfigureAwait(false);
                return body == null ? RemoteTicketGroupLookup.Unreachable() : RemoteTicketGroupLookup.Found(body);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                return RemoteTicketGroupLookup.Unreachable();
            }
        }

        public async Task<RemoteTicketAuthorizeResult> AuthorizeAsync(string? enrollmentKey, string? ticket, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/relay/agents/authorize")
                {
                    Content = JsonContent.Create(new AuthorizeRequest(enrollmentKey, ticket)),
                };
                request.Headers.TryAddWithoutValidation(SecretHeader, secret);
                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return Denied("The relay authority could not be reached.");
                var body = await response.Content.ReadFromJsonAsync<RemoteTicketAuthorizeResult>(cancellationToken).ConfigureAwait(false);
                return body ?? Denied("The relay authority could not be reached.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                return Denied("The relay authority could not be reached.");
            }
        }

        private static RemoteTicketAuthorizeResult Denied(string reason) =>
            new() { Accepted = false, DenialReason = reason };

        private sealed record AuthorizeRequest(string? EnrollmentKey, string? Ticket);
    }

    public readonly record struct RemoteTicketGroupLookup(bool Reachable, RemoteTicketGroupInfo? Info)
    {
        public static RemoteTicketGroupLookup Found(RemoteTicketGroupInfo info) => new(true, info);
        public static RemoteTicketGroupLookup Unknown() => new(true, null);
        public static RemoteTicketGroupLookup Unreachable() => new(false, null);
    }
}
