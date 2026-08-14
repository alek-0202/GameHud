# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/GamesHud.Api/GamesHud.Api.csproj src/GamesHud.Api/
RUN dotnet restore src/GamesHud.Api/GamesHud.Api.csproj

COPY . .
RUN dotnet publish src/GamesHud.Api/GamesHud.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GamesHud.Api.dll"]
