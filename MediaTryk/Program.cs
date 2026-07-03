using MediaTryk.Media;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddOptions<MediaLibraryOptions>()
    .Bind(builder.Configuration.GetSection(MediaLibraryOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "MediaLibrary:RootPath must be set")
    .ValidateOnStart();
builder.Services.AddSingleton<MediaPathResolver>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

app.MapGet("/api/media/browse/{*path}", (string? path, MediaPathResolver resolver) =>
    {
        if (!resolver.TryResolve(path, out var fullPath) || !Directory.Exists(fullPath))
        {
            return Results.NotFound();
        }

        var directories = new DirectoryInfo(fullPath)
            .GetDirectories()
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new MediaDirectoryDto(d.Name, Path.GetRelativePath(resolver.RootPath, d.FullName)))
            .ToList();

        var files = new DirectoryInfo(fullPath)
            .GetFiles()
            .Where(f => MediaFile.IsAllowed(f.Name))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => new MediaFileDto(
                f.Name,
                Path.GetRelativePath(resolver.RootPath, f.FullName),
                f.Length,
                f.Extension))
            .ToList();

        return Results.Ok(new MediaBrowseResultDto(path ?? string.Empty, directories, files));
    })
    .WithName("BrowseMedia");

app.MapGet("/api/media/stream/{*path}", (string? path, MediaPathResolver resolver) =>
    {
        if (string.IsNullOrEmpty(path) ||
            !resolver.TryResolve(path, out var fullPath) ||
            !File.Exists(fullPath) ||
            !MediaFile.IsAllowed(fullPath))
        {
            return Results.NotFound();
        }

        var contentType = MediaFile.GetContentType(fullPath);
        return Results.File(fullPath, contentType, enableRangeProcessing: true);
    })
    .WithName("StreamMedia");

app.Run();
