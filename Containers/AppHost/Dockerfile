FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# AWS Lambda Web Adapter: runs this ASP.NET host on AWS Lambda (Function URL / API Gateway) by
# proxying Lambda invocations to the app's HTTP listener on AWS_LWA_PORT. The adapter is a Lambda
# extension under /opt/extensions; it is launched only by the Lambda execution environment's
# extension loader, so on non-Lambda hosts (ECS/Fargate, EC2, local) nothing reads /opt/extensions
# and the binary stays dormant. This single image therefore works for both lambda-* and ecs-* topologies.
COPY --from=public.ecr.aws/awsguru/aws-lambda-adapter:1.0.1 /lambda-adapter /opt/extensions/lambda-adapter
# The aspnet:10.0 image already bakes ASPNETCORE_HTTP_PORTS=8080, so the app listens on 0.0.0.0:8080.
# AWS_LWA_PORT must match that port; EXPOSE documents it.
ENV AWS_LWA_PORT=8080
# Point the LWA readiness check at the real health endpoint instead of relying on the default "/" probe.
ENV AWS_LWA_READINESS_CHECK_PATH=/health
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

# Restore the specific container project (separate layer so package restore is cached)
WORKDIR /src/Containers/${ContainerName}
RUN dotnet restore "${ContainerName}.csproj"

# Publish directly (publish builds internally; a standalone `dotnet build` would be redundant since
# only /app/publish is copied into the final image). --no-restore reuses the cached restore above.
FROM build AS publish
ARG ContainerName=""
RUN dotnet publish "${ContainerName}.csproj" -c Release -o /app/publish \
    --no-restore -p:GeneratePackageOnBuild=false

FROM base AS final
ARG ContainerName=""
ENV CONTAINER_DLL=${ContainerName}.dll
WORKDIR /app
COPY --from=publish /app/publish .
# exec-form via sh so ${CONTAINER_DLL} is expanded AND `exec` makes dotnet PID 1, so SIGTERM is
# forwarded for graceful shutdown on ECS/Fargate. On Lambda the LWA extension manages lifecycle.
ENTRYPOINT ["sh", "-c", "exec dotnet ${CONTAINER_DLL}"]
