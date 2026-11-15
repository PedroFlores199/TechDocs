#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
command -v dotnet >/dev/null || { echo "Instala el SDK de .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0"; exit 1; }
npm ci --prefix web
npm run build --prefix web
mkdir -p src/TechDocs.Api/wwwroot/samples
cp -R web/dist/. src/TechDocs.Api/wwwroot/
cp samples/* src/TechDocs.Api/wwwroot/samples/
dotnet run --project src/TechDocs.Api --urls http://127.0.0.1:5080
