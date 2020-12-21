using System;
using System.Collections.Concurrent;
using System.IO;
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
using FileInfo = PeakSwc.StaticFiles.FileInfo;
using Microsoft.AspNetCore.Identity;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.FileProviders;

namespace PeakSwc.RemoteableWebWindows
{
    public class Startup
    {
        private readonly ConcurrentDictionary<string, ServiceState> rootDictionary = new ConcurrentDictionary<string, ServiceState>();
        private readonly ConcurrentDictionary<string, IPC> ipcDictionary = new ConcurrentDictionary<string, IPC>();

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

        public void Set(HttpContext context, string key, string value, int? expireTime)
        {
            CookieOptions option = new CookieOptions();

            if (expireTime.HasValue)
                option.Expires = DateTime.Now.AddMinutes(expireTime.Value);
            else
                option.Expires = DateTime.Now.AddMilliseconds(10);

            context.Response.Cookies.Append(key, value, option);
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
                 FileProvider = new FileResolver(app.ApplicationServices.GetService<ConcurrentDictionary<string,ServiceState>>())
                
                // TODO get from context
                // FileProvider = new FileResolver(rootDictionary),
            });

            app.UseEndpoints(endpoints =>
            {             
                endpoints.MapGrpcService<RemoteWebWindowService>();

                endpoints.MapGrpcService<BrowserIPCService>().EnableGrpcWeb();

                endpoints.MapGet("/app", async context =>
                {
                    string guid = "";
               
                    if (context.Request.Query.TryGetValue("guid", out StringValues value))
                    {
                        guid = value.ToString();
                        Set(context, "guid", guid,60);
                    } 
                    else
                    {
                        guid = context.Request.Cookies["guid"];
                    }
                        
                    if (rootDictionary.ContainsKey(guid))
                    {
                        var home = rootDictionary[guid].HtmlHostPath;

                        if (context.Request.QueryString.HasValue && context.Request.QueryString.Value.Contains("restart"))
                        {
                            ipcDictionary[guid].ReceiveMessage("booted:");
                            // TODO synchronize properly
                            Thread.Sleep(3000);

                            //  Need to wait until we get an initialized then refresh

                            context.Response.Redirect("/");                      
                        }
                        else
                        {
                            context.Response.Redirect(guid + "/" + home);
                        }
                            
                    }
                    else await context.Response.WriteAsync("Invalid Guid");

                });

                endpoints.MapRazorPages();               
            });
        }
    }
}
