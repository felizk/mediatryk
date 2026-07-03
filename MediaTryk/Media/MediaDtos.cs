namespace MediaTryk.Media;

public record MediaDirectoryDto(string Name, string Path);

public record MediaFileDto(string Name, string Path, long SizeBytes, string Extension);

public record MediaBrowseResultDto(string Path, IReadOnlyList<MediaDirectoryDto> Directories, IReadOnlyList<MediaFileDto> Files);
