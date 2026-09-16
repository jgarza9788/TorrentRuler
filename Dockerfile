# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Publishing against a concrete RID is what keeps `runtimes/` out of the output.
# A RID-agnostic publish ships the e_sqlite3 native binary for all 21 platforms
# SQLitePCLRaw supports (~29 MB) -- win-x64, osx-arm64, browser-wasm, linux-mips64
# and so on -- when exactly one of them can ever load. Must be a musl RID to match
# the Alpine runtime stage below. Override for arm64 hosts:
#   docker build --build-arg RID=linux-musl-arm64 .
ARG RID=linux-musl-x64
WORKDIR /src

COPY TorrentRuler.sln .
COPY src/TorrentRuler.Core/TorrentRuler.Core.csproj src/TorrentRuler.Core/
COPY src/TorrentRuler.Sources/TorrentRuler.Sources.csproj src/TorrentRuler.Sources/
COPY src/TorrentRuler.Snapshot/TorrentRuler.Snapshot.csproj src/TorrentRuler.Snapshot/
COPY src/TorrentRuler.Engine/TorrentRuler.Engine.csproj src/TorrentRuler.Engine/
COPY src/TorrentRuler.Infrastructure/TorrentRuler.Infrastructure.csproj src/TorrentRuler.Infrastructure/
COPY src/TorrentRuler.Web/TorrentRuler.Web.csproj src/TorrentRuler.Web/
COPY src/TorrentRuler.Tests/TorrentRuler.Tests.csproj src/TorrentRuler.Tests/
RUN dotnet restore src/TorrentRuler.Web/TorrentRuler.Web.csproj -r $RID

COPY src/ src/
# --self-contained false keeps this framework-dependent: the shared runtime still
# comes from the base image below, and the RID is here only to prune native assets.
RUN dotnet publish src/TorrentRuler.Web/TorrentRuler.Web.csproj \
        -c Release -r $RID --self-contained false \
        -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Not read from .git (excluded from the build context, see .dockerignore) -- pass them in with
# `docker build --build-arg GIT_SHA=$(git rev-parse --short HEAD) --build-arg GIT_BRANCH=$(git
# branch --show-current)` (docker-compose.yml does this from GIT_SHA/GIT_BRANCH env vars). Purely
# cosmetic: only feeds the Settings > Info card: BuildInfo.cs falls back to "unknown" if unset.
ARG GIT_SHA=unknown
ARG GIT_BRANCH=unknown
ENV TORRENTRULER_GIT_SHA=$GIT_SHA \
    TORRENTRULER_GIT_BRANCH=$GIT_BRANCH

# tzdata: rules resolve arbitrary IANA zone IDs at runtime (CronValidator,
# RuleSchedulerService), and compose passes TZ. Alpine ships none by default.
# su-exec: ~20 KB busybox-native stand-in for gosu (~2 MB); the entrypoint uses it
# to drop from root to the non-root "app" user after fixing volume ownership.
# No curl -- busybox already provides the wget used by the HEALTHCHECK below.
#
# /data holds the SQLite DB and the data-protection key ring; /log holds the rolling
# log files. chown them now so the non-root "app" user (built into the aspnet image
# since .NET 8) can write to them. A bind mount will re-set this to the host dir's
# owner, which is why the entrypoint re-applies the chown at runtime.
RUN apk add --no-cache tzdata su-exec \
    && mkdir -p /data /log \
    && chown app:app /data /log

COPY --from=build /app .
COPY --chmod=0755 docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    TORRENTRULER_DATA_DIR=/data \
    TORRENTRULER_LOG_DIR=/log \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080
VOLUME ["/data", "/log"]

# Deliberately NOT `USER app` here: the entrypoint starts as root so it can chown
# freshly bind-mounted /data and /log, then exec's the app as "app" via su-exec.

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD wget -qO- http://127.0.0.1:8080/healthz >/dev/null || exit 1

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
CMD ["dotnet", "TorrentRuler.Web.dll"]
