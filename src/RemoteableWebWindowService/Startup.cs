using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PeakSwc.StaticFiles;
using PeakSwc.Builder;
using RemoteableWebWindowService;
using RemoteableWebWindowService.Services;
using Microsoft.AspNetCore.Http;

namespace PeakSwc.RemoteableWebWindows
{
    public class Startup
    {
        private readonly ConcurrentDictionary<Guid, ServiceState> rootDictionary = new ConcurrentDictionary<Guid, ServiceState>();
        private readonly ConcurrentDictionary<Guid, IPC> ipcDictionary = new ConcurrentDictionary<Guid, IPC>();

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
 
            app.PeakSwcUseStaticFiles(new StaticFileOptions
            {
                // TODO get from context
                FileProvider = new FileResolver(rootDictionary),
                //OnPrepareResponse = context => {
                //    var components = context.Context.Request.Path.Value.Split('/');
                //    if(components.Length >= 2 && Guid.TryParse(components[1], out Guid g))
                //    {
                //        var newTarget =  string.Join("/", components.TakeLast(components.Length - 2));
                //        context.Context.Response.Redirect(newTarget, false);
                //    }
                //}
            });

            app.UseEndpoints(endpoints =>
            {             
                endpoints.MapGrpcService<RemoteWebWindowService>();

                endpoints.MapGrpcService<BrowserIPCService>().EnableGrpcWeb();

                endpoints.MapGet("/app", async context =>
                {
                    var guid = context.Request.Cookies["guid"];
                    var home = context.Request.Cookies["home"];

                    if (rootDictionary.ContainsKey(Guid.Parse(guid)))
                    {

                        if (context.Request.QueryString.HasValue && context.Request.QueryString.Value.Contains("restart"))
                        {
                            ipcDictionary[Guid.Parse(guid)].ReceiveMessage("booted:");
                            // TODO synchronize properly
                            Thread.Sleep(3000);


                            //  Need to wait until we get an initialized then refresh

                            context.Response.Redirect("/");
                           
                            
                        }
                        else
                            context.Response.Redirect(home);
                    }
                    else await context.Response.WriteAsync("Invalid Guid");

                });

                endpoints.MapRazorPages();               
            });
        }
    }
}
