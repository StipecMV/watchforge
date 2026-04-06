namespace WatchForge.MotionSentinel.Server.Service.Tests.FileAccess;

public sealed class LocalFileServiceTests
{
    [Test]
    public async Task ListNewRecordings_ReturnsOnlyUnprocessedMp4s()
    {
        // Given — two MP4s, one already has a detection JSON
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var recDir  = Directory.CreateDirectory(Path.Combine(tempDir, "recordings")).FullName;
        var detDir  = Directory.CreateDirectory(Path.Combine(tempDir, "detections")).FullName;

        await File.WriteAllTextAsync(Path.Combine(recDir, "cam1_08-00.mp4"), "");
        await File.WriteAllTextAsync(Path.Combine(recDir, "cam1_08-15.mp4"), "");
        await File.WriteAllTextAsync(Path.Combine(detDir, "cam1_08-00.json"), "{}");

        var service = new LocalFileService(Options.Create(new LocalFileServiceOptions
        {
            WatchDirectory  = recDir,
            OutputDirectory = detDir,
            FileExtensions  = ["*.mp4"],
        }));

        // When
        var result = await service.ListNewRecordingsAsync();

        // Then
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo("cam1_08-15.mp4");

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task WriteDetection_CreatesDetectionsDir_IfNotExists()
    {
        // Given — detections directory does not exist
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var recDir  = Directory.CreateDirectory(Path.Combine(tempDir, "recordings")).FullName;
        var detDir  = Path.Combine(tempDir, "detections");   // intentionally not created

        var service = new LocalFileService(Options.Create(new LocalFileServiceOptions
        {
            WatchDirectory = recDir,
            OutputDirectory = detDir,
        }));

        // When
        await service.WriteDetectionAsync("{}", "cam1_08-00.mp4");

        // Then — directory and file were created automatically
        await Assert.That(File.Exists(Path.Combine(detDir, "cam1_08-00.json"))).IsTrue();

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task GetRecordingPath_ReturnsCombinedPath()
    {
        // Given
        var service = new LocalFileService(Options.Create(new LocalFileServiceOptions
        {
            WatchDirectory = "/mnt/nvr/recordings",
            OutputDirectory = "/mnt/nvr/detections",
        }));

        // When
        var path = service.GetRecordingPath("cam1_08-00.mp4");

        // Then
        await Assert.That(path).IsEqualTo("/mnt/nvr/recordings/cam1_08-00.mp4");
    }

    [Test]
    public async Task DetectionExists_ReturnsTrue_WhenJsonFileExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var recDir  = Directory.CreateDirectory(Path.Combine(tempDir, "recordings")).FullName;
        var detDir  = Directory.CreateDirectory(Path.Combine(tempDir, "detections")).FullName;

        await File.WriteAllTextAsync(Path.Combine(detDir, "cam1_08-00.json"), "{}");

        var service = new LocalFileService(Options.Create(new LocalFileServiceOptions
        {
            WatchDirectory = recDir,
            OutputDirectory = detDir,
        }));

        await Assert.That(service.DetectionExists("cam1_08-00.mp4")).IsTrue();

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task DetectionExists_ReturnsFalse_WhenJsonFileDoesNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var recDir  = Directory.CreateDirectory(Path.Combine(tempDir, "recordings")).FullName;
        var detDir  = Directory.CreateDirectory(Path.Combine(tempDir, "detections")).FullName;

        var service = new LocalFileService(Options.Create(new LocalFileServiceOptions
        {
            WatchDirectory = recDir,
            OutputDirectory = detDir,
        }));

        await Assert.That(service.DetectionExists("cam1_08-15.mp4")).IsFalse();

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task GetRecordingPath_Throws_WhenFileNameIsNullOrWhitespace(string fileName)
    {
        var service = new LocalFileService(Options.Create(new LocalFileServiceOptions
        {
            WatchDirectory = "/mnt/nvr/recordings",
            OutputDirectory = "/mnt/nvr/detections",
        }));

        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(service.GetRecordingPath(fileName)));
    }

    [Test]
    [Arguments("../etc/passwd")]
    [Arguments("..\\windows\\system32\\config\\sam")]
    [Arguments("subdir/cam1.mp4")]
    [Arguments("subdir\\cam1.mp4")]
    public async Task GetRecordingPath_Throws_OnPathTraversal(string fileName)
    {
        var service = new LocalFileService(Options.Create(new LocalFileServiceOptions
        {
            WatchDirectory = "/mnt/nvr/recordings",
            OutputDirectory = "/mnt/nvr/detections",
        }));

        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(service.GetRecordingPath(fileName)));
    }
}
