using OpenCvSharp;
using OpenCvSharp.Dnn;
using WatchForge.Interfaces.Library;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Detection;

/// <summary>
/// Face recognition ML pipeline (S19, FR-05 — ML model povolený):
/// <list type="bullet">
/// <item><b>YuNet</b> (<c>face_detection_yunet_2023mar.onnx</c>) — detekcia tvárí
/// + 5 landmarkov (ľavé/pravé oko, nos, kútiky úst). Používa sa cez oficiálne
/// <see cref="FaceDetectorYN"/> API (OpenCV DNN, vnútorný NMS, ~230 KB).</item>
/// <item><b>SFace</b> (<c>face_recognition_sface_2021dec.onnx</c>) — 128-dim
/// embedding tváre, L2-normalizovaný. Cosine podobnosť vs databáza.</item>
/// </list>
/// Detekcia → affine align (narovnanie podľa landmarkov) → embedding →
/// cosine podobnosť → identity match (threshold) alebo „neznáma".
/// Beží na CPU (SFace ~5–20 ms/embedding) — face vrstva sa spúšťa len na
/// person framoch, výkon nie je kritický. Volajúci kód (AnalyzeJobHandler)
/// sa nemení — rovnaké <see cref="IFaceRecognizer"/> rozhranie.
///
/// Perzistencia (S10-2 vzor): embedding = 128 floatov (512 B, little-endian
/// float[]) v FACES.embedding BLOB. LearnAsync uloží embedding do DB (ak je
/// repo), LoadStateAsync načíta embeddingy a naplní pamäťovú databázu.
/// </summary>
public sealed class OnnxFaceRecognizer : IFaceRecognizer, IDisposable
{
    private const string DefaultYunetPath = "Detection/Assets/face_detection_yunet_2023mar.onnx";
    private const string DefaultSfacePath = "Detection/Assets/face_recognition_sface_2021dec.onnx";
    private const int EmbeddingDim = 128; // SFace 2021dec — 128-dim (nie ArcFace 512)
    private const int LandmarkCount = 5;

    private readonly FaceDetectorYN _yunet;
    private readonly Net _sface;
    private readonly string _sfaceOutName;

    /// <summary>Databáza identít: identityId → embeddings (každá identita môže mať viac vzoriek).</summary>
    private readonly Dictionary<int, List<float[]>> _identities = new();
    private readonly Dictionary<int, string> _names = new();

    /// <summary>YuNet vstupný rozmer (štvorcový).</summary>
    public Size InputSize { get; init; } = new(320, 320);

    /// <summary>YuNet score prah (0..1) pre detekciu tváre.</summary>
    public float ScoreThreshold { get; init; } = 0.6f;

    /// <summary>YuNet NMS prah.</summary>
    public float NmsThreshold { get; init; } = 0.3f;

    /// <summary>
    /// Cosine prah pre uznanie identity (0..1). Vyššia = prísnejšie.
    /// SFace typicky: &gt; 0.36 = rovnaká osoba (odporúčané rozsahy 0.30–0.45).
    /// </summary>
    public double CosineThreshold { get; init; } = 0.36;

    /// <summary>Ak je nastavený, LearnAsync perzistuje embedding do FACES (S10-2 vzor).</summary>
    public IFaceRepository? FaceRepository { get; init; }

    public OnnxFaceRecognizer(string? yunetPath = null, string? sfacePath = null)
    {
        _yunet = FaceDetectorYN.Create(
            ResolveAssetPath(yunetPath ?? DefaultYunetPath), "",
            InputSize, ScoreThreshold, NmsThreshold, 5000,
            Backend.OPENCV, Target.CPU);   // CPU (žiadna CUDA závislosť)
        _sface = CvDnn.ReadNetFromOnnx(ResolveAssetPath(sfacePath ?? DefaultSfacePath))!;
        _sface.SetPreferableBackend(Backend.OPENCV);
        _sface.SetPreferableTarget(Target.CPU);
        _sfaceOutName = GetOutputName(_sface);
    }

