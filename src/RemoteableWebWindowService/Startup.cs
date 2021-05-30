using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PeakSwc.StaticFiles;
//using PeakSwc.Builder;
using RemoteableWebWindowService;
using RemoteableWebWindowService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.Threading.Tasks;
using Microsoft.AspNetCore.StaticFiles;
using System.Net.Http;

namespace PeakSwc.RemoteableWebWindows
{
    public class Startup
    {
        private readonly ConcurrentDictionary<string, ServiceState> rootDictionary = new();
        private readonly ConcurrentDictionary<string, IPC> ipcDictionary = new();
        private readonly ConcurrentDictionary<string, BrowserIPCState> state = new();

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {         
            services.AddSingleton(ipcDictionary);
            services.AddSingleton(rootDictionary);
            services.AddSingleton(state);
            services.AddTransient<FileResolver>();
            services.AddHttpClient();

            services.AddRazorPages();             
            services.AddGrpc(options => { options.EnableDetailedErrors = true; } );    

            services.AddCors(o =>
            {
                o.AddPolicy("CorsPolicy", builder =>
                {
                    builder.AllowAnyOrigin();
                    builder.AllowAnyHeader();
                    builder.AllowAnyMethod();
                   
                    // TODO tighten this up
                    //builder.WithOrigins("localhost:443", "localhost", "YourCustomDomain");
                   // builder.WithMethods("POST, OPTIONS");
                    //builder.AllowAnyHeader();
                    builder.WithExposedHeaders("Grpc-Status", "Grpc-Message");
                });
            });
        }


        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            var provider = new FileExtensionContentTypeProvider();
            // Add new mappings
            provider.Mappings[".blat"] = "application/octet-stream";
            provider.Mappings[".dll"] = "application/octet-stream";
            provider.Mappings[".dat"] = "application/octet-stream";
            provider.Mappings[".json"] = "application/json";
            provider.Mappings[".wasm"] = "application/wasm";
            provider.Mappings[".woff"] = "application/font-woff";
            provider.Mappings[".woff2"] = "application/font-woff";
            provider.Mappings[".ico"] = "image/x-icon";

            app.UseRouting();

            app.UseCors("CorPolicy");

            app.UseGrpcWeb();

            app.UseStaticFiles(new StaticFileOptions
            {
                //FileProvider = new FileResolver(app.ApplicationServices.GetService<ConcurrentDictionary<string, ServiceState>>()),
                FileProvider = app.ApplicationServices.GetService<FileResolver>(),
                ContentTypeProvider = provider
            }); ; ;

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<RemoteWebWindowService>();

                endpoints.MapGrpcService<BrowserIPCService>().EnableGrpcWeb();

                endpoints.MapGet("/app/{id:guid}", async context =>
                {
                    string guid = context.Request.RouteValues["id"]?.ToString() ?? "";

                    if (rootDictionary.ContainsKey(guid))
                    {
                        var home = rootDictionary[guid].HtmlHostPath;
             
                        context.Response.Redirect($"/{guid}/{home}");
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Invalid Guid");
                    }
                });

                endpoints.MapGet("/restart/{id:guid}", async context =>
                {
                    string guid = context.Request.RouteValues["id"]?.ToString() ?? "";
                    
                    var text = "<script type='text/javascript'>" +
                    "var xmlHttp = new XMLHttpRequest();" +
                    "xmlHttp.onreadystatechange = function () {" +
                    "   if (xmlHttp.readyState == 4 && xmlHttp.status == 200)" +
                    $"     window.location.assign('/app/{guid}');" +
                    "  };" +
                    $" xmlHttp.open('GET', '/wait/{guid}', true);" +
                    "  xmlHttp.send(null);" +
                    "</script><h1 class='display-4'>Restarting...</h1>";
                   
                    await context.Response.WriteAsync(text);

                });

                endpoints.MapGet("/wait/{id:guid}",  async context =>
                {
                    string guid = context.Request.RouteValues["id"]?.ToString() ?? "";

                    for (int i = 0; i < 30; i++)
                    {
                        if (rootDictionary.ContainsKey(guid))
                            break;
                        await Task.Delay(1000);
                    }
                    if (rootDictionary.ContainsKey(guid))
                        await context.Response.WriteAsync($"Wait completed");
                    else
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync($"Unable to restart -> Timed out");
                    }
                        
                    
                });

                endpoints.MapGet("/{id:guid}", async context =>
                {
                    // Refresh from home page
                    // https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/

                    string guid = context.Request.RouteValues["id"]?.ToString() ?? "";

                    if (ipcDictionary.ContainsKey(guid))
                    {
                        ipcDictionary[guid].ReceiveMessage("booted:");
                        context.Response.Redirect($"/restart/{guid}");
                        await Task.CompletedTask;
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Invalid Guid");
                    }
                });

                endpoints.MapGet("/{id:guid}/{unused:alpha}", async context =>
                {
                    // Refresh from counter page
                    // https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/counter
                    string guid = context.Request.RouteValues["id"]?.ToString() ?? "";

                    if (ipcDictionary.ContainsKey(guid))
                    {
                        ipcDictionary[guid].ReceiveMessage("booted:");
                        context.Response.Redirect($"/restart/{guid}");
                        await Task.CompletedTask;
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Invalid Guid");
                    }

                });
                endpoints.MapRazorPages();               
            });
        }
    }
}
