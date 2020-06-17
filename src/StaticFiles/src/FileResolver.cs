using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using RemoteableWebWindowService.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PeakSwc.StaticFiles
{
    public class FileInfo : IFileInfo
    {
       
        private readonly ConcurrentDictionary<Guid, ServiceState> _rootDictionary;
        private string path;
        private readonly HttpContext context;
        Stream stream = null;

        private Stream GetStream()
        {
            if (stream == null)
            {
                if (string.IsNullOrEmpty(path)) return null;

                var guid = context.Request.Cookies["guid"];
                var home = context.Request.Cookies["home"];

                if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(home)) return null;

                var root = Path.GetDirectoryName(home);

                // TODO do we need this?
                if (File.Exists(path))
                    try
                    {
                        return File.Open(path, FileMode.Open, FileAccess.Read);
                    }
                    catch { return null; }
                    

                if (!path.Contains(root))
                    path = root + path;
                if (path.StartsWith('/'))
                    path = path.Substring(1);

                stream = ProcessFile(Guid.Parse(guid), path);
            }

            return stream;
        } 

        public FileInfo(HttpContext context, string path, ConcurrentDictionary<Guid, ServiceState> rootDictionary)
        {
            this.path = path;
            
            _rootDictionary = rootDictionary;
            this.context = context;
        }

        public bool Exists => GetStream() != null;

        public long Length => GetStream().Length;

        public string PhysicalPath => null;

        public string Name => Path.GetFileName(path);

        public DateTimeOffset LastModified => DateTime.Now;

        public bool IsDirectory => false;

        private Stream ProcessFile(Guid id, string appFile)
        {
            if (!_rootDictionary.ContainsKey(id))
                return null;

            _rootDictionary[id].FileDictionary[appFile] = (null, new ManualResetEventSlim());

            _rootDictionary[id].FileCollection.Writer.WriteAsync(appFile);
          

            _rootDictionary[id].FileDictionary[appFile].mres.Wait();
            var stream = _rootDictionary[id].FileDictionary[appFile].stream;
            stream.Position = 0;


            // TODO use the file specified in the dictionary
            if (appFile.EndsWith("index.html"))
            {

                using StreamReader sr = new StreamReader(stream);
                var contents = sr.ReadToEnd();
                //contents = contents.Replace("framework://blazor.desktop.js", "_framework/blazor.server.js");
                contents = contents.Replace("framework://blazor.desktop.js", "remote/remote.blazor.desktop.js");


                string inject = @$"
</app>
  <script type = 'text/javascript'>
       var webWindow = new Object();
       webWindow.guid = '{id}';
   </script>
";
                contents = contents.Replace("</app>", inject);

                stream = new MemoryStream(Encoding.ASCII.GetBytes(contents));

            }
            return stream;
        }


        public Stream CreateReadStream()
        {
            return GetStream();
        }
    }

    public class FileResolver : IFileProvider
    {
        //private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, (MemoryStream stream, ManualResetEventSlim mres)>> _fileDictionary;
        //private readonly ConcurrentDictionary<Guid,BlockingCollection<(Guid, string)>> _fileCollection;
        private readonly ConcurrentDictionary<Guid, ServiceState> _rootDictionary;

        public FileResolver (ConcurrentDictionary<Guid, ServiceState> rootDictionary) {
           
            _rootDictionary = rootDictionary;
        }

        public HttpContext Context { get; set; }


        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            throw new NotImplementedException();
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            return new FileInfo(Context, subpath, _rootDictionary);
        }

        public IChangeToken Watch(string filter)
        {
            return NullChangeToken.Singleton;
        }
    }
}
