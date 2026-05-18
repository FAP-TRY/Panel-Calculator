using System.Runtime.Versioning;
using PanelCalculator.Data.Security;
using Xunit;

namespace PanelCalculator.Tests.Security;

[SupportedOSPlatform("windows")]
public class MachineKeyProviderTests
{
    [Fact]
    public void GetKey_IsDeterministic_OnSameMachine()
    {
        MachineKeyProvider.ResetCache();
        var first = MachineKeyProvider.GetKey();
        MachineKeyProvider.ResetCache();
        var second = MachineKeyProvider.GetKey();

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetKey_ReturnsHexStringOf64Chars()
    {
        MachineKeyProvider.ResetCache();
        var key = MachineKeyProvider.GetKey();

        Assert.NotNull(key);
        Assert.Equal(64, key.Length);
        // SHA-256 hex is [0-9a-f]
        Assert.Matches("^[0-9a-f]{64}$", key);
    }

    [Fact]
    public void GetKey_CachesAfterFirstCall()
    {
        MachineKeyProvider.ResetCache();
        var first  = MachineKeyProvider.GetKey();
        var second = MachineKeyProvider.GetKey(); // no reset → from cache
        Assert.Same(first, second);
    }

    [Fact]
    public void HardwareFingerprint_IsDeterministic()
    {
        MachineKeyProvider.ResetCache();
        var first  = MachineKeyProvider.GetHardwareFingerprintBytes();
        MachineKeyProvider.ResetCache();
        var second = MachineKeyProvider.GetHardwareFingerprintBytes();

        Assert.Equal(first, second);
        Assert.Equal(8, first.Length);
    }

    [Fact]
    public void HardwareFingerprintDisplay_HasExpectedFormat()
    {
        MachineKeyProvider.ResetCache();
        var display = MachineKeyProvider.GetHardwareFingerprintDisplay();

        // Format: XXXX-XXXX-XXXX-XXXX (16 hex chars + 3 dashes = 19 chars)
        Assert.Equal(19, display.Length);
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", display);
    }

    [Fact]
    public void HardwareFingerprint_DisplayAndBytes_RoundTrip()
    {
        MachineKeyProvider.ResetCache();
        var bytes   = MachineKeyProvider.GetHardwareFingerprintBytes();
        var display = MachineKeyProvider.GetHardwareFingerprintDisplay();
        var parsed  = MachineKeyProvider.ParseHardwareFingerprintDisplay(display);

        Assert.Equal(bytes, parsed);
    }
}
