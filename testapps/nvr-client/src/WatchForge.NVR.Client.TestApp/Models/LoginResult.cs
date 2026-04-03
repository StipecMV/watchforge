namespace WatchForge.NVR.Client.TestApp;

public sealed class LoginResult
{
    public string DeviceType { get; init; } = "";
    public int ChannelNum { get; init; }
    public uint SessionId { get; init; }
    public int AliveInterval { get; init; }

    public string SessionIdHex => $"0x{SessionId:X8}";
}
