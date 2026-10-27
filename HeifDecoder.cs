using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibHeifSharp;

namespace NomacsNet;

public static class HeifDecoder
{
    public static BitmapSource Decode(string path)
    {
        using var ctx = new HeifContext(path);
        using var handle = ctx.GetPrimaryImageHandle();
        var hasAlpha = handle.HasAlphaChannel;
        var chroma = hasAlpha ? HeifChroma.InterleavedRgba32 : HeifChroma.InterleavedRgb24;
        using var img = handle.Decode(HeifColorspace.Rgb, chroma);

        int w = img.Width, h = img.Height;
        var plane = img.GetPlane(HeifChannel.Interleaved);
        int stride = plane.Stride;
        int bytesPerPixel = hasAlpha ? 4 : 3;
        int dstStride = w * bytesPerPixel;
        var data = new byte[dstStride * h];

        unsafe
        {
            var src = (byte*)plane.Scan0;
            fixed (byte* dst = data)
            {
                for (int y = 0; y < h; y++)
                {
                    var s = src + y * stride;
                    var d = dst + y * dstStride;
                    for (int x = 0; x < w; x++)
                    {
                        // libheif interleaved RGB(A) 为 R,G,(B),A 顺序，WPF 需要 B,G,R,(A)
                        d[0] = s[2];
                        d[1] = s[1];
                        d[2] = s[0];
                        if (hasAlpha) d[3] = s[3];
                        s += bytesPerPixel;
                        d += bytesPerPixel;
                    }
                }
            }
        }

        var format = hasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24;
        var bmp = BitmapSource.Create(w, h, 96, 96, format, null, data, dstStride);
        bmp.Freeze();
        return bmp;
    }

    public static bool IsHeifFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".avif" or ".heic" or ".heif" or ".heifs" or ".hif";
    }
}
