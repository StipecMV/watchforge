using System.Collections.Concurrent;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var cfg = builder.Configuration.GetSection("WatchForge");
string videoDir = cfg["VideoDirectory"] ?? "/home/miroslav/nvr-downloads";
string detectDir = cfg["DetectionsDirectory"] ?? "/home/miroslav/nvr-detections";
string cacheDir = cfg["CacheDirectory"] ?? "/home/miroslav/nvr-cache";

Directory.CreateDirectory(cacheDir);

// Track ongoing conversions: videoId -> progress 0-100 or -1 for error
var conversions = new ConcurrentDictionary<string, int>();

var app = builder.Build();
app.UseCors();

// ---- helpers ----

string MakeId(string filename) =>
    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(filename))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

string? FindMkv(string id)
{
    if (!Directory.Exists(videoDir)) return null;
    foreach (var f in Directory.GetFiles(videoDir, "*.mkv"))
        if (MakeId(Path.GetFileName(f)) == id) return f;
    return null;
}

string CachedMp4(string mkvPath) =>
    Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(mkvPath) + ".mp4");

string? FindDetections(string mkvPath)
{
    var baseName = Path.GetFileNameWithoutExtension(mkvPath);
    var candidate = Path.Combine(detectDir, baseName + ".json");
    return File.Exists(candidate) ? candidate : null;
}

(string channel, string date, string start, string end) ParseFilename(string filename)
{
    // [Ch0]_2026-04-06_07.00.00-07.15.00.mkv
    var name = Path.GetFileNameWithoutExtension(filename);
    var parts = name.Split('_');
    string channel = parts.Length > 0 ? parts[0].Trim('[', ']') : "Unknown";
    string date = parts.Length > 1 ? parts[1] : "";
    string timeRange = parts.Length > 2 ? parts[2] : "";
    var dashIdx = timeRange.IndexOf('-');
    string start = dashIdx > 0 ? timeRange[..dashIdx].Replace('.', ':') : "";
    string end = dashIdx > 0 ? timeRange[(dashIdx + 1)..].Replace('.', ':') : "";
    return (channel, date, start, end);
}

string? FindFfmpeg()
{
    foreach (var candidate in new[] { "ffmpeg", "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg" })
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(candidate, "-version")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            p?.WaitForExit(3000);
            if (p?.ExitCode == 0) return candidate;
        }
        catch { }
    }
    return null;
}

void StartConversion(string id, string mkvPath, string mp4Path)
{
    if (conversions.ContainsKey(id)) return;
    conversions[id] = 0;

    Task.Run(async () =>
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg == null) { conversions[id] = -1; return; }

        // Get duration first for progress tracking
        double totalSeconds = 0;
        try
        {
            var probe = new Process
            {
                StartInfo = new ProcessStartInfo(ffmpeg, $"-i \"{mkvPath}\" -f null -")
                    { RedirectStandardError = true, UseShellExecute = false }
            };
            probe.Start();
            var probeOut = await probe.StandardError.ReadToEndAsync();
            await probe.WaitForExitAsync();
            var durationMatch = System.Text.RegularExpressions.Regex.Match(
                probeOut, @"Duration: (\d+):(\d+):(\d+)");
            if (durationMatch.Success)
            {
                totalSeconds = int.Parse(durationMatch.Groups[1].Value) * 3600
                    + int.Parse(durationMatch.Groups[2].Value) * 60
                    + int.Parse(durationMatch.Groups[3].Value);
            }
        }
        catch { }

        var psi = new ProcessStartInfo(ffmpeg,
            $"-i \"{mkvPath}\" -c:v libx264 -preset fast -crf 23 -c:a aac -movflags +faststart \"{mp4Path}\"")
            { RedirectStandardError = true, UseShellExecute = false };
        var proc = new Process { StartInfo = psi };
        proc.Start();

        string? line;
        while ((line = await proc.StandardError.ReadLineAsync()) != null)
        {
            if (totalSeconds > 0)
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"time=(\d+):(\d+):(\d+)");
                if (m.Success)
                {
                    double elapsed = int.Parse(m.Groups[1].Value) * 3600
                        + int.Parse(m.Groups[2].Value) * 60
                        + int.Parse(m.Groups[3].Value);
                    conversions[id] = (int)Math.Min(99, elapsed / totalSeconds * 100);
                }
            }
        }
        await proc.WaitForExitAsync();
        conversions[id] = proc.ExitCode == 0 ? 100 : -1;
        if (proc.ExitCode != 0 && File.Exists(mp4Path))
            try { File.Delete(mp4Path); } catch { }
    });
}

