// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace PeakSWC.RemoteWebView
{
    public class StaticWebAssetsLoader
    {
        public static IFileProvider UseStaticWebAssets(IFileProvider systemProvider)
        {
            using var manifest = GetManifestStream();
            if (manifest == null)
                return systemProvider;
            else       
                return UseStaticWebAssetsCore(systemProvider, manifest);
           
        }
        private static Stream? GetManifestStream()
        {
            try
            {
                var filePath = ResolveRelativeToAssembly();

                if (filePath != null && File.Exists(filePath))
                {
                    return File.OpenRead(filePath);
                }
                else
                {
                    // A missing manifest might simply mean that the feature is not enabled, so we simply
                    // return early. Misconfigurations will be uncommon given that the entire process is automated
                    // at build time.
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static IFileProvider UseStaticWebAssetsCore(IFileProvider systemProvider, Stream manifest)
        {
            var staticWebAssetManifest = ManifestStaticWebAssetFileProvider.StaticWebAssetManifest.Parse(manifest);

            if (staticWebAssetManifest == null) return systemProvider;

            var provider = new ManifestStaticWebAssetFileProvider(
                  staticWebAssetManifest,
                  (contentRoot) => new PhysicalFileProvider(contentRoot));

            return new CompositeFileProvider(new[] { provider, systemProvider });
        }

        private static string? ResolveRelativeToAssembly()
        {
            var assembly = Assembly.GetEntryAssembly();
            var baseDirectory = AppContext.BaseDirectory;

            if (assembly == null || string.IsNullOrEmpty(baseDirectory))
            {
                return null;
            }

            var name = assembly.GetName().Name;

            return Path.Combine(baseDirectory, $"{name}.staticwebassets.runtime.json");
        }
       
    }
}
#nullable restore
