# Build from the repository root:
#   docker build -f src/OperationsCopilot.Api/Dockerfile .
# The build context is the solution root so that Directory.Packages.props and every project
# referenced by the API are available to restore.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy the manifests first so `restore` is cached independently of source changes.
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY src/OperationsCopilot.Domain/*.csproj src/OperationsCopilot.Domain/
COPY src/OperationsCopilot.Infrastructure/*.csproj src/OperationsCopilot.Infrastructure/
COPY src/OperationsCopilot.Agent/*.csproj src/OperationsCopilot.Agent/
COPY src/OperationsCopilot.Api/*.csproj src/OperationsCopilot.Api/
RUN dotnet restore src/OperationsCopilot.Api/OperationsCopilot.Api.csproj

COPY src/ src/
# The knowledge base is embedded into the Infrastructure assembly at build time.
COPY docs/knowledge-base/ docs/knowledge-base/

RUN dotnet publish src/OperationsCopilot.Api/OperationsCopilot.Api.csproj \
    -c Release \
    -o /app \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# wget backs the compose health check; it is not present in the base image.
RUN apt-get update \
    && apt-get install --no-install-recommends -y wget \
    && rm -rf /var/lib/apt/lists/*

# Run unprivileged. The image ships a non-root `app` user for exactly this.
USER app

COPY --from=build --chown=app:app /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "OperationsCopilot.Api.dll"]
