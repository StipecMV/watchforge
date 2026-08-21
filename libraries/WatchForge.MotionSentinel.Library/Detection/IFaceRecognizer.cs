using OpenCvSharp;
using WatchForge.Interfaces.Library;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Detection;

/// <summary>
/// Face recognition vrstva (FR-05) — najvyššia detekčná vrstva.
/// Spúšťa sa LEN keď person/object detection identifikoval osobu (pipeline reťazenie).
/// Implementácie držia databázu identít (LBPH embeddings) — volaj <see cref="LearnAsync"/>
/// na nové identity; <see cref="ResetAsync"/> vymaže naučené.
/// Perzistencia (S10-2): <see cref="LoadStateAsync"/> natrénuje model z FACES v DB,
/// <see cref="LearnAsync"/> uloží crop do FACES (embedding) a natrénuje.
/// </summary>
public interface IFaceRecognizer
{
    /// <summary>
    /// Detekuje a rozpozná tváre v jednom frami.
    /// Volá sa výhradne po person detekcii (nie na framoch bez osôb).
    /// </summary>
    Task<IReadOnlyList<FaceDetection>> DetectFacesAsync(
        VideoFrame currentFrame,
        CancellationToken ct = default);

    /// <summary>
    /// Naučí novú identitu z cropu tváre (LBPH embedding do databázy identít).
    /// Ak je <paramref name="faceRepository"/> nakonfigurovaný, crop sa uloží do FACES
    /// (embedding = JPEG obraz) a vráti sa faceId; inak vráti -1 (in-memory len).
    /// </summary>
    Task<int> LearnAsync(
        Mat faceCrop,
        int? identityId,
        string identityName,
        int? detectionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Natrénuje model z perzistovaných embeddings (FACES v DB) — volá sa pri štarte.
    /// Ak repo nie je nakonfigurované, nič sa nenačíta (in-memory režim).
    /// </summary>
    Task LoadStateAsync(
        IFaceRepository faceRepository,
        IIdentityRepository? identityRepository = null,
        CancellationToken ct = default);

    /// <summary>Vymaže všetky naučené identity (databáza tvárí sa resetuje).</summary>
    Task ResetAsync(CancellationToken ct = default);
}
