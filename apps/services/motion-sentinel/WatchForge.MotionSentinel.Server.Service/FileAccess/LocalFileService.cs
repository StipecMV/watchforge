namespace WatchForge.MotionSentinel.Server.Service.FileAccess;

/// <summary>
/// IFileAccessService implementation for local filesystem access.
/// The service runs on the same machine as the NVR recordings.
/// All paths are injected from configuration — no hardcoded locations.
/// </summary>
public sealed class LocalFileService : IFileAccessService
{
    private readonly LocalFileServiceOptions _options;

    public LocalFileService(IOptions<LocalFileServiceOptions> options)
        => _options = options.Value;

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListNewRecordingsAsync(CancellationToken ct = default)
    {
        var recordings = _options.FileExtensions
            .SelectMany(ext => Directory.EnumerateFiles(_options.WatchDirectory, ext))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var analyzed = Directory
            .EnumerateFiles(_options.OutputDirectory, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> result = recordings
            .Where(r => !analyzed.Contains(Path.GetFileNameWithoutExtension(r)))
            .ToList();

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public string GetRecordingPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty.", nameof(fileName));

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("File name contains invalid characters.", nameof(fileName));

        // Reject anything that could escape the recordings directory
        if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            throw new ArgumentException("Path traversal is not allowed.", nameof(fileName));

        return Path.Combine(_options.WatchDirectory, fileName);
    }

    /// <inheritdoc/>
    public bool DetectionExists(string fileName)
    {
        var jsonName = Path.GetFileNameWithoutExtension(fileName) + ".json";
        return File.Exists(Path.Combine(_options.OutputDirectory, jsonName));
    }

    /// <inheritdoc/>
    public async Task WriteDetectionAsync(
        string jsonContent, string videoFileName, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_options.OutputDirectory);

        var jsonName   = Path.GetFileNameWithoutExtension(videoFileName) + ".json";
        var outputPath = Path.Combine(_options.OutputDirectory, jsonName);

        await File.WriteAllTextAsync(outputPath, jsonContent, ct);
    }
}
