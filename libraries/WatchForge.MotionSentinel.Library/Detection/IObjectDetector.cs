using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Detection;

/// <summary>
/// Detekcia osôb/objektov (FR-04) — samostatná vrstva, ktorá sa spúšťa
/// LEN na framoch, kde motion detection našiel pohyb (pipeline reťazenie).
/// Implementácie sú bezstavové pre jeden frame (žiadna pamäť medzi framami).
/// </summary>
public interface IObjectDetector
{
    /// <summary>
    /// Detekuje objekty v jednom frami. Volá sa výhradne po tom,
    /// čo <see cref="IMotionDetector"/> vrátil aspoň jeden región pohybu.
    /// </summary>
    Task<IReadOnlyList<ObjectDetection>> DetectAsync(
        VideoFrame currentFrame,
        CancellationToken ct = default);
}
