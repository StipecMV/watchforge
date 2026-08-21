using WatchForge.Testing.FakeNvr;

namespace WatchForge.DVRIP.Library.Tests;

/// <summary>
/// S3-4: integračné testy download pipeline proti fake NVR
/// (login → file query → playback claim → download dáta),
/// využívajúce reusable knižnicu WatchForge.Testing.FakeNvr.
/// </summary>
public class DownloadPipelineIntegrationTests
{
    [Test]
    public async Task FullPipeline_LoginQueryDownload_EndToEnd()
    {
        // Given fake NVR s dvoma nahrávkami (segment + event klip)
        var recordings = new[]
        {
            new FakeDvripServer.RecordingEntry(
                FileName: "[Ch0]_2026-04-05_15.00.00-15.15.mkv",
                BeginTime: new DateTime(2026, 4, 5, 15, 0, 0),
                EndTime: new DateTime(2026, 4, 5, 15, 15, 0),
                LengthBlocks: 64),
            new FakeDvripServer.RecordingEntry(
                FileName: "[Ch2]_2026-04-05_15.30.00-15.30.30.mkv",
                BeginTime: new DateTime(2026, 4, 5, 15, 30, 0),
                EndTime: new DateTime(2026, 4, 5, 15, 30, 30),
                LengthBlocks: 8)
        };
        using var server = new FakeDvripServer(recordings: recordings);
        await server.StartAsync();

        // When kompletný pipeline: login → query → download (cez CreateClient helper)
        using var client = server.CreateClient("admin", "secret");
        var login = await client.LoginAsync();
        var files = await client.QueryFilesAsync(
            new DateTime(2026, 4, 5, 14, 0, 0),
            new DateTime(2026, 4, 5, 16, 0, 0),
            channel: 0);

        // Then login prebehol a query na channel 0 vrátil len záznam Ch0 (channel filter funguje)
        await Assert.That(login.SessionId).IsNotEqualTo(0u);
        await Assert.That(files).Count().IsEqualTo(1);
        await Assert.That(files[0].FileName).IsEqualTo(recordings[0].FileName);

        // And query na channel 2 vrátil event klip
        var ch2 = await client.QueryFilesAsync(
            new DateTime(2026, 4, 5, 14, 0, 0),
            new DateTime(2026, 4, 5, 16, 0, 0),
            channel: 2);
        await Assert.That(ch2).Count().IsEqualTo(1);
        await Assert.That(ch2[0].FileName).IsEqualTo(recordings[1].FileName);
        await Assert.That(ch2[0].BeginTime).IsEqualTo(new DateTime(2026, 4, 5, 15, 30, 0));
    }

