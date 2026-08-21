namespace WatchForge.DVRIP.Library.Models;

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

    /// <summary>
    /// Parsuje názov nahrávky z NVR, napr. "[Ch0]_2026-04-05_15.00.00-15.15.mkv".
    /// Formát: [ChN]_YYYY-MM-DD_HH.MM[.SS]-HH.MM[.SS]  (koniec môže byť bez sekúnd — reálny Movols NVR).
    /// Časové zložky môžu byť aj bez vedúcich núl ("6.00.00").
    /// </summary>
    public static NvrFileName ParseNvrFilename(string? filename)
    {
        if (string.IsNullOrEmpty(filename)) return NvrFileName.Invalid;

        var name = Path.GetFileNameWithoutExtension(filename);
        var parts = name.Split('_');
        if (parts.Length < 3) return NvrFileName.Invalid;

        var channel = parts[0].Trim('[', ']');
        var datePart = parts[1];
        var timeRange = parts[2];

        var dashIdx = timeRange.IndexOf('-');
        if (dashIdx <= 0) return NvrFileName.Invalid;

        var startTime = ParseTimePart(datePart, timeRange[..dashIdx]);
        var endTime = ParseTimePart(datePart, timeRange[(dashIdx + 1)..]);
        if (startTime == DateTime.MinValue || endTime == DateTime.MinValue) return NvrFileName.Invalid;

        return new NvrFileName(channel, startTime, endTime);
    }

    private static DateTime ParseTimePart(string datePart, string timePart)
    {
        // timePart: "15.00.00" alebo "15.00" alebo "6.00.00" (bez vedúcich núl)
        if (!DateTime.TryParseExact(datePart, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return DateTime.MinValue;

        var segments = timePart.Split('.');
        if (segments.Length is < 2 or > 3) return DateTime.MinValue;

        if (!int.TryParse(segments[0], out var hour) || hour is < 0 or > 23) return DateTime.MinValue;
        if (!int.TryParse(segments[1], out var minute) || minute is < 0 or > 59) return DateTime.MinValue;
        var second = segments.Length == 3 && int.TryParse(segments[2], out var s) ? s : 0;

        try
        {
            return new DateTime(date.Year, date.Month, date.Day, hour, minute, second);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MinValue;
        }
    }
}
