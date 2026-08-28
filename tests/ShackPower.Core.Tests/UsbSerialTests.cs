using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

public class UsbSerialTests
{
    [Theory]
    // Real serials seen on the AB0R bench (FTDI adapters for the LP-100A and the W2s).
    [InlineData(@"FTDIBUS\VID_0403+PID_6001+A10KMB4VA\0000", "A10KMB4VA")]
    [InlineData(@"FTDIBUS\VID_0403+PID_6001+AG0JFX7UA\0000", "AG0JFX7UA")]
    [InlineData(@"FTDIBUS\VID_0403+PID_6015+AD0JLU2FA\0000", "AD0JLU2FA")]
    public void ExtractsFtdiSerial(string pnp, string expected) =>
        Assert.Equal(expected, UsbSerial.Extract(pnp));

    [Fact]
    public void ExtractsGenericUsbSerial() =>
        Assert.Equal("0001", UsbSerial.Extract(@"USB\VID_10C4&PID_EA60\0001"));

    [Theory]
    // An adapter with no serial burned in gets a location-based id instead. Pinning to one would
    // follow the USB SOCKET, not the cable — so it must not be mistaken for a serial. Both of these
    // were live on the bench (a dual-channel FTDI) and were being accepted before.
    [InlineData(@"FTDIBUS\VID_0403+PID_6010+6&122B2E46&0&1&1\0000")]
    [InlineData(@"FTDIBUS\VID_0403+PID_6010+6&122B2E46&0&1&2\0000")]
    [InlineData(@"USB\VID_10C4&PID_EA60\6&2F1B0E5A&0&2")]
    public void RejectsLocationIdMasqueradingAsSerial(string pnp) =>
        Assert.Null(UsbSerial.Extract(pnp));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"ACPI\PNP0501\1")]                 // a legacy motherboard COM port
    [InlineData(@"BTHENUM\{0000110{-0000}\7&1A2B")] // bluetooth serial, no match
    public void ReturnsNullWhenThereIsNoSerial(string pnp) =>
        Assert.Null(UsbSerial.Extract(pnp));
}
