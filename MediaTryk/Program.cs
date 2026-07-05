using MediaTryk.Encoding;
using MediaTryk.Encoding.HandBrake;
using MediaTryk.Encoding.Mkv;
using MediaTryk.Media;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<MediaLibraryOptions>()
    .Bind(builder.Configuration.GetSection(MediaLibraryOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "MediaLibrary:RootPath must be set")
    .ValidateOnStart();
builder.Services.AddSingleton<MediaPathResolver>();

builder.Services.AddOptions<SourceLibraryOptions>()
    .Bind(builder.Configuration.GetSection(SourceLibraryOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "SourceLibrary:RootPath must be set")
    .ValidateOnStart();

builder.Services.AddOptions<HandBrakeOptions>()
    .Bind(builder.Configuration.GetSection(HandBrakeOptions.SectionName));
builder.Services.AddSingleton<HandBrakeCapabilities>();

builder.Services.AddSingleton<EncodeQueueStateStore>();
builder.Services.AddSingleton<EncodeQueue>();
builder.Services.AddSingleton<MkvMergeIdentifier>();
builder.Services.AddSingleton<IVideoEncoder, HandBrakeVideoEncoder>();
builder.Services.AddHostedService<EncodeQueueHostedService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();
app.UseWebSockets();

app.MapGet("/api/media/browse/{*path}", (string? path, MediaPathResolver resolver, EncodeQueue queue, bool encodedOnly = false) =>
    {
        if (!resolver.TryResolveSource(path, out var fullPath) || !Directory.Exists(fullPath))
        {
            return Results.NotFound();
        }

        var directories = new DirectoryInfo(fullPath)
            .GetDirectories()
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new MediaDirectoryDto(d.Name, Path.GetRelativePath(resolver.SourceRootPath, d.FullName)))
            .Where(d => !encodedOnly || Directory.Exists(Path.Combine(resolver.MediaRootPath, d.Path)))
            .ToList();

        var files = new DirectoryInfo(fullPath)
            .GetFiles()
            .Where(f => MediaFile.IsAllowed(f.Name))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f =>
            {
                var relativePath = Path.GetRelativePath(resolver.SourceRootPath, f.FullName);
                var encodedFile = new FileInfo(Path.Combine(
                    resolver.MediaRootPath,
                    Path.ChangeExtension(relativePath, HandBrakeEncodeProfile.OutputExtension)));

                var status = encodedFile.Exists ? MediaFileEncodeStatus.Encoded
                    : queue.IsActive(relativePath) ? MediaFileEncodeStatus.Encoding
                    : MediaFileEncodeStatus.NotEncoded;

                // Once encoded, the size that matters is the streamable file's.
                var sizeBytes = encodedFile.Exists ? encodedFile.Length : f.Length;

                return new MediaFileDto(f.Name, relativePath, sizeBytes, f.Extension, status);
            })
            .Where(f => !encodedOnly || f.EncodeStatus == MediaFileEncodeStatus.Encoded)
            .ToList();

        return Results.Ok(new MediaBrowseResultDto(path ?? string.Empty, directories, files));
    })
    .WithName("BrowseMedia");

app.MapGet("/api/media/stream/{*path}", (string? path, MediaPathResolver resolver) =>
    {
        if (string.IsNullOrEmpty(path) ||
            !resolver.TryResolveMedia(path, out var fullPath) ||
            !File.Exists(fullPath) ||
            !MediaFile.IsAllowed(fullPath))
        {
            return Results.NotFound();
        }

        var contentType = MediaFile.GetContentType(fullPath);
        return Results.File(fullPath, contentType, enableRangeProcessing: true);
    })
    .WithName("StreamMedia");

app.MapPost("/api/encode/queue", (EncodeQueueRequestDto request, MediaPathResolver resolver, EncodeQueue queue) =>
    {
        if (string.IsNullOrWhiteSpace(request.Path) ||
            !resolver.TryResolveSource(request.Path, out var fullPath) ||
            !File.Exists(fullPath) ||
            !MediaFile.IsAllowed(fullPath))
        {
            return Results.NotFound();
        }

        // Normalized so it matches the relative paths reported by browse.
        var job = queue.Enqueue(Path.GetRelativePath(resolver.SourceRootPath, fullPath));
        return Results.Created($"/api/encode/queue/{job.Id}", job.ToDto());
    })
    .WithName("QueueEncode");

app.MapGet("/api/encode/queue", (EncodeQueue queue) =>
        Results.Ok(queue.GetAll().Select(j => j.ToDto())))
    .WithName("ListEncodeJobs");

app.MapGet("/api/encode/queue/{id:guid}", (Guid id, EncodeQueue queue) =>
        queue.TryGet(id, out var job) ? Results.Ok(job!.ToDto()) : Results.NotFound())
    .WithName("GetEncodeJob");

app.MapDelete("/api/encode/queue/{id:guid}", (Guid id, EncodeQueue queue) =>
        queue.Cancel(id, out var job) switch
        {
            EncodeCancelResult.Canceled => Results.Ok(job!.ToDto()),
            EncodeCancelResult.CancellationRequested => Results.Accepted(value: job!.ToDto()),
            EncodeCancelResult.AlreadyFinished => Results.Ok(job!.ToDto()),
            _ => Results.NotFound()
        })
    .WithName("CancelEncodeJob");

app.MapPost("/api/encode/queue/{id:guid}/requeue", (Guid id, EncodeQueue queue) =>
        queue.Requeue(id, out var job) switch
        {
            EncodeRequeueResult.Requeued => Results.Ok(job!.ToDto()),
            EncodeRequeueResult.NotRequeueable => Results.Conflict(job!.ToDto()),
            _ => Results.NotFound()
        })
    .WithName("RequeueEncodeJob");

app.MapDelete("/api/encode/queue/finished", (EncodeQueue queue) =>
        Results.Ok(new { Removed = queue.ClearFinished() }))
    .WithName("ClearFinishedEncodeJobs");

app.Map("/api/encode/queue/ws", async (HttpContext context, EncodeQueue queue) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            return Results.BadRequest();
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await EncodeQueueWebSocketHandler.RunAsync(socket, queue, context.RequestAborted);
        return Results.Empty;
    })
    .WithName("WatchEncodeQueue");

app.Run();
