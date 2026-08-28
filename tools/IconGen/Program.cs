using SkiaSharp;
using Svg.Skia;

// usage: IconGen <svg-in> <ico-out> <png256-out>
if (args.Length < 3)
{
    Console.Error.WriteLine("usage: IconGen <svg-in> <ico-out> <png256-out>");
    return 1;
}

var (svgPath, icoOut, pngOut) = (args[0], args[1], args[2]);
int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };

var svg = new SKSvg();
if (svg.Load(svgPath) is null || svg.Picture is null)
{
    Console.Error.WriteLine($"failed to load svg: {svgPath}");
    return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(icoOut)!);

var pngs = new List<byte[]>();
foreach (var s in sizes)
{
    using var bmp = new SKBitmap(s, s, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bmp))
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(s / 512f);
        canvas.DrawPicture(svg.Picture);
    }
    using var img = SKImage.FromBitmap(bmp);
    var data = img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    pngs.Add(data);
    if (s == 256) File.WriteAllBytes(pngOut, data);
}

// Pack a PNG-in-ICO (Vista+); one directory entry per size.
using (var fs = File.Create(icoOut))
using (var w = new BinaryWriter(fs))
{
    w.Write((ushort)0);            // reserved
    w.Write((ushort)1);            // type: icon
    w.Write((ushort)pngs.Count);   // image count

    var offset = 6 + 16 * pngs.Count;
    for (var i = 0; i < sizes.Length; i++)
    {
        var s = sizes[i];
        w.Write((byte)(s >= 256 ? 0 : s));   // width  (0 = 256)
        w.Write((byte)(s >= 256 ? 0 : s));   // height (0 = 256)
        w.Write((byte)0);                    // palette
        w.Write((byte)0);                    // reserved
        w.Write((ushort)1);                  // color planes
        w.Write((ushort)32);                 // bits per pixel
        w.Write((uint)pngs[i].Length);       // bytes in resource
        w.Write((uint)offset);               // image offset
        offset += pngs[i].Length;
    }
    foreach (var p in pngs) w.Write(p);
}

Console.WriteLine($"wrote {icoOut} ({pngs.Count} sizes) and {pngOut}");
return 0;
