# Build context: repo root  (docker build -f src/EphemeralEnvs.Web/Dockerfile .)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first, from project files only: this layer is reused until a csproj or the shared props change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/EphemeralEnvs.Web/EphemeralEnvs.Web.csproj                         src/EphemeralEnvs.Web/
COPY src/EphemeralEnvs.Contracts/EphemeralEnvs.Contracts.csproj             src/EphemeralEnvs.Contracts/
COPY src/EphemeralEnvs.ServiceDefaults/EphemeralEnvs.ServiceDefaults.csproj src/EphemeralEnvs.ServiceDefaults/
RUN dotnet restore src/EphemeralEnvs.Web/EphemeralEnvs.Web.csproj

# Only the projects this app builds from: a change in another project does not rebuild this image.
COPY src/EphemeralEnvs.Web/             src/EphemeralEnvs.Web/
COPY src/EphemeralEnvs.Contracts/       src/EphemeralEnvs.Contracts/
COPY src/EphemeralEnvs.ServiceDefaults/ src/EphemeralEnvs.ServiceDefaults/
RUN dotnet publish src/EphemeralEnvs.Web/EphemeralEnvs.Web.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app .
# Non-root user built into the .NET images.
USER $APP_UID
ENTRYPOINT ["dotnet", "EphemeralEnvs.Web.dll"]
