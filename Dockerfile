# syntax=docker/dockerfile:1
# RemoteWebViewService (net10) for Railway TCP proxy: Kestrel does TLS + HTTP/2 on 8443.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Only what the server needs (no npm: the browser JS is streamed by the client app, server has no wwwroot)
COPY global.json Directory.Build.props ./
COPY icons/ icons/
COPY README.md ./
COPY src/Protos/ src/Protos/
COPY src/RemoteWebViewService.Lib/ src/RemoteWebViewService.Lib/
COPY src/RemoteWebViewService/ src/RemoteWebViewService/
RUN dotnet restore src/RemoteWebViewService/RemoteWebViewService.csproj -r linux-x64
# The commit is appended to InformationalVersion as 10.0.15+<sha>, shown on the admin About page. Railway passes
# RAILWAY_GIT_COMMIT_SHA for GitHub-triggered builds; for `railway up` (CLI) deploys it is empty, so fall back to the
# uploaded .git/HEAD (.dockerignore lets in only HEAD and refs). Railway allows no bind mounts, so copy it: the .gi[t]
# wildcard plus an always-present anchor file keeps a context without .git building (its contents land in /gitstamp).
COPY global.json .gi[t] /gitstamp/
ARG RAILWAY_GIT_COMMIT_SHA=""
RUN sha="$RAILWAY_GIT_COMMIT_SHA"; \
    if [ -z "$sha" ] && [ -f /gitstamp/HEAD ]; then \
      head="$(cat /gitstamp/HEAD)"; \
      case "$head" in \
        "ref: "*) ref="${head#ref: }"; \
          if [ -f "/gitstamp/$ref" ]; then sha="$(cat "/gitstamp/$ref")"; \
          elif [ -f /gitstamp/packed-refs ]; then sha="$(grep " $ref\$" /gitstamp/packed-refs | cut -d' ' -f1)"; fi ;; \
        *) sha="$head" ;; \
      esac; \
    fi; \
    echo "Relay commit: ${sha:-unknown}"; \
    dotnet publish src/RemoteWebViewService/RemoteWebViewService.csproj \
      -c Release -r linux-x64 --self-contained false --no-restore \
      -p:PackAsTool=false -p:GeneratePackageOnBuild=false \
      -p:SourceRevisionId="$sha" \
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
