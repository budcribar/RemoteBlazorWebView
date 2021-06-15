using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Threading.Tasks;

namespace RemoteableWebWindowSite
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");

            builder.Services.AddSingleton<AppVersionInfo>();

            //Add gRPC service
            builder.Services.AddSingleton(services =>
            {
                var baseUri = services.GetRequiredService<NavigationManager>().BaseUri;
                // Get the service address from appsettings.json
                var config = services.GetRequiredService<IConfiguration>();
                var backendUrl = config.GetValue<string>("BaseAddress");
                //var backendUrl = "https://127.0.0.1:443";
                //var backendUrl = "https://localhost:443";
                // Create a channel with a GrpcWebHandler that is addressed to the backend server.
                //
                // GrpcWebText is used because server streaming requires it. If server streaming is not used in your app
                // then GrpcWeb is recommended because it produces smaller messages.
                var httpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWebText, new HttpClientHandler());

                return GrpcChannel.ForAddress(baseUri, new GrpcChannelOptions { HttpHandler = httpHandler });
            });

            builder.Services.AddMsalAuthentication(options =>
            {
                //builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);

                builder.Configuration.Bind("AzureAdB2C", options.ProviderOptions.Authentication);
                options.ProviderOptions.DefaultAccessTokenScopes.Add("openid");
                options.ProviderOptions.DefaultAccessTokenScopes.Add("offline_access");
                options.ProviderOptions.LoginMode = "redirect";
            });

            await builder.Build().RunAsync();
        }
    }
}
