using ZXing;
using ZXing.Common;

namespace BrimDeck.Core;

// Reads an otpauth URI from a QR image the caller already decoded into pixels. ZXing is the public decoder; the image
// file and the clipboard are turned into BGRA pixels by the settings page.
public static class QrText
{
    public static string? Read(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4) return null;
        var reader = new BarcodeReaderGeneric { AutoRotate = true };
        reader.Options.TryHarder = true;
        reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];
        string? text = reader.Decode(new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGRA32))?.Text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
