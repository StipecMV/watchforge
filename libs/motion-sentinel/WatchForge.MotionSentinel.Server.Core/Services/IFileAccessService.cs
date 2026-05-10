namespace WatchForge.MotionSentinel.Server.Core.Services;

public interface IFileAccessService
{
    /// <summary>Returns filenames of MP4s that have no matching detection JSON yet.</summary>
    Task<IReadOnlyList<string>> ListNewRecordingsAsync(CancellationToken ct = default);

    /// <summary>Returns the full local path for a given recording filename.</summary>
    string GetRecordingPath(string fileName);

    /// <summary>Returns true if a detection JSON already exists for the given recording filename.</summary>
    bool DetectionExists(string fileName);

    /// <summary>Writes the detection JSON for a given video filename.</summary>
    Task WriteDetectionAsync(string jsonContent, string videoFileName, CancellationToken ct = default);
}
