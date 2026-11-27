# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/SurveillancePlatform.Api/SurveillancePlatform.Api.csproj src/SurveillancePlatform.Api/
RUN dotnet restore src/SurveillancePlatform.Api/SurveillancePlatform.Api.csproj
COPY src/ src/
RUN dotnet publish src/SurveillancePlatform.Api/SurveillancePlatform.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
RUN mkdir -p /data/snapshots /app/keys \
    && chown -R app:app /data /app/keys
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__LocalRootPath=/data/snapshots \
    DataProtection__KeyPath=/app/keys
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "SurveillancePlatform.Api.dll"]
