namespace PeakSWC.RemoteWebView
{
    public class Program
    {
        public static void Main(string[] args) => RemoteWebViewServer.Run(args);

        // Kept for callers of the old Program API; the implementation lives in PeakSWC.RemoteWebViewService.Lib.
        public static bool IsContainerMode => RemoteWebViewServer.IsContainerMode;
    }
}
