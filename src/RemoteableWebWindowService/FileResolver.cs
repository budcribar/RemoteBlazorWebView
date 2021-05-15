using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using RemoteableWebWindowService.Services;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PeakSwc.StaticFiles
{
    public class FileInfo : IFileInfo
    {
        private readonly ConcurrentDictionary<string, ServiceState> _rootDictionary;
        private string path;
        private string guid;
        private Stream? stream = null;

        private Stream? GetStream()
        {    
            if (stream == null)
            {
                if (string.IsNullOrEmpty(path)) return null;

                if (string.IsNullOrEmpty(guid) || !_rootDictionary.ContainsKey(guid)) return null;

                var home = _rootDictionary[guid].HtmlHostPath;

                if (string.IsNullOrEmpty(home)) return null;

                var root = Path.GetDirectoryName(home);

                // TODO
                //if (string.IsNullOrEmpty(root)) return null;
                if (string.IsNullOrEmpty(root))
                    root = "wwwroot";

                if (!path.Contains(root))
                    path = root + path;
                if (path.StartsWith('/'))
                    path = path.Substring(1);

                stream = ProcessFile(guid, path);
            }

            return stream;
        } 

        public FileInfo(ConcurrentDictionary<string, ServiceState> rootDictionary, string path)
        {
            try
            {
                guid = path.Split('/')[1];
                this.path = path.Remove(0, guid.Length + 1);
            }
            catch (Exception) { guid = ""; this.path = ""; }

            _rootDictionary = rootDictionary;
        }

        public bool Exists => GetStream() != null;

        public long Length => GetStream()?.Length ?? 0;

        public string? PhysicalPath => null;

        public string Name => Path.GetFileName(path);

        public DateTimeOffset LastModified => DateTime.Now;

        public bool IsDirectory => false;

        private Stream? ProcessFile(string id, string appFile)
        {
            Console.WriteLine($"Attempting to read {appFile}");
            if (!_rootDictionary.ContainsKey(id))
            {
                Console.WriteLine($"Cannot process {appFile} id {id} not found...");
                return null;
            }
                
            _rootDictionary[id].FileDictionary[appFile] = (null, new ManualResetEventSlim());
            _rootDictionary[id].FileCollection.Writer.WriteAsync(appFile);
          
            _rootDictionary[id].FileDictionary[appFile].resetEvent.Wait();
            MemoryStream? stream = _rootDictionary[id].FileDictionary[appFile].stream;
            if (stream == null) return null;

            stream.Position = 0;

            if (Path.GetFileName(appFile) == Path.GetFileName(_rootDictionary[id].HtmlHostPath))
            {

                using StreamReader sr = new StreamReader(stream);
                var contents = sr.ReadToEnd();
               
                contents = contents.Replace("_framework/blazor.webview.js", "remote.blazor.desktop.js");

                contents = Regex.Replace(contents, "<base.*href.*=.*(\"|').*/.*(\"|')", $"<base href=\"/{id}/\"", RegexOptions.Multiline);
                stream = new MemoryStream(Encoding.ASCII.GetBytes(contents));

            }
            Console.WriteLine($"Successfully read {appFile}");
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

        public FileResolver (ConcurrentDictionary<string, ServiceState> rootDictionary) {
           
            _rootDictionary = rootDictionary;
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            throw new NotImplementedException();
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            return new FileInfo(_rootDictionary, subpath);
        }

        public IChangeToken Watch(string filter)
        {
            return NullChangeToken.Singleton;
        }
    }
}
