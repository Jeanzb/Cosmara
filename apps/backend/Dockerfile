FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY NasaExplorer.sln ./
COPY src/NasaExplorer.API/NasaExplorer.API.csproj src/NasaExplorer.API/
COPY src/NasaExplorer.Application/NasaExplorer.Application.csproj src/NasaExplorer.Application/
COPY src/NasaExplorer.Domain/NasaExplorer.Domain.csproj src/NasaExplorer.Domain/
COPY src/NasaExplorer.Infrastructure/NasaExplorer.Infrastructure.csproj src/NasaExplorer.Infrastructure/
COPY tests/NasaExplorer.Application.Tests/NasaExplorer.Application.Tests.csproj tests/NasaExplorer.Application.Tests/
COPY tests/NasaExplorer.Infrastructure.Tests/NasaExplorer.Infrastructure.Tests.csproj tests/NasaExplorer.Infrastructure.Tests/
COPY tests/NasaExplorer.API.Tests/NasaExplorer.API.Tests.csproj tests/NasaExplorer.API.Tests/
RUN dotnet restore NasaExplorer.sln

FROM restore AS build
COPY . .
RUN dotnet publish src/NasaExplorer.API/NasaExplorer.API.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache icu-libs \
    && addgroup -S nasaexplorer \
    && adduser -S nasaexplorer -G nasaexplorer
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8080
COPY --from=build --chown=nasaexplorer:nasaexplorer /app/publish .
USER nasaexplorer
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "NasaExplorer.API.dll"]
