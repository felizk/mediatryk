# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & run

- Build: `dotnet build MediaTryk/MediaTryk.csproj -p:AllowMissingPrunePackageData=true` — without that flag the local SDK fails with NETSDK1226.
- `dotnet run` uses `launchSettings.json` and always listens on **http://localhost:5025** (`ASPNETCORE_URLS` is ignored).
- The configured library roots default to container paths (`/media`, `/source`). For a local run, override them:
  `MediaLibrary__RootPath=<dir> SourceLibrary__RootPath=<dir> dotnet run --project MediaTryk/MediaTryk.csproj`
- Runtime dependencies: `HandBrakeCLI` and `mkvmerge` (mkvtoolnix) must be on PATH.
- No test project. Verify changes end-to-end with `/verify-api` (builds, starts the server against scratch roots, exercises the API).

## Architecture (MediaTryk/)

- `Program.cs` — all endpoints (minimal API). Browse lists the **source** tree with per-file encode status; streaming serves from the **media** tree.
- `Media/MediaPathResolver.cs` — resolves relative paths against both roots and rejects traversal; all user-supplied paths must go through it.
- `Encoding/EncodeQueue.cs` — in-memory job store + channel feeding `EncodeQueueHostedService`, which encodes one job at a time via HandBrakeCLI (`--json` stdout is parsed for progress). Job updates fan out to WebSocket subscribers of `/api/encode/queue/ws`.
- `lock (job)` guards the Queued → Running/Canceled transition, shared between the worker's dequeue and `EncodeQueue.Cancel`. Keep new state transitions inside it.
- `Encoding/HandBrake/HandBrakeCapabilities.cs` probes `HandBrakeCLI --help` once for Intel QSV; if available (and `HandBrake:EnableHardwareEncoding` isn't set to false), jobs encode with `qsv_h265_10bit`, otherwise software `x265_10bit`. In Docker, QSV needs `--device /dev/dri --group-add <gid of /dev/dri/renderD128>` at run time; without it the probe fails and encoding silently falls back to software.
- Encodes write to `<dest>.encoding`, then move into place — an in-progress file is never visible through the media API.

## Constraints

- A separate client repo consumes this API: routes, DTO shapes, and WebSocket message format are contracts. Call out any breaking change explicitly, and update `docs/API.md` (the contract doc the client repo relies on) whenever they change.
- The Docker image runs as UID 99 / GID 100 with `umask 002` on purpose (Unraid host shares). Don't change the container user or the umask.
- Commit directly to `main`; every push to `main` publishes the GHCR image via GitHub Actions.