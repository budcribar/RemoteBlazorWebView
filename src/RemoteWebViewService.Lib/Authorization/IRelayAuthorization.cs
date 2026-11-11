using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// How the relay decides who may open a session's pages. One implementation is in use per relay:
    /// <list type="bullet">
    /// <item><see cref="NoAuthorization"/> (the default): nobody signs in.</item>
    /// <item><see cref="FakeAuthorization"/>: for tests; the request itself says who the user is.</item>
    /// <item>AzureB2C (PeakSWC.RemoteWebViewService.AzureB2C, not part of the native relay): Azure AD B2C sign-in.</item>
    /// </list>
    /// Chosen by the "Authorization:Provider" setting (None, Fake, AzureB2C), or handed to an embedded relay in
    /// <see cref="RemoteWebViewServerOptions.Authorization"/>. What a signed-in user may then open is the same for every
    /// provider: see <see cref="SessionAccessHandler"/>.
    /// </summary>
    public interface IRelayAuthorization
    {
        /// <summary>The name used in the "Authorization:Provider" setting.</summary>
        string Name { get; }

        /// <summary>Session pages need a signed-in user (false only for <see cref="NoAuthorization"/>).</summary>
        bool Enabled { get; }

        /// <summary>Registers the sign-in scheme and the <see cref="IUserService"/> that knows each user's groups.</summary>
        void ConfigureServices(IServiceCollection services, IConfiguration configuration);

        /// <summary>Adds the sign-in middleware; called between UseRouting and UseEndpoints.</summary>
        void Configure(IApplicationBuilder app);
    }

    /// <summary>The providers a relay build knows, and which one a configuration asks for.</summary>
    public static class RelayAuthorization
    {
        public const string ProviderKey = "Authorization:Provider";

        private static readonly ConcurrentDictionary<string, Func<IRelayAuthorization>> Providers = new(StringComparer.OrdinalIgnoreCase)
        {
            [NoAuthorization.ProviderName] = () => new NoAuthorization(),
            [FakeAuthorization.ProviderName] = () => new FakeAuthorization(),
        };

        /// <summary>Used when the "Authorization:Provider" setting is absent. A relay built to require sign-in sets this
        /// at start-up, so that forgetting the setting does not leave it open.</summary>
        public static string DefaultProvider { get; set; } = NoAuthorization.ProviderName;

        /// <summary>Makes a provider available under its name (called at start-up by the host that references it).</summary>
        public static void Register(string name, Func<IRelayAuthorization> create) => Providers[name] = create;

        /// <summary>The provider the configuration asks for. Throws when it is unknown to this build, or is the fake one
        /// anywhere but in the Development environment.</summary>
        public static IRelayAuthorization Resolve(IConfiguration configuration)
        {
            var name = configuration[ProviderKey];
            if (string.IsNullOrWhiteSpace(name)) name = DefaultProvider;

            if (!Providers.TryGetValue(name, out var create))
                throw new InvalidOperationException(
                    $"{ProviderKey} is '{name}', which this build of the relay does not include. Known: {string.Join(", ", Providers.Keys)}.");

            var provider = create();
            if (provider is FakeAuthorization && !string.Equals(configuration["environment"], "Development", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"{ProviderKey} is '{FakeAuthorization.ProviderName}', which lets any request say who its user is. It is only allowed in the Development environment.");
            return provider;
        }
    }

    /// <summary>No sign-in: every session page is open to anyone with its link (and the owner key rules still apply).</summary>
    public sealed class NoAuthorization : IRelayAuthorization
    {
        public const string ProviderName = "None";
        public string Name => ProviderName;
        public bool Enabled => false;

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
            services.AddTransient<IUserService, MockUserService>();

        public void Configure(IApplicationBuilder app) { }
    }
}
