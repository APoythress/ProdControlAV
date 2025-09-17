# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /workspace

COPY . .

# Restore & publish for linux-arm64 self-contained single-file
RUN dotnet restore "src/ProdControlAV.Agent/ProdControlAV.Agent.csproj"
RUN dotnet publish "src/ProdControlAV.Agent/ProdControlAV.Agent.csproj" \
    -c $BUILD_CONFIGURATION \
    -r linux-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o /app/publish

FROM debian:bookworm-slim AS runtime
# create non-root user with fixed uid/gid (1000) to simplify host volume ownership
RUN groupadd -g 1000 prodcontrolav && \
    useradd -m -u 1000 -g prodcontrolav -s /usr/sbin/nologin prodcontrolav

WORKDIR /app
COPY --from=build /app/publish/ /app/

# create data dir expected by app
RUN mkdir -p /var/lib/prodcontrolav-agent/data && chown -R prodcontrolav:prodcontrolav /var/lib/prodcontrolav-agent

# run as non-root
USER prodcontrolav

ENV PRODCONTROL_AGENT_DATA_DIR=/var/lib/prodcontrolav-agent/data
ENTRYPOINT ["/app/ProdControlAV.Agent"]
