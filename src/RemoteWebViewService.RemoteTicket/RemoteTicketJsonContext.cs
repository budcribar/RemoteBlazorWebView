using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Source-generated JSON for the ticket payload and the relay authority calls, so the relay can be compiled with
    /// Native AOT. Web defaults (camelCase, case-insensitive reads) match what JsonContent.Create and ReadFromJsonAsync
    /// used before.
    /// </summary>
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(RemoteTicketPayload))]
    [JsonSerializable(typeof(RemoteTicketGroupInfo))]
    [JsonSerializable(typeof(RemoteTicketAuthorizeResult))]
    [JsonSerializable(typeof(RemoteTicketAuthority.AuthorizeRequest))]
    internal sealed partial class RemoteTicketJsonContext : JsonSerializerContext
    {
    }
}
