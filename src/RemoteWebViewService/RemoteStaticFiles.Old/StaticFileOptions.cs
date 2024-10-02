// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Http.Features;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using System;

namespace PeakSWC.RemoteWebView;

/// <summary>
/// Options for serving static files
/// </summary>
public class StaticFileOptions : SharedOptionsBase
{
    internal static readonly Action<StaticFileResponseContext> _defaultOnPrepareResponse = _ => { };

    /// <summary>
    /// Defaults to all request paths
    /// </summary>
    public StaticFileOptions() : this(new SharedOptions())
    {
    }

    /// <summary>
    /// Defaults to all request paths
    /// </summary>
    /// <param name="sharedOptions"></param>
    public StaticFileOptions(SharedOptions sharedOptions) : base(sharedOptions)
    {
        OnPrepareResponse = _defaultOnPrepareResponse;
    }

    /// <summary>
    /// Used to map files to content-types.
    /// </summary>
    public Microsoft.AspNetCore.StaticFiles.IContentTypeProvider ContentTypeProvider { get; set; } = default!;

    /// <summary>
    /// The default content type for a request if the ContentTypeProvider cannot determine one.
    /// None is provided by default, so the client must determine the format themselves.
    /// http://www.w3.org/Protocols/rfc2616/rfc2616-sec7.html#sec7
    /// </summary>
    public string? DefaultContentType { get; set; }

    /// <summary>
    /// If the file is not a recognized content-type should it be served?
    /// Default: false.
    /// </summary>
    public bool ServeUnknownFileTypes { get; set; }

    /// <summary>
    /// Indicates if files should be compressed for HTTPS requests when the Response Compression middleware is available.
    /// The default value is <see cref="HttpsCompressionMode.Compress"/>.
    /// </summary>
    /// <remarks>
    /// Enabling compression on HTTPS requests for remotely manipulable content may expose security problems.
    /// </remarks>
    public HttpsCompressionMode HttpsCompression { get; set; } = HttpsCompressionMode.Compress;

    /// <summary>
    /// Called after the status code and headers have been set, but before the body has been written.
    /// This can be used to add or change the response headers.
    /// </summary>
    public Action<StaticFileResponseContext> OnPrepareResponse { get; set; }
}
