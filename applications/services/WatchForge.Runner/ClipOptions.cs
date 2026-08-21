namespace WatchForge.Runner;

/// <summary>
/// Konfigurácia generovania klipov (S5-5). WatchForge__Runner__Clips__* env vars.
/// </summary>
public sealed class ClipOptions
{
    /// <summary>Adresár pre vygenerované klipy (video + fotky).</summary>
    public string ClipsDir { get; set; } = "/tmp/watchforge-clips";

    /// <summary>Expirácia klipov (default 24 h — potom purge job zmaže).</summary>
    public TimeSpan ClipExpiry { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Maximálny počet klipov na jednu požiadavku (event-first, nie stovky).</summary>
    public int MaxClipsPerRequest { get; set; } = 10;

    /// <summary>Kontext pred udalosťou (sekundy) — default pre prípad chýbajúceho requestu.</summary>
    public int DefaultContextBeforeSec { get; set; } = 15;

    /// <summary>Kontext po udalosti (sekundy).</summary>
    public int DefaultContextAfterSec { get; set; } = 15;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClipsDir))
            throw new ArgumentException("ClipsDir is required.", nameof(ClipsDir));
        if (MaxClipsPerRequest < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxClipsPerRequest), "Must be >= 1.");
    }
}
