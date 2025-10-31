# RemoteWebViewService.Lib

[![NuGet](https://img.shields.io/nuget/v/PeakSWC.RemoteWebViewService.Lib)](https://www.nuget.org/packages/PeakSWC.RemoteWebViewService.Lib/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**RemoteWebViewService.Lib** is a .NET 9 library that provides the core hosting infrastructure for the RemoteWebViewService. This library enables developers to create server applications that can host and manage remote Blazor WebView clients, allowing desktop applications to be accessed remotely through web browsers.

## Overview

This library serves as the foundation for the RemoteWebViewService, providing:
- **gRPC Services** for real-time communication between server and clients
- **HTTP Endpoints** for web browser access and client management
- **File Synchronization** for seamless content delivery
- **Client Management** for handling multiple concurrent connections
- **Security Features** including authentication and rate limiting
- **Monitoring and Statistics** for operational insights

## Key Features

### ?? **Multi-Protocol Communication**
- **gRPC**: High-performance communication for WebView clients
- **HTTP/HTTPS**: Standard web access for browsers
- **WebSocket-like** real-time messaging
- **File streaming** for efficient content delivery

### ?? **Security & Authentication**
- Azure AD B2C integration
- JWT Bearer authentication
- Rate limiting and DoS protection
- CORS policy management
- Client capacity limits

### ?? **Client Management**
- Real-time client status monitoring
- Session management and cleanup
- Client mirroring capabilities
- Dynamic markup updates
- Ping/health monitoring

### ?? **File Synchronization**
- Efficient file transfer between clients and server
- Caching mechanisms (client and server-side)
- File metadata management
- Real-time file change notifications

## Architecture

### Core Services

#### **RemoteWebViewService**
Main gRPC service handling WebView client connections:
```csharp
// Create and manage WebView instances
rpc CreateWebView(CreateWebViewRequest) returns (stream WebMessageResponse);
// Handle file synchronization
rpc RequestClientFileRead(stream ClientFileReadResponse) returns (stream ServerFileReadRequest);
// Client lifecycle management
rpc Shutdown(IdMessageRequest) returns (google.protobuf.Empty);
```

#### **ClientIPCService**
Manages client information and status:
```csharp
// Get connected clients for monitoring
rpc GetClients(UserMessageRequest) returns (stream ClientResponseList);
// Get server status and statistics
rpc GetServerStatus(google.protobuf.Empty) returns (ServerResponse);
// Configure caching settings
rpc SetCache(CacheRequest) returns (google.protobuf.Empty);
```

#### **BrowserIPCService**
Handles browser-to-server communication:
```csharp
// Receive messages from browsers
rpc ReceiveMessage(ReceiveMessageRequest) returns (stream StringRequest);
// Send messages to WebView clients
rpc SendMessage(SendSequenceMessageRequest) returns (SendMessageResponse);
```

### HTTP Endpoints

The library provides several HTTP endpoints for different functionalities:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Contact information and version |
| `/app/{id:guid}` | GET | Start a WebView session |
| `/{id:guid}` | GET | Start or refresh a session |
| `/mirror/{id:guid}` | GET | Mirror an existing session |
| `/status/{id:guid}` | GET | Get client connection status |
| `/wait/{id:guid}` | GET | Wait for client to connect |
| `/setmarkup/{id:guid}` | POST | Update client markup dynamically |
| `/health` | GET | Health check endpoint |
| `/grpcbaseuri` | GET | Get gRPC service URI |
| `/stats` | GET | Server statistics (if enabled) |
| `/favicon.ico` | GET | Favicon |

## Installation

### NuGet Package
```bash
dotnet add package PeakSWC.RemoteWebViewService.Lib
```

### Package Manager Console
```powershell
Install-Package PeakSWC.RemoteWebViewService.Lib
```

## Usage

### Basic Server Setup

```csharp
using PeakSWC.RemoteWebView;

// Create and configure the server
var builder = WebApplication.CreateBuilder(args);

// Add RemoteWebViewService
builder.Services.AddSingleton<RemoteWebViewService>();
builder.Services.AddSingleton<ClientIPCService>();
builder.Services.AddSingleton<BrowserIPCService>();

var app = builder.Build();

// Configure the request pipeline
app.UseRouting();
app.UseGrpcWeb();

// Map gRPC services
app.MapGrpcService<RemoteWebViewService>();
app.MapGrpcService<ClientIPCService>();
app.MapGrpcService<BrowserIPCService>();

// Map HTTP endpoints
app.MapGet("/", Endpoints.Contact());
app.MapGet("/health", Endpoints.Health());
app.MapPost("/setmarkup/{id:guid}", Endpoints.SetMarkup());
// ... other endpoints

app.Run();
```

### Using the RemoteWebViewServer Helper

```csharp
using PeakSWC.RemoteWebView;

// Simple server with default configuration
RemoteWebViewServer.Run(args);

// Server with custom port and client limits
RemoteWebViewServer.Run(
    port: 5001, 
    maxNumClients: 10,
    frameAncestors: new[] { "https://trusted-domain.com", "'self'" }
);
```

### Client Capacity Management

```csharp
// Configure maximum concurrent clients
services.AddSingleton(new MaxClientsOptions { MaxClients = 50 });
```

### Caching Configuration

```csharp
// Configure file caching options
services.Configure<RemoteFilesOptions>(options =>
{
    options.UseServerCache = true;
    options.UseClientCache = true;
});
```

## Configuration

### AppSettings.json Example

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "RemoteFilesOptions": {
    "UseServerCache": true,
    "UseClientCache": true
  },
  "AzureAdB2C": {
    "ClientId": "your-client-id",
    "DirectoryId": "your-directory-id"
  }
}
```

### Environment Variables

- `ASPNETCORE_ENVIRONMENT`: Set to `Development`, `Staging`, or `Production`
- `ASPNETCORE_URLS`: Configure listening URLs
- `Secret`: Client secret for Azure AD B2C (if using authentication)

## Authentication & Authorization

The library supports multiple authentication modes:

### No Authentication (Development)
```csharp
#if NOAUTHORIZATION
// No authentication required
#endif
```

### Azure AD B2C Authentication
```csharp
services.AddMicrosoftIdentityWebAppAuthentication(configuration, "AzureAdB2C");
services.AddAuthorization();
```

## Monitoring & Statistics

### Server Statistics
Access real-time server statistics:
```csharp
// Get server status via gRPC
var status = await clientIpcService.GetServerStatus(new Empty());

// Includes:
// - Memory usage (working set, peak)
// - Thread and handle counts
// - Connection information
// - Processing times
// - Cache status
```

### Health Monitoring
```csharp
// Health check endpoint
app.MapGet("/health", Endpoints.Health());

// Returns server health status
```

## File Synchronization

The library provides efficient file synchronization between clients and server:

### Client-Side File Management
```csharp
// Files are automatically synchronized from client to server
// Supports metadata caching and differential updates
// Real-time file change notifications
```

### Server-Side Caching
```csharp
// Configure caching behavior
services.Configure<RemoteFilesOptions>(options =>
{
    options.UseServerCache = true;  // Cache files on server
    options.UseClientCache = true;  // Enable client-side caching
});
```

## Advanced Features

### Dynamic Markup Updates
```csharp
// Update client markup in real-time
POST /setmarkup/{clientId}
Content-Type: text/html

<div class="updated-content">New markup content</div>
```

### Client Mirroring
```csharp
// Mirror an existing client session
GET /mirror/{clientId}

// Allows multiple users to view the same session
```

### Rate Limiting
```csharp
#if RATELIMIT
services.AddRateLimiter(options =>
{
    options.AddPolicy("StaticFilesRateLimit", context =>
        RateLimitPartition.GetConcurrencyLimiter(
            partitionKey: "static_file_limiter",
            factory: _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 100,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 1000
            }));
});
#endif
```

## API Reference

### ServiceState Class
Represents the state of a connected client:
```csharp
public class ServiceState
{
    public string Id { get; init; }
    public string Markup { get; set; }
    public string Group { get; init; }
    public bool InUse { get; set; }
    public bool EnableMirrors { get; set; }
    public string User { get; set; }
    public string HostName { get; init; }
    // ... additional properties
}
```

### GenMarkup Helper
Generate default HTML markup for clients:
```csharp
public static string GenMarkup(Uri? uri, Guid id)
{
    // Generates a card-style HTML representation
    // Used when no custom markup is provided
}
```

## Performance Considerations

- **Connection Pooling**: Efficient management of gRPC connections
- **Async/Await**: Non-blocking operations throughout
- **Memory Management**: Proper disposal and cleanup
- **Caching**: Multiple levels of caching for optimal performance
- **Rate Limiting**: Protection against resource exhaustion

## Troubleshooting

### Common Issues

1. **Port Already in Use**
   ```
   Configure different ports in appsettings.json or use environment variables
   ```

2. **Authentication Failures**
   ```
   Verify Azure AD B2C configuration and client secrets
   ```

3. **File Sync Issues**
   ```
   Check file permissions and caching settings
   ```

4. **Performance Issues**
   ```
   Monitor using /stats endpoint and adjust rate limiting
   ```

## Dependencies

- **.NET 9**: Target framework
- **ASP.NET Core 9.0**: Web hosting and HTTP pipeline
- **gRPC**: High-performance RPC framework
- **Google.Protobuf**: Protocol buffer support
- **Microsoft.Identity.Web**: Azure AD B2C integration
- **Azure.Identity**: Azure authentication

## Contributing

This library is part of the [RemoteBlazorWebView](https://github.com/budcribar/RemoteBlazorWebView) project. Contributions are welcome!

## License

Licensed under the [MIT License](https://opensource.org/licenses/MIT).

## Support

- **GitHub Issues**: [Report bugs or request features](https://github.com/budcribar/RemoteBlazorWebView/issues)
- **Email**: budcribar@msn.com
- **Company**: Peak Software Consulting, LLC

---

**RemoteWebViewService.Lib** - Enabling remote desktop applications through web browsers with .NET 9 and Blazor.