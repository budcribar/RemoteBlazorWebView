using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using System.Net.Http;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Azure AD B2C sign-in for the relay ("Authorization:Provider": "AzureB2C"). Settings: the "AzureAdB2C" section
    /// (ClientId, DirectoryId and the rest Microsoft.Identity.Web reads) and "Secret", the client secret used to read
    /// each user's groups from Microsoft Graph. Not part of the native (AOT) relay.
    /// </summary>
    public sealed class AzureB2CAuthorization : IRelayAuthorization
    {
        public const string ProviderName = "AzureB2C";
        public string Name => ProviderName;
        public bool Enabled => true;

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddTransient(_ => CreateApiHelper(configuration));
            services.AddTransient<IUserService, UserService>();
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
                // Handling SameSite cookie according to https://docs.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-3.1
                options.HandleSameSiteCookieCompatibility();
            });
            // Configuration to sign-in users with Azure AD B2C
            services.AddMicrosoftIdentityWebAppAuthentication(configuration, "AzureAdB2C");
            services.AddControllersWithViews().AddMicrosoftIdentityUI();
            services.AddRazorPages();

            //Configuring appsettings section AzureAdB2C, into IOptions
            services.AddOptions();
            services.Configure<OpenIdConnectOptions>(configuration.GetSection("AzureAdB2C"));
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        private static async Task<ProtectedApiCallHelper> CreateApiHelper(IConfiguration configuration)
        {
            IConfidentialClientApplication confidentialClientApplication =
               ConfidentialClientApplicationBuilder
               .Create(configuration.GetValue<string>("AzureAdB2C:ClientId"))
               .WithTenantId(configuration.GetValue<string>("AzureAdB2C:DirectoryId"))
               .WithClientSecret(configuration.GetValue<string>("Secret"))
               .Build();

            string[] scopes = ["https://graph.microsoft.com/.default"];
            AuthenticationResult result = await confidentialClientApplication.AcquireTokenForClient(scopes).ExecuteAsync().ConfigureAwait(false);
            var httpClient = new HttpClient();
            return new ProtectedApiCallHelper(httpClient, result.AccessToken);
        }
    }
}
