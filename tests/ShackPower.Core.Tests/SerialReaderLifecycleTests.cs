using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

/// <summary>
/// Start/Stop/Dispose lifecycle only — no port is ever opened, so these run anywhere. The
/// reader's actual serial behavior still needs the shunt; these pin the shutdown contract,
/// which is pure object lifecycle. Ported from W2 Monitor, whose file explains what was and
/// wasn't ever actually broken here.
/// </summary>
public class SerialReaderLifecycleTests
{
    [Fact]
    public void Dispose_is_idempotent()
    {
        var r = new SerialReader();
        r.Dispose();
        r.Dispose();
    }

    [Fact]
    public void Stop_without_start_is_safe()
    {
        var r = new SerialReader();
        r.Stop();
        r.Stop();
    }

    [Fact]
    public void Stop_after_dispose_is_safe()
    {
        var r = new SerialReader();
        r.Dispose();
        r.Stop();   // a shutdown path must never throw at a caller who is only tidying up
    }

    [Fact]
    public void Start_after_dispose_throws_objectdisposed()
    {
        var r = new SerialReader();
        r.Dispose();
        Assert.Throws<ObjectDisposedException>(() => r.Start("COM_NOT_A_REAL_PORT"));
    }

    [Fact]
    public void Is_not_running_before_start_or_after_dispose()
    {
        var r = new SerialReader();
        Assert.False(r.IsRunning);
        r.Dispose();
        Assert.False(r.IsRunning);
    }
}
