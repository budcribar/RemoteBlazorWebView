using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Signed tickets from a separate account host ("Authorization:Provider": "RemoteTicket").
    /// The relay has no shop pages. Settings: RelayTicket:SigningKey (environment RELAY_TICKET_SECRET),
    /// RelayTicket:AuthorityUrl, RelayTicket:LoginUrl, RelayTicket:AllowUnregisteredGroups.
    /// </summary>
    public sealed class RemoteTicketAuthorization : IRelayAuthorization
    {
        public const string ProviderName = "RemoteTicket";
        public const string Scheme = "RemoteTicket";
        public const string HttpClientName = "RemoteTicket";

        public string Name => ProviderName;
        public bool Enabled => true;

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            var settings = RemoteTicketSettings.From(configuration);
            if (string.IsNullOrEmpty(settings.SigningKey))
                throw new InvalidOperationException("RelayTicket:SigningKey (or RELAY_TICKET_SECRET) is required for the RemoteTicket provider.");
            if (System.Text.Encoding.UTF8.GetByteCount(settings.SigningKey) < RemoteTicketSettings.MinimumSigningKeyBytes)
                throw new InvalidOperationException($"RelayTicket:SigningKey (or RELAY_TICKET_SECRET) must be at least {RemoteTicketSettings.MinimumSigningKeyBytes} bytes for the RemoteTicket provider.");
            if (string.IsNullOrEmpty(settings.AuthorityUrl))
                throw new InvalidOperationException("RelayTicket:AuthorityUrl is required for the RemoteTicket provider.");

            services.AddSingleton(settings);
            services.AddSingleton<RemoteTicketUsers>();
            services.AddTransient<IUserService, RemoteTicketUserService>();
            if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
                services.AddSingleton(TimeProvider.System);

            services.AddHttpClient(HttpClientName, client =>
            {
                client.BaseAddress = new Uri(settings.AuthorityUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(15);
            }).ConfigurePrimaryHttpMessageHandler(sp => (HttpMessageHandler?)sp.GetService<RemoteTicketAuthorityHandler>() ?? new SocketsHttpHandler());

            services.AddSingleton<IShopSessionPolicy>(sp => new RemoteTicketSessionPolicy(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                settings.SigningKey,
                sp.GetRequiredService<TimeProvider>(),
                settings.AllowUnregisteredGroups));
            services.AddSingleton<IAgentTenantPolicy>(sp => new RemoteTicketAgentPolicy(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                settings.SigningKey));

            services.AddAuthentication(Scheme)
                .AddScheme<AuthenticationSchemeOptions, RemoteTicketAuthenticationHandler>(Scheme, null);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }
    }

    public sealed class RemoteTicketSettings
    {
        /// <summary>The shortest signing key the RemoteTicket provider starts with. The account host uses the same limit.</summary>
        public const int MinimumSigningKeyBytes = 32;

        public string SigningKey { get; init; } = "";
        public string AuthorityUrl { get; init; } = "";
        public string LoginUrl { get; init; } = "";
        public bool AllowUnregisteredGroups { get; init; }

        public static RemoteTicketSettings From(IConfiguration configuration)
        {
            var key = configuration["RelayTicket:SigningKey"];
            if (string.IsNullOrWhiteSpace(key))
                key = configuration["RELAY_TICKET_SECRET"];
            if (string.IsNullOrWhiteSpace(key))
                key = configuration["RelayTicket__SigningKey"];
            // No built-in default: without a configured key, ConfigureServices refuses to start the provider.
            return new RemoteTicketSettings
            {
                SigningKey = key?.Trim() ?? "",
                AuthorityUrl = configuration["RelayTicket:AuthorityUrl"]?.Trim() ?? "",
                LoginUrl = configuration["RelayTicket:LoginUrl"]?.Trim() ?? "",
                AllowUnregisteredGroups = bool.TryParse(configuration["RelayTicket:AllowUnregisteredGroups"], out var allow) && allow,
            };
        }
    }

    public sealed class RemoteTicketUsers : IDisposable
    {
        public const int DefaultMaxEntries = 10_000;
        public static readonly TimeSpan DefaultSlidingExpiration = TimeSpan.FromHours(1);

        private readonly Microsoft.Extensions.Caching.Memory.MemoryCache cache;
        private readonly TimeSpan slidingExpiration;

        public RemoteTicketUsers() : this(DefaultMaxEntries, DefaultSlidingExpiration) { }

        public RemoteTicketUsers(int maxEntries, TimeSpan slidingExpiration)
        {
            this.slidingExpiration = slidingExpiration;
            cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions
            {
                SizeLimit = maxEntries
            });
        }

        public void Remember(string oid, string[] userGroups)
        {
            if (string.IsNullOrEmpty(oid))
                return;
            cache.Set(oid, userGroups, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = slidingExpiration
            });
        }

        public IReadOnlyList<string> Groups(string oid) =>
            !string.IsNullOrEmpty(oid) && cache.TryGetValue(oid, out string[]? found) && found is not null ? found : [];

        public void Dispose() => cache.Dispose();
    }

    public sealed class RemoteTicketUserService(RemoteTicketUsers users) : IUserService
    {
        public Task<IReadOnlyList<string>> GetUserGroups(string oid) => Task.FromResult(users.Groups(oid));
    }

    public sealed class RemoteTicketAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        RemoteTicketSettings settings,
        RemoteTicketUsers users,
        TimeProvider time)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = RemoteTicketReader.From(Context);
            if (string.IsNullOrEmpty(token))
                return Task.FromResult(AuthenticateResult.NoResult());
            if (!RemoteTicketFormat.TryRead(token, settings.SigningKey, time.GetUtcNow(), out var payload, out var error) || payload == null)
                return Task.FromResult(AuthenticateResult.Fail(error ?? "invalid_ticket"));

            users.Remember(payload.Oid, payload.Groups);
            var claims = new List<Claim>
            {
                new("oid", payload.Oid),
                new("name", payload.Name ?? ""),
                new(ClaimTypes.Email, payload.Email ?? ""),
                new("tier", payload.Tier ?? ""),
            };
            if (!string.IsNullOrWhiteSpace(payload.Role))
                claims.Add(new Claim(ClaimTypes.Role, payload.Role));
            var identity = new ClaimsIdentity(claims, RemoteTicketAuthorization.Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), RemoteTicketAuthorization.Scheme)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            if (string.IsNullOrEmpty(settings.LoginUrl))
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            var original = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}{Request.QueryString}";
            var separator = settings.LoginUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            Response.Redirect(settings.LoginUrl + separator + "returnUrl=" + Uri.EscapeDataString(original));
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }
}
