namespace WatchForge.MotionSentinel.Server.Service.FileAccess;

/// <summary>Configuration options for <see cref="LocalFileService"/>, bound from the <c>Files</c> config section.</summary>
public sealed record LocalFileServiceOptions
{
    /// <summary>Absolute path to the folder containing NVR MP4 recordings.</summary>
    public string RecordingsPath { get; init; } = string.Empty;

    /// <summary>Absolute path where detection JSON files will be written.</summary>
    public string DetectionsPath { get; init; } = string.Empty;
}
