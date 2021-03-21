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

namespace PeakSwc.RemoteableWebWindows
{
    public class Startup
    {
        private readonly ConcurrentDictionary<string, ServiceState> rootDictionary = new();
        private readonly ConcurrentDictionary<string, IPC> ipcDictionary = new();

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {         
            services.AddSingleton(ipcDictionary);
            services.AddSingleton(rootDictionary);

            services.AddRazorPages();             
            services.AddGrpc();    

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

            app.UseRouting();

            app.UseCors("CorPolicy");

            app.UseGrpcWeb();
 
            app.UseStaticFiles(new StaticFileOptions
            {
                 FileProvider = new FileResolver(app.ApplicationServices.GetService<ConcurrentDictionary<string,ServiceState>>()),
            });

            app.UseEndpoints(endpoints =>
            {
            endpoints.MapGrpcService<RemoteWebWindowService>();

            endpoints.MapGrpcService<BrowserIPCService>().EnableGrpcWeb();

            endpoints.MapGet("/app", async context =>
            {
                if (context.Request.Query.TryGetValue("guid", out StringValues value))
                {
                    string guid = value.ToString();

                    if (rootDictionary.ContainsKey(guid))
                    {
                        var home = rootDictionary[guid].HtmlHostPath;
             
                        context.Response.Redirect(guid + "/" + home);
                    }
                    else await context.Response.WriteAsync("Invalid Guid");
                }
                else await context.Response.WriteAsync("Invalid Guid");
            });

            endpoints.MapGet("/wait/{id:guid}",  async context =>
            {
                var id = context.Request.RouteValues["id"];
                var sid = id?.ToString() ?? "";

                for (int i = 0; i < 30; i++)
                {
                    if (rootDictionary.ContainsKey(sid))
                        break;
                    await Task.Delay(1000);
                }
                if (rootDictionary.ContainsKey(sid))
                    await context.Response.WriteAsync($"Wait completed");
                else
                    await context.Response.WriteAsync($"Unable to restart -> Timed out");
                    
            });

            endpoints.MapGet("/{id:guid}", async context =>
            {        
                // restart url
                var id = context.Request.RouteValues["id"];
                var sid = id.ToString();
                if (sid == null) return;
                
                ipcDictionary[sid].ReceiveMessage("booted:");
                
                context.Response.Redirect($"/restart?guid={sid}");
                await Task.CompletedTask;
            });

                endpoints.MapRazorPages();               
            });
        }
    }
}
