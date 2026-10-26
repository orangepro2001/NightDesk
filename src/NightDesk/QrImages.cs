using QRCoder;
using System.IO;
using System.Windows.Media.Imaging;

namespace NightDesk;

/// <summary>Creates the frozen QR image shared by both pairing windows.</summary>
public static class QrImages
{
    public static BitmapImage Create(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        byte[] png = new PngByteQRCode(data).GetGraphic(8);
        using var stream = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
