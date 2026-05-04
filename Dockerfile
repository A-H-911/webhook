FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/WebhookService.API/WebhookService.API.csproj", "src/WebhookService.API/"]
COPY ["src/WebhookService.Application/WebhookService.Application.csproj", "src/WebhookService.Application/"]
COPY ["src/WebhookService.Domain/WebhookService.Domain.csproj", "src/WebhookService.Domain/"]
COPY ["src/WebhookService.Infrastructure/WebhookService.Infrastructure.csproj", "src/WebhookService.Infrastructure/"]

RUN dotnet restore "src/WebhookService.API/WebhookService.API.csproj"

COPY . .

RUN dotnet publish "src/WebhookService.API/WebhookService.API.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "WebhookService.API.dll"]
