using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Sign-in for tests: the request says who its user is, as "name;group1,group2", in (first found) the header
    /// X-Rbwv-Fake-User, the query value fake-user (which also sets the cookie, so a browser stays signed in), or the
    /// cookie rbwv-fake-user. A request without any of them is not signed in (401 on a session page). Everything after
    /// that - the session group check, the owner page, names in the roster - is the real code.
    /// Never for a relay others can reach: anyone can claim to be anyone. By configuration it is refused outside the
    /// Development environment (<see cref="RelayAuthorization.Resolve"/>).
    /// </summary>
    public sealed class FakeAuthorization : IRelayAuthorization
    {
        public const string ProviderName = "Fake";
        public const string Scheme = "Fake";
        public const string Header = "X-Rbwv-Fake-User";
        public const string Query = "fake-user";
        public const string Cookie = "rbwv-fake-user";
        /// <summary>The claim holding the user's id, as Azure AD issues it; the fake user's id is their name.</summary>
        public const string ObjectIdClaim = "oid";

        public string Name => ProviderName;
        public bool Enabled => true;

        /// <summary>"name;group1,group2" for a user, as the header, query value or cookie carries it.</summary>
        public static string User(string name, params string[] groups) => $"{name};{string.Join(',', groups)}";

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<FakeUsers>();
            services.AddTransient<IUserService, FakeUserService>();
            services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, FakeAuthenticationHandler>(Scheme, null);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.ApplicationServices.GetService<ILoggerFactory>()?.CreateLogger<FakeAuthorization>()
                .LogWarning("FAKE AUTHORIZATION IS ON: any request can say who its user is. For tests only.");
            app.UseAuthentication();
            app.UseAuthorization();
        }
    }

    /// <summary>The groups of the fake users seen so far, by user id.</summary>
    public sealed class FakeUsers
    {
        private readonly ConcurrentDictionary<string, IReadOnlyList<string>> groups = new(StringComparer.Ordinal);
        public void Set(string id, IReadOnlyList<string> userGroups) => groups[id] = userGroups;
        public IReadOnlyList<string> Groups(string id) => groups.TryGetValue(id, out var g) ? g : [];
    }

    public sealed class FakeUserService(FakeUsers users) : IUserService
    {
        public Task<IReadOnlyList<string>> GetUserGroups(string oid) => Task.FromResult(users.Groups(oid));
    }

    public sealed class FakeAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, FakeUsers users)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? value = Request.Headers[FakeAuthorization.Header].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(value) && Request.Query.TryGetValue(FakeAuthorization.Query, out var fromQuery))
            {
                value = fromQuery.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                    Response.Cookies.Append(FakeAuthorization.Cookie, value, new CookieOptions { Path = "/", HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true });
            }
            if (string.IsNullOrWhiteSpace(value)) Request.Cookies.TryGetValue(FakeAuthorization.Cookie, out value);
            if (string.IsNullOrWhiteSpace(value)) return Task.FromResult(AuthenticateResult.NoResult());

            var parts = value.Split(';', 2);
            var name = parts[0].Trim();
            if (name.Length == 0) return Task.FromResult(AuthenticateResult.Fail("The fake user has no name."));
            var groups = parts.Length > 1 ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
            users.Set(name, groups);

            var identity = new ClaimsIdentity(
                [new Claim(ClaimsIdentity.DefaultNameClaimType, name), new Claim(FakeAuthorization.ObjectIdClaim, name)], FakeAuthorization.Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), FakeAuthorization.Scheme)));
        }

        // No sign-in page to send anyone to: plain 401 / 403.
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }
}
