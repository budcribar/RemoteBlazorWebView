namespace PeakSWC.RemoteWebView;

/// <summary>Supplies a registration-scoped key for an optional local voice endpoint.</summary>
public interface IVoiceHost
{
    string VoiceHostKey { get; }
}
