namespace WatchForge.NVR.Client.TestApp.Tests;

public class FileQueryTests
{
    // Known FileQuery response from the actual Movols NVR (from spec).
    // The NVR emits malformed datetimes: "2026-04-0210:08:02" (no space).
    private const string KnownNvrResponse = """
        {
          "Name": "OPFileQuery",
          "OPFileQuery": [{
            "BeginTime": "2026-04-0210:08:02",
            "DiskNo": 0,
            "EndTime": "2026-04-0210:30:00",
            "FileLength": "0x00103D75",
            "FileName": "/idea0/2026-04-02/002/10.08.02-10.30.00[R][@8600f][1].h264",
            "SerialNo": 0
          }],
          "Ret": 100,
          "SessionID": "0x1a"
        }
        """;

    // ── Normal response ───────────────────────────────────────────────────────

    [Test]
    public async Task ParseFileQueryResponse_KnownNvrResponse_ReturnsSingleFile()
    {
        // Given the known NVR response
        // When parsed
        var files = DvripClient.ParseFileQueryResponse(KnownNvrResponse);

        // Then exactly one file is returned
        await Assert.That(files.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ParseFileQueryResponse_KnownNvrResponse_FileNameIsCorrect()
    {
        // Given the known NVR response
        var files = DvripClient.ParseFileQueryResponse(KnownNvrResponse);

        // When the filename is read
        // Then it matches the NVR path exactly
        await Assert.That(files[0].FileName)
            .IsEqualTo("/idea0/2026-04-02/002/10.08.02-10.30.00[R][@8600f][1].h264");
    }

    [Test]
    public async Task ParseFileQueryResponse_KnownNvrResponse_MalformedBeginTimeParsedCorrectly()
    {
        // Given the malformed datetime "2026-04-0210:08:02"
        var files = DvripClient.ParseFileQueryResponse(KnownNvrResponse);

        // When BeginTime is read
        // Then the missing space is handled and the correct DateTime is produced
        await Assert.That(files[0].BeginTime).IsEqualTo(new DateTime(2026, 4, 2, 10, 8, 2));
    }

    [Test]
    public async Task ParseFileQueryResponse_KnownNvrResponse_MalformedEndTimeParsedCorrectly()
    {
        // Given the malformed datetime "2026-04-0210:30:00"
        var files = DvripClient.ParseFileQueryResponse(KnownNvrResponse);

        // When EndTime is read
        // Then it is parsed correctly
        await Assert.That(files[0].EndTime).IsEqualTo(new DateTime(2026, 4, 2, 10, 30, 0));
    }

    [Test]
    public async Task ParseFileQueryResponse_KnownNvrResponse_HexFileLengthParsedToBytes()
    {
        // Given FileLength "0x00103D75" = 1064309 1024-byte blocks
        var files = DvripClient.ParseFileQueryResponse(KnownNvrResponse);

        // When FileLengthBytes is read
        // Then it equals 1064309 * 1024 bytes
        await Assert.That(files[0].FileLengthBytes).IsEqualTo(1_064_309L * 1024);
    }

    [Test]
    public async Task ParseFileQueryResponse_KnownNvrResponse_DiskNoAndSerialNoAreZero()
    {
        // Given the known NVR response where DiskNo=0 and SerialNo=0
        var files = DvripClient.ParseFileQueryResponse(KnownNvrResponse);

        // When read
        // Then both are zero
        await Assert.That(files[0].DiskNo).IsEqualTo(0);
        await Assert.That(files[0].SerialNo).IsEqualTo(0);
    }

    // ── Empty file list ───────────────────────────────────────────────────────

    [Test]
    public async Task ParseFileQueryResponse_EmptyFileList_ReturnsEmptyList()
    {
        // Given a valid response with an empty OPFileQuery array
        var json = """
            {
              "Name": "OPFileQuery",
              "OPFileQuery": [],
              "Ret": 100,
              "SessionID": "0x1a"
            }
            """;

        // When parsed
        var files = DvripClient.ParseFileQueryResponse(json);

        // Then an empty list is returned (not null, not an exception)
        await Assert.That(files.Count).IsEqualTo(0);
    }

    // ── Non-100 return codes ──────────────────────────────────────────────────

    [Test]
    public async Task ParseFileQueryResponse_RetCode515_ReturnsEmptyList()
    {
        // Given a response with Ret=515 (no files / permission error on some firmwares)
        var json = """
            {
              "Name": "OPFileQuery",
              "OPFileQuery": [],
              "Ret": 515,
              "SessionID": "0x1a"
            }
            """;

        // When parsed
        var files = DvripClient.ParseFileQueryResponse(json);

        // Then an empty list is returned
        await Assert.That(files.Count).IsEqualTo(0);
    }

    // ── Multiple files ────────────────────────────────────────────────────────

    [Test]
    public async Task ParseFileQueryResponse_MultipleFiles_ReturnsAllFiles()
    {
        // Given a response with two files
        var json = """
            {
              "Name": "OPFileQuery",
              "OPFileQuery": [
                {
                  "BeginTime": "2026-04-02 08:00:00",
                  "DiskNo": 0,
                  "EndTime": "2026-04-02 08:30:00",
                  "FileLength": "0x00100000",
                  "FileName": "/idea0/2026-04-02/001/08.00.00-08.30.00[R][1].h264",
                  "SerialNo": 0
                },
                {
                  "BeginTime": "2026-04-02 09:00:00",
                  "DiskNo": 0,
                  "EndTime": "2026-04-02 09:30:00",
                  "FileLength": "0x00200000",
                  "FileName": "/idea0/2026-04-02/001/09.00.00-09.30.00[R][2].h264",
                  "SerialNo": 1
                }
              ],
              "Ret": 100,
              "SessionID": "0x1a"
            }
            """;

        // When parsed
        var files = DvripClient.ParseFileQueryResponse(json);

        // Then both files are returned
        await Assert.That(files.Count).IsEqualTo(2);
        await Assert.That(files[0].SerialNo).IsEqualTo(0);
        await Assert.That(files[1].SerialNo).IsEqualTo(1);
    }

    // ── Mixed datetime formats ────────────────────────────────────────────────

    [Test]
    public async Task ParseFileQueryResponse_MixedDatetimeFormats_BothParsedCorrectly()
    {
        // Given a response where one file has the standard format and one has the malformed format
        var json = """
            {
              "Name": "OPFileQuery",
              "OPFileQuery": [
                {
                  "BeginTime": "2026-04-02 08:00:00",
                  "DiskNo": 0,
                  "EndTime": "2026-04-0208:30:00",
                  "FileLength": "0x100",
                  "FileName": "/idea0/file1.h264",
                  "SerialNo": 0
                }
              ],
              "Ret": 100,
              "SessionID": "0x1a"
            }
            """;

        // When parsed
        var files = DvripClient.ParseFileQueryResponse(json);

        // Then both formats are handled correctly in the same record
        await Assert.That(files[0].BeginTime).IsEqualTo(new DateTime(2026, 4, 2, 8, 0, 0));
        await Assert.That(files[0].EndTime).IsEqualTo(new DateTime(2026, 4, 2, 8, 30, 0));
    }
}
