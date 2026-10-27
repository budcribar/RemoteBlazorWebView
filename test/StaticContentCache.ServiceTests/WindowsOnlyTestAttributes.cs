namespace StaticContentCache.Tests;

/// <summary>
/// Skips on non-Windows hosts. Release <c>Startup.cs</c> registers <c>AddEventLog</c> under <c>#if !DEBUG</c>,
/// and Linux has no Windows Event Log.
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public const string Reason =
        "Requires Windows. Release Startup.cs compiles AddEventLog under #if !DEBUG, and Linux has no Windows Event Log.";

    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = Reason;
    }
}
