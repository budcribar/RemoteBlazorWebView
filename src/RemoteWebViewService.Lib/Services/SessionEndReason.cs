namespace PeakSWC.RemoteWebView.Services
{
    /// <summary>Why the relay tore a session down. Logged with every shutdown so a "Connection lost" on the app can be traced.</summary>
    public enum SessionEndReason
    {
        /// <summary>The app asked for it (its Shutdown call, e.g. the app is closing).</summary>
        RequestedByApp,
        /// <summary>The app missed a ping reply within the ping interval.</summary>
        PingTimeout,
        /// <summary>The app's ping stream was cancelled (its connection to the relay closed or dropped).</summary>
        PingStreamClosed,
        /// <summary>The app's ping stream failed with an error.</summary>
        PingStreamFailed,
        /// <summary>The app's file-read stream ended (its connection to the relay closed or dropped).</summary>
        FileReadStreamEnded,
        /// <summary>CreateWebView failed after the session had started.</summary>
        CreateWebViewFailed,
        /// <summary>The browser connection in control closed and no owner connection was attached to take control back.</summary>
        ControllerLeft,
        /// <summary>The browser connection in control failed with an error.</summary>
        ControllerFailed,
        /// <summary>A browser asked for a restart and the app didn't shut down within the grace period.</summary>
        RestartNotHonoured,
        /// <summary>A page refresh asked for a restart and the app didn't respond in time.</summary>
        RefreshRestartTimedOut,
    }
}
