using System.Globalization;

namespace WatchForge.NVR.Client.TestApp;

public sealed class NvrFile
{
    public string FileName { get; init; } = "";
    public DateTime BeginTime { get; init; }
    public DateTime EndTime { get; init; }
    public long FileLengthBytes { get; init; }
    public int DiskNo { get; init; }
    public int SerialNo { get; init; }

    public double FileLengthMB => FileLengthBytes / 1_048_576.0;

    /// <summary>
    /// Parses DVRIP datetime strings. Handles both the standard format
    /// ("2026-04-02 10:08:02") and the malformed format returned by some
    /// Sofia/Xiongmai firmware ("2026-04-0210:08:02" — no space between date and time).
    /// Returns <see cref="DateTime.MinValue"/> for null, empty, or unparseable input.
    /// </summary>
    public static DateTime ParseNvrDateTime(string? s)
    {
        if (string.IsNullOrEmpty(s)) return DateTime.MinValue;

        // Standard format
        if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;

        // Malformed NVR format: "2026-04-0210:08:02" (18 chars, no space)
        // Date part is always 10 chars ("yyyy-MM-dd"), time follows immediately.
        if (s.Length >= 18)
        {
            var normalized = s[..10] + " " + s[10..];
            if (DateTime.TryParseExact(normalized, "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
                return dt2;
        }

        return DateTime.MinValue;
    }

    /// <summary>
    /// Parses DVRIP FileLength values. Accepts hex strings ("0x00103D75") and plain decimals.
    /// FileLength is in 1024-byte blocks (confirmed against real Movols/Xiongmai NVR).
    /// Returns 0 for null, empty, or unparseable input.
    /// </summary>
    public static long ParseFileLength(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        try
        {
            long blocks = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt64(s, 16)
                : long.Parse(s, CultureInfo.InvariantCulture);
            return blocks * 1024;
        }
        catch
        {
            return 0;
        }
    }
}
