using System.Drawing;
using System.Text.Json.Serialization;

namespace PeakSWC.RemoteWebView
{
    [JsonSerializable(typeof(StatusResponse))]
    [JsonSerializable(typeof(GrpcBaseUriResponse))]   
    [JsonSerializable(typeof(Point))]
    [JsonSerializable(typeof(Size))]
    [JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, string>))]
    public partial class JsonContext : JsonSerializerContext
    {
    }
}
