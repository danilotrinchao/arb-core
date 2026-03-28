FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Arb.Core.SignalEngine.Worker/Arb.Core.SignalEngine.Worker.csproj", "Arb.Core.SignalEngine.Worker/"]
COPY ["Arb.Core.Application/Arb.Core.Application.csproj", "Arb.Core.Application/"]
COPY ["Arb.Core.Infrastructure/Arb.Core.Infrastructure.csproj", "Arb.Core.Infrastructure/"]
COPY ["Arb.Core.Contracts/Arb.Core.Contracts.csproj", "Arb.Core.Contracts/"]
COPY ["Arb.Core.Domain/Arb.Core.Domain.csproj", "Arb.Core.Domain/"]

RUN dotnet restore "./Arb.Core.SignalEngine.Worker/Arb.Core.SignalEngine.Worker.csproj"

COPY . .

WORKDIR "/src/Arb.Core.SignalEngine.Worker"
RUN dotnet publish "./Arb.Core.SignalEngine.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Arb.Core.SignalEngine.Worker.dll"]