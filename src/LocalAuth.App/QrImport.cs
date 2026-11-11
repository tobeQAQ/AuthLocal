using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ZXing;
using ZXing.Common;
using FormatException = System.FormatException;

namespace LocalAuth.App;

internal static class QrImport
{
    internal static string Read(string path)
    {
        using var image = Image.FromFile(path);
        if (image.Width > 10000 || image.Height > 10000 || (long)image.Width * image.Height > 30_000_000)
            throw new FormatException("图片尺寸过大，请先裁剪到二维码区域。");
        var ratio = Math.Min(1d, 2600d / Math.Max(image.Width, image.Height));
        using var bitmap = new Bitmap(Math.Max(1, (int)(image.Width * ratio)), Math.Max(1, (int)(image.Height * ratio)), PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.DrawImage(image, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        }
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var pixels = new byte[bitmap.Width * bitmap.Height * 3];
        try
        {
            for (var row = 0; row < bitmap.Height; row++)
                Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), pixels, row * bitmap.Width * 3, bitmap.Width * 3);
        }
        finally { bitmap.UnlockBits(data); }
        try
        {
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = [BarcodeFormat.QR_CODE] }
            };
            var result = reader.Decode(new RGBLuminanceSource(pixels, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.BGR24));
            return result?.Text ?? throw new FormatException("没有识别到二维码，请使用清晰的二维码图片或直接粘贴密钥。");
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(pixels); }
    }
}
