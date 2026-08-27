# Generic build image for every Nakshatra .NET microservice.
# Usage: docker build -f backend/Dockerfile --build-arg SERVICE=Nakshatra.Catalog.Service .
ARG SERVICE=Nakshatra.Gateway

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SERVICE
WORKDIR /src
COPY backend/ ./backend/
RUN dotnet publish "backend/src/${SERVICE}/${SERVICE}.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG SERVICE
# The media service transcodes uploads with ffmpeg; other services skip the extra layer.
ARG INSTALL_FFMPEG=false
RUN if [ "$INSTALL_FFMPEG" = "true" ]; then \
        apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*; \
    fi
WORKDIR /app
COPY --from=build /app/publish .
# Run as a non-root user.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENV SERVICE_DLL=${SERVICE}.dll
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet ${SERVICE_DLL}"]
