using System.Text.Json.Serialization;

namespace MediaTryk.Media;

public record MediaDirectoryDto(string Name, string Path);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MediaFileEncodeStatus
{
    NotEncoded,
    Encoding,
    Encoded
}

public record MediaFileDto(string Name, string Path, long SizeBytes, string Extension, MediaFileEncodeStatus EncodeStatus);

public record MediaBrowseResultDto(string Path, IReadOnlyList<MediaDirectoryDto> Directories, IReadOnlyList<MediaFileDto> Files);

public record EncodedMediaDto(string? SourcePath, string MediaPath, long SizeBytes, DateTimeOffset EncodedAt);

public record MediaStatusRequestDto(List<string>? Paths);

public record MediaStatusDto(string Path, bool Exists, MediaFileEncodeStatus EncodeStatus, long? SizeBytes, Guid? JobId);
