FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MediaTryk.sln ./
COPY MediaTryk/MediaTryk.csproj MediaTryk/
RUN dotnet restore MediaTryk/MediaTryk.csproj

COPY MediaTryk/ MediaTryk/
RUN dotnet publish MediaTryk/MediaTryk.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
    && apt-get install -y --no-install-recommends handbrake-cli mkvtoolnix \
    && rm -rf /var/lib/apt/lists/* \
    && id -u 99 >/dev/null 2>&1 || useradd -u 99 -g 100 -M -s /usr/sbin/nologin appuser

WORKDIR /app
COPY --from=build /app/publish .
RUN chown -R 99:100 /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER 99:100
ENTRYPOINT ["/bin/sh", "-c", "umask 002 && exec dotnet MediaTryk.dll"]
