FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish DaisiGit.Worker -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Install common build tools for RunScript steps
RUN apt-get update && apt-get install -y --no-install-recommends \
    git curl wget jq unzip \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "DaisiGit.Worker.dll"]
