FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Scale5x2Migration.sln ./
COPY src/Core/Domain/Domain.csproj src/Core/Domain/
COPY src/Core/Application/Application.csproj src/Core/Application/
COPY src/Infrastructure/Data/Data.csproj src/Infrastructure/Data/
COPY src/Presentation/WebApp.Blazor/WebApp.Blazor.csproj src/Presentation/WebApp.Blazor/

RUN dotnet restore src/Presentation/WebApp.Blazor/WebApp.Blazor.csproj

COPY src/ src/
RUN dotnet publish src/Presentation/WebApp.Blazor/WebApp.Blazor.csproj \
    --configuration Release \
    --no-restore \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish ./

USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "WebApp.Blazor.dll"]

