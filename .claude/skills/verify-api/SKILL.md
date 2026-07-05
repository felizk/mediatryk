---
name: verify-api
description: End-to-end verification of the MediaTryk API — builds, starts the server against scratch library roots, exercises browse/queue/cancel/WebSocket, and cleans up. Use after changing endpoints, the encode pipeline, or DTOs.
---

Verify the API by driving a real server instance. Work in the session scratchpad directory (call it `$SCRATCH` below).

1. **Build**: `dotnet build MediaTryk/MediaTryk.csproj -p:AllowMissingPrunePackageData=true`. Stop and report if it fails.

2. **Scratch roots**: create `$SCRATCH/source` and `$SCRATCH/media`. Put test files in `source`:
   - A bogus file (`printf 'x' > $SCRATCH/source/broken.mkv`) fails fast at mkvmerge — ideal for exercising the queue lifecycle (Queued → Running → Failed) without a long encode.
   - For real encode/progress verification, symlink an actual MKV into `source` (don't copy multi-GB files). Ask the user for one if none is known.

3. **Start the server** in the background:
   ```
   MediaLibrary__RootPath=$SCRATCH/media SourceLibrary__RootPath=$SCRATCH/source \
   dotnet run --project MediaTryk/MediaTryk.csproj --no-build -p:AllowMissingPrunePackageData=true
   ```
   It always listens on http://localhost:5025 (launchSettings pins the port). Poll `/api/encode/queue` until it responds.

4. **Exercise what changed**, at minimum:
   - `GET /api/media/browse/` — source files listed with `encodeStatus` (`NotEncoded`/`Encoding`/`Encoded` strings).
   - `POST /api/encode/queue {"path":"<file>"}` → 201; `DELETE /api/encode/queue/{id}` cancels (200 queued / 202 running, idempotent); `DELETE /api/encode/queue/finished` clears terminal jobs.
   - WebSocket `/api/encode/queue/ws`: snapshot on connect, then one message per job change. Test it with a single-file C# script (`dotnet run script.cs` works on .NET 10) using `ClientWebSocket`; there is no websocat/wscat on this machine.

5. **Clean up**: stop the server task, verify no `HandBrakeCLI` process is left (`pgrep HandBrakeCLI`), and remove scratch test files.

Report what was exercised and any behavior that differs from the expectations above.