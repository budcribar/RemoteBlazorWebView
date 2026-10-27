using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.EndPoints;

namespace StaticContentCache.Tests;

public class EndpointIdTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task Status_rejects_a_missing_or_invalid_id(string? id)
    {
        var (status, body, contentType) = await InvokeAsync(Endpoints.Status(), id);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal($"Invalid or missing GUID {id}", body);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task Status_reports_whether_the_client_is_connected()
    {
        var id = Guid.NewGuid();
        var clients = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var (absentStatus, absentBody, absentType) = await InvokeAsync(Endpoints.Status(), id.ToString(), clients);

        Assert.Equal(StatusCodes.Status200OK, absentStatus);
        Assert.Equal("application/json", absentType);
        Assert.False(JsonSerializer.Deserialize<StatusResponse>(absentBody)!.Connected);

        clients[id.ToString()] = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (presentStatus, presentBody, presentType) = await InvokeAsync(Endpoints.Status(), id.ToString(), clients);

        Assert.Equal(StatusCodes.Status200OK, presentStatus);
        Assert.Equal("application/json", presentType);
        Assert.True(JsonSerializer.Deserialize<StatusResponse>(presentBody)!.Connected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task Wait_rejects_a_missing_or_invalid_id(string? id)
    {
        var (status, body, contentType) = await InvokeAsync(Endpoints.Wait(), id);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal($"Invalid or missing GUID {id}", body);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task Wait_completes_when_the_client_is_already_registered()
    {
        var id = Guid.NewGuid();
        var clients = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        clients[id.ToString()] = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);

        var (status, body, contentType) = await InvokeAsync(Endpoints.Wait(), id.ToString(), clients);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal("text/plain", contentType);
        Assert.Equal("Wait completed", body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task StartOrRefresh_rejects_a_missing_or_invalid_id(string? id)
    {
        var (status, body, contentType) = await InvokeAsync(Endpoints.StartOrRefresh(), id);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal($"Invalid or missing GUID {id}", body);
        Assert.Null(contentType);
    }

    private static async Task<(int Status, string Body, string? ContentType)> InvokeAsync(
        RequestDelegate endpoint,
        string? id,
        ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>? clients = null)
    {
        var services = new ServiceCollection();
        if (clients != null)
            services.AddSingleton(clients);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (id != null)
            context.Request.RouteValues["id"] = id;
        context.Response.Body = new MemoryStream();

        await endpoint(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return (context.Response.StatusCode, body, context.Response.ContentType);
    }
}
