FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SolisManager/SolisManager.Client/SolisManager.Client.csproj", "SolisManager/SolisManager.Client/"]
RUN dotnet restore "SolisManager/SolisManager.Client/SolisManager.Client.csproj"
COPY . .
WORKDIR "/src/SolisManager/SolisManager.Client"
RUN dotnet build "SolisManager.Client.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "SolisManager.Client.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SolisManager.Client.dll"]
