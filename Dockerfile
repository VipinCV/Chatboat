# Build-time arguments – use valid Microsoft images
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:8.0

# Build stage
FROM ${SDK_IMAGE} AS build-env
WORKDIR /app

COPY *.csproj ./
RUN dotnet restore

COPY . ./
# Suppress experimental warning SKEXP0010
RUN dotnet publish -c Release -o out /p:NoWarn=SKEXP0010

# Runtime stage
FROM ${RUNTIME_IMAGE}
WORKDIR /app
COPY --from=build-env /app/out .

# Disable file watching for configuration (fixes inotify limit crash)
ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080
ENTRYPOINT ["dotnet", "BusinessChatbotApi.dll"]
