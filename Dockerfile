FROM node:22-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/ src/
RUN dotnet publish src/TechDocs.Api -c Release -o /app/api
RUN dotnet publish src/TechDocs.Worker -c Release -o /app/worker

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends libfreetype6 libfontconfig1 fonts-dejavu-core curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/api ./
COPY --from=web /web/dist ./wwwroot
COPY samples/ ./wwwroot/samples/
RUN chown -R app:app /app
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TechDocs.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=build /app/worker ./
USER app
ENTRYPOINT ["dotnet", "TechDocs.Worker.dll"]
