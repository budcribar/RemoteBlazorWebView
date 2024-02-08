using System.Drawing;
using System.Text.Json.Serialization;

namespace PeakSWC.RemoteWebView
{
    [JsonSerializable(typeof(StatusResponse))]
    [JsonSerializable(typeof(GrpcBaseUriResponse))]   
    [JsonSerializable(typeof(Point))]
    [JsonSerializable(typeof(Size))]
    public partial class JsonContext : JsonSerializerContext
    {
    }
}
