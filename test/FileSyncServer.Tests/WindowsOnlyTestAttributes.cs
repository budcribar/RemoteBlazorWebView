namespace FileSyncServer.Tests;

/// <summary>
/// Skips on non-Windows hosts. These tests start win-x64 executables or a WPF/WinForms window.
/// The published client can be built on Linux; it cannot be executed there.
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public const string Reason = "Requires Windows. This test starts a win-x64 executable or a WPF/WinForms window.";

    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = Reason;
    }
}

/// <summary>
/// Skips on non-Windows hosts. See <see cref="WindowsOnlyFactAttribute"/>.
/// </summary>
public sealed class WindowsOnlyTheoryAttribute : TheoryAttribute
{
    public WindowsOnlyTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = WindowsOnlyFactAttribute.Reason;
    }
}
