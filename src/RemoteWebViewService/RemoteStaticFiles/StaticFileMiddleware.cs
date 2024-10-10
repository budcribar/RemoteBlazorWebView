// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView;

/// <summary>
/// Enables serving static files for a given request path
/// </summary>
public class StaticFileMiddleware
{
    private readonly StaticFileOptions _options;
    private readonly PathString _matchUrl;
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;
    private readonly IFileProvider _fileProvider;
    private readonly IContentTypeProvider _contentTypeProvider;

    /// <summary>
    /// Creates a new instance of the StaticFileMiddleware.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="hostingEnv">The <see cref="IWebHostEnvironment"/> used by this middleware.</param>
    /// <param name="options">The configuration options.</param>
    /// <param name="loggerFactory">An <see cref="ILoggerFactory"/> instance used to create loggers.</param>
    public StaticFileMiddleware(RequestDelegate next, IWebHostEnvironment hostingEnv, IOptions<StaticFileOptions> options, ILoggerFactory loggerFactory)
    {
        if (next == null)
        {
            throw new ArgumentNullException(nameof(next));
        }

        if (hostingEnv == null)
        {
            throw new ArgumentNullException(nameof(hostingEnv));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (loggerFactory == null)
        {
            throw new ArgumentNullException(nameof(loggerFactory));
        }

        _next = next;
        _options = options.Value;
        _contentTypeProvider = _options.ContentTypeProvider ?? new FileExtensionContentTypeProvider();
        _fileProvider = _options.FileProvider ?? Helpers.ResolveFileProvider(hostingEnv);
        _matchUrl = _options.RequestPath;
        _logger = loggerFactory.CreateLogger<StaticFileMiddleware>();
    }

    /// <summary>
    /// Processes a request to determine if it matches a known file, and if so, serves it.
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public Task Invoke(HttpContext context)
    {
        if (!ValidateNoEndpointDelegate(context))
        {
            _logger.EndpointMatched();
        }
        else if (!ValidateMethod(context))
        {
            _logger.RequestMethodNotSupported(context.Request.Method);
        }
        else if (!ValidatePath(context, _matchUrl, out var subPath))
        {
            _logger.PathMismatch(subPath);
        }
        else if (!LookupContentType(_contentTypeProvider, _options, subPath, out var contentType))
        {
            _logger.FileTypeNotSupported(subPath);
        }
        else
        {
            // If we get here, we can try to serve the file
            return TryServeStaticFile(context, contentType, subPath);
        }

        return _next(context);
    }

    // Return true because we only want to run if there is no endpoint delegate.
    private static bool ValidateNoEndpointDelegate(HttpContext context) => context.GetEndpoint()?.RequestDelegate is null;

    private static bool ValidateMethod(HttpContext context)
    {
        return Helpers.IsGetOrHeadMethod(context.Request.Method);
    }

    internal static bool ValidatePath(HttpContext context, PathString matchUrl, out PathString subPath) => Helpers.TryMatchPath(context, matchUrl, forDirectory: false, out subPath);

    internal static bool LookupContentType(IContentTypeProvider contentTypeProvider, StaticFileOptions options, PathString subPath, out string? contentType)
    {
        if (contentTypeProvider.TryGetContentType(subPath.Value!, out contentType))
        {
            return true;
        }

        if (options.ServeUnknownFileTypes)
        {
            contentType = options.DefaultContentType;
            return true;
        }

        return false;
    }

    private (string path, Guid guid) ParsePathAndGuid(string path, string referrer)
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

    private Task TryServeStaticFile(HttpContext context, string? contentType, PathString subPath)
    {
        var referrer = context.Request.Headers.TryGetValue(HeaderNames.Referer, out var referrerValues)
               ? referrerValues.FirstOrDefault() ?? string.Empty
               : string.Empty;

        var png = ParsePathAndGuid(subPath, referrer);
        subPath = $"/{png.guid}/{png.path}";

        var fileContext = new StaticFileContext(context, _options, _logger, _fileProvider, contentType, subPath);

        if (!fileContext.LookupFileInfo())
        {
            _logger.FileNotFound(fileContext.SubPath);
        }
        else
        {
            // If we get here, we can try to serve the file
            return fileContext.ServeStaticFile(context, _next);
        }

        return _next(context);
    }
}
