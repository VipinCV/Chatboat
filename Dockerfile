# 1. Use the official Microsoft .NET SDK image to build the app
FROM ://microsoft.com AS build-env
WORKDIR /app

# Copy csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build the release binaries
COPY . ./
RUN dotnet publish -c Release -o out

# 2. Build the runtime image using a slim footprint
FROM ://microsoft.com
WORKDIR /app
COPY --from=build-env /app/out .

# Render dynamically assigns a port via the PORT environment variable
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BusinessChatbotApi.dll"]
