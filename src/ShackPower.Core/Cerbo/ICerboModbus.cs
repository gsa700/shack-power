using System.Net;
using FluentModbus;

namespace ShackPower.Core.Cerbo;

/// <summary>
/// The thin Modbus surface the Cerbo layer needs, so <see cref="CerboReadingSource"/>,
/// <see cref="CerboDiscovery"/> and <see cref="ChargeInhibit"/> test against a fake and only
/// <see cref="FluentModbusCerbo"/> touches a socket. All calls are synchronous and throw on any
/// transport or Modbus-exception failure — callers decide what a failure means.
/// </summary>
public interface ICerboModbus : IDisposable
{
    bool IsConnected { get; }
    void Connect();
    void Disconnect();

    /// <summary>Read <paramref name="count"/> holding registers (function 3) from one unit.</summary>
    ushort[] ReadHolding(byte unit, ushort start, ushort count);

    /// <summary>Write one holding register (function 6).</summary>
    void WriteSingle(byte unit, ushort address, ushort value);
}

/// <summary>FluentModbus-backed <see cref="ICerboModbus"/> for a real GX device (port 502).</summary>
public sealed class FluentModbusCerbo : ICerboModbus
{
    private readonly IPEndPoint _endpoint;
    private readonly ModbusTcpClient _client = new();

    public FluentModbusCerbo(IPEndPoint endpoint, int connectTimeoutMs = 3000, int readTimeoutMs = 2000)
    {
        _endpoint = endpoint;
        _client.ConnectTimeout = connectTimeoutMs;
        _client.ReadTimeout = readTimeoutMs;
        _client.WriteTimeout = readTimeoutMs;
    }

    public bool IsConnected => _client.IsConnected;

    // Victron registers are plain big-endian 16-bit words, which is also what FluentModbus
    // returns through its Span<T> overloads when asked for BigEndian.
    public void Connect() => _client.Connect(_endpoint, ModbusEndianness.BigEndian);

    public void Disconnect()
    {
        try { _client.Disconnect(); } catch { /* already gone */ }
    }

    public ushort[] ReadHolding(byte unit, ushort start, ushort count) =>
        _client.ReadHoldingRegisters<ushort>(unit, start, count).ToArray();

    public void WriteSingle(byte unit, ushort address, ushort value) =>
        _client.WriteSingleRegister(unit, address, value);

    public void Dispose() => Disconnect();
}
