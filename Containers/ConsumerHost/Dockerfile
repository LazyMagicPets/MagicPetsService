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
COPY ["Containers/ConsumerHost/Nuget.Config.Docker", "./nuget.config"]

# Copy NuGet packages prepared by Deploy-DockerAws
COPY ["DockerPackages/", "DockerPackages/"]

# Copy project file and restore
COPY ["Containers/ConsumerHost/ConsumerHost.csproj", "ConsumerHost/"]
WORKDIR /src/ConsumerHost
RUN dotnet restore "ConsumerHost.csproj"

# Copy all container source files
WORKDIR /src
COPY ["Containers/ConsumerHost/", "ConsumerHost/"]

# Copy all referenced projects (Modules, Schemas, etc.)
COPY ["Modules/", "Modules/"]
COPY ["Schemas/", "Schemas/"]

# Build
WORKDIR /src/ConsumerHost
RUN dotnet build "ConsumerHost.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ConsumerHost.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ConsumerHost.dll"]