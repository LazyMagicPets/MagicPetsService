FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy shared build configuration files
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["CommonPackageHandling.targets", "."]
COPY ["ServiceVersion.props", "."]
COPY ["Containers/StoreContainer/Nuget.Config.Docker", "./nuget.config"]

# Copy NuGet packages prepared by Deploy-DockerAws
COPY ["DockerPackages/", "DockerPackages/"]

# Copy project file and restore
COPY ["Containers/StoreContainer/StoreContainer.csproj", "StoreContainer/"]
WORKDIR /src/StoreContainer
RUN dotnet restore "StoreContainer.csproj"

# Copy all container source files
WORKDIR /src
COPY ["Containers/StoreContainer/", "StoreContainer/"]

# Copy all referenced projects (Modules, Schemas, etc.)
COPY ["Modules/", "Modules/"]
COPY ["Schemas/", "Schemas/"]

# Build
WORKDIR /src/StoreContainer
RUN dotnet build "StoreContainer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "StoreContainer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "StoreContainer.dll"]