namespace MediaTryk.Media;

public record MediaDirectoryDto(string Name, string Path);

public enum MediaFileEncodeStatus
{
    NotEncoded,
    Encoding,
    Encoded
}

public record MediaFileDto(string Name, string Path, long SizeBytes, string Extension, MediaFileEncodeStatus EncodeStatus);

public record MediaBrowseResultDto(string Path, IReadOnlyList<MediaDirectoryDto> Directories, IReadOnlyList<MediaFileDto> Files);
