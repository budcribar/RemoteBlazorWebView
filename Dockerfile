# syntax=docker/dockerfile:1
# RemoteWebViewService (net10) for Railway TCP proxy: Kestrel does TLS + HTTP/2 on 8443.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Only what the server needs (no npm: the browser JS is streamed by the client app, server has no wwwroot)
COPY global.json Directory.Build.props ./
COPY icons/ icons/
COPY README.md ./
COPY src/Protos/ src/Protos/
COPY src/RemoteWebViewService/ src/RemoteWebViewService/
RUN dotnet restore src/RemoteWebViewService/RemoteWebViewService.csproj -r linux-x64
RUN dotnet publish src/RemoteWebViewService/RemoteWebViewService.csproj \
      -c Release -r linux-x64 --self-contained false --no-restore \
      -p:PackAsTool=false -p:GeneratePackageOnBuild=false \
      -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    REMOTEWEBVIEW_TLS_PORT=8443 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8443
USER $APP_UID
ENTRYPOINT ["dotnet", "RemoteWebViewService.dll"]
