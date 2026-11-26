# syntax=docker/dockerfile:1
# RemoteWebViewService (net11) for Railway TCP proxy: Kestrel does TLS + HTTP/2 on 8443.
# global.json pins SDK 11.0.100 (rollForward latestMinor); the 11.0 tag satisfies it.
# Native AOT (-p:RelayAot=true): one self-contained binary with the RemoteTicket provider and no Azure AD B2C, on the
# chiseled runtime-deps image (no shell, no package manager, non-root).
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
# The AOT compiler links with clang and needs zlib.
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
# Only what the server needs (no npm: the browser JS is streamed by the client app, server has no wwwroot)
COPY global.json Directory.Build.props ./
COPY icons/ icons/
COPY README.md ./
COPY src/Protos/ src/Protos/
COPY src/RemoteWebViewService.Lib/ src/RemoteWebViewService.Lib/
COPY src/RemoteWebViewService.RemoteTicket/ src/RemoteWebViewService.RemoteTicket/
COPY src/RemoteWebViewService/ src/RemoteWebViewService/
RUN dotnet restore src/RemoteWebViewService/RemoteWebViewService.csproj -r linux-x64 -p:RelayAot=true
# The commit is appended to InformationalVersion as 11.0.0+<sha>, shown on the admin About page. Railway passes
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
      -c Release -r linux-x64 --self-contained true --no-restore -p:RelayAot=true \
      -p:PackAsTool=false -p:GeneratePackageOnBuild=false \
      -p:SourceRevisionId="$sha" \
      -o /app

# -extra adds ICU and time zone data, which the aspnet image had.
FROM mcr.microsoft.com/dotnet/runtime-deps:11.0-resolute-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app/RemoteWebViewService ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    REMOTEWEBVIEW_TLS_PORT=8443 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8443
USER $APP_UID
ENTRYPOINT ["./RemoteWebViewService"]
