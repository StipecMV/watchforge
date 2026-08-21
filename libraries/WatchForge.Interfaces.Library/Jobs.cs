namespace WatchForge.Interfaces.Library;

/// <summary>Zdroj požiadavky — určuje prioritu jobu (FR-08).</summary>
public enum JobSource
{
    Background = 0,
    System = 1,
    WebUi = 2,
    Telegram = 3,
    WhatsApp = 4,
}

/// <summary>Priorita jobu — čím vyššie, tým skôr sa spracuje (FR-08).</summary>
public static class JobPriority
{
    public const int Background = 10;
    public const int System = 50;
    public const int WebUi = 80;
    public const int Telegram = 90;
    public const int WhatsApp = 100;

    public static int FromSource(JobSource source) => source switch
    {
        JobSource.WhatsApp => WhatsApp,
        JobSource.Telegram => Telegram,
        JobSource.WebUi => WebUi,
        JobSource.System => System,
        _ => Background,
    };
}

/// <summary>Stav jobu (stavový model v architektúre, sekcia 4).</summary>
public enum JobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Interrupted = 4,
    Cancelled = 5,
}

public static class JobStatusExtensions
{
    /// <summary>
    /// Povolené prechody stavového modelu jobu:
    /// queued → running|cancelled ; running → completed|failed|interrupted ;
    /// interrupted → queued ; failed → queued (retry).
    /// </summary>
    public static bool CanTransitionTo(this JobStatus from, JobStatus to) => (from, to) switch
    {
        (JobStatus.Queued, JobStatus.Running) => true,
        (JobStatus.Queued, JobStatus.Cancelled) => true,
        (JobStatus.Running, JobStatus.Completed) => true,
        (JobStatus.Running, JobStatus.Failed) => true,
        (JobStatus.Running, JobStatus.Interrupted) => true,
        (JobStatus.Interrupted, JobStatus.Queued) => true,
        (JobStatus.Failed, JobStatus.Queued) => true,
        _ => false,
    };
}

/// <summary>Typ jobu.</summary>
public enum JobType
{
    Download = 0,
    Analyze = 1,
    ClipExtract = 2,
    Sync = 3,
    Purge = 4,
    ExportRange = 5, // S20: export vybranej časovej úsečky (dôkazový klip)
}
