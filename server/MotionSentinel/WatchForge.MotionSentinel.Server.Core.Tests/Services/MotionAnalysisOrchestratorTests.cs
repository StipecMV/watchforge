namespace WatchForge.MotionSentinel.Server.Core.Tests.Services;

public sealed class MotionAnalysisOrchestratorTests
{
    private static MotionAnalysisOrchestrator Build(
        Mock<IFileAccessService> fileAccess,
        Mock<IMotionDetector>    detector,
        Mock<IDateTimeProvider>  clock,
        Mock<IAppInfoProvider>   appInfo,
        IVideoSource?            videoSource = null)
    {
        var source = videoSource ?? EmptyVideoSource();
        return new MotionAnalysisOrchestrator(
            fileAccess.Object,
            _ => source,
            detector.Object,
            new DetectionJsonSerializer(),
            clock.Object,
            appInfo.Object);
    }

    private static IVideoSource EmptyVideoSource()
    {
        var mock = new Mock<IVideoSource>();
        mock.Setup(s => s.GetFramesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<VideoFrame>());
        return mock.Object;
    }

    private static IVideoSource SingleFrameVideoSource(long timestampMs)
    {
        var mock  = new Mock<IVideoSource>();
        var frame = new VideoFrame(new object(), timestampMs);
        mock.Setup(s => s.GetFramesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { frame }.ToAsyncEnumerable());
        return mock.Object;
    }

    // ── Backfill ─────────────────────────────────────────────────────

