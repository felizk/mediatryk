# MediaTryk API — client guide

Contract reference for building a client against the MediaTryk server. Copy this file into the client repo (or import it from its CLAUDE.md with `@docs/API.md`) so Claude sessions there have the full contract.

## Basics

- Base URL: `http://<host>:8080` in Docker; `http://localhost:5025` when run locally via `dotnet run`.
- No authentication. CORS allows any origin, method, and header.
- All JSON is camelCase. Timestamps are ISO 8601 with offset (e.g. `"2026-07-05T07:22:36.36+00:00"`). Enums serialize as strings.
- There are two file trees: the **source** library (original MKVs, what browse shows) and the **media** library (encoded MP4s, what streaming serves). Encoding a source file produces a media file at the *same relative path* with the extension changed to `.mp4`.

## Endpoints

### `GET /api/media/browse/{path}`

Lists one directory of the **source** tree. `path` is a root-relative directory path (omit it for the root). Returns `404` for paths that don't exist or escape the root.

Optional query parameter `encodedOnly` (bool, default `false`): with `?encodedOnly=true`, `files` only includes entries with `encodeStatus: "Encoded"`, and `directories` only includes folders whose relative path also exists as a directory in the media tree — i.e. folders that (probably) contain encoded content. Note the directory filter is existence-based: a media folder left behind by deleted encodes still shows up, and a directory can appear even if all its encoded content is in subfolders.

```json
{
  "path": "Shows/DangersInMyHeart",
  "directories": [ { "name": "Season 01", "path": "Shows/DangersInMyHeart/Season 01" } ],
  "files": [
    {
      "name": "ep1.mkv",
      "path": "Shows/DangersInMyHeart/ep1.mkv",
      "sizeBytes": 4977113228,
      "extension": ".mkv",
      "encodeStatus": "NotEncoded"
    }
  ]
}
```

`encodeStatus` is one of:
- `"Encoded"` — the corresponding `.mp4` exists in the media library (streamable now).
- `"Encoding"` — a queued or running encode job exists for this file.
- `"NotEncoded"` — neither of the above.

