using ShackPower.Core.Cerbo;

namespace ShackPower.Core.Tests.Cerbo;

/// <summary>
/// In-memory GX: a sparse (unit, address) → raw word map. Reading any address that isn't
/// present throws a plain exception, mirroring the Modbus exception dbus-modbustcp answers for a
/// D-Bus path the device doesn't publish; a multi-register read fails if <i>any</i> word in the
/// range is missing, which is exactly the behavior the decoder's narrow reads are designed around.
/// </summary>
public sealed class FakeCerboModbus : ICerboModbus
{
    public Dictionary<(byte Unit, ushort Address), ushort> Registers { get; } = new();
    public List<(byte Unit, ushort Address, ushort Value)> Writes { get; } = new();
    public int ReadCount { get; private set; }
    public bool ThrowTransportOnRead { get; set; }
    public bool IsConnected { get; private set; }

    public void Set(byte unit, RegisterSpec spec, double value)
    {
        var raw = value * spec.Scale;
        Registers[(unit, spec.Address)] = spec.Type == RegType.Int16
            ? unchecked((ushort)(short)Math.Round(raw))
            : (ushort)Math.Round(raw);
    }

    public void SetRaw(byte unit, ushort address, ushort raw) => Registers[(unit, address)] = raw;

    public void Connect() => IsConnected = true;
    public void Disconnect() => IsConnected = false;

    public ushort[] ReadHolding(byte unit, ushort start, ushort count)
    {
        ReadCount++;
        if (ThrowTransportOnRead) throw new IOException("socket closed");
        var result = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            if (!Registers.TryGetValue((unit, (ushort)(start + i)), out var v))
                throw new InvalidDataException($"Modbus exception: unit {unit} register {start + i} not published");
            result[i] = v;
        }
        return result;
    }

    public void WriteSingle(byte unit, ushort address, ushort value)
    {
        Writes.Add((unit, address, value));
        Registers[(unit, address)] = value;
    }

    public void Dispose() => Disconnect();
}
