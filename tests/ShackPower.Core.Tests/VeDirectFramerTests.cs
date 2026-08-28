using System.Text;
using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

/// <summary>
/// Locks down the byte-level framing. The scenarios that matter most are the ones a
/// string-based framer gets wrong: a raw checksum byte that happens to be a line-ending or tab,
/// and delivery split at arbitrary chunk boundaries. Fixtures are constructed per the published
/// protocol (checksum computed so the whole block sums to 0 mod 256); real captured frames get
/// committed at cutover when the live port frees up.
/// </summary>
public class VeDirectFramerTests
{
    // ---- fixture helper: body + "Checksum\t" + the byte that makes the sum ≡ 0 mod 256 ----

    private static byte[] Block(string body)
    {
        var prefix = Encoding.Latin1.GetBytes(body + "Checksum\t");
        var check = (byte)((256 - prefix.Sum(b => (int)b) % 256) % 256);
        return [.. prefix, check];
    }

    private static byte[] BodyBytes(string body) => Encoding.Latin1.GetBytes(body);

    private const string MainBody = "\r\nPID\t0xC038\r\nV\t13960\r\nI\t6298\r\nP\t88\r\nAlarm\tOFF\r\nAR\t0\r\nBMV\tSmartShunt 300A\r\nFW\t0419\r\nMON\t1\r\n";

    [Fact]
    public void A_valid_block_comes_back_as_its_body()
    {
        var framer = new VeDirectFramer();
        var blocks = framer.Feed(Block(MainBody));
        var body = Assert.Single(blocks);
        Assert.Equal(BodyBytes(MainBody), body);
    }

    [Fact]
    public void Two_blocks_in_one_chunk_both_come_back_in_order()
    {
        var framer = new VeDirectFramer();
        var second = "\r\nH7\t13842\r\nH8\t13992\r\nH17\t225\r\nH18\t225\r\n";
        var chunk = Block(MainBody).Concat(Block(second)).ToArray();
        var blocks = framer.Feed(chunk);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(BodyBytes(MainBody), blocks[0]);
        Assert.Equal(BodyBytes(second), blocks[1]);
    }

    [Fact]
    public void Delivery_split_one_byte_at_a_time_still_frames()
    {
        var framer = new VeDirectFramer();
        var all = new List<byte[]>();
        foreach (var b in Block(MainBody))
            all.AddRange(framer.Feed(new[] { b }));
        Assert.Equal(BodyBytes(MainBody), Assert.Single(all));
    }

    [Theory]
    [InlineData((byte)'\r')]
    [InlineData((byte)'\n')]
    [InlineData((byte)'\t')]
    [InlineData((byte)0x00)]
    public void A_checksum_byte_that_is_a_delimiter_byte_still_verifies(byte wanted)
    {
        // Tune a numeric field until the checksum byte lands on the awkward value — proving the
        // framer never confuses the raw checksum with the text structure around it.
        for (var n = 10000; n < 10600; n++)
        {
            var body = $"\r\nV\t{n}\r\nI\t0\r\nP\t0\r\n";
            var block = Block(body);
            if (block[^1] != wanted) continue;

            var framer = new VeDirectFramer();
            var blocks = framer.Feed(block);
            Assert.Equal(BodyBytes(body), Assert.Single(blocks));
            return;
        }
        Assert.Fail($"no fixture found with checksum byte {wanted} — widen the search range");
    }

    [Fact]
    public void Garbage_before_the_first_block_sinks_that_block_and_the_next_parses()
    {
        // Connecting mid-stream means the first "frame" is a tail fragment: its checksum fails and
        // it is dropped silently. The stream self-heals at the next block boundary.
        var framer = new VeDirectFramer();
        var second = "\r\nV\t13961\r\nI\t6280\r\n";
        var chunk = BodyBytes("3992\r\nH17\t225\r\n").Concat(Block(MainBody)).Concat(Block(second)).ToArray();
        var blocks = framer.Feed(chunk);
        // The garbage glues onto the first block's frame and takes it down with it; only the
        // second survives. One lost block costs one second of data, once, at connect.
        Assert.Equal(BodyBytes(second), Assert.Single(blocks));
    }

    [Fact]
    public void A_corrupted_block_is_dropped_and_the_stream_recovers()
    {
        var framer = new VeDirectFramer();
        var corrupted = Block(MainBody);
        corrupted[5] ^= 0xFF;   // flip bits mid-body: checksum must fail
        var good = "\r\nV\t13950\r\nI\t6200\r\n";
        var blocks = framer.Feed(corrupted.Concat(Block(good)).ToArray());
        Assert.Equal(BodyBytes(good), Assert.Single(blocks));
    }

    [Fact]
    public void A_stream_with_no_marker_is_bounded_by_the_resync_flush()
    {
        // Wrong-baud/wrong-device noise must not grow the buffer without bound.
        var framer = new VeDirectFramer();
        var noise = new byte[9000];
        Array.Fill(noise, (byte)'x');
        Assert.Empty(framer.Feed(noise));
        // After the flush a clean block parses as if nothing happened.
        Assert.Equal(BodyBytes(MainBody), Assert.Single(framer.Feed(Block(MainBody))));
    }

    [Fact]
    public void Reset_discards_a_part_frame_so_sessions_cannot_glue()
    {
        var framer = new VeDirectFramer();
        var block = Block(MainBody);
        Assert.Empty(framer.Feed(block.AsSpan(0, 30).ToArray()));   // first half of a frame
        framer.Reset();
        // The tail alone is garbage now (its frame fails checksum); the block after it survives.
        var blocks = framer.Feed(block.AsSpan(30).ToArray().Concat(Block(MainBody)).ToArray());
        Assert.Equal(BodyBytes(MainBody), Assert.Single(blocks));
    }
}
