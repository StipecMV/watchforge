namespace WatchForge.MotionSentinel.Server.Core.Output;

/// <summary>Serialises a <see cref="DetectionResult"/> to a camelCase, indented JSON string.</summary>
public sealed class DetectionJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.Never,
        WriteIndented               = true,
    };

    /// <summary>Returns the JSON representation of <paramref name="result"/>.</summary>
    public string Serialize(DetectionResult result)
        => JsonSerializer.Serialize(result, Options);
}