    [Test]
    public async Task Backfill_WritesDetection_ForEachUnprocessedRecording()
    {
        // Given
        var fileAccess = new Mock<IFileAccessService>();
        fileAccess
            .Setup(f => f.ListNewRecordingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["cam1_08-00.mp4", "cam1_08-15.mp4"]);
        fileAccess
            .Setup(f => f.GetRecordingPath(It.IsAny<string>()))
            .Returns<string>(n => $"/recordings/{n}");

        var orchestrator = Build(fileAccess, new Mock<IMotionDetector>(),
            new Mock<IDateTimeProvider>(), new Mock<IAppInfoProvider>());

        // When
        await orchestrator.RunBackfillAsync(CancellationToken.None);

        // Then
        fileAccess.Verify(
            f => f.WriteDetectionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task Backfill_WritesNoDetections_WhenAllRecordingsAlreadyProcessed()
    {
        // Given
        var fileAccess = new Mock<IFileAccessService>();
        fileAccess
            .Setup(f => f.ListNewRecordingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var orchestrator = Build(fileAccess, new Mock<IMotionDetector>(),
            new Mock<IDateTimeProvider>(), new Mock<IAppInfoProvider>());

        // When
        await orchestrator.RunBackfillAsync(CancellationToken.None);

        // Then
        fileAccess.Verify(
            f => f.WriteDetectionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── RunForFile ───────────────────────────────────────────────────

    [Test]
    public async Task RunForFile_CallsDetectorReset_BeforeAnalysis()
    {
        // Given
        var fileAccess = new Mock<IFileAccessService>();
        fileAccess.Setup(f => f.GetRecordingPath(It.IsAny<string>())).Returns("/rec/cam1.mp4");

        var detector = new Mock<IMotionDetector>();
        detector
            .Setup(d => d.DetectAsync(It.IsAny<VideoFrame>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var orchestrator = Build(fileAccess, detector,
            new Mock<IDateTimeProvider>(), new Mock<IAppInfoProvider>());

        // When
        await orchestrator.RunForFileAsync("cam1_08-00.mp4", CancellationToken.None);

        // Then
        detector.Verify(d => d.Reset(), Times.Once);
    }

    [Test]
    public async Task RunForFile_WritesDetection_WithCorrectVideoFileName()
    {
        // Given
        var fileAccess = new Mock<IFileAccessService>();
        fileAccess.Setup(f => f.GetRecordingPath(It.IsAny<string>())).Returns("/rec/cam1.mp4");

        var orchestrator = Build(fileAccess, new Mock<IMotionDetector>(),
            new Mock<IDateTimeProvider>(), new Mock<IAppInfoProvider>());

        // When
        await orchestrator.RunForFileAsync("cam1_08-00.mp4", CancellationToken.None);

        // Then
        fileAccess.Verify(
            f => f.WriteDetectionAsync(
                It.IsAny<string>(), "cam1_08-00.mp4", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RunForFile_ReportsMotionEvent_WhenDetectorFindsMotion()
    {
        // Given
        var fileAccess = new Mock<IFileAccessService>();
        fileAccess.Setup(f => f.GetRecordingPath(It.IsAny<string>())).Returns("/rec/cam1.mp4");

        string? capturedJson = null;
        fileAccess
            .Setup(f => f.WriteDetectionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((json, _, _) => capturedJson = json);

        var detector = new Mock<IMotionDetector>();
        detector
            .Setup(d => d.DetectAsync(It.IsAny<VideoFrame>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MotionRegion { X = 0.1f, Y = 0.1f, Width = 0.2f, Height = 0.2f, Intensity = 0.5f }]);

        var orchestrator = Build(fileAccess, detector,
            new Mock<IDateTimeProvider>(), new Mock<IAppInfoProvider>(),
            videoSource: SingleFrameVideoSource(1000));

        // When
        await orchestrator.RunForFileAsync("cam1_08-00.mp4", CancellationToken.None);

        // Then
        var result = JsonSerializer.Deserialize<DetectionResult>(capturedJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        await Assert.That(result.Events.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task RunForFile_PopulatesVideoMetadata_FromVideoSource()
    {
        // Given
        var fileAccess = new Mock<IFileAccessService>();
        fileAccess.Setup(f => f.GetRecordingPath(It.IsAny<string>())).Returns("/rec/cam1.mp4");

        string? capturedJson = null;
        fileAccess
            .Setup(f => f.WriteDetectionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((json, _, _) => capturedJson = json);

        var videoSrc = new Mock<IVideoSource>();
        videoSrc.Setup(v => v.GetFramesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<VideoFrame>());
        videoSrc.Setup(v => v.Width).Returns(1920);
        videoSrc.Setup(v => v.Height).Returns(1080);
        videoSrc.Setup(v => v.FrameRate).Returns(25.0f);

        var orchestrator = Build(fileAccess, new Mock<IMotionDetector>(),
            new Mock<IDateTimeProvider>(), new Mock<IAppInfoProvider>(),
            videoSource: videoSrc.Object);

        // When
        await orchestrator.RunForFileAsync("cam1_08-00.mp4", CancellationToken.None);

        // Then
        var result = JsonSerializer.Deserialize<DetectionResult>(capturedJson!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        await Assert.That(result.Metadata.Width).IsEqualTo(1920);
        await Assert.That(result.Metadata.Height).IsEqualTo(1080);
        await Assert.That(result.Metadata.FrameRate).IsEqualTo(25.0f);
        await Assert.That(result.Metadata.TotalFramesAnalyzed).IsEqualTo(0);
    }

    [Test]
    public async Task RunForFile_SkipsAnalysis_WhenDetectionAlreadyExists()
    {
        // Given — detection JSON already exists for this file
        var fileAccess = new Mock<IFileAccessService>();
        fileAccess.Setup(f => f.DetectionExists("cam1_08-00.mp4")).Returns(true);

        var orchestrator = Build(fileAccess, new Mock<IMotionDetector>(),
            new Mock<IDateTimeProvider>(), new Mock<IAppInfoProvider>());

        // When
        await orchestrator.RunForFileAsync("cam1_08-00.mp4", CancellationToken.None);

        // Then — file access for actual recording path and write should not happen
        fileAccess.Verify(f => f.GetRecordingPath(It.IsAny<string>()), Times.Never);
        fileAccess.Verify(
            f => f.WriteDetectionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunForFile_WritesNoEvents_WhenNoMotionDetected()
    {
        // Given
        var fileAccess = new Mock<IFileAccessService>();
        fileAccess.Setup(f => f.GetRecordingPath(It.IsAny<string>())).Returns("/rec/cam1.mp4");

        string? capturedJson = null;
        fileAccess
            .Setup(f => f.WriteDetectionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((json, _, _) => capturedJson = json);

        var detector = new Mock<IMotionDetector>();
        detector
            .Setup(d => d.DetectAsync(It.IsAny<VideoFrame>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var orchestrator = Build(fileAccess, detector,
            new Mock<IDateTimeProvider>(), new Mock<IAppInfoProvider>(),
            videoSource: SingleFrameVideoSource(1000));

        // When
        await orchestrator.RunForFileAsync("cam1_08-00.mp4", CancellationToken.None);

        // Then
        var result = JsonSerializer.Deserialize<DetectionResult>(capturedJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        await Assert.That(result.Events.Count).IsEqualTo(0);
    }
}
