FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 ca-certificates \
    && rm -rf /var/lib/apt/lists/*
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Arb.Core.OddsIngestor.Worker/Arb.Core.OddsIngestor.Worker.csproj", "Arb.Core.OddsIngestor.Worker/"]
COPY ["Arb.Core.Application/Arb.Core.Application.csproj", "Arb.Core.Application/"]
COPY ["Arb.Core.Infrastructure/Arb.Core.Infrastructure.csproj", "Arb.Core.Infrastructure/"]
COPY ["Arb.Core.Contracts/Arb.Core.Contracts.csproj", "Arb.Core.Contracts/"]


RUN dotnet restore "./Arb.Core.OddsIngestor.Worker/Arb.Core.OddsIngestor.Worker.csproj"

COPY . .

WORKDIR "/src/Arb.Core.OddsIngestor.Worker"
RUN dotnet publish "./Arb.Core.OddsIngestor.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Arb.Core.OddsIngestor.Worker.dll"]