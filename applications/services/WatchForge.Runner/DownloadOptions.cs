namespace WatchForge.Runner;

/// <summary>
/// Konfigurácia download jobov (S4-3). Hodnoty cez WATCHFORGE_RUNNER__Download__* env vars.
/// </summary>
public sealed class DownloadOptions
{
    /// <summary>Adresár pre dočasné stiahnuté videá (media cache).</summary>
    public string TempDir { get; set; } = "/tmp/watchforge-media";

    /// <summary>Výstupný formát po konverzii: "mp4" (default) alebo "mkv".</summary>
    public string OutputFormat { get; set; } = "mp4";

    /// <summary>Maximálny počet pokusov downloadu (default 3).</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Vek .downloading súboru, po ktorom sa považuje za zvyšok po reštarte (default 1 h).</summary>
    public TimeSpan StaleDownloadingAge { get; set; } = TimeSpan.FromHours(1);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TempDir))
            throw new ArgumentException("TempDir is required.", nameof(TempDir));
        if (OutputFormat is not ("mp4" or "mkv"))
            throw new ArgumentException("OutputFormat must be 'mp4' or 'mkv'.", nameof(OutputFormat));
        if (MaxRetries < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxRetries), "Must be >= 1.");
    }
}
