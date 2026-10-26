using QRCoder;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NightDesk;

public sealed class PairingWindow : Window
{
    public PairingWindow(PairingQr pairing, string origin, Window owner)
    {
        Owner = owner;
        Title = "NightDesk · 扫码绑定";
        Width = 500;
        Height = 650;
        MinWidth = 440;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(17, 24, 23));
        Foreground = new SolidColorBrush(Color.FromRgb(230, 238, 234));
        FontFamily = new FontFamily("Microsoft YaHei UI");
        ResizeMode = ResizeMode.CanResize;

        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        panel.Children.Add(new TextBlock
        {
            Text = "NIGHTDESK PAIRING",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 238, 154))
        });
        panel.Children.Add(new TextBlock
        {
            Text = "用 NightDesk Android 扫码绑定",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 6)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "二维码只含 5 分钟有效的一次性配对票据，不含长期设备令牌。票据成功使用或过期后立即失效。",
            Foreground = new SolidColorBrush(Color.FromRgb(172, 188, 175)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 23
        });

        var qrBorder = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 20, 0, 14),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        qrBorder.Child = new Image
        {
            Source = CreateQr(pairing.Payload),
            Width = 340,
            Height = 340,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        panel.Children.Add(qrBorder);

        panel.Children.Add(new TextBlock
        {
            Text = origin,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 238, 154)),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"票据有效期至 {pairing.ExpiresUtc.ToLocalTime():HH:mm:ss}",
            Foreground = new SolidColorBrush(Color.FromRgb(166, 181, 170)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 18)
        });

        var close = new Button
        {
            Content = "关闭",
            Padding = new Thickness(18, 9, 18, 9),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(210, 238, 154)),
            Foreground = new SolidColorBrush(Color.FromRgb(23, 32, 24)),
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
    }

    private static BitmapImage CreateQr(string payload)
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