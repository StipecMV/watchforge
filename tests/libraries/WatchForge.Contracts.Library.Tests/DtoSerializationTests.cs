namespace WatchForge.Contracts.Library.Tests;

/// <summary>
/// S1-6: overuje serializáciu a štruktúru základných DTO kontraktov,
/// ktoré API/service/web zdieľajú (DRY — jeden zdroj pravdy).
/// </summary>
public class DtoSerializationTests
{
    [Test]
    public async Task CameraDto_SerializesWithCamelCase()
    {
        // Given a camera DTO
        var dto = new CameraDto
        {
            CameraId = 3,
            NvrId = 1,
            Channel = 2,
            FriendlyName = "Dvor",
            IconId = "garden",
            IsActive = true
        };

        // When serialized
        var json = System.Text.Json.JsonSerializer.Serialize(dto);

        // Then JSON uses camelCase (frontend convention) and contains all fields
        await Assert.That(json).Contains("\"cameraId\":3");
        await Assert.That(json).Contains("\"channel\":2");
        await Assert.That(json).Contains("\"friendlyName\":\"Dvor\"");
        await Assert.That(json).Contains("\"isActive\":true");
    }

    [Test]
    public async Task RecordingDto_RoundTrips()
    {
        // Given a recording DTO with all fields
        var dto = new RecordingDto
        {
            RecordingId = 42,
            CameraId = 1,
            NvrId = 1,
            SourceType = "segment",
            NvrFilename = "[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            BeginTime = new DateTime(2026, 4, 5, 15, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 4, 5, 15, 15, 0, DateTimeKind.Utc),
            DurationSec = 900,
            SizeBytes = 700_000_000,
            Codec = "hevc",
            Width = 3840,
            Height = 2160,
            Availability = "available",
            AnalysisState = "completed",
            Persisted = false
        };

        // When round-tripped through JSON
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        var back = System.Text.Json.JsonSerializer.Deserialize<RecordingDto>(json);

        // Then all fields survive
        await Assert.That(back).IsNotNull();
        await Assert.That(back!.RecordingId).IsEqualTo(42);
        await Assert.That(back.NvrFilename).IsEqualTo(dto.NvrFilename);
        await Assert.That(back.BeginTime).IsEqualTo(dto.BeginTime);
        await Assert.That(back.DurationSec).IsEqualTo(900);
        await Assert.That(back.SizeBytes).IsEqualTo(700_000_000L);
        await Assert.That(back.Width).IsEqualTo(3840);
        await Assert.That(back.Availability).IsEqualTo("available");
    }

    [Test]
    public async Task DetectionDto_IncludesFlagAndNormalizedRegion()
    {
        // Given a detection DTO with flag + normalized coordinates (0..1)
        var dto = new DetectionDto
        {
            DetectionId = 7,
            RecordingId = 42,
            CameraId = 1,
            DetectionType = "person",
            TimestampMs = 65_000,
            DurationMs = 2_500,
            Confidence = 0.87f,
            RegionX = 0.25f,
            RegionY = 0.10f,
            RegionW = 0.30f,
            RegionH = 0.50f,
            Intensity = 0f,
            ObjectClass = "person",
            Flag = "none"
        };

        // When serialized
        var json = System.Text.Json.JsonSerializer.Serialize(dto);

        // Then normalized coordinates and flag are present
        await Assert.That(json).Contains("\"regionX\":0.25");
        await Assert.That(json).Contains("\"detectionType\":\"person\"");
        await Assert.That(json).Contains("\"flag\":\"none\"");
    }

    [Test]
    public async Task CreateRequestDto_DefaultsAreSensible()
    {
        // Given a new request DTO (agent/web vytvárajú interaktívnu požiadavku)
        var dto = new CreateRequestDto
        {
            FromTime = new DateTime(2026, 4, 5, 14, 0, 0, DateTimeKind.Utc),
            ToTime = new DateTime(2026, 4, 5, 16, 0, 0, DateTimeKind.Utc),
            CameraId = null,       // všetky kamery
            DetectionTypeFilter = null, // motion default
            ContextBeforeSec = 15,
            ContextAfterSec = 15
        };

        // When validated
        var errors = new List<string>();
        if (dto.FromTime >= dto.ToTime) errors.Add("from >= to");
        if (dto.ToTime - dto.FromTime > TimeSpan.FromHours(24)) errors.Add("range > 24h");
        if (dto.ContextBeforeSec is < 0 or > 120) errors.Add("contextBefore out of range");
        if (dto.ContextAfterSec is < 0 or > 120) errors.Add("contextAfter out of range");

        // Then defaults pass validation
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task RequestStatusDto_ContainsEstimate()
    {
        // Given a request status DTO (agent polluje stav)
        var dto = new RequestStatusDto
        {
            RequestId = 99,
            Status = "processing",
            Estimate = "~3 min (2 segmenty)",
            ClipIds = [],
            Error = null
        };

        // When serialized
        var json = System.Text.Json.JsonSerializer.Serialize(dto);

        // Then agent-friendly fields are present
        await Assert.That(json).Contains("\"requestId\":99");
        await Assert.That(json).Contains("\"status\":\"processing\"");
        await Assert.That(json).Contains("\"estimate\":\"~3 min (2 segmenty)\"");
    }
}
