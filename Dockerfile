# syntax=docker/dockerfile:1
# RemoteWebViewService (net11) for Railway TCP proxy: Kestrel does TLS + HTTP/2 on 8443.
# global.json pins SDK 11.0.100-rc.3.26522.148. The floating 11.0 tag and
# 11.0.100-rc.3 are that SDK (digest sha256:346e33428a30be5cad70380dbd6ab6b149fa1226be3984bbf96b58c827f9fbae).
# 11.0-preview is still 11.0.100-preview.7.26381.103 and cannot satisfy this pin.
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.3 AS build
WORKDIR /src
# Only what the server needs (no npm: the browser JS is streamed by the client app, server has no wwwroot)
COPY global.json Directory.Build.props ./
COPY icons/ icons/
COPY README.md ./
COPY src/Protos/ src/Protos/
COPY src/RemoteWebViewService.Lib/ src/RemoteWebViewService.Lib/
COPY src/RemoteWebViewService/ src/RemoteWebViewService/
RUN dotnet restore src/RemoteWebViewService/RemoteWebViewService.csproj -r linux-x64
RUN dotnet publish src/RemoteWebViewService/RemoteWebViewService.csproj \
      -c Release -r linux-x64 --self-contained false --no-restore \
      -p:PackAsTool=false -p:GeneratePackageOnBuild=false \
      -o /app

# Same digest as aspnet:11.0 (sha256:6f3d41d4360eb7087b9ab995dc325347c1814023333f33ee99689ee9e5463292):
# ASP.NET runtime 11.0.0-rc.3.26522.148. 11.0-preview is still preview.7.
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.3 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    REMOTEWEBVIEW_TLS_PORT=8443 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8443
USER $APP_UID
ENTRYPOINT ["dotnet", "RemoteWebViewService.dll"]
