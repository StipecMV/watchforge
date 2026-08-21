namespace WatchForge.DVRIP.Library;

/// <summary>
/// Contract for a minimal DVRIP client for Xiongmai/Sofia-based NVR devices
/// (e.g. Movols brand). S3-2: interface oddelenie — umožňuje nahradiť
/// implementáciu (fake server v testoch, mock, iný firmware) a testovať
/// závislé komponenty (Runner, API) bez reálneho NVR.
/// </summary>
public interface IDvripClient : IDisposable
{
    /// <summary>
    /// Opens a TCP connection to the NVR and authenticates.
    /// </summary>
    Task<LoginResult> LoginAsync(CancellationToken ct = default);

    /// <summary>
    /// Queries recorded files in the given time range and channel.
    /// Channel is zero-indexed (0 = first channel). Returns an empty list
    /// if the NVR has no files for the period.
    /// </summary>
    Task<List<NvrFile>> QueryFilesAsync(
        DateTime from, DateTime to, int channel = 0, CancellationToken ct = default);

    /// <summary>
    /// Downloads a recorded file, converts it via ffmpeg, and returns the
    /// output path. Supported output formats: "mp4" (default) or "mkv".
    /// S22n: <paramref name="trimFromSegmentStart"/> — oreže výstup na 15-min okno
    /// (offset od začiatku segmentu; null = celý segment).
    /// </summary>
    Task<string> DownloadFileAsync(
        NvrFile file, string destinationPath, string outputFormat = "mp4",
        IProgress<long>? progress = null, TimeSpan? trimFromSegmentStart = null,
        CancellationToken ct = default);

    /// <summary>S19: Live monitor stream z NVR (OPMonitor) — volá onData pre každý HEVC paket.</summary>
    Task MonitorStreamAsync(
        int channel, string streamType, Func<ReadOnlyMemory<byte>, CancellationToken, Task> onData,
        CancellationToken ct = default);
}
