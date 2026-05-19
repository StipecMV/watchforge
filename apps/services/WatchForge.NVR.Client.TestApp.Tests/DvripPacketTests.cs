namespace WatchForge.NVR.Client.TestApp.Tests;

public class DvripPacketTests
{
    // ── Build ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Build_AlwaysStartsWithMagicByte()
    {
        // Given any valid input
        var payload = "{}"u8.ToArray();

        // When a packet is built
        var bytes = DvripPacket.Build(sessionId: 0, seqNum: 0, msgId: 1000, payload);

        // Then byte 0 is 0xFF
        await Assert.That(bytes[0]).IsEqualTo(DvripPacket.Magic);
    }

    [Test]
    public async Task Build_VersionByteIsZero()
    {
        // Given a packet built with any parameters
        var bytes = DvripPacket.Build(0, 0, 1000, "{}"u8.ToArray());

        // When byte 1 is inspected
        // Then version is 0x00
        await Assert.That(bytes[1]).IsEqualTo(DvripPacket.Version);
    }

    [Test]
    public async Task Build_LoginPacket_HasCorrectMessageId()
    {
        // Given a login request message ID (1000)
        const ushort loginMsgId = 1000;
        var payload = "{}"u8.ToArray();

        // When built
        var bytes = DvripPacket.Build(0, 0, loginMsgId, payload);

        // Then bytes 14-15 (LE uint16) equal 1000
        var msgId = (ushort)(bytes[14] | (bytes[15] << 8));
        await Assert.That(msgId).IsEqualTo(loginMsgId);
    }

    [Test]
    public async Task Build_TotalLengthIsHeaderPlusPayload()
    {
        // Given a 10-byte payload
        var payload = new byte[10];

        // When built
        var bytes = DvripPacket.Build(0, 0, 1000, payload);

        // Then total length = 20 (header) + 10 (payload)
        await Assert.That(bytes.Length).IsEqualTo(DvripPacket.HeaderSize + 10);
    }

    [Test]
    public async Task Build_SessionIdIsEncodedLittleEndian()
    {
        // Given session ID 0x0000001B (27 decimal)
        uint sessionId = 0x0000001B;

        // When built
        var bytes = DvripPacket.Build(sessionId, 0, 1000, Array.Empty<byte>());

        // Then bytes 4-7 are LE encoding: 0x1B 0x00 0x00 0x00
        await Assert.That(bytes[4]).IsEqualTo((byte)0x1B);
        await Assert.That(bytes[5]).IsEqualTo((byte)0x00);
        await Assert.That(bytes[6]).IsEqualTo((byte)0x00);
        await Assert.That(bytes[7]).IsEqualTo((byte)0x00);
    }

    [Test]
    public async Task Build_PayloadLengthFieldMatchesActualPayload()
    {
        // Given a 7-byte payload
        var payload = new byte[7];

        // When built
        var bytes = DvripPacket.Build(0, 0, 1000, payload);

        // Then bytes 16-19 (LE uint32) equal 7
        var len = (uint)(bytes[16] | (bytes[17] << 8) | (bytes[18] << 16) | (bytes[19] << 24));
        await Assert.That(len).IsEqualTo(7u);
    }

    [Test]
    public async Task Build_PayloadBytesAppendedAfterHeader()
    {
        // Given a recognisable payload
        var payload = new byte[] { 0xAA, 0xBB, 0xCC };

        // When built
        var bytes = DvripPacket.Build(0, 0, 1000, payload);

        // Then bytes 20-22 equal the payload
        await Assert.That(bytes[20]).IsEqualTo((byte)0xAA);
        await Assert.That(bytes[21]).IsEqualTo((byte)0xBB);
        await Assert.That(bytes[22]).IsEqualTo((byte)0xCC);
    }

