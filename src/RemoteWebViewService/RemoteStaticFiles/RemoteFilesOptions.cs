namespace PeakSWC.RemoteWebView
{
    public class RemoteFilesOptions
    {
        /// <summary>
        /// The root directory from which files are served.
        /// </summary>
        public string RootDirectory { get; set; } = "client_cache";

        /// <summary>
        /// Determines whether the server should cache files.
        /// If false, the server retrieves files directly from the file system on each request.
        /// </summary>
        public bool UseServerCache { get; set; } = false;

        /// <summary>
        /// Determines whether the server should utilize client-side caching using ETags.
        /// If true, the server checks the client's ETag and avoids sending the file if it's unchanged.
        /// </summary>
        public bool UseClientCache { get; set; } = false;
    }
}
