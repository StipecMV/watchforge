namespace WatchForge.MotionSentinel.Server.Service.FileAccess;

/// <summary>Configuration options for <see cref="LocalFileService"/>, bound from the <c>MotionSentinel</c> config section.</summary>
public sealed record LocalFileServiceOptions
{
    /// <summary>Absolute path to the folder containing NVR recordings.</summary>
    public string WatchDirectory { get; init; } = string.Empty;

    /// <summary>Absolute path where detection JSON files will be written.</summary>
    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>Glob patterns for video files to watch and process (e.g. "*.mp4", "*.mkv").</summary>
    public IReadOnlyList<string> FileExtensions { get; init; } = ["*.mp4"];
}
