// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PeakSwc.StaticFiles;

namespace PeakSwc.Builder
{
    /// <summary>
    /// Extension methods for the StaticFileMiddleware
    /// </summary>
    public static class StaticFileExtensions
    {
        /// <summary>
        /// Enables static file serving for the current request path
        /// </summary>
        /// <param name="app"></param>
        /// <returns></returns>
        public static IApplicationBuilder PeakSwcUseStaticFiles(this IApplicationBuilder app)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app.UseMiddleware<PeakSwcStaticFileMiddleware>();
        }

        /// <summary>
        /// Enables static file serving for the given request path
        /// </summary>
        /// <param name="app"></param>
        /// <param name="requestPath">The relative request path.</param>
        /// <returns></returns>
        public static IApplicationBuilder PeakSwcUseStaticFiles(this IApplicationBuilder app, string requestPath)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app.PeakSwcUseStaticFiles(new StaticFileOptions
            {
                RequestPath = new PathString(requestPath)
            });
        }

        /// <summary>
        /// Enables static file serving with the given options
        /// </summary>
        /// <param name="app"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static IApplicationBuilder PeakSwcUseStaticFiles(this IApplicationBuilder app, StaticFileOptions options)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return app.UseMiddleware<PeakSwcStaticFileMiddleware>(Options.Create(options));
        }
    }
}
