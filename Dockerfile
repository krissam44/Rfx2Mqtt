# ─────────────────────────────────────────────────────────────────────────────
# EN: Multi-stage Dockerfile for Rfx2Mqtt — RFXCom ↔ MQTT bridge.
#     Stage 1: build using the official .NET SDK image.
#     Stage 2: minimal runtime image (ASP.NET) — much smaller than carrying the SDK.
#     Default platforms: linux/amd64 and linux/arm64 (works on RPi 4 / 5 / RockPro).
#
# FR: Dockerfile multi-étapes pour Rfx2Mqtt — pont RFXCom ↔ MQTT.
#     Étape 1 : build avec l'image officielle .NET SDK.
#     Étape 2 : image runtime minimale (ASP.NET) — bien plus petite que de trimballer le SDK.
#     Plateformes par défaut : linux/amd64 et linux/arm64 (RPi 4 / 5 / RockPro).
#
# Build:
#   docker build -t rfx2mqtt:latest .
#   docker buildx build --platform linux/amd64,linux/arm64 -t rfx2mqtt:latest .
# ─────────────────────────────────────────────────────────────────────────────

# ── EN/FR: Build stage / Étape de build ─────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# EN: Copy only the csproj first to leverage Docker layer caching for restore.
# FR: Copier d'abord le csproj uniquement pour bénéficier du cache Docker sur le restore.
COPY Rfx2Mqtt/Rfx2Mqtt.csproj Rfx2Mqtt/
RUN dotnet restore Rfx2Mqtt/Rfx2Mqtt.csproj

# EN: Copy the rest of the source and publish a framework-dependent build.
# FR: Copier le reste des sources et publier en framework-dependent.
COPY Rfx2Mqtt/ Rfx2Mqtt/
RUN dotnet publish Rfx2Mqtt/Rfx2Mqtt.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ── EN/FR: Runtime stage / Étape runtime ────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

LABEL maintainer="Christian Sammut"
LABEL description="RFXCom ↔ MQTT bridge — Oregon/Bresser probes, Somfy RTS, Chacon/DIO, X10 Security"
LABEL org.opencontainers.image.source="https://github.com/krissam44/Rfx2Mqtt"
LABEL org.opencontainers.image.licenses="MIT"

# EN: libudev1 is needed by System.IO.Ports for USB serial enumeration on some slim images.
# FR: libudev1 est nécessaire à System.IO.Ports pour l'énumération USB sur certaines images slim.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libudev1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# EN: Web UI port — matches WebUi:Url default in Program.cs.
# FR: Port de l'UI web — correspond au défaut WebUi:Url dans Program.cs.
EXPOSE 5080

# EN: Mount point for persistent inventory (devices.yaml) and runtime state.
# FR: Point de montage pour l'inventaire persistant (devices.yaml) et l'état runtime.
VOLUME ["/app/data"]

ENV ASPNETCORE_URLS=http://+:5080

ENTRYPOINT ["dotnet", "Rfx2Mqtt.dll"]
