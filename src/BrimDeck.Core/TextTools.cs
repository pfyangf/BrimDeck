using System.Text;
using System.Text.Json;
using ZXing;
using ZXing.Common;

namespace BrimDeck.Core;

// Offline helpers for the mini-tools page: JSON, common encodings, QR pixels (BGRA).
public static class TextTools
{
    public static string FormatJson(string text, bool indent)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "null" : text);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = indent });
    }

    public static bool TryFormatJson(string text, bool indent, out string result, out string error)
    {
        try { result = FormatJson(text, indent); error = ""; return true; }
        catch (JsonException ex) { result = text; error = ex.Message; return false; }
    }

    public static string EncodeBase64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));
    public static bool TryDecodeBase64(string text, out string result, out string error)
    {
        try
        {
            result = Encoding.UTF8.GetString(Convert.FromBase64String((text ?? "").Trim()));
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is System.FormatException or ArgumentException)
        {
            result = text ?? ""; error = ex.Message; return false;
        }
    }

    public static string EncodeUrl(string text) => Uri.EscapeDataString(text ?? "");
    public static string DecodeUrl(string text) => Uri.UnescapeDataString(text ?? "");

    public static string EncodeHex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? "");
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static bool TryDecodeHex(string text, out string result, out string error)
    {
        string hex = new string((text ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (hex.Length % 2 != 0) { result = text ?? ""; error = "odd length"; return false; }
        try
        {
            var bytes = Convert.FromHexString(hex);
            result = Encoding.UTF8.GetString(bytes);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is System.FormatException or ArgumentException)
        {
            result = text ?? ""; error = ex.Message; return false;
        }
    }

    // Returns BGRA32 pixels for a QR of the given size. Empty text yields null.
    public static (byte[] Pixels, int Size)? QrBgra(string text, int size = 240)
    {
        if (string.IsNullOrEmpty(text)) return null;
        size = Math.Clamp(size, 64, 512);
        var writer = new BarcodeWriterGeneric
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 1 }
        };
        writer.Options.Hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
        BitMatrix matrix = writer.Encode(text);
        int w = matrix.Width, h = matrix.Height;
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            byte v = matrix[x, y] ? (byte)0 : (byte)255;
            pixels[i] = v; pixels[i + 1] = v; pixels[i + 2] = v; pixels[i + 3] = 255;
        }
        return (pixels, w);
    }
}
