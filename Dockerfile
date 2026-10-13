# Windows containers, not Linux: VMS.StorageEngine's ImmutableArchiveManager calls Windows-only
# ACL APIs (System.Security.AccessControl.FileSystemAccessRule) to write-protect archive
# files — those throw PlatformNotSupportedException on Linux, so a Linux base image would build
# fine and then crash the moment anything touches the archive. Requires Docker Desktop switched
# to "Windows containers" mode (right-click the tray icon) to build/run — it cannot run
# side-by-side with this repo's own docker-compose.yml, which runs vms-postgres/vms-es0x as
# ordinary Linux containers.
#
# Build:   docker build -t vms-backend .
# Run:     docker run -p 5080:5080 ^
#            -e ConnectionStrings__VmsDatabase="Host=host.docker.internal;Port=5433;Database=vms;Username=vms;Password=vms_dev_password" ^
#            -e Vms__ElasticsearchUri="http://host.docker.internal:9200" ^
#            vms-backend
#          (appsettings.json's "localhost" only means something inside the container itself —
#          the ASP.NET Core double-underscore env var convention above overrides it to reach
#          Postgres/Elasticsearch running as this repo's own docker-compose.yml containers on the
#          host instead; swap host.docker.internal for a real hostname in a non-dev deployment).
#          Archive/Clips/Snapshots/EMaps default to relative paths under /app and so live inside
#          the container's own writable layer unless also given a volume mount, e.g. add
#          `-v C:\vms-data:C:\app\Archive` for just the archive, or point ArchiveRootPath/etc at
#          C:\data entirely via more -e overrides and mount a single `-v C:\vms-data:C:\data`.
FROM mcr.microsoft.com/dotnet/sdk:10.0-nanoserver-ltsc2022 AS build
WORKDIR /src

COPY VMS.Core/VMS.Core.csproj VMS.Core/
COPY VMS.MediaEngine/VMS.MediaEngine.csproj VMS.MediaEngine/
COPY VMS.StorageEngine/VMS.StorageEngine.csproj VMS.StorageEngine/
COPY VMS.MetadataIndexer/VMS.MetadataIndexer.csproj VMS.MetadataIndexer/
COPY VMS.Backend.Server/VMS.Backend.Server.csproj VMS.Backend.Server/
RUN dotnet restore VMS.Backend.Server/VMS.Backend.Server.csproj

COPY VMS.Core/ VMS.Core/
COPY VMS.MediaEngine/ VMS.MediaEngine/
COPY VMS.StorageEngine/ VMS.StorageEngine/
COPY VMS.MetadataIndexer/ VMS.MetadataIndexer/
COPY VMS.Backend.Server/ VMS.Backend.Server/
RUN dotnet publish VMS.Backend.Server/VMS.Backend.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-nanoserver-ltsc2022 AS runtime
WORKDIR /app
COPY --from=build /app .

# One level above WORKDIR (C:/, not C:/app/) because that's what appsettings.json's
# "../tools/ffmpeg/ffmpeg.exe" / "../models/..." / "../tessdata" already assume — those paths
# were written for `dotnet run`, whose working directory is the project folder one level below
# the repo root, and this COPY layout reproduces that same relative shape so the paths don't
# need a container-specific override. Provisioned once via scripts/setup-tools.ps1 on the build
# host (not downloaded inside the image) — keeps the image small and avoids re-downloading
# ~600MB+ of tooling on every rebuild.
COPY tools/ffmpeg/ C:/tools/ffmpeg/
COPY models/ C:/models/
COPY tessdata/ C:/tessdata/

EXPOSE 5080
ENTRYPOINT ["dotnet", "VMS.Backend.Server.dll", "--urls", "http://0.0.0.0:5080"]
