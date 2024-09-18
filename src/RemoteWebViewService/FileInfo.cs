using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeakSwc.StaticFiles
{
    public partial class FileInfo : IFileInfo
    {
        private readonly ConcurrentDictionary<string, ServiceState> _rootDictionary = default!;
        private string path = string.Empty;
        private readonly string guid = string.Empty;
        private Stream? stream = null;
        private long length = -1;
        private readonly ILogger<RemoteFileResolver> _logger = default!;
        private readonly Task<Stream> getStreamTask = default!;

        private FileInfo() { }

        private async Task<Stream> GetStreamAsync()
        {
            if (stream != null)
            {
                throw new InvalidOperationException("Stream not null");
            }
            if (stream == null)
            {
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(guid)) return new MemoryStream();

                if (!_rootDictionary.TryGetValue(guid, out ServiceState? serviceState)) return new MemoryStream(); 

                var home = serviceState.HtmlHostPath;

                if (string.IsNullOrEmpty(home)) return new MemoryStream(); 

                var root = Path.GetDirectoryName(home);

                if (string.IsNullOrEmpty(root))
                    root = "wwwroot";

                if (!path.Contains(root))
                    path = root + path;
                if (path.StartsWith('/'))
                    path = path[1..];

                stream = await ProcessFile(serviceState, guid, path).ConfigureAwait(false);

                if(stream == null) stream = new MemoryStream();
            }

            return stream;
        }

        private async Task<Stream> ProcessFile(ServiceState serviceState, string id, string appFile)
        {
            Stopwatch stopWatch = new ();
            stopWatch.Start();

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogDebug($"Attempting to read {appFile}");

            Stream stream;

            if (Path.GetFileName(appFile) == "blazor.modules.json")
            {
                stream = new MemoryStream(Encoding.ASCII.GetBytes("[]"));
                length = stream.Length;
            }
            else
            {
                FileEntry fileEntry = new FileEntry { Path = appFile };
               
                if (serviceState.FileDictionary.TryGetValue(appFile, out ConcurrentList<FileEntry>? fileEntryList))
                {
                    // add the duplicate file
                    if (fileEntryList == null)
                    {
                        _logger.LogError($"Could not find FileEntryList {appFile} id {id}");
                        return new MemoryStream();
                    }

                    fileEntry.Instance = fileEntryList.Add(fileEntry);
                } else
                {
                    fileEntryList = new();
                    fileEntryList.Add(fileEntry);

                    if (!serviceState.FileDictionary.TryAdd(appFile, fileEntryList))
                    {                       
                        _logger.LogError($"Unable to insert {appFile} id {id} to dictionary");
                        return new MemoryStream();                      
                    }
                }
               
                await serviceState.FileCollection.Writer.WriteAsync(fileEntry).ConfigureAwait(false);

                bool timedOut = false;
                try
                {
                    timedOut = !fileEntry.Semaphore.Wait(TimeSpan.FromSeconds(60), serviceState.Token);
                }
                catch (Exception)
                {
                    timedOut = true;
                }

                if (timedOut)
                {
                    _logger.LogError($"Timeout processing {appFile} id {id}");
                    return new MemoryStream();
                }

                length = fileEntry.Length;
                if (length <= 0)
                {
                    _logger.LogError($"Cannot process {appFile} id {id} stream not found...");
                    return new MemoryStream();
                }

                stream = fileEntry.Pipe.Reader.AsStream();

                if (Path.GetFileName(appFile) == Path.GetFileName(serviceState.HtmlHostPath))
                {
                    // Edit the href in index.html
                    using StreamReader sr = new(stream);
                    var contents = await sr.ReadToEndAsync().ConfigureAwait(false);
                    var initialLength = contents.Length;
                    contents = HrefRegEx().Replace(contents, $"<base href=\"/{id}/\"");
                    if (contents.Length == initialLength) _logger.LogError("Unable to find base.href in the home page");
                    stream.Dispose();
                    stream = new MemoryStream(Encoding.ASCII.GetBytes(contents));
                    length = stream.Length;
                }
            }

            TimeSpan fileReadTime = stopWatch.Elapsed;

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogDebug($"Successfully read {length} bytes from {appFile} id {id}");
                _logger.LogDebug($"Last file read in {fileReadTime} id {id}");
            }
                
            lock (serviceState)
            {
                serviceState.TotalFilesRead++;
                serviceState.TotalBytesRead += length;
                serviceState.TotalFileReadTime += fileReadTime;
                if (fileReadTime > serviceState.MaxFileReadTime)
                    serviceState.MaxFileReadTime = fileReadTime;
            }

            return stream;
        }

        public static async Task<IFileInfo> CreateFileInfo(ConcurrentDictionary<string, ServiceState> rootDictionary, string path, ILogger<RemoteFileResolver> logger)
        {
            var fi = new FileInfo(rootDictionary,path,logger);
            var g = fi.guid;

            await fi.getStreamTask.ConfigureAwait(false);

            return fi;
        }
        private FileInfo(ConcurrentDictionary<string, ServiceState> rootDictionary, string path, ILogger<RemoteFileResolver> logger)
        {
            _logger = logger;

            try
            {
                guid = path.Split('/')[1];
                this.path = path.Remove(0, guid.Length + 1);
            }
            catch (Exception)
            {
                _logger.LogError($"Illegal File path '{path}'");
                guid = string.Empty;
                this.path = string.Empty;
            }

            _rootDictionary = rootDictionary;
            getStreamTask = GetStreamAsync();
        }

        public bool Exists => stream != null;

        public long Length => stream == null ? -1 : length;

        public string? PhysicalPath => null;

        public string Name => Path.GetFileName(path);

        public DateTimeOffset LastModified => DateTime.UtcNow;

        public bool IsDirectory => false;

        public Stream CreateReadStream()
        {
            return stream ?? new MemoryStream();
        }

        [GeneratedRegex("<base.*href.*=.*(\"|').*/.*(\"|')", RegexOptions.Multiline)]
        private static partial Regex HrefRegEx();
    }
}
