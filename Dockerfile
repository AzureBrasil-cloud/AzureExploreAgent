# syntax=docker/dockerfile:1

# ---- Build ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached until a project file changes.
COPY AzureExploreAgent.sln ./
COPY src/AzureExploreAgent.Api/AzureExploreAgent.Api.csproj src/AzureExploreAgent.Api/
COPY src/AzureExploreAgent.Application/AzureExploreAgent.Application.csproj src/AzureExploreAgent.Application/
COPY src/AzureExploreAgent.Infrastructure/AzureExploreAgent.Infrastructure.csproj src/AzureExploreAgent.Infrastructure/
RUN dotnet restore src/AzureExploreAgent.Api/AzureExploreAgent.Api.csproj

COPY src/ src/
RUN dotnet publish src/AzureExploreAgent.Api/AzureExploreAgent.Api.csproj \
    -c Release --no-restore -o /app/publish

# ---- Runtime ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Azure CLI is a hard runtime requirement: the app starts `az` as a child process
# (see AzureCli:ExecutablePath). Without it, startup fails at the service principal login.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl \
 && curl -sL https://aka.ms/InstallAzureCLIDeb | bash \
 && az version \
 && apt-get purge -y curl \
 && apt-get autoremove -y \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Isolated Azure CLI session; mount a writable volume here if the root filesystem is read only.
ENV AzureCli__ConfigDirectory=/home/app/.azure-explore-agent \
    ASPNETCORE_HTTP_PORTS=8080

# aspnet:10.0 ships a non-root `app` user.
RUN mkdir -p /home/app/.azure-explore-agent && chown -R app:app /home/app
USER app

EXPOSE 8080
ENTRYPOINT ["dotnet", "AzureExploreAgent.Api.dll"]
