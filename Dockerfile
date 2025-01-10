FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS publish
WORKDIR /src
COPY . .
WORKDIR "/src"
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish SolisManager/SolisManager.csproj -c Release --runtime linux-x64 --self-contained true -p:PublishTrimmed=false --property:PublishDir=/app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["/app/SolisManager", "/appdata"]
