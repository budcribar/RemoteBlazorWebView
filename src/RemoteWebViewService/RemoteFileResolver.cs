using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using PeakSWC.RemoteWebView;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSwc.StaticFiles
{
    internal class FileInfo : IFileInfo
    {
        private readonly ConcurrentDictionary<string, ServiceState> _rootDictionary;
        private string path;
        private readonly string guid;
        private Stream? stream = null;
        private long length = -1;
        private readonly ILogger<RemoteFileResolver> _logger;

        private async Task<Stream?> GetStream()
        {
            if (stream == null)
            {
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(guid)) return null;

                if (!_rootDictionary.TryGetValue(guid, out ServiceState? serviceState)) return null;

                var home = serviceState.HtmlHostPath;

                if (string.IsNullOrEmpty(home)) return null;

                var root = Path.GetDirectoryName(home);

                if (string.IsNullOrEmpty(root))
                    root = "wwwroot";

                if (!path.Contains(root))
                    path = root + path;
                if (path.StartsWith('/'))
                    path = path[1..];

                stream = await ProcessFile(guid, path);
            }

            return stream;
        }

        private async Task<Stream?> ProcessFile(string id, string appFile)
        {
            Stopwatch stopWatch = new ();
            stopWatch.Start();

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation($"Attempting to read {appFile}");

            if (!_rootDictionary.TryGetValue(id, out ServiceState? serviceState))
            {
                _logger.LogError($"Cannot process {appFile} id {id} not found...");
                return null;
            }
            Stream stream;

            if (Path.GetFileName(appFile) == "blazor.modules.json")
            {
                stream = new MemoryStream(Encoding.ASCII.GetBytes("[]"));
                length = stream.Length;
            }
            else
            {             
                serviceState.FileDictionary.TryAdd(appFile, new FileEntry());

                FileEntry fileEntry = serviceState.FileDictionary[appFile];
                lock(fileEntry)
                {
                    serviceState.FileCollection.Writer.WriteAsync(appFile);

                    if (!fileEntry.ResetEvent.Wait(TimeSpan.FromSeconds(60)))
                    {
                        _logger.LogError($"Timeout processing {appFile} id {id}");
                        return null;
                    }

                    length = fileEntry.Length;
                    if (length <= 0)
                    {
                        _logger.LogError($"Cannot process {appFile} id {id} stream not found...");
                        return null;
                    }

                    stream = fileEntry.Pipe.Reader.AsStream();

                    if (Path.GetFileName(appFile) == Path.GetFileName(serviceState.HtmlHostPath))
                    {
                        // Edit the href in index.html
                        using StreamReader sr = new(stream);
                        var contents = sr.ReadToEnd();
                        var initialLength = contents.Length;
                        contents = Regex.Replace(contents, "<base.*href.*=.*(\"|').*/.*(\"|')", $"<base href=\"/{id}/\"", RegexOptions.Multiline);
                        if (contents.Length == initialLength) _logger.LogError("Unable to find base.href in the home page");
                        stream.Dispose();
                        stream = new MemoryStream(Encoding.ASCII.GetBytes(contents));
                        length = stream.Length;
                    }
                    
                    fileEntry.Reset();
                }


                
            }

            TimeSpan fileReadTime = stopWatch.Elapsed;

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation($"Successfully read {length} bytes from {appFile} id {id}");
                _logger.LogInformation($"Last file read in {fileReadTime} id {id}");
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

        public FileInfo(ConcurrentDictionary<string, ServiceState> rootDictionary, string path, ILogger<RemoteFileResolver> logger)
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
        }

        public bool Exists =>  
            GetStream().Result != null;

        public long Length => 
            GetStream().Result == null ? -1 : length;

        public string? PhysicalPath => null;

        public string Name => Path.GetFileName(path);

        public DateTimeOffset LastModified => DateTime.Now;

        public bool IsDirectory => false;

        public Stream? CreateReadStream()
        {
            return GetStream().Result;
        }
    }

    public class RemoteFileResolver : IFileProvider
    {
        private readonly ConcurrentDictionary<string, ServiceState> _rootDictionary;
        private readonly ILogger<RemoteFileResolver> _logger;

        public RemoteFileResolver(ConcurrentDictionary<string, ServiceState> rootDictionary, ILogger<RemoteFileResolver> logger)
        {
            _rootDictionary = rootDictionary;
            _logger = logger;
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            _logger.LogError("Directory contents not supported");
            return new NotFoundDirectoryContents();
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            return new FileInfo(_rootDictionary, subpath, _logger);
        }

        public IChangeToken Watch(string _)
        {
            return NullChangeToken.Singleton;
        }
    }
}
