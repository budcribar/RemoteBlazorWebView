using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Text;

namespace PeakSWC.RemoteWebView.RemoteStaticFiles
{
    public class RemoteFilesMiddleware(
        RequestDelegate next,
        IMemoryCache memoryCache,
        ILogger<RemoteFilesMiddleware> logger,
        RemoteFileResolver remoteFileResolver,
        ServerFileSyncManager fileSyncManager
    )
    {
        public const string MauiDefaultCacheControl = "no-cache, max-age=0, must-revalidate, no-store";
        public const int MaxCacheControlLength = 1024;

        public async Task InvokeAsync(HttpContext context)
        {
            // GET is the only method that serves a remote static file and applies Cache-Control.
            // HEAD, POST, and every other method are passed through with no Cache-Control from this middleware.
            // A Range header on GET is not interpreted: the response stays 200 with the full body.
            if (HttpMethods.IsGet(context.Request.Method))
            {
                await HandleGetAsync(context).ConfigureAwait(false);
            }
            else if (HttpMethods.IsPost(context.Request.Method))
            {
                await HandlePostAsync(context).ConfigureAwait(false);
            }
            else
            {
                await next(context).ConfigureAwait(false);
            }
        }

        // Original ParsePathAndGuid method for reference
        /*
        private (string subPath, Guid clientGuid) ParsePathAndGuid(string path, string referrer)
        {
            // Try to parse the client GUID from the request path
            var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (pathSegments.Length >= 1)
            {
                string clientIdString = pathSegments[0];
                if (Guid.TryParse(clientIdString, out var clientGuid))
                {
                    // The remaining segments form the subPath
                    string subPath = string.Join('/', pathSegments.Skip(1));
                    return (subPath, clientGuid);
                }
            }

            // If not found in the path, try to parse the client GUID from the referrer
            if (!string.IsNullOrEmpty(referrer))
            {
                var referrerUri = new Uri(referrer);
                var referrerSegments = referrerUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (referrerSegments.Length >= 1)
                {
                    string clientIdString = referrerSegments[0];
                    if (Guid.TryParse(clientIdString, out var clientGuid))
                    {
                        // Use the original path as the subPath
                        return (path.TrimStart('/'), clientGuid);
                    }
                }
            }

            // If the client GUID is not found, return an empty GUID
            return (path.TrimStart('/'), Guid.Empty);
        }
        */

        private (string, Guid) ParsePathAndGuid(string path, string referrer)
        {
            string subPath;

            var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (pathSegments?.Length > 0)
            {
                string clientIdString = pathSegments[0];
                if (Guid.TryParse(clientIdString, out var clientId))
                {
                    subPath = string.Join('/', pathSegments.Skip(1));
                    return (subPath, clientId);
                }

                // Attempt to get clientId from referrer
                var referrerSegments = referrer.Split("/", StringSplitOptions.RemoveEmptyEntries);
                if (referrerSegments?.Length > 2)
                {
                    clientIdString = referrerSegments[2];
                    if (Guid.TryParse(clientIdString, out clientId))
                        return (path[1..], clientId);
                }
            }

            var referrerSegments2 = referrer.Split("/", StringSplitOptions.RemoveEmptyEntries);
            if (referrerSegments2?.Length > 0)
            {
                if (Guid.TryParse(referrerSegments2[0], out var clientId2))
                    return (path, clientId2);
            }

            // Can't find GUID
            return (path, Guid.Empty);
        }

        private async Task HandleGetAsync(HttpContext context)
        {
            RemoteFilesOptions options = context.RequestServices.GetRequiredService<RemoteFilesOptions>();

            // Extract clientId and subPath from the request path and referrer
            var path = context.Request.Path.Value ?? string.Empty;
            var referrer = context.Request.Headers.TryGetValue(HeaderNames.Referer, out var referrerValues)
                ? referrerValues.FirstOrDefault() ?? string.Empty
                : string.Empty;

            var (subPath, clientId) = ParsePathAndGuid(path, referrer);

            if (clientId == Guid.Empty)
            {
                logger.LogWarning("Invalid request path. Expected format: /clientId/subPath");
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Invalid request path. Expected format: /clientId/subPath", context.RequestAborted).ConfigureAwait(false);
                return;
            }

            logger.LogDebug("Received GET request for file '{SubPath}' from client ID '{ClientId}'", subPath, clientId);

            var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>();
            var serviceStateTaskSource = serviceDictionary.GetOrAdd(clientId.ToString(), _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));

            // Step 1: Retrieve client metadata
            FileMetadata clientMetadata;
            try
            {
                var serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60), context.RequestAborted).ConfigureAwait(false);
                var ready = await serviceState.FileManagerReady.Task.WaitAsync(TimeSpan.FromSeconds(60), context.RequestAborted).ConfigureAwait(false);
                var contentType = GetContentType(subPath);
                var requestUri = "/" + subPath.TrimStart('/') + context.Request.QueryString.Value;
                clientMetadata = await remoteFileResolver.GetFileMetaDataAsync(clientId.ToString(), subPath, requestUri, contentType).ConfigureAwait(false);
                FileStats.Update(serviceState, clientId.ToString(), clientMetadata);
                ILogger<RemoteWebViewService> serviceLogger = context.RequestServices.GetRequiredService<ILogger<RemoteWebViewService>>();
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving metadata for file '{SubPath}' from client ID '{ClientId}'", subPath, clientId);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync("Error retrieving file metadata from client.", context.RequestAborted).ConfigureAwait(false);
                return;
            }

            if (clientMetadata.StatusCode != StatusCodes.Status200OK)
            {
                logger.LogWarning("Client ID '{ClientId}' does not have the file '{SubPath}'. Cannot serve.", clientId, subPath);
                context.Response.StatusCode = clientMetadata.StatusCode;
                context.Response.ContentType = "text/plain";
                context.Response.Headers[HeaderNames.CacheControl] = MauiDefaultCacheControl;
                // Body.WriteAsync keeps Content-Type as text/plain. WriteAsync(string) adds a charset.
                var message = Encoding.UTF8.GetBytes($"There is no content at {subPath}");
                await context.Response.Body.WriteAsync(message, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            bool needsUpdate = true;
            CachedRemoteFile? cached = null;

            // The cache is per desktop client. The host page is rewritten with that client's id
            // after the etag is hashed, so a path-only key can serve one client another's bytes.
            var cacheKey = ServerCacheKey(clientId, subPath);
            if (options.UseServerCache && memoryCache.TryGetValue(cacheKey, out CachedRemoteFile? cachedFile) && cachedFile is not null)
            {
                cached = cachedFile;
                if (cachedFile.Metadata.ETag != clientMetadata.ETag)
                {
                    needsUpdate = true;
                    logger.LogDebug("File '{SubPath}' needs update in server cache.", subPath);
                }
                else
                {
                    needsUpdate = false;
                    logger.LogDebug("File '{SubPath}' is up-to-date in server cache.", subPath);
                }
            }
            else
            {
                needsUpdate = true;
                logger.LogDebug("File '{SubPath}' not found in server cache or caching is disabled.", subPath);
            }

            // Step 3: Set response headers, then handle conditional GETs.
            // Cache-Control is applied to 304 responses as well so the browser keeps the provider's policy.
            SetResponseHeaders(context, clientMetadata, subPath, options.UseClientCache);

            if (options.UseClientCache && context.Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var ifNoneMatch))
            {
                string eTag = clientMetadata.ETag;
                if (!string.IsNullOrEmpty(eTag) && MatchesETag(ifNoneMatch, eTag))
                {
                    logger.LogDebug("ETag matches for file '{SubPath}'. Returning 304 Not Modified.", subPath);
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    return;
                }
            }

            // Step 5: Serve the file
            if (needsUpdate)
            {
                logger.LogDebug("Fetching file '{SubPath}' from client ID '{ClientId}'", subPath, clientId);

                FileStream dataRequest;
                try
                {
                    dataRequest = await remoteFileResolver.GetFileStreamAsync(clientId.ToString(), subPath).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to retrieve file '{SubPath}' from client ID '{ClientId}'", subPath, clientId);
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync($"Error retrieving file from client. {subPath}", context.RequestAborted).ConfigureAwait(false);
                    return;
                }

                // The host page is rewritten after metadata is measured, so a seekable
                // stream's length is the body actually written. A pipe uses the metadata length.
                context.Response.ContentLength = dataRequest.Stream.CanSeek
                    ? dataRequest.Stream.Length
                    : clientMetadata.Length;

                try
                {
                    if (options.UseServerCache)
                    {
                        // Optionally cache the data
                        using MemoryStream memStream = new();
                        await dataRequest.Stream.CopyToAsync(memStream, context.RequestAborted).ConfigureAwait(false);
                        memStream.Position = 0;

                        memoryCache.Set(cacheKey, new CachedRemoteFile(clientMetadata, memStream.ToArray()), TimeSpan.FromSeconds(fileSyncManager.CacheTimeoutSeconds));

                        // Write the data to the response
                        context.Response.ContentLength = memStream.Length;
                        memStream.Position = 0;
                        await memStream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
                    }
                    else
                    {
                        // Stream directly without caching
                        await dataRequest.Stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    return;
                }

                logger.LogDebug("Successfully fetched and served file '{SubPath}' from client ID '{ClientId}'", subPath, clientId);
            }
            else
            {
                // needsUpdate is false only after a cache hit assigned cached.
                logger.LogDebug("Serving file '{SubPath}' from in-memory cache.", subPath);
                context.Response.ContentLength = cached!.Data.Length;
                await context.Response.Body.WriteAsync(cached.Data.AsMemory(), context.RequestAborted).ConfigureAwait(false);
            }
        }

        private void SetResponseHeaders(HttpContext context, FileMetadata clientMetadata, string subPath, bool useClientCache)
        {
            // Set the content type based on the file extension
            context.Response.ContentType = GetContentType(subPath);

            var cacheControl = NormalizeCacheControl(clientMetadata.CacheControl);
            if (cacheControl is not null)
            {
                context.Response.Headers[HeaderNames.CacheControl] = cacheControl;
            }
            else if (useClientCache)
            {
                // Existing remote opt-in, used only when the desktop client did not run a provider.
                context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=3600";
            }

            // Set ETag and Last-Modified headers if client caching is enabled
            if (useClientCache)
            {
                string eTag = clientMetadata.ETag;
                context.Response.Headers[HeaderNames.ETag] = eTag;

                // Ensure Last-Modified is set if within valid DateTimeOffset Unix seconds range
                long lastModified = clientMetadata.LastModified;
                if (lastModified >= -62135596800L && lastModified <= 253402300799L)
                {
                    context.Response.Headers[HeaderNames.LastModified] = DateTimeOffset.FromUnixTimeSeconds(lastModified).ToString("r");
                }
            }
        }

        /// <summary>
        /// Empty means the desktop client did not run a provider. Whitespace, CR/LF, and values longer than
        /// <see cref="MaxCacheControlLength"/> are rejected and become <see cref="MauiDefaultCacheControl"/>.
        /// </summary>
        public static string? NormalizeCacheControl(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return null;
            if (string.IsNullOrWhiteSpace(value) || value.Contains('\r') || value.Contains('\n') || value.Length > MaxCacheControlLength)
                return MauiDefaultCacheControl;
            return value;
        }

        private async Task HandlePostAsync(HttpContext context)
        {
            // Implement POST handling logic if necessary
            // For now, simply pass to the next middleware
            await next(context).ConfigureAwait(false);
            return;
        }
        private static readonly FileExtensionContentTypeProvider _contentTypeProvider = new();
       
        private string GetContentType(string fileName) => _contentTypeProvider.TryGetContentType(fileName, out var contentType) ? contentType : "application/octet-stream";

        private static bool MatchesETag(Microsoft.Extensions.Primitives.StringValues ifNoneMatch, string eTag)
        {
            var normalizedETag = eTag.AsSpan().Trim();
            if (normalizedETag.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
                normalizedETag = normalizedETag[2..].Trim();
            normalizedETag = normalizedETag.Trim('"');
            if (normalizedETag.IsEmpty)
                return false;

            foreach (var header in ifNoneMatch)
            {
                if (HeaderMatchesETag(header, normalizedETag))
                    return true;
            }
            return false;
        }

        private static bool HeaderMatchesETag(string? header, ReadOnlySpan<char> normalizedETag)
        {
            if (string.IsNullOrEmpty(header))
                return false;
            foreach (var part in header.Split(','))
            {
                if (CandidateMatchesETag(part, normalizedETag))
                    return true;
            }
            return false;
        }

        private static bool CandidateMatchesETag(ReadOnlySpan<char> candidate, ReadOnlySpan<char> normalizedETag)
        {
            candidate = candidate.Trim();
            if (candidate.Equals("*", StringComparison.Ordinal))
                return true;
            if (candidate.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
                candidate = candidate[2..].Trim();
            candidate = candidate.Trim('"');
            return candidate.Equals(normalizedETag, StringComparison.Ordinal);
        }

        private static string ServerCacheKey(Guid clientId, string subPath)
            => clientId.ToString("N") + "/" + subPath;

        private sealed record CachedRemoteFile(FileMetadata Metadata, byte[] Data);
    }
    
}