`sizeBytes` is the source file's size, except when `encodeStatus` is `"Encoded"`: then it's the size of the encoded `.mp4` in the media library (the file you'd actually stream).

Only `.mkv` and `.mp4` files are listed; other files are hidden.

### `GET /api/media/stream/{path}`

Streams a file from the **media** tree with HTTP range support — usable directly as a `<video src>`. `path` is the media-relative file path: take a browse result's `path` and replace the extension with `.mp4` (e.g. browse shows `Shows/X/ep1.mkv` → stream `Shows/X/ep1.mp4`). `404` if missing or not `.mkv`/`.mp4`.

### `POST /api/encode/queue`

Body: `{ "path": "<source-relative file path>" }`. Queues an encode; returns `201` with the job DTO and a `Location: /api/encode/queue/{id}` header. `404` if the path doesn't resolve to an allowed file. **No dedup** — posting the same path twice creates two jobs; check `encodeStatus`/the queue first if that matters.

### `GET /api/encode/queue` / `GET /api/encode/queue/{id}`

The full job list (ordered by `order`, see below) or a single job (`404` if unknown). Job DTO:

```json
{
  "id": "20ad184f-0556-4a13-bb16-d6af799c740e",
  "sourcePath": "Shows/X/ep1.mkv",
  "destinationPath": "Shows/X/ep1.mp4",
  "status": "Running",
  "order": 3,
  "progress": 0.42,
  "etaSeconds": 118,
  "queuedAt": "2026-07-05T07:22:36.36+00:00",
  "startedAt": "2026-07-05T07:22:36.36+00:00",
  "completedAt": null,
  "errorMessage": null
}
```

- `status`: `"Queued" | "Running" | "Completed" | "Failed" | "Canceled"`.
- `order`: integer processing-order key — queued jobs run lowest-first. **Sort by `order`, not `queuedAt`**: a requeued job (see below) keeps its original `queuedAt` but gets a new `order` that puts it at the front. Values are opaque: they can be negative, can change on requeue and across server restarts (renumbered on load), and are only meaningful relative to other jobs in the same list/snapshot. Ties are possible — a requeued job shares its `order` with the job that was running when it was requeued; break ties by `startedAt` with `null` last (i.e. the requeued job sorts *after* the one it's waiting on). The list endpoint already returns this ordering.
- `progress`: fraction 0–1, `null` until the first progress report; pinned to `1` on completion.
- `etaSeconds`: HandBrake's estimate; `null` or `0` for the first few seconds of an encode while the rate estimate stabilizes — render as "calculating…" rather than "0s".
- `errorMessage` is set only for `Failed`.
- The job list is **persisted across server restarts** (stored under the media root). A job that was `Queued` or `Running` when the server stopped comes back as `Queued` with `progress: null` and encodes again from the start; job `id`s are stable across the restart. Finished jobs reappear as history until cleared.

### `DELETE /api/encode/queue/{id}`

Cancel semantics, idempotent:
- Queued job → immediately `Canceled`, returns `200` + DTO.
- Running job → kills the encode, returns `202` + DTO (status still `Running` in the response; the `Canceled` transition arrives via WebSocket/polling moments later).
- Already finished job → `200` + DTO, unchanged.
- Unknown id → `404`.

### `POST /api/encode/queue/{id}/requeue`

Re-enqueues a `Failed` or `Canceled` job **under the same `id`** and moves it to the front of the queue: it runs right after the currently running encode (or immediately if nothing is running). The job's `status` flips back to `"Queued"`, `progress`/`startedAt`/`completedAt`/`errorMessage` reset to `null`, `queuedAt` keeps its original value, and `order` changes to the new front-of-queue position. Because the `id` is reused, WebSocket subscribers see it as a normal in-place update — no new entry appears.

- `Failed`/`Canceled` job → `200` + updated DTO.
- Job in any other state (`Queued`, `Running`, `Completed`) → `409` + unchanged DTO.
- Unknown id → `404`.

Requeueing several jobs puts each new one at the very front, so the most recently requeued runs first.

### `DELETE /api/encode/queue/finished`

Removes all finished jobs (`Completed`, `Canceled`, and `Failed`) from the list. Returns `200` with `{ "removed": <count> }`. No WebSocket message is sent for removals — see below.

## WebSocket: `ws://<host>/api/encode/queue/ws`

Live job updates. Protocol:

1. On connect, the server sends a **snapshot**: one text frame per known job (the same DTO as the REST endpoints).
2. After that, one frame per job state change. During an encode, progress updates arrive at most once per second.
3. The connection is one-way; the server ignores client frames. Close normally when done.

Client rules:

- **Treat every message as the job's current state, keyed by `id`** — upsert into a map. Messages can repeat the same state and can skip intermediate states (e.g. two `Running` frames and no `Queued` frame if transitions outraced the send loop).
- **Display the map sorted by `order`.** A requeue arrives as an update to an existing id whose `order` (and `status`) changed — re-sort on every message rather than assuming positions are fixed.
- **There is no removal message.** After calling clear-finished (or if another client does), your map holds stale entries. On reconnect, *replace* your state with the new snapshot rather than merging, and refetch `GET /api/encode/queue` after you call clear-finished yourself.
- A non-WebSocket request to this route returns `400`.

## Typical flows

- **Library UI**: `GET /api/media/browse/{dir}` per directory; show `encodeStatus` per file; offer "play" (stream URL with `.mp4` extension) when `Encoded`, "encode" when `NotEncoded`.
- **"Already encoded" view**: same browse calls with `?encodedOnly=true` — everything returned is streamable (or a folder on the way to something encoded).
- **Encode with live progress**: open the WebSocket first, then `POST /api/encode/queue`; correlate by the `id` from the 201 response and drive a progress bar from `progress`/`etaSeconds`.
- **After a job completes**: re-browse the affected directory (or flip that file locally to `Encoded`) so play buttons appear.
- **Retry a failed encode**: `POST /api/encode/queue/{id}/requeue` on the `Failed` job — same id, jumps to the front of the queue. Offer it for `Canceled` jobs too.