# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0.302-noble@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ShrinkFrame.sln ./
COPY src/ShrinkFrame.Domain/ShrinkFrame.Domain.csproj src/ShrinkFrame.Domain/
COPY src/ShrinkFrame.Domain/packages.lock.json src/ShrinkFrame.Domain/
COPY src/ShrinkFrame.Application/ShrinkFrame.Application.csproj src/ShrinkFrame.Application/
COPY src/ShrinkFrame.Application/packages.lock.json src/ShrinkFrame.Application/
COPY src/ShrinkFrame.Infrastructure/ShrinkFrame.Infrastructure.csproj src/ShrinkFrame.Infrastructure/
COPY src/ShrinkFrame.Infrastructure/packages.lock.json src/ShrinkFrame.Infrastructure/
COPY src/ShrinkFrame.Web/ShrinkFrame.Web.csproj src/ShrinkFrame.Web/
COPY src/ShrinkFrame.Web/packages.lock.json src/ShrinkFrame.Web/
COPY tests/ShrinkFrame.Domain.Tests/ShrinkFrame.Domain.Tests.csproj tests/ShrinkFrame.Domain.Tests/
COPY tests/ShrinkFrame.Domain.Tests/packages.lock.json tests/ShrinkFrame.Domain.Tests/
COPY tests/ShrinkFrame.Infrastructure.Tests/ShrinkFrame.Infrastructure.Tests.csproj tests/ShrinkFrame.Infrastructure.Tests/
COPY tests/ShrinkFrame.Infrastructure.Tests/packages.lock.json tests/ShrinkFrame.Infrastructure.Tests/
RUN dotnet restore ShrinkFrame.sln --locked-mode

COPY src/ src/
RUN dotnet publish src/ShrinkFrame.Web/ShrinkFrame.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10-noble@sha256:f1126d438ccc359f51cc6d4701a8deae513856cf10f5fe645d29ea6403dcac6b AS runtime

# Ubuntu Noble archive package. The exact version is intentionally fail-closed.
ARG FFMPEG_VERSION=7:6.1.1-3ubuntu5
RUN apt-get update \
    && apt-get install --yes --no-install-recommends "ffmpeg=${FFMPEG_VERSION}" \
    && ffmpeg -hide_banner -version \
    && ffprobe -hide_banner -version \
    && ffmpeg -hide_banner -encoders 2>/dev/null | grep -q 'libx264' \
    && ffmpeg -hide_banner -encoders 2>/dev/null | grep -q 'libx265' \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build --chown=1654:1654 /app/publish/ ./
RUN install -d -o 1654 -g 1654 -m 0750 /data /data/keys /data/work

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
VOLUME ["/data"]

# The official image's app user is UID 1654. dotnet remains PID 1 and receives
# SIGTERM directly; ShrinkFrame kills/awaits its own FFmpeg process trees.
USER 1654:1654
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080; printf 'GET /health/ready HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3; head -n 1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "ShrinkFrame.Web.dll"]
