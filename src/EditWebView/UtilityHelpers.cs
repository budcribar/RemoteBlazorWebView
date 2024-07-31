using System.Text.RegularExpressions;

internal static partial class UtilityHelpers
{

    [GeneratedRegex(@"/(?:v)?([\d.-]+(?:-[\w.]+)?)\.zip$")]
    public static partial Regex MyRegex();
}