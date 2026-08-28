namespace ShackPower.Core;

/// <summary>
/// A source of <see cref="PowerReading"/>s on a background thread. Implemented by the real
/// <see cref="SerialReader"/> and by <see cref="VeDirectSimReader"/>, so the app layer can drive
/// the exact same pipeline from a live SmartShunt or from synthetic data. Events fire on a
/// background thread — subscribers must marshal to their UI thread.
///
/// Unlike the W2/LP-100A seam this one has no <c>Send()</c>: VE.Direct's text mode is a pure
/// unsolicited broadcast, so there is no command path at all. Don't add one speculatively — the
/// HEX protocol does exist, but engaging it changes the device's output mode and is exactly the
/// kind of "monitor that touches the device" this app was built to avoid.
/// </summary>
public interface IReadingSource : IDisposable
{
    event Action<PowerReading>? ReadingReceived;
    event Action<string, bool>? StatusChanged;   // (message, isError)

    bool IsRunning { get; }

    /// <summary>
    /// Start reading. <paramref name="resolvePort"/>, if given, is re-queried on every (re)connect
    /// to follow a USB replug/renumber to the cable's current port; null keeps the fixed port.
    /// </summary>
    void Start(string portName, Func<string?>? resolvePort = null);
    void Stop();
}
