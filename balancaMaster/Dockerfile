# Base image with ASP.NET Core runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build image with .NET SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["balancaMaster/balancaMaster.csproj", "balancaMaster/"]
RUN dotnet restore "./balancaMaster/balancaMaster.csproj"
COPY . .
WORKDIR "/src/balancaMaster"
RUN dotnet build "./balancaMaster.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish the build output
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./balancaMaster.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final image with application and printing utilities
FROM base AS final
WORKDIR /app

# Install CUPS client utilities
RUN apt-get update && apt-get install -y cups-client

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "balancaMaster.dll"]
