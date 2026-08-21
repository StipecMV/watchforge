using WatchForge.DVRIP.Library;

namespace WatchForge.DVRIP.Library.Tests;

/// <summary>
/// S3-2: DvripClientOptions validácia + interface kontrakt (IDvripClient).
/// </summary>
public class DvripClientOptionsTests
{
    [Test]
    public async Task Options_DefaultPort_IsDvripPort()
    {
        // Given nové options bez portu
        var options = new DvripClientOptions { Host = "192.168.68.10", Username = "admin", Password = "p" };

        // Then default port je DVRIP 34567
        await Assert.That(options.Port).IsEqualTo(DvripClientOptions.DefaultPort);
        await Assert.That(options.Port).IsEqualTo(34567);
    }

    [Test]
    public async Task Options_Valid_DoesNotThrow()
    {
        // Given platná konfigurácia
        var options = new DvripClientOptions { Host = "192.168.68.10", Username = "nvr-user", Password = "secret" };

        // When validate
        // Then nevyhodí výnimku
        options.Validate();
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task Options_MissingHost_Throws()
    {
        // Given konfigurácia bez hostu
        var options = new DvripClientOptions { Username = "admin", Password = "p" };

        // When validate
        var ex = await Assert.That(() => options.Validate()).Throws<ArgumentException>();
        await Assert.That(ex!.ParamName).IsEqualTo("Host");
    }

    [Test]
    public async Task Options_BlankHost_Throws()
    {
        // Given host len z bielych znakov
        var options = new DvripClientOptions { Host = "   ", Username = "admin", Password = "p" };

        // When validate
        // Then throw
        await Assert.That(() => options.Validate()).Throws<ArgumentException>();
    }

    [Test]
    public async Task Options_InvalidPortZero_Throws()
    {
        // Given port 0
        var options = new DvripClientOptions { Host = "h", Port = 0, Username = "u", Password = "p" };

        // When validate
        var ex = await Assert.That(() => options.Validate()).Throws<ArgumentOutOfRangeException>();
        await Assert.That(ex!.ParamName).IsEqualTo("Port");
    }

    [Test]
    public async Task Options_InvalidPortTooHigh_Throws()
    {
        // Given port nad 65535
        var options = new DvripClientOptions { Host = "h", Port = 70000, Username = "u", Password = "p" };

        // When validate
        // Then throw
        await Assert.That(() => options.Validate()).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Options_MissingUsername_Throws()
    {
        // Given konfigurácia bez username
        var options = new DvripClientOptions { Host = "h", Password = "p" };

        // When validate
        var ex = await Assert.That(() => options.Validate()).Throws<ArgumentException>();
        await Assert.That(ex!.ParamName).IsEqualTo("Username");
    }

    [Test]
    public async Task Options_NullPassword_Throws()
    {
        // Given password = null
        var options = new DvripClientOptions { Host = "h", Username = "u", Password = null! };

        // When validate
        var ex = await Assert.That(() => options.Validate()).Throws<ArgumentException>();
        await Assert.That(ex!.ParamName).IsEqualTo("Password");
    }

    [Test]
    public async Task DvripClient_NullOptions_Throws()
    {
        // Given null options
        // When konštruktor
        // Then ArgumentNullException
        await Assert.That(() => new DvripClient(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task DvripClient_InvalidOptions_Throws()
    {
        // Given neplatné options (bez hostu)
        var options = new DvripClientOptions { Username = "u", Password = "p" };

        // When konštruktor
        // Then ArgumentException
        await Assert.That(() => new DvripClient(options)).Throws<ArgumentException>();
    }

    [Test]
    public async Task DvripClient_ImplementsIDvripClient()
    {
        // Given inštancia cez options
        using var client = new DvripClient(new DvripClientOptions
        {
            Host = "127.0.0.1", Port = 34567, Username = "admin", Password = "secret"
        });

        // Then je to IDvripClient (kontrakt pre DI / testability)
        await Assert.That(client).IsTypeOf<DvripClient>();
        IDvripClient contract = client;
        await Assert.That(contract).IsNotNull();
    }

    [Test]
    public async Task DvripClient_PositionalCtor_IsCompatible()
    {
        // Given pozičný konštruktor (kompatibilný shim)
        using var client = new DvripClient("127.0.0.1", 34567, "admin", "secret");

        // Then funguje ako IDvripClient
        IDvripClient contract = client;
        await Assert.That(contract).IsNotNull();
    }
}
