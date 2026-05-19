namespace WatchForge.MotionSentinel.Library.Tests.Output;

public sealed class DetectionJsonSerializerTests
{
    private static readonly DetectionJsonSerializer Serializer = new();

    // ── camelCase property names ───────────────────────────────────────────────

    [Test]
    public async Task Serialize_TopLevelProperties_AreCamelCase()
    {
        // Given a result with all top-level properties set
        var result = new DetectionResult
        {
            VideoFile  = "cam1.mp4",
            AnalyzedAt = "2026-04-02T10:00:00Z",
            AppVersion = "1.0.0",
            Events     = []
        };

        // When serialised
        var json = Serializer.Serialize(result);

        // Then property names are camelCase (not PascalCase)
        await Assert.That(json).Contains("\"videoFile\"");
        await Assert.That(json).Contains("\"analyzedAt\"");
        await Assert.That(json).Contains("\"appVersion\"");
        await Assert.That(json).Contains("\"metadata\"");
        await Assert.That(json).Contains("\"events\"");
    }

    [Test]
    public async Task Serialize_NoPascalCaseKeys_InOutput()
    {
        // Given any result
        var result = new DetectionResult { VideoFile = "cam1.mp4" };

        // When serialised
        var json = Serializer.Serialize(result);

        // Then none of the known PascalCase property names appear
        await Assert.That(json).DoesNotContain("\"VideoFile\"");
        await Assert.That(json).DoesNotContain("\"AnalyzedAt\"");
        await Assert.That(json).DoesNotContain("\"AppVersion\"");
        await Assert.That(json).DoesNotContain("\"Events\"");
        await Assert.That(json).DoesNotContain("\"Metadata\"");
    }

    // ── indented output ───────────────────────────────────────────────────────

    [Test]
    public async Task Serialize_Output_IsIndented()
    {
        // Given a non-trivial result
        var result = new DetectionResult { VideoFile = "cam1.mp4" };

        // When serialised
        var json = Serializer.Serialize(result);

        // Then the output contains newlines (i.e. it is indented, not on a single line)
        await Assert.That(json).Contains("\n");
    }

    // ── values are correct ────────────────────────────────────────────────────

    [Test]
    public async Task Serialize_VideoFileName_IsPresentInOutput()
    {
        // Given a known video file name
        var result = new DetectionResult { VideoFile = "recording_2026.mp4" };

        // When serialised
        var json = Serializer.Serialize(result);

        // Then the value appears verbatim in the output
        await Assert.That(json).Contains("\"recording_2026.mp4\"");
    }

    [Test]
    public async Task Serialize_EmptyEventsList_ProducesEmptyJsonArray()
    {
        // Given a result with no motion events
        var result = new DetectionResult { Events = [] };

        // When serialised
        var json = Serializer.Serialize(result);

        // Then the events field is an empty JSON array
        await Assert.That(json).Contains("\"events\": []");
    }

    [Test]
    public async Task Serialize_MotionEvent_IncludesTimestampAndRegions()
    {
        // Given a result with one motion event containing one region
        var result = new DetectionResult
        {
            Events =
            [
                new MotionEvent
                {
                    TimestampMs = 1500,
                    DurationMs  = 500,
                    Regions     = [new MotionRegion { X = 0.1f, Y = 0.2f, Width = 0.3f, Height = 0.4f, Intensity = 0.75f }]
                }
            ]
        };

        // When serialised
        var json = Serializer.Serialize(result);

        // Then the event fields use camelCase and the values are present
        await Assert.That(json).Contains("\"timestampMs\"");
        await Assert.That(json).Contains("\"durationMs\"");
        await Assert.That(json).Contains("\"regions\"");
        await Assert.That(json).Contains("\"intensity\"");
    }

    [Test]
    public async Task Serialize_VideoMetadata_IncludesAllFields()
    {
        // Given a result with metadata
        var result = new DetectionResult
        {
            Metadata = new VideoMetadata
            {
                Width               = 1920,
                Height              = 1080,
                FrameRate           = 25.0f,
                TotalFramesAnalyzed = 120
            }
        };

        // When serialised
        var json = Serializer.Serialize(result);

        // Then metadata fields are camelCase and values are correct
        await Assert.That(json).Contains("\"width\"");
        await Assert.That(json).Contains("\"height\"");
        await Assert.That(json).Contains("\"frameRate\"");
        await Assert.That(json).Contains("\"totalFramesAnalyzed\"");
        await Assert.That(json).Contains("1920");
        await Assert.That(json).Contains("1080");
    }

    // ── round-trip ────────────────────────────────────────────────────────────

    [Test]
    public async Task Serialize_ThenDeserialize_PreservesAllValues()
    {
        // Given a fully-populated result
        var original = new DetectionResult
        {
            VideoFile  = "cam1_08-00.mp4",
            AnalyzedAt = "2026-04-02T08:00:00Z",
            AppVersion = "2.1.0",
            Metadata   = new VideoMetadata { Width = 1920, Height = 1080, FrameRate = 25f, TotalFramesAnalyzed = 360 },
            Events     =
            [
                new MotionEvent
                {
                    TimestampMs = 2000,
                    DurationMs  = 500,
                    Regions     = [new MotionRegion { X = 0.5f, Y = 0.5f, Width = 0.1f, Height = 0.1f, Intensity = 0.9f }]
                }
            ]
        };

        // When serialised and deserialised back
        var json     = Serializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<DetectionResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        // Then all values survive the round-trip
        await Assert.That(restored.VideoFile).IsEqualTo(original.VideoFile);
        await Assert.That(restored.AnalyzedAt).IsEqualTo(original.AnalyzedAt);
        await Assert.That(restored.AppVersion).IsEqualTo(original.AppVersion);
        await Assert.That(restored.Metadata.Width).IsEqualTo(original.Metadata.Width);
        await Assert.That(restored.Events.Count).IsEqualTo(1);
        await Assert.That(restored.Events[0].TimestampMs).IsEqualTo(2000L);
        await Assert.That(restored.Events[0].Regions[0].Intensity).IsEqualTo(0.9f);
    }
}
