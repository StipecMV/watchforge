namespace WatchForge.MotionSentinel.Server.Core.Models;

/// <summary>Top-level output written as JSON for each analysed recording.</summary>
public sealed record DetectionResult
{
    /// <summary>File name of the analysed recording (without directory path).</summary>
    public string VideoFile { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the analysis completed, formatted as ISO 8601.</summary>
    public string AnalyzedAt { get; init; } = string.Empty;

    /// <summary>Version string of the analyser service that produced this result.</summary>
    public string AppVersion { get; init; } = string.Empty;

    /// <summary>Technical metadata extracted from the video file.</summary>
    public VideoMetadata Metadata { get; init; } = new();

    /// <summary>Ordered list of motion events detected in the recording.</summary>
    public IReadOnlyList<MotionEvent> Events { get; init; } = [];
}

