FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MediaTryk.sln ./
COPY MediaTryk/MediaTryk.csproj MediaTryk/
RUN dotnet restore MediaTryk/MediaTryk.csproj

COPY MediaTryk/ MediaTryk/
RUN dotnet publish MediaTryk/MediaTryk.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Intel QSV/VAAPI hardware encoding: the Ubuntu handbrake-cli build has QSV
# enabled (via libvpl2); it additionally needs the Intel media driver and the
# VPL GPU runtimes (libmfx-gen1.2 for Tiger Lake and newer, libmfx1 for older
# generations). The GPU must be passed into the container at runtime:
#   docker run --device /dev/dri --group-add $(stat -c %g /dev/dri/renderD128) ...
# Without /dev/dri the app automatically falls back to software x265.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        handbrake-cli mkvtoolnix \
        intel-media-va-driver-non-free libmfx-gen1.2 libmfx1 vainfo \
    && rm -rf /var/lib/apt/lists/* \
    && id -u 99 >/dev/null 2>&1 || useradd -u 99 -g 100 -M -s /usr/sbin/nologin appuser

WORKDIR /app
COPY --from=build /app/publish .
RUN chown -R 99:100 /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER 99:100
ENTRYPOINT ["/bin/sh", "-c", "umask 002 && exec dotnet MediaTryk.dll"]
