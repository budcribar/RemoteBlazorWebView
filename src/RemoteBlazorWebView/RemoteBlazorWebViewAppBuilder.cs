using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using Photino.Blazor;
using Microsoft.Extensions.Options;

namespace PeakSWC.RemoteWebView
{
    public class RemoteBlazorWebViewAppBuilder
    {
        internal RemoteBlazorWebViewAppBuilder()
        {
            RootComponents = new RootComponentList();
            Services = new ServiceCollection();
        }

        public static RemoteBlazorWebViewAppBuilder CreateDefault(string[]? args = default)
        {
            // We don't use the args for anything right now, but we want to accept them
            // here so that it shows up this way in the project templates.
            // var jsRuntime = DefaultWebAssemblyJSRuntime.Instance;
            var builder = new RemoteBlazorWebViewAppBuilder();
            builder.Services
                //.AddScoped(sp => new HttpClient(new PhotinoHttpHandler(sp.GetService<PhotinoBlazorApp>())) { BaseAddress = new Uri(PhotinoWebViewManager.AppBaseUri) })
                .AddSingleton<RemotePhotinoBlazorApp>()
                .AddBlazorWebView();

            // Right now we don't have conventions or behaviors that are specific to this method
            // however, making this the default for the template allows us to add things like that
            // in the future, while giving `new BlazorDesktopHostBuilder` as an opt-out of opinionated
            // settings.
            return builder;
        }

        public RootComponentList RootComponents { get; }

        public IServiceCollection Services { get; }   

        public RemotePhotinoBlazorApp Build(Uri serverUrl, Guid id)
        {
            var sp = Services.BuildServiceProvider();
            var app = sp.GetRequiredService<RemotePhotinoBlazorApp>();
            app.Initialize(sp, RootComponents, serverUrl, id);
            return app;
        }
    }
}