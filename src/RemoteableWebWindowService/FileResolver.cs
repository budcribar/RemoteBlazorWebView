using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using RemoteableWebWindowService.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PeakSwc.StaticFiles
{
    // TODO Send files in chunks
    public class FileInfo : IFileInfo
    {
        private readonly ConcurrentDictionary<string, ServiceState> _rootDictionary;
        private string path;
        private readonly string guid;
        private Stream? stream = null;
        private readonly ILogger<FileResolver> _logger;

        private Stream? GetStream()
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

                stream = ProcessFile(guid, path);
            }

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
            catch (Exception) {
                _logger.LogError($"Illegal File path '{path}'");
                guid = ""; 
                this.path = ""; 
            }

            _rootDictionary = rootDictionary;
        }
        
        public bool Exists => GetStream() != null;

        public long Length => GetStream()?.Length ?? -1;

        public string? PhysicalPath => null;

        public string Name => Path.GetFileName(path);

        public DateTimeOffset LastModified => DateTime.Now;

        public bool IsDirectory => false;

        private Stream? ProcessFile(string id, string appFile)
        {
            _logger.LogInformation($"Attempting to read {appFile}");
          
            if (!_rootDictionary.ContainsKey(id))
            {
                _logger.LogError($"Cannot process {appFile} id {id} not found...");
                return null;
            }
                
            _rootDictionary[id].FileDictionary[appFile] = (null, new ManualResetEventSlim());
            _rootDictionary[id].FileCollection.Writer.WriteAsync(appFile);
          
            _rootDictionary[id].FileDictionary[appFile].resetEvent.Wait();
            MemoryStream? stream = _rootDictionary[id].FileDictionary[appFile].stream;
            if (stream == null || stream.Length == 0)
            {
                _logger.LogError($"Cannot process {appFile} id {id} stream not found...");
                return null;
            }

            stream.Position = 0;

            if (Path.GetFileName(appFile) == Path.GetFileName(_rootDictionary[id].HtmlHostPath))
            {
                using StreamReader sr = new(stream);
                var contents = sr.ReadToEnd();
                var initialLength = contents.Length;
                contents = contents.Replace("_framework/blazor.webview.js", "remote.blazor.desktop.js");
                if (contents.Length == initialLength) _logger.LogError("Unable to find blazor javacript reference in the home page");
                initialLength = contents.Length;
                contents = Regex.Replace(contents, "<base.*href.*=.*(\"|').*/.*(\"|')", $"<base href=\"/{id}/\"", RegexOptions.Multiline);
                if (contents.Length == initialLength) _logger.LogError("Unable to find base.href in the home page");
                stream = new MemoryStream(Encoding.ASCII.GetBytes(contents));
            }
            _logger.LogInformation($"Successfully read {appFile}");
           
            return stream;
        }

        public Stream? CreateReadStream()
        {
            return GetStream();
        }
    }

    public class FileResolver : IFileProvider
    {      
        private readonly ConcurrentDictionary<string, ServiceState> _rootDictionary;
        private readonly ILogger<FileResolver> _logger;

        public FileResolver (ConcurrentDictionary<string, ServiceState> rootDictionary, ILogger<FileResolver> logger) {
           
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

        public IChangeToken Watch(string filter)
        {
            return NullChangeToken.Singleton;
        }
    }
}
