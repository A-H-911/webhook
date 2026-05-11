ARG PROJECT_NAME=Hookbin.API

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT_NAME
WORKDIR /src

COPY ["src/Hookbin.API/Hookbin.API.csproj", "src/Hookbin.API/"]
COPY ["src/Hookbin.Application/Hookbin.Application.csproj", "src/Hookbin.Application/"]
COPY ["src/Hookbin.Domain/Hookbin.Domain.csproj", "src/Hookbin.Domain/"]
COPY ["src/Hookbin.Infrastructure/Hookbin.Infrastructure.csproj", "src/Hookbin.Infrastructure/"]
COPY ["src/Hookbin.StreamWorker/Hookbin.StreamWorker.csproj", "src/Hookbin.StreamWorker/"]
COPY ["src/Hookbin.JobsWorker/Hookbin.JobsWorker.csproj", "src/Hookbin.JobsWorker/"]

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
