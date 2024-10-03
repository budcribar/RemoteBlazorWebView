﻿using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using PeakSwc.StaticFiles;
using PeakSWC.RemoteWebView;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using IFileProvider = PeakSwc.StaticFiles.IFileProvider;

namespace PeakSWC.RemoteWebView.RemoteStaticFiles.Old
{
    public class RemoteFileResolver(ConcurrentDictionary<string, ServiceState> rootDictionary, ILogger<RemoteFileResolver> logger) : IFileProvider
    {
        public Task<bool> FileStreamExistsAsync(string subpath)
        {
            return Task.FromResult(true);
        }

        //public IDirectoryContents GetDirectoryContents(string subpath)
        //{
        //    logger.LogError("Directory contents not supported");
        //    return new NotFoundDirectoryContents();
        //}

        public Task<IFileInfo?> GetFileInfo(string subpath)
        {
            return FileInfo.CreateFileInfo(rootDictionary, subpath, logger);

        }

        //public IChangeToken Watch(string _)
        //{
        //    return NullChangeToken.Singleton;
        //}
    }
}