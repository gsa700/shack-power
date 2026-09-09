using System.Net;
using System.Net.Sockets;
using FluentModbus;
using ShackPower.Core;
using ShackPower.Core.Cerbo;
using Xunit;

namespace ShackPower.Core.Tests.Cerbo;

/// <summary>
/// End to end over a real TCP socket: a FluentModbus server on loopback stands in for the Cerbo,
/// the production <see cref="FluentModbusCerbo"/> client talks to it, and the reading source's
/// supervisor thread turns registers into <see cref="PowerReading"/>s. This is the closest the
/// test box can get to Thursday's hardware — byte order, register addressing and the threading
/// are all the real thing; only the register values are staged.
/// </summary>
public class CerboReadingSourceTests : IDisposable
{
    private const byte Shunt = 226;
    private const byte Multi = 227;
    private readonly ModbusTcpServer _server = new(isAsynchronous: true);
    private readonly IPEndPoint _endpoint;

    public CerboReadingSourceTests()
    {
        _endpoint = new IPEndPoint(IPAddress.Loopback, FreePort());
        _server.AddUnit(Shunt);
        _server.AddUnit(Multi);
        _server.AddUnit(CerboRegisters.SystemUnit);

        var shunt = _server.GetHoldingRegisters(Shunt);
        Set(shunt, CerboRegisters.Battery.Power, -77);
        Set(shunt, CerboRegisters.Battery.Voltage, 13.10);
        Set(shunt, CerboRegisters.Battery.Current, -5.9);
        Set(shunt, CerboRegisters.Battery.ConsumedAh, -12.4);
        Set(shunt, CerboRegisters.Battery.Soc, 87.5);
        Set(shunt, CerboRegisters.Battery.MinVoltage, 12.86);
        Set(shunt, CerboRegisters.Battery.MaxVoltage, 14.28);
        Set(shunt, CerboRegisters.Battery.DischargedKwh, 2.3);
        Set(shunt, CerboRegisters.Battery.ChargedKwh, 0.4);
        shunt.SetBigEndian<ushort>(303, 162);

        var multi = _server.GetHoldingRegisters(Multi);
        Set(multi, CerboRegisters.VeBus.State, 5);
        Set(multi, CerboRegisters.VeBus.AcInVoltage, 121.3);
        Set(multi, CerboRegisters.VeBus.AcInPower, 160);
        Set(multi, CerboRegisters.VeBus.DcCurrent, 1.2);
        Set(multi, CerboRegisters.VeBus.ActiveInput, 0);

        _server.GetHoldingRegisters(CerboRegisters.SystemUnit).SetBigEndian<short>(2705, -1);
        _server.Start(_endpoint);
    }

    private static void Set(Span<short> regs, RegisterSpec spec, double value)
    {
        var raw = (int)Math.Round(value * spec.Scale);
        if (spec.Type == RegType.Int16) regs.SetBigEndian<short>(spec.Address, (short)raw);
        else regs.SetBigEndian<ushort>(spec.Address, (ushort)raw);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public void Reads_shunt_multi_and_dvcc_limit_through_a_real_socket()
    {
        var settings = new CerboSettings { BatteryUnit = Shunt, VeBusUnit = Multi };
        using var source = new CerboReadingSource(settings);
        var got = new TaskCompletionSource<PowerReading>(TaskCreationOptions.RunContinuationsAsynchronously);
        var statuses = new List<(string, bool)>();
        source.ReadingReceived += r => got.TrySetResult(r);
        source.StatusChanged += (m, e) => { lock (statuses) statuses.Add((m, e)); };

        source.Start($"{_endpoint.Address}:{_endpoint.Port}");
        Assert.True(got.Task.Wait(TimeSpan.FromSeconds(10)), "no reading arrived");
        var r = got.Task.Result;

        Assert.Equal(13.10, r.Volts!.Value, 3);
        Assert.Equal(-5.9, r.Amps!.Value, 3);
        Assert.Equal(-77, r.Watts!.Value, 3);
        Assert.Equal(87.5, r.Soc!.Value, 3);
        Assert.Equal(-12.4, r.ConsumedAh!.Value, 3);
        Assert.Equal(270, r.TtgMinutes!.Value, 3);
        Assert.NotNull(r.Charger);
        Assert.Equal("Float", r.Charger!.StateName);
        Assert.Equal(121.3, r.Charger.AcInVolts!.Value, 3);
        Assert.Equal(160, r.Charger.AcInWatts!.Value, 3);
        Assert.Equal(1.2, r.Charger.DcAmps!.Value, 3);
        Assert.True(r.Charger.AcInConnected);
        Assert.Null(r.Solar);
        Assert.Equal(-1, r.DvccChargeLimitAmps);
        Assert.False(r.ChargeInhibited);

        source.Stop();
        lock (statuses)
        {
            Assert.Contains(statuses, s => s.Item1.StartsWith("Connected to Cerbo GX") && !s.Item2);
            Assert.Equal(("Disconnected", false), statuses.Last());
        }
    }

    [Fact]
    public void Unreachable_host_reports_an_error_and_keeps_retrying_until_stopped()
    {
        // Nothing listens here: connection refused, every attempt.
        var dead = new IPEndPoint(IPAddress.Loopback, FreePort());
        using var source = new CerboReadingSource(new CerboSettings { BatteryUnit = Shunt },
            ep => new FluentModbusCerbo(ep, connectTimeoutMs: 500, readTimeoutMs: 500));
        var firstError = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.StatusChanged += (m, e) => { if (e) firstError.TrySetResult(m); };

        source.Start($"{dead.Address}:{dead.Port}");
        Assert.True(firstError.Task.Wait(TimeSpan.FromSeconds(10)), "no error status");
        Assert.Contains("Cerbo GX", firstError.Task.Result);
        Assert.True(source.IsRunning);                          // still supervising, not given up

        source.Stop();
        Assert.False(source.IsRunning);
    }

    [Fact]
    public void Bad_address_text_is_reported_not_thrown()
    {
        using var source = new CerboReadingSource(new CerboSettings { BatteryUnit = Shunt });
        var status = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.StatusChanged += (m, e) => { if (e) status.TrySetResult(m); };
        source.Start("not a host:99999");
        Assert.True(status.Task.Wait(TimeSpan.FromSeconds(5)));
        Assert.Contains("not valid", status.Task.Result);
        source.Stop();
    }

    [Fact]
    public void ChargeInhibit_writes_land_on_the_server()
    {
        using var client = new FluentModbusCerbo(_endpoint);
        client.Connect();
        var inhibit = new ChargeInhibit(client);
        inhibit.Engage();
        Assert.Equal(0, _server.GetHoldingRegisters(CerboRegisters.SystemUnit).GetBigEndian<short>(2705));
        inhibit.Release();
        Assert.Equal(-1, _server.GetHoldingRegisters(CerboRegisters.SystemUnit).GetBigEndian<short>(2705));
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }
}
