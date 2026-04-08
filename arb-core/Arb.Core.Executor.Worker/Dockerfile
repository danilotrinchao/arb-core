FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 ca-certificates \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Arb.Core.Executor.Worker/Arb.Core.Executor.Worker.csproj", "Arb.Core.Executor.Worker/"]
COPY ["Arb.Core.Application/Arb.Core.Application.csproj", "Arb.Core.Application/"]
COPY ["Arb.Core.Infrastructure/Arb.Core.Infrastructure.csproj", "Arb.Core.Infrastructure/"]
COPY ["Arb.Core.Contracts/Arb.Core.Contracts.csproj", "Arb.Core.Contracts/"]

RUN dotnet restore "./Arb.Core.Executor.Worker/Arb.Core.Executor.Worker.csproj"

COPY . .

WORKDIR "/src/Arb.Core.Executor.Worker"
RUN dotnet publish "./Arb.Core.Executor.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Arb.Core.Executor.Worker.dll"]