    [Test]
    public async Task Download_SequentialFiles_ReconnectsPerFile()
    {
        // Given fake NVR s tromi nahrávkami
        var recordings = Enumerable.Range(0, 3)
            .Select(i => new FakeDvripServer.RecordingEntry(
                FileName: $"[Ch0]_2026-04-05_{16 + i}.00.00-{16 + i}.15.mkv",
                BeginTime: new DateTime(2026, 4, 5, 16 + i, 0, 0),
                EndTime: new DateTime(2026, 4, 5, 16 + i, 15, 0),
                LengthBlocks: 16))
            .ToArray();
        using var server = new FakeDvripServer(recordings: recordings, fillByte: 0xBB);
        await server.StartAsync();

        var tmpDir = Path.Combine(Path.GetTempPath(), "watchforge-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            // When sťahujeme všetky súbory sekvenčne cez jedného klienta
            using var client = server.CreateClient("admin", "secret");
            await client.LoginAsync();
            var files = await client.QueryFilesAsync(
                new DateTime(2026, 4, 5, 16, 0, 0),
                new DateTime(2026, 4, 5, 19, 0, 0),
                channel: 0);

            var outputs = new List<string>();
            foreach (var file in files)
                outputs.Add(await client.DownloadFileAsync(file, Path.Combine(tmpDir, file.FileName), "mkv"));

            // Then každý súbor je stiahnutý a má očakávanú veľkosť (ffmpeg nenájdený → raw)
            await Assert.That(outputs).Count().IsEqualTo(3);
            foreach (var output in outputs)
            {
                await Assert.That(File.Exists(output)).IsTrue();
                var bytes = await File.ReadAllBytesAsync(output);
                await Assert.That(bytes.Length).IsEqualTo(16 * 1024);
                await Assert.That(bytes.All(b => b == 0xBB)).IsTrue();
            }
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Test]
    public async Task Server_TracksRequestCount()
    {
        // Given fake NVR
        using var server = new FakeDvripServer();
        await server.StartAsync();

        // When urobíme login + query
        using var client = server.CreateClient("admin", "secret");
        await client.LoginAsync();
        await client.QueryFilesAsync(
            new DateTime(2026, 4, 5, 0, 0, 0),
            new DateTime(2026, 4, 5, 1, 0, 0));

        // Then server zaznamenal aspoň 2 requesty (login + file query)
        await Assert.That(server.RequestCount).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task Download_OutputFormatMatchesRawExtension_KeepsRawFile()
    {
        // S22j regresia: ak cieľová prípona == prípona rawPath (napr. ".mkv"),
        // download NESMIE vymazať stiahnutý súbor (outputPath == rawPath —
        // predtým ffmpeg konvertoval do seba, zlyhal a cleanup zmazal raw).
        var recordings = new[]
        {
            new FakeDvripServer.RecordingEntry(
                FileName: "[Ch0]_2026-04-05_17.00.00-17.15.mkv",
                BeginTime: new DateTime(2026, 4, 5, 17, 0, 0),
                EndTime: new DateTime(2026, 4, 5, 17, 15, 0),
                LengthBlocks: 16)
        };
        using var server = new FakeDvripServer(recordings: recordings, fillByte: 0xBB);
        await server.StartAsync();

        var tmpDir = Path.Combine(Path.GetTempPath(), "watchforge-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            using var client = server.CreateClient("admin", "secret");
            await client.LoginAsync();
            var files = await client.QueryFilesAsync(
                new DateTime(2026, 4, 5, 17, 0, 0),
                new DateTime(2026, 4, 5, 17, 30, 0),
                channel: 0);

            // When stiahneme s outputFormat zhodným s príponou názvu (mkv)
            var output = await client.DownloadFileAsync(
                files[0], Path.Combine(tmpDir, files[0].FileName), "mkv");

            // Then súbor stále existuje s pôvodnými dátami (nie je zmazaný)
            await Assert.That(File.Exists(output)).IsTrue();
            var bytes = await File.ReadAllBytesAsync(output);
            await Assert.That(bytes.Length).IsEqualTo(16 * 1024);
            await Assert.That(bytes.All(b => b == 0xBB)).IsTrue();
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Test]
    public async Task Query_NoRecordings_ReturnsEmpty()
    {
        // Given fake NVR bez nahrávok
        using var server = new FakeDvripServer();
        await server.StartAsync();

        // When query na ľubovoľný čas
        using var client = server.CreateClient("admin", "secret");
        await client.LoginAsync();
        var files = await client.QueryFilesAsync(
            new DateTime(2026, 4, 5, 0, 0, 0),
            new DateTime(2026, 4, 5, 23, 59, 59));

        // Then prázdny zoznam
        await Assert.That(files).IsEmpty();
    }

    [Test]
    public async Task Download_UnknownFile_TimesOut()
    {
        // Given fake NVR s jednou nahrávkou
        var recording = new FakeDvripServer.RecordingEntry(
            FileName: "[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            BeginTime: new DateTime(2026, 4, 5, 15, 0, 0),
            EndTime: new DateTime(2026, 4, 5, 15, 15, 0),
            LengthBlocks: 32);
        using var server = new FakeDvripServer(recordings: [recording]);
        await server.StartAsync();

        var tmpDir = Path.Combine(Path.GetTempPath(), "watchforge-unknown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            // When sťahujeme súbor, ktorý fake server nemá (krátky read timeout 2s)
            using var client = server.CreateClient("admin", "secret", readTimeoutSeconds: 2);
            await client.LoginAsync();
            var bogus = new NvrFile
            {
                FileName = "[Ch0]_2026-04-05_18.00.00-18.15.mkv",
                BeginTime = new DateTime(2026, 4, 5, 18, 0, 0),
                EndTime = new DateTime(2026, 4, 5, 18, 15, 0),
                FileLengthBytes = 32 * 1024L
            };

            // Then download vyhodí IOException (NVR neodpovedá — timeout, nie večné čakanie)
            await Assert.That(async () =>
                {
                    await client.DownloadFileAsync(bogus, Path.Combine(tmpDir, "bogus.raw"), "mkv");
                })
                .Throws<IOException>();
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }
}
