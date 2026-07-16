# syntax=docker/dockerfile:1
#
# Multi-stage build for the EBICO hosts (issue #61). A single, parameterised Dockerfile builds any of
# the ASP.NET Core web projects: `docker build .` produces the EBICO.Server image (the headline
# deliverable); pass `--build-arg PROJECT=EBICO.Suite` (and override the start command with
# EBICO.Suite.dll) to build the Blazor Suite instead. See docs/deployment/container.md.

ARG DOTNET_VERSION=10.0

# ---- build stage -------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG PROJECT=EBICO.Server
WORKDIR /src

# Restore layer: copy the manifests and the central build/version config first so `dotnet restore`
# is cached and only re-runs when a project or package reference changes. There are no lock files in
# this repo (central package management), so restore is not run in --locked-mode.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/EBICO.Core/EBICO.Core.csproj src/EBICO.Core/
COPY src/EBICO.Connector/EBICO.Connector.csproj src/EBICO.Connector/
COPY src/EBICO.Server/EBICO.Server.csproj src/EBICO.Server/
COPY src/EBICO.Suite/EBICO.Suite.csproj src/EBICO.Suite/
RUN dotnet restore "src/${PROJECT}/${PROJECT}.csproj"

# Copy the rest of the sources and publish the selected project (framework-dependent).
COPY . .
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app --no-restore

# ---- runtime stage -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app
COPY --from=build /app ./

# Kestrel binds HTTP on 8080 (the .NET container default); override via ASPNETCORE_HTTP_PORTS /
# ASPNETCORE_URLS. The emulator's own settings bind from the "Ebico" configuration section, e.g.
# Ebico__EndpointPath (see docs/deployment/container.md).
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Run as the non-root user the aspnet image provides (APP_UID).
USER $APP_UID

# ENTRYPOINT is just the runtime; CMD names the assembly so the default image runs the server and the
# compose Suite service can override it with ["EBICO.Suite.dll"].
ENTRYPOINT ["dotnet"]
CMD ["EBICO.Server.dll"]
