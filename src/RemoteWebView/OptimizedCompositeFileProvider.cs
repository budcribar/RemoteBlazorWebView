namespace PeakSWC.RemoteWebView;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Composite;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;

public class OptimizedCompositeFileProvider : IFileProvider
{
    private readonly IFileProvider[] _fileProviders;

    public OptimizedCompositeFileProvider(params IFileProvider[]? fileProviders)
    {
        _fileProviders = fileProviders?.Where(p => p != null).ToArray() ?? Array.Empty<IFileProvider>();
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        if (string.IsNullOrWhiteSpace(subpath))
            throw new ArgumentException("Subpath cannot be null or empty.", nameof(subpath));

        var normalizedSubpath = subpath.TrimStart('/', '\\');
        bool isUnderscorePath = normalizedSubpath.StartsWith("_framework", StringComparison.OrdinalIgnoreCase);

        // First pass: prioritize embedded files if the path starts with an underscore
        if (isUnderscorePath)
        {
            foreach (var provider in _fileProviders.Where(p => p is EmbeddedFileProvider or ManifestEmbeddedFileProvider))
            {
                var fileInfo = provider.GetFileInfo(normalizedSubpath);
                if (fileInfo.Exists)
                {
                    return fileInfo;
                }
            }
        }

        // Second pass: check all other providers (including embedded if not handled earlier)
        foreach (var provider in _fileProviders)
        {
            // Skip previously checked embedded providers for underscore paths
            if (isUnderscorePath && provider is EmbeddedFileProvider or ManifestEmbeddedFileProvider)
            {
                continue;
            }

            var fileInfo = provider.GetFileInfo(subpath);
            if (!fileInfo.Exists && !ReferenceEquals(normalizedSubpath, subpath))
            {
                fileInfo = provider.GetFileInfo(normalizedSubpath);
            }
            if (fileInfo.Exists)
            {
                return fileInfo;
            }
        }

        // Return a NotFoundFileInfo if no match is found
        return new NotFoundFileInfo(subpath);
    }
    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        if (subpath == null)
            return NotFoundDirectoryContents.Singleton;

        var directoryContents = new CompositeDirectoryContents(_fileProviders, subpath);
        return directoryContents;
    }
    public IChangeToken Watch(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return NullChangeToken.Singleton;

        // Watch all file providers
        var changeTokens = new List<IChangeToken>();
        foreach (IFileProvider fileProvider in _fileProviders)
        {
            IChangeToken changeToken = fileProvider.Watch(pattern);
            if (changeToken != null)
            {
                changeTokens.Add(changeToken);
            }
        }

        // There is no change token with active change callbacks
        if (changeTokens.Count == 0)
        {
            return NullChangeToken.Singleton;
        }

        return new CompositeChangeToken(changeTokens);
    }
  
}
