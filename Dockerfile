# syntax=docker/dockerfile:1

ARG BUILDPLATFORM

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/CustomerAssetService/CustomerAssetService.csproj", "src/CustomerAssetService/"]
RUN dotnet restore "src/CustomerAssetService/CustomerAssetService.csproj"

COPY . .
WORKDIR "/src/src/CustomerAssetService"
RUN dotnet publish "CustomerAssetService.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Staging

EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CustomerAssetService.dll"]
