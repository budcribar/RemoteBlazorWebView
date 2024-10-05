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


namespace PeakSWC.RemoteWebView 
{
    public class RemoteFilesMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<RemoteFilesMiddleware> _logger;
        private readonly RemoteFileResolver _remoteFileResolver;

        public RemoteFilesMiddleware(
            RequestDelegate next,
            IMemoryCache memoryCache,
            ILogger<RemoteFilesMiddleware> logger,
            RemoteFileResolver remoteFileResolver
           )
        {
            _next = next;
            _memoryCache = memoryCache;
            _logger = logger;
            _remoteFileResolver = remoteFileResolver;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (HttpMethods.IsGet(context.Request.Method))
            {
                await HandleGetAsync(context);
            }
            else if (HttpMethods.IsPost(context.Request.Method))
            {
                await HandlePostAsync(context);
            }
            else
            {
                // Pass to the next middleware for other HTTP methods
                await _next(context);
            }
        }
        //private (string subPath, Guid clientGuid) ParsePathAndGuid(string path, string referrer)
        //{
        //    // Try to parse the client GUID from the request path
        //    var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        //    if (pathSegments.Length >= 1)
        //    {
        //        string clientIdString = pathSegments[0];
        //        if (Guid.TryParse(clientIdString, out var clientGuid))
        //        {
        //            // The remaining segments form the subPath
        //            string subPath = string.Join('/', pathSegments.Skip(1));
        //            return (subPath, clientGuid);
        //        }
        //    }

        //    // If not found in the path, try to parse the client GUID from the referrer
        //    if (!string.IsNullOrEmpty(referrer))
        //    {
        //        var referrerUri = new Uri(referrer);
        //        var referrerSegments = referrerUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        //        if (referrerSegments.Length >= 1)
        //        {
        //            string clientIdString = referrerSegments[0];
        //            if (Guid.TryParse(clientIdString, out var clientGuid))
        //            {
        //                // Use the original path as the subPath
        //                return (path.TrimStart('/'), clientGuid);
        //            }
        //        }
        //    }

        //    // If the client GUID is not found, return an empty GUID
        //    return (path.TrimStart('/'), Guid.Empty);
        //}

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
                // getClientId from referrer

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