    // ── Parse ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Parse_ValidPacket_ReturnsCorrectSessionId()
    {
        // Given a packet built with session ID 0x1B
        var original = DvripPacket.Build(0x1B, 0, 1000, Array.Empty<byte>());

        // When parsed
        var packet = DvripPacket.Parse(original);

        // Then session ID is preserved
        await Assert.That(packet.SessionId).IsEqualTo(0x1Bu);
    }

    [Test]
    public async Task Parse_ValidPacket_ReturnsCorrectMessageId()
    {
        // Given a packet with message ID 1442 (OPFileQuery)
        var original = DvripPacket.Build(0, 5, 1442, Array.Empty<byte>());

        // When parsed
        var packet = DvripPacket.Parse(original);

        // Then message ID is 1442
        await Assert.That(packet.MessageId).IsEqualTo((ushort)1442);
    }

    [Test]
    public async Task Parse_ValidPacket_ReturnsCorrectSequenceNumber()
    {
        // Given a packet with sequence number 42
        var original = DvripPacket.Build(0, 42, 1000, Array.Empty<byte>());

        // When parsed
        var packet = DvripPacket.Parse(original);

        // Then the sequence number is preserved
        await Assert.That(packet.SequenceNumber).IsEqualTo(42u);
    }

    [Test]
    public async Task Parse_ValidPacket_PayloadMatchesOriginal()
    {
        // Given a packet with a known payload
        var payload  = new byte[] { 0x01, 0x02, 0x03 };
        var original = DvripPacket.Build(0, 0, 1000, payload);

        // When parsed
        var packet = DvripPacket.Parse(original);

        // Then payload bytes match
        await Assert.That(packet.Payload).IsEquivalentTo(payload);
    }

    [Test]
    public async Task Parse_TruncatedData_ThrowsInvalidDataException()
    {
        // Given a buffer that is shorter than the DVRIP header
        var truncated = new byte[10];

        // When parsed
        // Then InvalidDataException is thrown (not ArgumentException or similar)
        await Assert.That(() => DvripPacket.Parse(truncated))
            .ThrowsException()
            .WithMessageContaining("too short");
    }

    [Test]
    public async Task Parse_WrongMagicByte_ThrowsInvalidDataException()
    {
        // Given a 20-byte buffer where byte 0 is NOT 0xFF
        var data = new byte[20];
        data[0] = 0xFE; // wrong magic

        // When parsed
        // Then InvalidDataException is thrown mentioning the invalid magic
        await Assert.That(() => DvripPacket.Parse(data))
            .ThrowsException()
            .WithMessageContaining("magic");
    }

    [Test]
    public async Task Parse_PayloadTruncatedRelativeToHeader_ThrowsInvalidDataException()
    {
        // Given a header that declares 100 bytes of payload but only 5 follow
        var data    = new byte[DvripPacket.HeaderSize + 5];
        data[0]     = DvripPacket.Magic;
        // Write payload length = 100 in LE at offset 16
        data[16] = 100; data[17] = 0; data[18] = 0; data[19] = 0;

        // When parsed
        // Then InvalidDataException is thrown mentioning truncation
        await Assert.That(() => DvripPacket.Parse(data))
            .ThrowsException()
            .WithMessageContaining("truncated");
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Test]
    public async Task BuildThenParse_PreservesAllFields()
    {
        // Given known field values
        uint   sessionId = 0xDEADBEEF;
        uint   seqNum    = 99;
        ushort msgId     = 1443;
        var    payload   = new byte[] { 0x7B, 0x7D, 0x00 }; // "{}\0"

        // When built and then parsed
        var bytes  = DvripPacket.Build(sessionId, seqNum, msgId, payload);
        var packet = DvripPacket.Parse(bytes);

        // Then all fields survive the round-trip
        await Assert.That(packet.SessionId).IsEqualTo(sessionId);
        await Assert.That(packet.SequenceNumber).IsEqualTo(seqNum);
        await Assert.That(packet.MessageId).IsEqualTo(msgId);
        await Assert.That(packet.Payload).IsEquivalentTo(payload);
    }
}
