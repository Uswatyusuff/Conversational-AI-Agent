# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy everything (respects .dockerignore at project root)
COPY . .

RUN dotnet restore
RUN dotnet build -c Release
RUN dotnet publish -c Release -o /app/publish

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install system dependencies for Playwright (headless Chromium) + curl for HEALTHCHECK
RUN apt-get update && apt-get install -y --no-install-recommends \
    wget \
    curl \
    unzip \
    gnupg \
    python3 \
    python3-pip \
    libnss3 \
    libatk-bridge2.0-0 \
    libx11-xcb1 \
    libgtk-3-0 \
    libgbm1 \
    libasound2t64 \
    libxshmfence1 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxrandr2 \
    libcups2 \
    libpango-1.0-0 \
    libpangocairo-1.0-0 \
    libatspi2.0-0 \
    libdrm2 \
    && rm -rf /var/lib/apt/lists/*

# Install Playwright browser binaries into the Playwright cache
RUN python3 -m pip install --break-system-packages playwright \
    && python3 -m playwright install chromium

COPY --from=build /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# The /health endpoint is mapped in Program.cs.
# start_period is generous because on first run the app calls EmbeddingService
# to build chunk embeddings before the HTTP server becomes available.
HEALTHCHECK --interval=30s --timeout=10s --start-period=180s --retries=5 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "CouncilChatbotPrototype.dll"]
