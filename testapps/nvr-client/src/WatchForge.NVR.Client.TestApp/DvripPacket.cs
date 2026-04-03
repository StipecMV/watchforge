namespace WatchForge.NVR.Client.TestApp;

/// <summary>
/// Represents a raw DVRIP (Sofia/Xiongmai) protocol packet.
///
/// Header layout (20 bytes, little-endian):
///   [0]     Magic = 0xFF
///   [1]     Version = 0x00
///   [2-3]   Reserved = 0x00 0x00
///   [4-7]   Session ID (uint32 LE)
///   [8-11]  Sequence number (uint32 LE)
///   [12]    Total packets
///   [13]    Current packet
///   [14-15] Message ID (uint16 LE)
///   [16-19] Payload length (uint32 LE)
///   [20+]   JSON payload (UTF-8, null-terminated)
/// </summary>
public sealed class DvripPacket
{
    public const byte Magic = 0xFF;
    public const byte Version = 0x00;
    public const int HeaderSize = 20;

    public uint SessionId { get; }
    public uint SequenceNumber { get; }
    public ushort MessageId { get; }
    public byte[] Payload { get; }

    public DvripPacket(uint sessionId, uint sequenceNumber, ushort messageId, byte[] payload)
    {
        SessionId = sessionId;
        SequenceNumber = sequenceNumber;
        MessageId = messageId;
        Payload = payload;
    }

    /// <summary>
    /// Builds a DVRIP packet byte array from the given fields.
    /// The payload is written as-is (caller is responsible for null termination if needed).
    /// </summary>
    public static byte[] Build(uint sessionId, uint seqNum, ushort msgId, byte[] payload)
    {
        var packet = new byte[HeaderSize + payload.Length];
        packet[0] = Magic;
        packet[1] = Version;
        // bytes [2-3]: reserved, remain 0
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), seqNum);
        packet[12] = 0; // total packets
        packet[13] = 0; // current packet
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14), msgId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), (uint)payload.Length);
        payload.CopyTo(packet, HeaderSize);
        return packet;
    }

    /// <summary>
    /// Parses a complete DVRIP packet from a byte array (header + payload).
    /// Throws <see cref="InvalidDataException"/> if the data is too short,
    /// the magic byte is wrong, or the buffer is truncated relative to the declared payload length.
    /// </summary>
    public static DvripPacket Parse(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException(
                $"Packet too short: {data.Length} bytes, need at least {HeaderSize}");

        if (data[0] != Magic)
            throw new InvalidDataException(
                $"Invalid magic byte: 0x{data[0]:X2}, expected 0x{Magic:X2}");

        var sessionId  = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var seqNum     = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        var msgId      = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14));
        var payloadLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16));

        if (data.Length < HeaderSize + payloadLen)
            throw new InvalidDataException(
                $"Packet truncated: expected {HeaderSize + payloadLen} bytes, got {data.Length}");

        var payload = data[HeaderSize..(HeaderSize + payloadLen)];
        return new DvripPacket(sessionId, seqNum, msgId, payload);
    }

    /// <summary>
    /// Reads the payload from a stream after the 20-byte header has already been read.
    /// The header bytes are passed in so this method can parse the length field.
    /// </summary>
    public static async Task<DvripPacket> ReadFromStreamAsync(Stream stream, byte[] header, CancellationToken ct = default)
    {
        if (header.Length < HeaderSize)
            throw new InvalidDataException($"Header buffer too short: {header.Length}");

        if (header[0] != Magic)
            throw new InvalidDataException($"Invalid magic byte: 0x{header[0]:X2}");

        var sessionId  = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        var seqNum     = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        var msgId      = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14));
        var payloadLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));

        var payload = new byte[payloadLen];
        if (payloadLen > 0)
            await ReadExactAsync(stream, payload, ct);

        return new DvripPacket(sessionId, seqNum, msgId, payload);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) throw new EndOfStreamException("Connection closed while reading DVRIP payload");
            offset += read;
        }
    }
}
