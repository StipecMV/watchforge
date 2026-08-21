namespace WatchForge.Runner;

/// <summary>
/// Konfigurácia Runnera (S4-1). Poll JOBS frontu, semafory pre súbežnosť.
/// Hodnoty sa dajú prepísať cez environment variables (WATCHFORGE_RUNNER__*).
/// </summary>
public sealed class RunnerOptions
{
    // S16: po reálnom benchmarku (S15 v2 — NVR neškrtí agregovanú šírku, per-stream
    // 5 Mbit/s drží do 8 streamov) zvýšené z 2 → 4. Analýza je bottleneck (Farneback
    // ~616 ms/f @1080p), 8 jadier zvládne 4 paralelné analýzy na 81 % CPU.
    public const int DefaultMaxParallelAnalyses = 4;
    public const int DefaultMaxParallelDownloads = 4;
    public const int DefaultPollIntervalMs = 1000;

    /// <summary>Maximálny počet súbežných Analyze/ClipExtract jobov (default 4 — 8 jadier, S15 benchmark).</summary>
    public int MaxParallelAnalyses { get; set; } = DefaultMaxParallelAnalyses;

    /// <summary>Maximálny počet súbežných Download jobov (default 4 — NVR drží 5 Mbit/s per stream).</summary>
    public int MaxParallelDownloads { get; set; } = DefaultMaxParallelDownloads;

    /// <summary>
    /// S22j: analýzy zapnuté/vypnuté (WatchForge__Runner__AnalysesEnabled).
    /// false = AutoPlanner nevytvára nové Analyze joby (backlog sa nespracúva).
    /// </summary>
    public bool AnalysesEnabled { get; set; } = true;

    /// <summary>Interval pollovania job frontu v ms (default 1000).</summary>
    public int PollIntervalMs { get; set; } = DefaultPollIntervalMs;

    /// <summary>S22l: dĺžka okna v minútach (default 15 — kvartál NVR segmentov).</summary>
    public int WindowMinutes { get; set; } = 15;

    /// <summary>S22l: počet okien držaných lokálne (default 4 = 60 min; pribudnú disky → zvýšiť).</summary>
    public int RetentionWindows { get; set; } = 4;

    /// <summary>S22l: interval sync jobu (default 15 min — NVR uzatvára segment na kvartál).</summary>
    public int SyncIntervalMinutes { get; set; } = 15;

    /// <summary>Cesta k SQLite databáze (default watchforge.db v pracovnom adresári).</summary>
    public string DbPath { get; set; } = "watchforge.db";

    /// <summary>Port internal HTTP endpointov (S4-6, default 8081).</summary>
    public int HttpPort { get; set; } = 8081;

    public void Validate()
    {
        if (MaxParallelAnalyses < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxParallelAnalyses), "Must be >= 1.");
        if (MaxParallelDownloads < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxParallelDownloads), "Must be >= 1.");
        if (PollIntervalMs < 50)
            throw new ArgumentOutOfRangeException(nameof(PollIntervalMs), "Must be >= 50 ms.");
        if (string.IsNullOrWhiteSpace(DbPath))
            throw new ArgumentException("DbPath is required.", nameof(DbPath));
    }
}
