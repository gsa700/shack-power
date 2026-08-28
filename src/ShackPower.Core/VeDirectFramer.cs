namespace ShackPower.Core;

/// <summary>
/// Splits a raw VE.Direct byte stream into checksum-verified blocks. A block is everything up to
/// and including the byte after a <c>Checksum\t</c> marker, and it verifies when the sum of every
/// byte in it (checksum byte included) is 0 mod 256.
///
/// This deliberately works on <b>bytes, not decoded text</b> — the one place the family's string
/// framers don't map. The checksum byte is a raw binary value that can legally be <c>\r</c>,
/// <c>\n</c>, <c>\t</c>, or anything else, so decoding first and splitting on line endings would
/// corrupt roughly 1 in 85 frames in a way that looks like random checksum noise. The algorithm
/// is the Python prototype's, which ran validated against the real shunt.
///
/// Failed blocks are dropped silently; the stream self-heals at the next marker (the first
/// "block" after connecting starts mid-stream and is expected to fail). A wrong-baud/wrong-port
/// stream that never produces the marker is bounded by an 8 KB resync flush — same purpose as
/// the LP-100A framer's tail cap.
/// </summary>
public sealed class VeDirectFramer
{
    private const int MaxBuffer = 8192;
    private static readonly byte[] Marker = "Checksum\t"u8.ToArray();

    private byte[] _buf = [];

    /// <summary>
    /// Feed one chunk; returns the verified block bodies it completed (marker and checksum byte
    /// stripped), oldest first. Call <see cref="Reset"/> at session start so a part-frame can't
    /// glue across sessions.
    /// </summary>
    public List<byte[]> Feed(ReadOnlySpan<byte> chunk)
    {
        var merged = new byte[_buf.Length + chunk.Length];
        _buf.CopyTo(merged, 0);
        chunk.CopyTo(merged.AsSpan(_buf.Length));
        _buf = merged;

        var blocks = new List<byte[]>();
        while (true)
        {
            var idx = IndexOfMarker(_buf);
            var end = idx + Marker.Length + 1;   // one raw checksum byte after the marker
            if (idx < 0 || _buf.Length < end)
            {
                if (_buf.Length > MaxBuffer) _buf = [];   // not VE.Direct on this port; resync
                return blocks;
            }

            var sum = 0;
            for (var i = 0; i < end; i++) sum += _buf[i];
            if (sum % 256 == 0) blocks.Add(_buf[..idx]);

            _buf = _buf[end..];
        }
    }

    public void Reset() => _buf = [];

    private static int IndexOfMarker(byte[] buf) => buf.AsSpan().IndexOf(Marker);
}
