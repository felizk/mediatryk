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
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MediaTryk.dll"]
