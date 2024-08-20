using System.Text.RegularExpressions;
namespace EditWebView
{
    public static partial class UtilityHelpers
    {

        [GeneratedRegex(@"/(?:v)?([\d.-]+(?:-[\w.]+)?)\.zip$")]
        public static partial Regex MyRegex();
    }
}