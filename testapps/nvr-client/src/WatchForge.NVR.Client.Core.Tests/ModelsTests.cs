namespace WatchForge.NVR.Client.Core.Tests;

public class ModelsTests
{
    [Test]
    public async Task DeviceInformation_Constructor_SetsProperties()
    {
        var device = new DeviceInformation(
            Manufacturer: "TestManufacturer",
            Model: "TestModel",
            FirmwareVersion: "1.0.0",
            SerialNumber: "SN123",
            HardwareId: "HW456");

        await Assert.That(device.Manufacturer).IsEqualTo("TestManufacturer");
        await Assert.That(device.Model).IsEqualTo("TestModel");
        await Assert.That(device.FirmwareVersion).IsEqualTo("1.0.0");
        await Assert.That(device.SerialNumber).IsEqualTo("SN123");
        await Assert.That(device.HardwareId).IsEqualTo("HW456");
    }

    [Test]
    public async Task DeviceInformation_WithNullOptionalFields_NullablePropertiesAreNull()
    {
        var device = new DeviceInformation(
            Manufacturer: "TestManufacturer",
            Model: "TestModel",
            FirmwareVersion: "1.0.0");

        await Assert.That(device.SerialNumber).IsNull();
        await Assert.That(device.HardwareId).IsNull();
    }

    [Test]
    public async Task MediaProfile_Constructor_SetsProperties()
    {
        var profile = new MediaProfile(
            Token: "token1",
            Name: "Profile1",
            IsFixed: true);

        await Assert.That(profile.Token).IsEqualTo("token1");
        await Assert.That(profile.Name).IsEqualTo("Profile1");
        await Assert.That(profile.IsFixed).IsTrue();
    }

    [Test]
    public async Task MediaProfile_WithNullIsFixed_IsFixedIsNull()
    {
        var profile = new MediaProfile(
            Token: "token1",
            Name: "Profile1");

        await Assert.That(profile.IsFixed).IsNull();
    }

    [Test]
    public async Task StreamUri_Constructor_SetsAllProperties()
    {
        var uri = new StreamUri(
            Uri: "rtsp://example.com/stream",
            InvalidAfterConnect: "true",
            InvalidAfterReboot: "false",
            Timeout: "30");

        await Assert.That(uri.Uri).IsEqualTo("rtsp://example.com/stream");
        await Assert.That(uri.InvalidAfterConnect).IsEqualTo("true");
        await Assert.That(uri.InvalidAfterReboot).IsEqualTo("false");
        await Assert.That(uri.Timeout).IsEqualTo("30");
    }

    [Test]
    public async Task OnvifService_Constructor_SetsProperties()
    {
        var service = new OnvifService(
            Namespace: "http://www.onvif.org/ver20/device/wsdl",
            XAddr: "http://192.168.1.1:8080/onvif/device_service",
            Version: "1.0");

        await Assert.That(service.Namespace).IsEqualTo("http://www.onvif.org/ver20/device/wsdl");
        await Assert.That(service.XAddr).IsEqualTo("http://192.168.1.1:8080/onvif/device_service");
        await Assert.That(service.Version).IsEqualTo("1.0");
    }

    [Test]
    public async Task Recording_Constructor_SetsProperties()
    {
        var recording = new Recording(
            Token: "rec1",
            Description: "Recording 1",
            Source: "Camera 1");

        await Assert.That(recording.Token).IsEqualTo("rec1");
        await Assert.That(recording.Description).IsEqualTo("Recording 1");
        await Assert.That(recording.Source).IsEqualTo("Camera 1");
    }

    [Test]
    public async Task MotionEvent_Constructor_SetsProperties()
    {
        var timestamp = DateTime.UtcNow;

        var @event = new MotionEvent(
            Timestamp: timestamp,
            IsMotion: true,
            Source: "Camera 1",
            Description: "Motion detected");

        await Assert.That(@event.Timestamp).IsEqualTo(timestamp);
        await Assert.That(@event.IsMotion).IsTrue();
        await Assert.That(@event.Source).IsEqualTo("Camera 1");
        await Assert.That(@event.Description).IsEqualTo("Motion detected");
    }

    [Test]
    public async Task StreamType_Enum_HasCorrectValues()
    {
        await Assert.That((int)StreamType.RTPUnicast).IsEqualTo(0);
        await Assert.That((int)StreamType.RTPMulticast).IsEqualTo(1);
    }

    [Test]
    public async Task TransportProtocol_Enum_HasCorrectValues()
    {
        await Assert.That((int)TransportProtocol.RTSP).IsEqualTo(0);
        await Assert.That((int)TransportProtocol.RTP).IsEqualTo(1);
        await Assert.That((int)TransportProtocol.UDP).IsEqualTo(2);
        await Assert.That((int)TransportProtocol.TCP).IsEqualTo(3);
    }

    [Test]
    public async Task DeviceInformation_IsRecord_SupportsValueEquality()
    {
        var device1 = new DeviceInformation("Mfg", "Model", "1.0", "SN1", "HW1");
        var device2 = new DeviceInformation("Mfg", "Model", "1.0", "SN1", "HW1");
        var device3 = new DeviceInformation("Mfg", "Model", "2.0", "SN1", "HW1");

        await Assert.That(device1).IsEqualTo(device2);
        await Assert.That(device1).IsNotEqualTo(device3);
    }

    [Test]
    public async Task MediaProfile_IsRecord_SupportsValueEquality()
    {
        var profile1 = new MediaProfile("token1", "Profile1", true);
        var profile2 = new MediaProfile("token1", "Profile1", true);
        var profile3 = new MediaProfile("token2", "Profile1", true);

        await Assert.That(profile1).IsEqualTo(profile2);
        await Assert.That(profile1).IsNotEqualTo(profile3);
    }
}
