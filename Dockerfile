# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Install tools needed by download-cyberchef.sh
RUN apt-get update && apt-get install -y --no-install-recommends unzip curl && rm -rf /var/lib/apt/lists/*

# Copy project files and restore
COPY src/Directory.Build.props src/Directory.Build.props
COPY src/Directory.Packages.props src/Directory.Packages.props
COPY src/shmoxy.slnx src/shmoxy.slnx
COPY src/shmoxy/shmoxy.csproj src/shmoxy/shmoxy.csproj
COPY src/shmoxy.api/shmoxy.api.csproj src/shmoxy.api/shmoxy.api.csproj
COPY src/shmoxy.frontend/shmoxy.frontend.csproj src/shmoxy.frontend/shmoxy.frontend.csproj
COPY src/shmoxy.shared/shmoxy.shared.csproj src/shmoxy.shared/shmoxy.shared.csproj
RUN dotnet restore src/shmoxy.api/shmoxy.api.csproj
RUN dotnet restore src/shmoxy/shmoxy.csproj

# Copy everything else
COPY src/ src/
COPY scripts/ scripts/

# Download CyberChef assets (gitignored, must be fetched at build time)
RUN scripts/download-cyberchef.sh

ARG VERSION=0.0.0-dev

# Publish API (includes frontend RCL assets)
RUN dotnet publish src/shmoxy.api -c Release -o /app /p:Version="$VERSION" --nologo -v quiet

# Publish proxy into its own subdirectory to avoid DLL conflicts with the API
RUN dotnet publish src/shmoxy -c Release -o /app/proxy /p:Version="$VERSION" --nologo -v quiet

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# API port
EXPOSE 5000
# Proxy port
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:5000
ENV ApiConfig__ProxyPort=8080

ENTRYPOINT ["dotnet", "shmoxy.api.dll"]
