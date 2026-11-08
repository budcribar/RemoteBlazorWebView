namespace PeakSWC.RemoteBlazorWebView.RemoteComponents;

/// <summary>
/// Marks an interface as a remote component contract. The source generator that ships with this package writes two classes next to it:
/// <list type="bullet">
/// <item><c>{Name}Frame</c> (the app): a component with a [Parameter] per get-only property and an EventCallback per method; it frames
/// the component and sends the values whenever they change.</item>
/// <item><c>{Name}Receiver</c> (the component's process): implements the interface with the values the app sent; calling a method sends
/// that callback to the app. Register it with <c>services.AddRemoteComponentReceiver&lt;{Name}Receiver&gt;(connection)</c>.</item>
/// </list>
/// {Name} is the interface name without its leading I. Properties must be get-only and JSON-serializable; methods must return Task
/// and take at most one (JSON-serializable) argument. Put the contract in a library both sides reference, not in the project whose
/// .razor files use the frame: Razor compiles before source generators run, so it can't see the generated frame in the same project.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class RemoteComponentContractAttribute : Attribute
{
    /// <summary>The frame component's name (default {Name}Frame).</summary>
    public string? FrameName { get; set; }

    /// <summary>The receiver's name (default {Name}Receiver).</summary>
    public string? ReceiverName { get; set; }
}
