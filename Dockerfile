# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Build.props
COPY src/Conora.Domain/Conora.Domain.csproj src/Conora.Domain/
COPY src/Conora.Infrastructure/Conora.Infrastructure.csproj src/Conora.Infrastructure/
COPY src/Conora.Repository/Conora.Repository.csproj src/Conora.Repository/
COPY src/Conora.Services/Conora.Services.csproj src/Conora.Services/
COPY src/Conora.Api/Conora.Api.csproj src/Conora.Api/

RUN dotnet restore src/Conora.Api/Conora.Api.csproj

COPY src/ src/

RUN dotnet publish src/Conora.Api/Conora.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --chown=$APP_UID:$APP_UID --from=build /app/publish .
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080

ENTRYPOINT ["dotnet", "Conora.Api.dll"]
