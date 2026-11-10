# The TillKnowIT API (and the background worker's work that runs inside it). Build from this folder:  docker build -t tillknowit-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props TillKnowIT.sln ./
COPY src ./src
RUN dotnet publish src/Host/TillKnowIT.Api/TillKnowIT.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Hosts such as Render and Cloud Run tell the app which port to use in PORT; 8080 otherwise.
ENV ASPNETCORE_HTTP_PORTS=
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet TillKnowIT.Api.dll"]
