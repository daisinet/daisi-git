# Minimal runtime — base tools only (git, curl, wget, jq, unzip)
# This is the default and smallest image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY daisi-git/ ./daisi-git/
COPY daisi-sdk-dotnet/ ./daisi-sdk-dotnet/
RUN dotnet publish daisi-git/DaisiGit.Worker -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends \
    git curl wget jq unzip \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "DaisiGit.Worker.dll"]
