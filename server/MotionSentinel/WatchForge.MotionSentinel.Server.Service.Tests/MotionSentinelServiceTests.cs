namespace WatchForge.MotionSentinel.Server.Service.Tests;

public sealed class MotionSentinelServiceTests
{
    private static (MotionAnalysisOrchestrator Orchestrator, Mock<IFileAccessService> FileAccess)
        BuildOrchestrator(IReadOnlyList<string>? recordings = null)
    {
        var fileAccess = new Mock<IFileAccessService>();
        fileAccess
            .Setup(f => f.ListNewRecordingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(recordings ?? []);
        fileAccess
            .Setup(f => f.GetRecordingPath(It.IsAny<string>()))
            .Returns<string>(n => $"/rec/{n}");

        var videoSrc = new Mock<IVideoSource>();
        videoSrc
            .Setup(v => v.GetFramesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<VideoFrame>());

        var orchestrator = new MotionAnalysisOrchestrator(
            fileAccess.Object,
            _ => videoSrc.Object,
            new Mock<IMotionDetector>().Object,
            new DetectionJsonSerializer(),
            new Mock<IDateTimeProvider>().Object,
            new Mock<IAppInfoProvider>().Object);

        return (orchestrator, fileAccess);
    }

    [Test]
    public async Task StartingAsync_ThrowsDirectoryNotFoundException_WhenRecordingsPathMissing()
    {
        // Given
        var (orchestrator, _) = BuildOrchestrator();
        var service = new MotionSentinelService(
            orchestrator,
            Options.Create(new LocalFileServiceOptions
            {
                RecordingsPath = "/nonexistent/path",
                DetectionsPath = "/tmp/det",
            }));

        // When / Then
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => service.StartingAsync(CancellationToken.None));
    }

    [Test]
    public async Task StartAsync_RunsBackfill_OnStartup()
    {
        // Given — recordings directory must exist for StartingAsync
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var recDir  = Directory.CreateDirectory(Path.Combine(tempDir, "recordings")).FullName;
        var detDir  = Path.Combine(tempDir, "detections");

        var (orchestrator, fileAccess) = BuildOrchestrator(["cam1_08-00.mp4"]);

        var service = new MotionSentinelService(
            orchestrator,
            Options.Create(new LocalFileServiceOptions
            {
                RecordingsPath = recDir,
                DetectionsPath = detDir,
            }));

        // When
        await service.StartingAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);

        // Then — backfill called ListNewRecordingsAsync
        fileAccess.Verify(
            f => f.ListNewRecordingsAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        Directory.Delete(tempDir, recursive: true);
    }
}
