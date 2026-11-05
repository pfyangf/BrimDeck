using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrimDeck.Core;

namespace BrimDeck.Native;

// Turns a picture the user picked, or a bitmap on the clipboard, into pixels QrText can read.
internal static class QrImage
{
    public static string? Read(BitmapSource? source)
    {
        if (source is null) return null;
        try
        {
            source = Shrink(source);
            var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = formatted.PixelWidth, height = formatted.PixelHeight;
            if (width <= 0 || height <= 0) return null;
            var pixels = new byte[width * height * 4];
            formatted.CopyPixels(pixels, width * 4, 0);
            return QrText.Read(pixels, width, height);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or IOException)
        {
            return null;
        }
    }

    public static BitmapSource? Open(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource Shrink(BitmapSource source)
    {
        double longest = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longest <= 1600) return source;
        double scale = 1600 / longest;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }
}
