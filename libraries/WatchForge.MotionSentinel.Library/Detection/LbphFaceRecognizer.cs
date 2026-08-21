using OpenCvSharp;
using OpenCvSharp.Face;
using WatchForge.Interfaces.Library;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Detection;

/// <summary>
/// Face recognition klasickou CV technikou (FR-05): LBP cascade na detekciu
/// tvárí + LBPH (Local Binary Patterns Histograms) na rozpoznanie identity.
/// Nie je to ML model — žiadna neurónová sieť, len hand-crafted features
/// + histogramové porovnanie (rovnaký duch ako HOG person detection).
///
/// Perzistencia (S10-2): embeddings = JPEG crop tváre uložený v FACES.embedding.
/// <see cref="LearnAsync"/> uloží crop do DB (ak je repo k dispozícii) a natrénuje
/// model; <see cref="LoadStateAsync"/> načíta všetky FACES s identity_id a natrénuje
/// model odznova (volá sa pri štarte Runnera).
/// </summary>
public sealed class LbphFaceRecognizer : IFaceRecognizer, IDisposable
{
    private const string DefaultCascadePath = "Detection/Assets/lbpcascade_frontalface.xml";

    private readonly CascadeClassifier _cascade;
    private LBPHFaceRecognizer _recognizer = LBPHFaceRecognizer.Create();
    private readonly Dictionary<int, string> _labels = new();
    private int _nextLabel;

    /// <summary>Minimálna veľkosť tváre (px) pre detekciu.</summary>
    public int MinFaceSize { get; init; } = 48;

    /// <summary>
    /// Maximálna LBPH vzdialenosť (chi-square) pre uznanie identity.
    /// Nižšia = prísnejšie; nad ňou sa tvár označí ako neznáma.
    /// </summary>
    public double MaxDistance { get; init; } = 120.0;

    /// <summary>Ak je nastavený, LearnAsync perzistuje crop do FACES (S10-2).</summary>
    public IFaceRepository? FaceRepository { get; init; }

    public LbphFaceRecognizer(string? cascadePath = null)
    {
        var path = cascadePath ?? DefaultCascadePath;
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, path);
        _cascade = new CascadeClassifier(path);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FaceDetection>> DetectFacesAsync(
        VideoFrame currentFrame, CancellationToken ct = default)
    {
        var currentMat = (Mat)currentFrame.NativeBuffer;

        Rect[] faces = _cascade.DetectMultiScale(
            currentMat,
            scaleFactor: 1.1,
            minNeighbors: 5,
            flags: HaarDetectionTypes.ScaleImage,
            minSize: new Size(MinFaceSize, MinFaceSize));

        var results = new List<FaceDetection>(faces.Length);
        foreach (var rect in faces)
        {
            using var faceGray = new Mat();
            using var roi = new Mat(currentMat, rect);
            Cv2.CvtColor(roi, faceGray, ColorConversionCodes.BGR2GRAY);
            Cv2.Resize(faceGray, faceGray, new Size(100, 100));

            int label = -1;
            double distance = 0;
            _recognizer.Predict(faceGray, out label, out distance);

            float confidence = distance <= 0 ? 1f : Math.Clamp((float)(1.0 - distance / MaxDistance), 0f, 1f);
            bool known = label >= 0 && _labels.ContainsKey(label) && distance <= MaxDistance;

            results.Add(new FaceDetection
            {
                Region = new NormalizedRegion(
                    (float)rect.X / currentMat.Width,
                    (float)rect.Y / currentMat.Height,
                    (float)rect.Width / currentMat.Width,
                    (float)rect.Height / currentMat.Height),
                IdentityId   = known ? label : null,
                IdentityName = known ? _labels[label] : "",
                Confidence   = known ? confidence : 0f,
            });
        }

        return Task.FromResult<IReadOnlyList<FaceDetection>>(results);
    }

    /// <inheritdoc/>
    public async Task<int> LearnAsync(
        Mat faceCrop, int? identityId, string identityName, int? detectionId = null,
        CancellationToken ct = default)
    {
        // Perzistencia (S10-2): crop ako embedding do FACES — vždy keď je repo
        if (FaceRepository is not null)
        {
            using var encoded = new Mat();
            var ok = Cv2.ImEncode(".jpg", faceCrop, out var jpeg);
            if (ok && jpeg.Length > 0)
            {
                var faceId = await FaceRepository.InsertAsync(new FaceRecord
                {
                    DetectionId   = detectionId ?? 0,
                    IdentityId    = identityId,
                    Embedding     = jpeg,
                    Confidence    = 1.0,
                    TempCropPath  = "",
                }, ct);
                if (identityId is int id && !_labels.ContainsKey(id))
                {
                    _labels[id] = identityName;
                    _nextLabel = Math.Max(_nextLabel, id + 1);
                }
                TrainInternal(faceCrop, identityId);
                return faceId;
            }
        }

        // In-memory fallback (bez repo — testy)
        if (identityId is int memId && !_labels.ContainsKey(memId))
        {
            _labels[memId] = identityName;
            _nextLabel = Math.Max(_nextLabel, memId + 1);
        }
        TrainInternal(faceCrop, identityId);
        return -1;
    }

    /// <inheritdoc/>
    public Task LoadStateAsync(
        IFaceRepository faceRepository, IIdentityRepository? identityRepository = null,
        CancellationToken ct = default)
    {
        _labels.Clear();
        _nextLabel = 0;
        _recognizer.Dispose();
        _recognizer = LBPHFaceRecognizer.Create();

        var faces = faceRepository.GetAllWithIdentityAsync(ct).GetAwaiter().GetResult();
        var nameById = new Dictionary<int, string>();
        if (identityRepository is not null)
        {
            foreach (var identity in identityRepository.GetAllAsync(ct).GetAwaiter().GetResult())
                nameById[identity.IdentityId] = identity.Name;
        }

        foreach (var face in faces)
        {
            if (face.IdentityId is not int identityId || face.Embedding is not { Length: > 0 } jpeg)
                continue;

            using var img = Cv2.ImDecode(jpeg, ImreadModes.Grayscale);
            if (img.Empty()) continue;

            var name = nameById.GetValueOrDefault(identityId, $"identity-{identityId}");
            if (!_labels.ContainsKey(identityId))
            {
                _labels[identityId] = name;
                _nextLabel = Math.Max(_nextLabel, identityId + 1);
            }
            TrainInternal(img, identityId);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ResetAsync(CancellationToken ct = default)
    {
        _labels.Clear();
        _nextLabel = 0;
        _recognizer.Dispose();
        _recognizer = LBPHFaceRecognizer.Create(); // čistý model — nová inštancia
        return Task.CompletedTask;
    }

    /// <summary>Natrénuje LBPH na jednom cropu (grayscale 100×100).</summary>
    private void TrainInternal(Mat faceCrop, int? identityId)
    {
        if (identityId is not int id) return;
        using var faceGray = new Mat();
        if (faceCrop.Channels() == 1)
            faceCrop.CopyTo(faceGray); // už grayscale (ImDecode z DB)
        else
            Cv2.CvtColor(faceCrop, faceGray, ColorConversionCodes.BGR2GRAY);
        Cv2.Resize(faceGray, faceGray, new Size(100, 100));
        _recognizer.Update([faceGray], [id]);
    }

    public void Dispose()
    {
        _cascade.Dispose();
        _recognizer.Dispose();
    }
}
