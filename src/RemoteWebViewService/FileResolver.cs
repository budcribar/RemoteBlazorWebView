using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using PeakSWC.RemoteWebView;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSwc.StaticFiles
{
    public class FileInfo : IFileInfo
    {
        private static TimeSpan total = TimeSpan.FromSeconds(0);
        private readonly ConcurrentDictionary<string, ServiceState> _rootDictionary;
        private string path;
        private readonly string guid;
        private Stream? stream = null;
        private readonly ILogger<FileResolver> _logger;

        private async Task<Stream?> GetStream()
        {
            if (stream == null)
            {
                if (string.IsNullOrEmpty(path)) return null;

                if (string.IsNullOrEmpty(guid) || !_rootDictionary.ContainsKey(guid)) return null;

                var home = _rootDictionary[guid].HtmlHostPath;

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
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            _logger.LogInformation($"Attempting to read {appFile}");

            if (!_rootDictionary.ContainsKey(id))
            {
                _logger.LogError($"Cannot process {appFile} id {id} not found...");
                return null;
            }

            _rootDictionary[id].FileDictionary[appFile] = (new MemoryStream(), new ManualResetEventSlim());
            await _rootDictionary[id].FileCollection.Writer.WriteAsync(appFile);
            _rootDictionary[id].FileDictionary[appFile].resetEvent.Wait();
            MemoryStream stream = _rootDictionary[id].FileDictionary[appFile].stream;
            if (stream.Length == 0)
            {
                _logger.LogError($"Cannot process {appFile} id {id} stream not found...");
                return null;
            }

            stream.Position = 0;

            if (Path.GetFileName(appFile) == "blazor.modules.json")
			{
                stream = new MemoryStream(Encoding.ASCII.GetBytes("[]"));
            }

            if (Path.GetFileName(appFile) == Path.GetFileName(_rootDictionary[id].HtmlHostPath))
            {
                using StreamReader sr = new(stream);
                var contents = sr.ReadToEnd();
                var initialLength = contents.Length;
				contents = Regex.Replace(contents, "<base.*href.*=.*(\"|').*/.*(\"|')", $"<base href=\"/{id}/\"", RegexOptions.Multiline);
                if (contents.Length == initialLength) _logger.LogError("Unable to find base.href in the home page");
                stream = new MemoryStream(Encoding.ASCII.GetBytes(contents));
            }
            total += stopWatch.Elapsed;
            _logger.LogInformation($"Successfully read {stream.Length} bytes from {appFile} in {stopWatch.Elapsed.TotalSeconds}");
            _logger.LogInformation($"Total file read time  {total.TotalSeconds}");
            return stream;
        }

        public FileInfo(ConcurrentDictionary<string, ServiceState> rootDictionary, string path, ILogger<FileResolver> logger)
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

        public bool Exists => GetStream().Result != null;

        public long Length => GetStream().Result?.Length ?? -1;

        public string? PhysicalPath => null;

        public string Name => Path.GetFileName(path);

        public DateTimeOffset LastModified => DateTime.Now;

        public bool IsDirectory => false;

        public Stream? CreateReadStream()
        {
            return GetStream().Result;
        }
    }

    public class FileResolver : IFileProvider
    {
        private readonly ConcurrentDictionary<string, ServiceState> _rootDictionary;
        private readonly ILogger<FileResolver> _logger;

        public FileResolver(ConcurrentDictionary<string, ServiceState> rootDictionary, ILogger<FileResolver> logger)
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
