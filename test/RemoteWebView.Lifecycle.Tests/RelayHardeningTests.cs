using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PeakSWC.RemoteWebView;

namespace RemoteWebView.Lifecycle.Tests;

/// <summary>Startup's gRPC error detail and HSTS settings per environment and hosting mode.</summary>
public class RelayHardeningTests
{
    private static ServiceProvider Build(string environment, bool embedded)
    {
        var settings = new Dictionary<string, string?>();
        if (embedded)
            settings["RemoteWebView:Embedded"] = "true";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
        new Startup(configuration).ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public void Detailed_grpc_errors_only_in_development(string environment, bool expected)
    {
        using var provider = Build(environment, embedded: false);
        Assert.Equal(expected, provider.GetRequiredService<IOptions<GrpcServiceOptions>>().Value.EnableDetailedErrors);
    }

    [Fact]
    public void Embedded_server_never_sends_detailed_grpc_errors()
    {
        using var provider = Build("Production", embedded: true);
        Assert.False(provider.GetRequiredService<IOptions<GrpcServiceOptions>>().Value.EnableDetailedErrors);
    }

    [Fact]
    public void Hosted_relay_sends_a_one_year_hsts_header()
    {
        using var provider = Build("Production", embedded: false);
        var hsts = provider.GetRequiredService<IOptions<HstsOptions>>().Value;
        Assert.Equal(TimeSpan.FromDays(365), hsts.MaxAge);
        Assert.False(hsts.IncludeSubDomains);
        Assert.False(hsts.Preload);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "RelayHardeningTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
