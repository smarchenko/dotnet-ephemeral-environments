# Build context: repo root  (docker build -f src/EphemeralEnvs.Api/Dockerfile .)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first, from project files only: this layer is reused until a csproj or the shared props change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/EphemeralEnvs.Api/EphemeralEnvs.Api.csproj                         src/EphemeralEnvs.Api/
COPY src/EphemeralEnvs.Application/EphemeralEnvs.Application.csproj         src/EphemeralEnvs.Application/
COPY src/EphemeralEnvs.Contracts/EphemeralEnvs.Contracts.csproj             src/EphemeralEnvs.Contracts/
COPY src/EphemeralEnvs.Data/EphemeralEnvs.Data.csproj                       src/EphemeralEnvs.Data/
COPY src/EphemeralEnvs.Domain/EphemeralEnvs.Domain.csproj                   src/EphemeralEnvs.Domain/
COPY src/EphemeralEnvs.ServiceDefaults/EphemeralEnvs.ServiceDefaults.csproj src/EphemeralEnvs.ServiceDefaults/
RUN dotnet restore src/EphemeralEnvs.Api/EphemeralEnvs.Api.csproj

COPY src/ src/
RUN dotnet publish src/EphemeralEnvs.Api/EphemeralEnvs.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app .
# Non-root user built into the .NET images.
USER $APP_UID
ENTRYPOINT ["dotnet", "EphemeralEnvs.Api.dll"]
