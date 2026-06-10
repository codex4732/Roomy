# Build context is the repository root (needs global.json / Directory.Build.props).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY global.json Directory.Build.props ./
COPY src/Roomy.Api/Roomy.Api.csproj src/Roomy.Api/
RUN dotnet restore src/Roomy.Api

COPY src/ src/
RUN dotnet publish src/Roomy.Api --no-restore -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Roomy.Api.dll"]
