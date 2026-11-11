namespace PeakSWC.RemoteWebView;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Composite;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class OptimizedCompositeFileProvider : IFileProvider
{
    private const string BlazorModulesManifestPath = "_framework/blazor.modules.json";
    private static readonly FieldInfo? EmbeddedAssemblyField =
        typeof(EmbeddedFileProvider).GetField("_assembly", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly IFileProvider[] _fileProviders;

    public OptimizedCompositeFileProvider(params IFileProvider[]? fileProviders)
    {
        _fileProviders = fileProviders?.Where(p => p != null).ToArray() ?? Array.Empty<IFileProvider>();
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        if (string.IsNullOrWhiteSpace(subpath))
            throw new ArgumentException("Subpath cannot be null or empty.", nameof(subpath));

        var trimmed = subpath.Replace('\\', '/').TrimStart('/');
        if (trimmed.Length == 0)
            return new NotFoundFileInfo(subpath);

        // RemoteWebView embeds an empty [] fallback at _framework/blazor.modules.json.
        // Check application and static-web-asset providers first so an app's own module
        // manifest is not shadowed, falling back to RemoteWebView's embedded [] only when absent.
        if (string.Equals(trimmed, BlazorModulesManifestPath, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var provider in _fileProviders)
            {
                if (IsRemoteWebViewEmbeddedProvider(provider))
                    continue;

                var fileInfo = GetProviderFileInfo(provider, trimmed, BlazorModulesManifestPath);
                if (fileInfo.Exists)
                    return fileInfo;
            }

            foreach (var provider in _fileProviders)
            {
                if (!IsRemoteWebViewEmbeddedProvider(provider))
                    continue;

                var fileInfo = GetProviderFileInfo(provider, trimmed, BlazorModulesManifestPath);
                if (fileInfo.Exists)
                    return fileInfo;
            }

            return new NotFoundFileInfo(subpath);
        }

        bool isUnderscorePath = trimmed.StartsWith("_framework", StringComparison.OrdinalIgnoreCase);
        string canonicalTrimmed = isUnderscorePath && !trimmed.StartsWith("_framework", StringComparison.Ordinal)
            ? "_framework" + trimmed["_framework".Length..]
            : trimmed;

        // First pass: prioritize embedded files if the path starts with an underscore
        if (isUnderscorePath)
        {
            foreach (var provider in _fileProviders)
            {
                if (!IsRemoteWebViewEmbeddedProvider(provider))
                    continue;

                var fileInfo = GetProviderFileInfo(provider, trimmed, canonicalTrimmed);
                if (fileInfo.Exists)
                {
                    return fileInfo;
                }
            }

            foreach (var provider in _fileProviders.OfType<EmbeddedFileProvider>())
            {
                if (IsRemoteWebViewEmbeddedProvider(provider))
                    continue;

                var fileInfo = GetProviderFileInfo(provider, trimmed, canonicalTrimmed);
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
            if (isUnderscorePath && provider is EmbeddedFileProvider)
            {
                continue;
            }

            var fileInfo = GetProviderFileInfo(provider, trimmed, canonicalTrimmed);
            if (fileInfo.Exists)
            {
                return fileInfo;
            }
        }

        // Return a NotFoundFileInfo if no match is found
        return new NotFoundFileInfo(subpath);
    }

    private static bool IsRemoteWebViewEmbeddedProvider(IFileProvider provider)
    {
        if (provider is not EmbeddedFileProvider embedded)
            return false;

        return EmbeddedAssemblyField?.GetValue(embedded) is Assembly assembly
            ? assembly == typeof(RemoteWebView).Assembly
            : true;
    }

    private static IFileInfo GetProviderFileInfo(IFileProvider provider, string trimmed, string canonicalPath)
    {
        var fileInfo = provider.GetFileInfo(trimmed);
        if (!fileInfo.Exists && !string.Equals(trimmed, canonicalPath, StringComparison.Ordinal))
            fileInfo = provider.GetFileInfo(canonicalPath);
        return fileInfo;
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
