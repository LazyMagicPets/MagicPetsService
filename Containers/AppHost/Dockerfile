FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG ContainerName=""

# Copy entire solution
COPY . .

# Generate nuget.config to use DockerPackages folder
RUN echo '<?xml version="1.0" encoding="utf-8"?>' > nuget.config && \
    echo '<configuration>' >> nuget.config && \
    echo '  <packageSources>' >> nuget.config && \
    echo '    <clear />' >> nuget.config && \
    echo '    <add key="DockerCache" value="/src/DockerPackages" />' >> nuget.config && \
    echo '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />' >> nuget.config && \
    echo '  </packageSources>' >> nuget.config && \
    echo '  <fallbackPackageFolders>' >> nuget.config && \
    echo '    <clear />' >> nuget.config && \
    echo '  </fallbackPackageFolders>' >> nuget.config && \
    echo '</configuration>' >> nuget.config

# Restore the specific container project
WORKDIR /src/Containers/${ContainerName}
RUN dotnet restore "${ContainerName}.csproj"

# Build (disable package generation to avoid NuGet pack issues)
RUN dotnet build "${ContainerName}.csproj" -c Release -o /app/build -p:GeneratePackageOnBuild=false

FROM build AS publish
ARG ContainerName=""
RUN dotnet publish "${ContainerName}.csproj" -c Release -o /app/publish

FROM base AS final
ARG ContainerName=""
ENV CONTAINER_DLL=${ContainerName}.dll

# Lambda Web Adapter: lets this SAME image serve HTTP on ECS/Fargate AND run as a
# Lambda container image. On ECS the extension is never loaded (only the Lambda
# runtime reads /opt/extensions), so it is inert; on Lambda it proxies the Runtime
# API to the app listening on AWS_LWA_PORT. See Platform/LambdaTopology.md §3.
COPY --from=public.ecr.aws/awsguru/aws-lambda-adapter:0.9.0 /lambda-adapter /opt/extensions/lambda-adapter
ENV AWS_LWA_PORT=8080

WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT sh -c "dotnet ${CONTAINER_DLL}"