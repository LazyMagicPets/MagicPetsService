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
COPY ["Containers/AspNetContainer/Nuget.Config.Docker", "."]

# Copy NuGet packages prepared by Deploy-DockerAws
COPY ["DockerPackages/", "DockerPackages/"]

# Copy project file and restore
COPY ["Containers/AspNetContainer/AspNetContainer.csproj", "AspNetContainer/"]
WORKDIR /src/AspNetContainer
RUN dotnet restore "AspNetContainer.csproj" --configfile /src/Nuget.Config.Docker

# Copy all container source files
WORKDIR /src
COPY ["Containers/AspNetContainer/", "AspNetContainer/"]

# Copy all referenced projects (Modules, Schemas, etc.)
COPY ["Modules/", "Modules/"]
COPY ["Schemas/", "Schemas/"]

# Build
WORKDIR /src/AspNetContainer
RUN dotnet build "AspNetContainer.csproj" -c Release -o /app/build --configfile /src/Nuget.Config.Docker

FROM build AS publish
RUN dotnet publish "AspNetContainer.csproj" -c Release -o /app/publish --configfile /src/Nuget.Config.Docker

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AspNetContainer.dll"]