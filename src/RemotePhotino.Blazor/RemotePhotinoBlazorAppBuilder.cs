using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using Photino.Blazor;

namespace PeakSWC.RemoteWebView
{
    public class RemotePhotinoBlazorAppBuilder
    {
        internal RemotePhotinoBlazorAppBuilder()
        {
            RootComponents = new RootComponentList();
            Services = new ServiceCollection();
        }

        public static RemotePhotinoBlazorAppBuilder CreateDefault(string[]? args = default)
        {
            // We don't use the args for anything right now, but we want to accept them
            // here so that it shows up this way in the project templates.
            // var jsRuntime = DefaultWebAssemblyJSRuntime.Instance;
            var builder = new RemotePhotinoBlazorAppBuilder();
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


        public RemotePhotinoBlazorApp Build()
        {
            var sp = Services.BuildServiceProvider();
            var app = sp.GetService<RemotePhotinoBlazorApp>();
            if(app == null) throw new ArgumentNullException(nameof(app));
            app.Initialize(sp, RootComponents);
            return app;
        }
    }
}