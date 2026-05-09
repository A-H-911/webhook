ARG PROJECT_NAME=WebhookService.API

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT_NAME
WORKDIR /src

COPY ["src/WebhookService.API/WebhookService.API.csproj", "src/WebhookService.API/"]
COPY ["src/WebhookService.Application/WebhookService.Application.csproj", "src/WebhookService.Application/"]
COPY ["src/WebhookService.Domain/WebhookService.Domain.csproj", "src/WebhookService.Domain/"]
COPY ["src/WebhookService.Infrastructure/WebhookService.Infrastructure.csproj", "src/WebhookService.Infrastructure/"]
COPY ["src/WebhookService.StreamWorker/WebhookService.StreamWorker.csproj", "src/WebhookService.StreamWorker/"]
COPY ["src/WebhookService.JobsWorker/WebhookService.JobsWorker.csproj", "src/WebhookService.JobsWorker/"]

RUN dotnet restore "src/${PROJECT_NAME}/${PROJECT_NAME}.csproj"

COPY . .

RUN dotnet publish "src/${PROJECT_NAME}/${PROJECT_NAME}.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG PROJECT_NAME
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV APP_DLL=${PROJECT_NAME}.dll

EXPOSE 8080

ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
