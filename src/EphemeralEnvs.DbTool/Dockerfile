# `migrator` image: one-off database steps of every environment, run before the apps start.
# Build context: repo root  (docker build -f src/EphemeralEnvs.DbTool/Dockerfile .)
#
# Contract (used by compose now, by branch environments and CI later; keep it stable):
#   ./efbundle --connection "<connection string>"         apply EF Core migrations
#   dotnet EphemeralEnvs.DbTool.dll seed [--restore]       demo data (non-production only)
#   env: ConnectionStrings__Default, App__EnvironmentName   (for DbTool)
# No ENTRYPOINT: the caller picks the command.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first, from project files only: this layer is reused until a csproj, the shared props or the tool manifest change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig dotnet-tools.json ./
COPY src/EphemeralEnvs.DbTool/EphemeralEnvs.DbTool.csproj                   src/EphemeralEnvs.DbTool/
COPY src/EphemeralEnvs.Data/EphemeralEnvs.Data.csproj                       src/EphemeralEnvs.Data/
COPY src/EphemeralEnvs.Domain/EphemeralEnvs.Domain.csproj                   src/EphemeralEnvs.Domain/
COPY src/EphemeralEnvs.ServiceDefaults/EphemeralEnvs.ServiceDefaults.csproj src/EphemeralEnvs.ServiceDefaults/
RUN dotnet tool restore && dotnet restore src/EphemeralEnvs.DbTool/EphemeralEnvs.DbTool.csproj

# Only the projects DbTool builds from: a change in Api/Web/Application/Contracts does not rebuild this image.
COPY src/EphemeralEnvs.DbTool/         src/EphemeralEnvs.DbTool/
COPY src/EphemeralEnvs.Data/           src/EphemeralEnvs.Data/
COPY src/EphemeralEnvs.Domain/         src/EphemeralEnvs.Domain/
COPY src/EphemeralEnvs.ServiceDefaults/ src/EphemeralEnvs.ServiceDefaults/
RUN dotnet publish src/EphemeralEnvs.DbTool/EphemeralEnvs.DbTool.csproj -c Release --no-restore -o /app

# Migration bundle: a single executable with all migrations of AppDbContext. Framework-dependent (the final image
# has the .NET runtime), built for the platform of this build stage.
RUN dotnet ef migrations bundle \
        --project src/EphemeralEnvs.Data --startup-project src/EphemeralEnvs.Data \
        --configuration Release --output /app/efbundle

# aspnet, not runtime: ServiceDefaults (JSON logs, shared with the apps) references the ASP.NET Core framework.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
# Non-root user built into the .NET images.
USER $APP_UID
