namespace WatchForge.DVRIP.Library;

/// <summary>
/// Konfigurácia <see cref="DvripClient"/> (S3-2). Namiesto pozičného
/// konštruktora s 4 stringami sa konfigurácia prenáša ako options objekt —
/// pripravené na binding z appsettings / environment variables.
/// </summary>
public sealed class DvripClientOptions
{
    public const int DefaultPort = 34567;

    /// <summary>Predvolený read timeout v sekundách (ochrana pred zamrznutím NVR).</summary>
    public const int DefaultReadTimeoutSeconds = 30;

    /// <summary>Hostname alebo IP adresa NVR (povinné).</summary>
    public string Host { get; set; } = "";

    /// <summary>DVRIP port (default 34567).</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>Prihlasovacie meno (povinné).</summary>
    public string Username { get; set; } = "";

    /// <summary>Heslo (povinné).</summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// Read timeout v sekundách pre sieťové čítania (default 30).
    /// Ak NVR neodpovedá (zamrzne, neznámy súbor), čítanie vyhodí
    /// <see cref="System.IO.IOException"/> namiesto večného čakania.
    /// </summary>
    public int ReadTimeoutSeconds { get; set; } = DefaultReadTimeoutSeconds;

    /// <summary>
    /// Overí konfiguráciu a vyhodí <see cref="ArgumentException"/> pri
    /// neplatných hodnotách. Volá sa v konštruktore <see cref="DvripClient"/>.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            throw new ArgumentException("DvripClientOptions.Host is required.", nameof(Host));
        if (Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be in range 1..65535.");
        if (string.IsNullOrWhiteSpace(Username))
            throw new ArgumentException("DvripClientOptions.Username is required.", nameof(Username));
        if (Password is null)
            throw new ArgumentException("DvripClientOptions.Password is required.", nameof(Password));
        if (ReadTimeoutSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(ReadTimeoutSeconds), ReadTimeoutSeconds, "ReadTimeoutSeconds must be >= 1.");
    }
}