            // can't find guid
            return (path, Guid.Empty);

        }

       

        private async Task HandleGetAsync(HttpContext context)
        {
            RemoteFilesOptions options = context.RequestServices.GetRequiredService<RemoteFilesOptions>();

            // Extract clientGuid and subPath from the request path and referrer
            var path = context.Request.Path.Value ?? string.Empty;
            var referrer = context.Request.Headers.TryGetValue(HeaderNames.Referer, out var referrerValues)
                ? referrerValues.FirstOrDefault() ?? string.Empty
                : string.Empty;

            var (subPath, clientGuid) = ParsePathAndGuid(path, referrer);

            if (clientGuid == Guid.Empty)
            {
                _logger.LogWarning("Invalid request path. Expected format: /clientGuid/subPath");
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Invalid request path. Expected format: /clientGuid/subPath");
                return;
            }

            _logger.LogInformation($"Received GET request for file '{subPath}' from client GUID '{clientGuid}'.");
            var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>();
            var serviceStateTaskSource = serviceDictionary.GetOrAdd(clientGuid.ToString(), _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));

            // Step 1: Retrieve client metadata
            FileMetadata clientMetadata;
            try
            {
                var serviceState = await serviceStateTaskSource.Task.WaitWithTimeout(TimeSpan.FromSeconds(60));
                var ready = await serviceState.FileManagerReady.Task.WaitWithTimeout(TimeSpan.FromSeconds(60));
                clientMetadata = await _remoteFileResolver.GetFileMetaDataAsync(clientGuid.ToString(), subPath);
                FileStats.Update(serviceState, clientGuid.ToString(), clientMetadata);
                ILogger<RemoteWebViewService> logger = context.RequestServices.GetRequiredService<ILogger<RemoteWebViewService>>();
                //logger.LogCritical($"Read {serviceState.TotalFilesRead} file {subPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving metadata for file '{subPath}' from client GUID '{clientGuid}'.");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync("Error retrieving file metadata from client.");
                return;
            }

            if (clientMetadata.StatusCode != StatusCodes.Status200OK)
            {
                _logger.LogWarning($"Client GUID '{clientGuid}' does not have the file '{subPath}'. Cannot serve.");
                context.Response.StatusCode = clientMetadata.StatusCode;
                await context.Response.WriteAsync($"File not found. {subPath}");
                return;
            }

            bool needsUpdate = false;
            FileMetadata? serverMetadata = null;

            // Step 2: Check if server cache is enabled and retrieve server metadata
            // Step 2: Check if server cache is enabled and retrieve server metadata
            if (options.UseServerCache && _memoryCache.TryGetValue(subPath, out serverMetadata))
            {
                if (serverMetadata?.Length != clientMetadata.Length || serverMetadata.LastModified != clientMetadata.LastModified)
                {
                    needsUpdate = true;
                    _logger.LogInformation($"File '{subPath}' needs update in server cache.");
                }
                else
                {
                    _logger.LogInformation($"File '{subPath}' is up-to-date in server cache.");
                }
            }
            else
            {
                needsUpdate = true;
                _logger.LogInformation($"File '{subPath}' not found in server cache or caching is disabled.");
            }

            // Step 3: Handle Conditional GETs (ETag and If-None-Match)
            if (options.UseClientCache && context.Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var ifNoneMatch))
            {
                string eTag = GenerateETag(clientMetadata);
                if (ifNoneMatch.Contains(eTag))
                {
                    _logger.LogInformation($"ETag matches for file '{subPath}'. Returning 304 Not Modified.");
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    return;
                }
            }

            // Step 4: Set response headers
            SetResponseHeaders(context, clientMetadata, subPath, options.UseClientCache);

            // Step 5: Serve the file
            if (needsUpdate)
            {
                _logger.LogInformation($"Fetching file '{subPath}' from client GUID '{clientGuid}'.");

                FileStream dataRequest;
                try
                {
                    dataRequest = await _remoteFileResolver.GetFileStreamAsync(clientGuid.ToString(), subPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to retrieve file '{subPath}' from client GUID '{clientGuid}'.");
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync($"Error retrieving file from client. {subPath}");
                    return;
                }

                if (dataRequest.StatusCode != HttpStatusCode.OK)
                {
                    _logger.LogWarning($"Failed to retrieve file '{subPath}' from client GUID '{clientGuid}'. Status code: {dataRequest.StatusCode}");
                    context.Response.StatusCode = (int)dataRequest.StatusCode;
                    await context.Response.WriteAsync($"Failed to retrieve file from client. {subPath}");
                    return;
                }

                // Stream directly to the response
                context.Response.ContentLength = clientMetadata.Length;

                if (options.UseServerCache)
                {
                    // Optionally cache the data
                    using (var memStream = new MemoryStream())
                    {
                        await dataRequest.Stream.CopyToAsync(memStream);
                        memStream.Position = 0;

                        // Update metadata in cache
                        serverMetadata = new FileMetadata
                        {
                            Length = clientMetadata.Length,
                            LastModified = clientMetadata.LastModified
                        };
                        _memoryCache.Set(subPath, serverMetadata, TimeSpan.FromMinutes(10));

                        // Cache the data
                        _memoryCache.Set($"{subPath}_data", memStream.ToArray(), TimeSpan.FromMinutes(10));

                        // Write the data to the response
                        memStream.Position = 0;
                        await memStream.CopyToAsync(context.Response.Body);
                    }
                }
                else
                {
                    // Stream directly without caching
                    await dataRequest.Stream.CopyToAsync(context.Response.Body);
                }

                _logger.LogInformation($"Successfully fetched and served file '{subPath}' from client GUID '{clientGuid}'.");
            }
            else
            {
                // Serve from cache
                if (_memoryCache.TryGetValue($"{subPath}_data", out byte[]? cachedData))
                {
                    _logger.LogInformation($"Serving file '{subPath}' from in-memory cache.");
                    context.Response.ContentLength = cachedData?.Length ?? 0;
                    await context.Response.Body.WriteAsync(cachedData.AsMemory());
                }
                else
                {
                    _logger.LogWarning($"Data for file '{subPath}' not found in cache. Fetching from client.");

                    // Fetch from client
                    FileStream dataRequest;
                    try
                    {
                        dataRequest = await _remoteFileResolver.GetFileStreamAsync(clientGuid.ToString(), subPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to retrieve file '{subPath}' from client GUID '{clientGuid}'.");
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        await context.Response.WriteAsync($"Error retrieving file from client. {subPath}");
                        return;
                    }

                    if (dataRequest.StatusCode != HttpStatusCode.OK)
                    {
                        _logger.LogWarning($"Failed to retrieve file '{subPath}' from client GUID '{clientGuid}'. Status code: {dataRequest.StatusCode}");
                        context.Response.StatusCode = (int)dataRequest.StatusCode;
                        await context.Response.WriteAsync($"Failed to retrieve file from client. {subPath}");
                        return;
                    }

                    // Optionally cache the data
                    if (options.UseServerCache)
                    {
                        using (var memStream = new MemoryStream())
                        {
                            await dataRequest.Stream.CopyToAsync(memStream);
                            memStream.Position = 0;

                            // Update metadata in cache
                            serverMetadata = new FileMetadata
                            {
                                Length = clientMetadata.Length,
                                LastModified = clientMetadata.LastModified
                            };
                            _memoryCache.Set(subPath, serverMetadata, TimeSpan.FromMinutes(10));

                            // Cache the data
                            _memoryCache.Set($"{subPath}_data", memStream.ToArray(), TimeSpan.FromMinutes(10));

                            // Write the data to the response
                            memStream.Position = 0;
                            context.Response.ContentLength = memStream.Length;
                            await memStream.CopyToAsync(context.Response.Body);
                        }
                    }
                    else
                    {
                        // Stream directly without caching
                        context.Response.ContentLength = clientMetadata.Length;
                        await dataRequest.Stream.CopyToAsync(context.Response.Body);
                    }

                    _logger.LogInformation($"Successfully fetched and served file '{subPath}' from client GUID '{clientGuid}'.");
                }
            }
        }
        private void SetResponseHeaders(HttpContext context, FileMetadata clientMetadata, string subPath, bool useClientCache)
        {
            // Set the content type based on the file extension
            context.Response.ContentType = GetContentType(subPath);

            // Set ETag and Last-Modified headers if client caching is enabled
            if (useClientCache)
            {
                string eTag = GenerateETag(clientMetadata);
                context.Response.Headers[HeaderNames.ETag] = eTag;

                // Ensure Last-Modified is set
                context.Response.Headers[HeaderNames.LastModified] = DateTimeOffset.FromUnixTimeSeconds(clientMetadata.LastModified).ToString("r");

                // Set Cache-Control as needed
                context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=3600";
            }
        }


        private async Task HandlePostAsync(HttpContext context)
        {
            // Implement POST handling logic if necessary
            // For now, simply pass to the next middleware
            await _next(context);
            return;
        }

        private string GenerateETag(FileMetadata metadata)
        {
            return $"\"{metadata.Length}-{metadata.LastModified}\"";
        }

        private string GetContentType(string fileName)
        {
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fileName, out string? contentType))
            {
                contentType = "application/octet-stream";
            }
            return contentType;
        }
    }
}