// ---- endpoints ----

app.MapGet("/api/videos", () =>
{
    if (!Directory.Exists(videoDir)) return Results.Ok(Array.Empty<object>());

    var files = Directory.GetFiles(videoDir, "*.mkv");
    var grouped = files
        .Select(f =>
        {
            var fn = Path.GetFileName(f);
            var id = MakeId(fn);
            var (ch, date, start, end) = ParseFilename(fn);
            var mp4 = CachedMp4(f);
            return new
            {
                id,
                filename = fn,
                channelId = ch,
                date,
                startTime = start,
                endTime = end,
                hasCachedH264 = File.Exists(mp4),
                hasDetections = FindDetections(f) != null
            };
        })
        .GroupBy(v => v.channelId)
        .Select(g => new
        {
            channelId = g.Key,
            videos = g.OrderBy(v => v.date).ThenBy(v => v.startTime).ToArray()
        })
        .OrderBy(g => g.channelId)
        .ToArray();

    return Results.Ok(grouped);
});

app.MapGet("/api/videos/{id}/stream", async (string id, HttpContext ctx) =>
{
    var mkvPath = FindMkv(id);
    if (mkvPath == null) return Results.NotFound();

    var mp4Path = CachedMp4(mkvPath);
    if (!File.Exists(mp4Path))
    {
        StartConversion(id, mkvPath, mp4Path);
        ctx.Response.StatusCode = 202;
        await ctx.Response.WriteAsJsonAsync(new { status = "converting" });
        return Results.Empty;
    }

    // Range-aware streaming
    var fileInfo = new FileInfo(mp4Path);
    var rangeHeader = ctx.Request.Headers.Range.ToString();
    long fileStart = 0, fileEnd = fileInfo.Length - 1;

    if (!string.IsNullOrEmpty(rangeHeader))
    {
        var match = System.Text.RegularExpressions.Regex.Match(rangeHeader, @"bytes=(\d*)-(\d*)");
        if (match.Success)
        {
            if (match.Groups[1].Value != "") fileStart = long.Parse(match.Groups[1].Value);
            if (match.Groups[2].Value != "") fileEnd = long.Parse(match.Groups[2].Value);
        }
    }

    long length = fileEnd - fileStart + 1;
    ctx.Response.StatusCode = string.IsNullOrEmpty(rangeHeader) ? 200 : 206;
    ctx.Response.ContentType = "video/mp4";
    ctx.Response.ContentLength = length;
    ctx.Response.Headers.Append("Accept-Ranges", "bytes");
    if (!string.IsNullOrEmpty(rangeHeader))
        ctx.Response.Headers.Append("Content-Range",
            $"bytes {fileStart}-{fileEnd}/{fileInfo.Length}");

    using var fs = new FileStream(mp4Path, FileMode.Open, FileAccess.Read, FileShare.Read);
    fs.Seek(fileStart, SeekOrigin.Begin);
    var buffer = new byte[64 * 1024];
    long remaining = length;
    while (remaining > 0)
    {
        int read = await fs.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, remaining));
        if (read == 0) break;
        await ctx.Response.Body.WriteAsync(buffer, 0, read);
        remaining -= read;
    }
    return Results.Empty;
});

app.MapGet("/api/videos/{id}/conversion-status", (string id) =>
{
    var mkvPath = FindMkv(id);
    if (mkvPath == null) return Results.NotFound();

    var mp4Path = CachedMp4(mkvPath);
    if (File.Exists(mp4Path) && (!conversions.TryGetValue(id, out var p) || p == 100))
        return Results.Ok(new { status = "ready", progress = 100 });

    if (!conversions.TryGetValue(id, out var progress))
        return Results.Ok(new { status = "not_started", progress = 0 });

    if (progress == -1)
        return Results.Ok(new { status = "error", progress = 0 });

    return Results.Ok(new { status = "converting", progress });
});

app.MapGet("/api/videos/{id}/detections", (string id) =>
{
    var mkvPath = FindMkv(id);
    if (mkvPath == null) return Results.NotFound();

    var detPath = FindDetections(mkvPath);
    if (detPath == null) return Results.NotFound();

    var json = File.ReadAllText(detPath);
    return Results.Content(json, "application/json");
});

app.Run("http://0.0.0.0:5000");