    private static string ResolveAssetPath(string path)
        => File.Exists(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    /// <summary>Názov výstupnej vrstvy (posledná unconnected vrstva) pre Forward().</summary>
    private static string GetOutputName(Net net)
    {
        var layers = net.GetUnconnectedOutLayers();   // 1-based indexy
        var names = net.GetLayerNames();
        if (layers.Length == 0) return "";
        var name = layers[0] - 1 < names.Length ? names[layers[0] - 1] : null;
        return string.IsNullOrEmpty(name) ? "" : name;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FaceDetection>> DetectFacesAsync(
        VideoFrame currentFrame, CancellationToken ct = default)
    {
        var currentMat = (Mat)currentFrame.NativeBuffer;
        var results = new List<FaceDetection>();

        // YuNet vyžaduje vstup presne InputSize (štvorec) — resize + detekcia
        using var resized = new Mat();
        Cv2.Resize(currentMat, resized, InputSize);
        using var faces = new Mat();
        var count = _yunet.Detect(resized, faces);   // faces: [N, 15] CV_32F
        if (count <= 0 || faces.Total() == 0 || faces.Size(1) < 15)
            return Task.FromResult<IReadOnlyList<FaceDetection>>(results);

        var data = new float[faces.Total()];
        faces.GetArray(out data);
        var rows = faces.Size(0);
        var cols = faces.Size(1);

        var scaleX = currentMat.Width / (double)InputSize.Width;
        var scaleY = currentMat.Height / (double)InputSize.Height;

        for (int i = 0; i < rows; i++)
        {
            ct.ThrowIfCancellationRequested();

            float x     = data[i * cols + 0];
            float y     = data[i * cols + 1];
            float w     = data[i * cols + 2];
            float h     = data[i * cols + 3];
            float score = data[i * cols + 14];

            if (score < ScoreThreshold) continue;

            // Landmarky (stĺpce 4..13): páry (x, y) v resized koordinátoch
            var lm = new float[LandmarkCount * 2];
            for (int l = 0; l < LandmarkCount * 2; l++)
                lm[l] = data[i * cols + 4 + l];

            var rect = new Rect(
                (int)(x * scaleX), (int)(y * scaleY),
                (int)(w * scaleX), (int)(h * scaleY));

            // SFace embedding z aligned cropu
            using var aligned = AlignAndCrop(currentMat, lm, scaleX, scaleY);
            if (aligned is null || aligned.Empty()) continue;

            var embedding = ComputeEmbedding(aligned);
            if (embedding is null) continue;

            var (identityId, identityName, confidence) = MatchIdentity(embedding);
            results.Add(new FaceDetection
            {
                Region = new NormalizedRegion(
                    (float)rect.X / currentMat.Width,
                    (float)rect.Y / currentMat.Height,
                    (float)rect.Width / currentMat.Width,
                    (float)rect.Height / currentMat.Height),
                IdentityId   = identityId,
                IdentityName = identityName,
                Confidence   = confidence,
            });
        }

        return Task.FromResult<IReadOnlyList<FaceDetection>>(results);
    }

    /// <inheritdoc/>
    public async Task<int> LearnAsync(
        Mat faceCrop, int? identityId, string identityName, int? detectionId = null,
        CancellationToken ct = default)
    {
        if (identityId is not int id) return -1;

        var embedding = ComputeEmbedding(faceCrop);
        if (embedding is null) return -1;

        if (!_identities.TryGetValue(id, out var list))
        {
            list = new List<float[]>();
            _identities[id] = list;
        }
        list.Add(embedding);
        _names[id] = identityName;

        // Perzistencia (S10-2 vzor): embedding ako float[] → BLOB (little-endian)
        if (FaceRepository is not null)
        {
            var blob = new byte[embedding.Length * sizeof(float)];
            Buffer.BlockCopy(embedding, 0, blob, 0, blob.Length);
            return await FaceRepository.InsertAsync(new FaceRecord
            {
                DetectionId  = detectionId ?? 0,
                IdentityId   = identityId,
                Embedding    = blob,
                Confidence   = 1.0,
                TempCropPath = "",
            }, ct);
        }
        return -1;
    }

    /// <inheritdoc/>
    public Task LoadStateAsync(
        IFaceRepository faceRepository, IIdentityRepository? identityRepository = null,
        CancellationToken ct = default)
    {
        _identities.Clear();
        _names.Clear();

        if (identityRepository is not null)
        {
            foreach (var identity in identityRepository.GetAllAsync(ct).GetAwaiter().GetResult())
                _names[identity.IdentityId] = identity.Name;
        }

        foreach (var face in faceRepository.GetAllWithIdentityAsync(ct).GetAwaiter().GetResult())
        {
            if (face.IdentityId is not int id || face.Embedding is not { Length: > 0 } blob)
                continue;

            // BLOB → float[] (128 × 4 B = 512 B)
            if (blob.Length % sizeof(float) != 0) continue;
            var embedding = new float[blob.Length / sizeof(float)];
            Buffer.BlockCopy(blob, 0, embedding, 0, blob.Length);

            if (!_identities.TryGetValue(id, out var list))
            {
                list = new List<float[]>();
                _identities[id] = list;
            }
            list.Add(embedding);
            _names.TryAdd(id, $"identity-{id}");
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ResetAsync(CancellationToken ct = default)
    {
        _identities.Clear();
        _names.Clear();
        return Task.CompletedTask;
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Affine align: narovná tvár podľa landmarkov na 112×112 (SFace vstup).
    /// Landmarky sú v resized (InputSize) koordinátoch — prepočítame na fram.
    /// </summary>
    private Mat? AlignAndCrop(Mat frame, float[] lm, double scaleX, double scaleY)
    {
        // Landmarky v resized priestore → fram priestor
        var src = new Point2f[LandmarkCount];
        for (int l = 0; l < LandmarkCount; l++)
            src[l] = new Point2f((float)(lm[l * 2] * scaleX), (float)(lm[l * 2 + 1] * scaleY));

        // Cieľové pozície (SFace/YuNet štandard — normalizované na 112×112)
        var dst = new Point2f[]
        {
            new(38.2946f, 51.6963f), // ľavé oko
            new(73.5318f, 51.5014f), // pravé oko
            new(56.0252f, 71.7366f), // nos
            new(41.5493f, 92.3655f), // ľavý kútik úst
            new(70.7299f, 92.2041f), // pravý kútik úst
        };

        using var srcM = Mat.FromPixelData(LandmarkCount, 1, MatType.CV_32FC2, src);
        using var dstM = Mat.FromPixelData(LandmarkCount, 1, MatType.CV_32FC2, dst);
        var warp = new Mat();
        Cv2.EstimateAffinePartial2D(srcM, dstM, warp);

        var aligned = new Mat();
        Cv2.WarpAffine(frame, aligned, warp, new Size(112, 112));
        return aligned;
    }

    /// <summary>Vypočíta SFace embedding (128-dim, L2-normalizovaný).</summary>
    private float[]? ComputeEmbedding(Mat face)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(face, rgb, ColorConversionCodes.BGR2RGB);
        using var blob = CvDnn.BlobFromImage(rgb, 1.0 / 128.0, new Size(112, 112),
            new Scalar(127.5, 127.5, 127.5), swapRB: false, crop: false);
        _sface.SetInput(blob, "");
        using var output = _sface.Forward(_sfaceOutName);

        if (output.Total() < EmbeddingDim) return null;
        var embedding = new float[EmbeddingDim];
        output.GetArray(out embedding);

        // L2 normalizácia (cosine podobnosť = dot product po normalizácii)
        var norm = 0.0;
        foreach (var v in embedding) norm += v * v;
        norm = Math.Sqrt(norm);
        if (norm < 1e-9) return null;
        for (int i = 0; i < embedding.Length; i++)
            embedding[i] = (float)(embedding[i] / norm);

        return embedding;
    }

    /// <summary>Najlepší match cez cosine podobnosť; null identita, ak je pod prahom.</summary>
    private (int? Id, string Name, float Confidence) MatchIdentity(float[] embedding)
    {
        int? bestId = null;
        var bestName = "";
        var bestScore = 0.0;

        foreach (var (id, samples) in _identities)
        {
            foreach (var sample in samples)
            {
                var score = CosineSimilarity(embedding, sample);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = id;
                    bestName = _names.GetValueOrDefault(id, "");
                }
            }
        }

        if (bestId is null || bestScore < CosineThreshold)
            return (null, "", 0f);

        return (bestId, bestName, (float)bestScore);
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        var dot = 0.0;
        for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return dot; // oba L2-normalizované → dot = cosine
    }

    public void Dispose()
    {
        _yunet.Dispose();
        _sface.Dispose();
    }
}
