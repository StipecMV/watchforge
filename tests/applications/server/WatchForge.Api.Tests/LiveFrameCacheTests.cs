using TUnit.Assertions.Exceptions;
using WatchForge.Api;

namespace WatchForge.Api.Tests;

/// <summary>
/// S22e: LiveFrameCache — release a idle cleanup.
/// Streamy nesmú žiť, keď ich nikto nepozerá (úspora CPU + upload šírky).
/// </summary>
public class LiveFrameCacheTests
{
    [Test]
    public void Release_OnUnknownCamera_DoesNotThrow()
    {
        var cache = new LiveFrameCache();
        try
        {
            cache.Release(999, 640, 1);
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Test]
    public void Release_RemovesSession_SecondReleaseIsNoOp()
    {
        var cache = new LiveFrameCache();
        try
        {
            cache.Release(1, 640, 1);
            cache.Release(1, 640, 1);
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Test]
    public void Dispose_CancelsAllSessions_DoesNotThrow()
    {
        var cache = new LiveFrameCache();
        cache.Release(1, 640, 1);
        cache.Release(2, 640, 1);
        cache.Dispose();
    }

    [Test]
    public void ReleaseAll_OnUnknownCamera_DoesNotThrow()
    {
        var cache = new LiveFrameCache();
        try
        {
            cache.ReleaseAll(999);
        }
        finally
        {
            cache.Dispose();
        }
    }

    private static string FindSourceFile()
    {
        // bin/Release/net10.0 → koreň repo (hľadáme LiveFrameCache.cs)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "applications", "server", "WatchForge.Api", "LiveFrameCache.cs");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("LiveFrameCache.cs sa nenašiel z test working dir");
    }

    [Test]
    public void IdleTimeout_Is20Seconds_Not300()
    {
        // S22e: stream sa po ~20 s nečinnosti uvoľní (predtým 300 s = 5 minút
        // upload/CPU navyše po tom, čo nikto nepozerá). Overíme cez zdrojový kód.
        var source = File.ReadAllText(FindSourceFile());
        if (!source.Contains("TimeSpan.FromSeconds(20)"))
            throw new AssertionException("LiveFrameCache.IsIdle musí byť 20 s (nie 300 s)");
        if (source.Contains("TimeSpan.FromSeconds(300)"))
            throw new AssertionException("LiveFrameCache.IsIdle nesmie byť 300 s");
    }

    [Test]
    public void CleanupInterval_Is10Seconds()
    {
        // S22e: cleanup timer každých 10 s (predtým 30 s) — rýchlejšie uvoľnenie.
        var source = File.ReadAllText(FindSourceFile());
        if (!source.Contains("TimeSpan.FromSeconds(10)"))
            throw new AssertionException("Cleanup timer musí byť 10 s");
    }
}
