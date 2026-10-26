using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NightDesk;

/// <summary>
/// Displays the complete pinned-pairing payload for the Android NX viewer.
/// The QR contains a short-lived code but never a long-term credential.
/// </summary>
public sealed class NxPairingWindow : Window
{
    public NxPairingWindow(NxPairingPayload pairing, Window owner)
    {
        pairing.Validate();
        Owner = owner;
        Title = "NightDesk · 低带宽远控配对";
        Width = 520;
        Height = 760;
        MinWidth = 460;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(17, 24, 23));
        Foreground = new SolidColorBrush(Color.FromRgb(230, 238, 234));
        FontFamily = new FontFamily("Microsoft YaHei UI");
        ResizeMode = ResizeMode.CanResize;

        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        panel.Children.Add(new TextBlock
        {
            Text = "NIGHTDESK NX PAIRING",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 238, 154))
        });
        panel.Children.Add(new TextBlock
        {
            Text = "用 NightDesk Android 扫码",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 6)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "手机必须先校验证书指纹，再发送 6 位配对码。二维码只含短期配对码、地址和主机指纹，不含长期令牌。",
            Foreground = new SolidColorBrush(Color.FromRgb(172, 188, 175)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 23
        });

        var qrBorder = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 18, 0, 14),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        qrBorder.Child = new Image
        {
            Source = QrImages.Create(pairing.ToJson()),
            Width = 320,
            Height = 320,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        panel.Children.Add(qrBorder);

        panel.Children.Add(new TextBlock
        {
            Text = pairLine("主机", pairing.Name),
            Foreground = new SolidColorBrush(Color.FromRgb(166, 181, 170)),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = pairLine("分辨率", $"{pairing.Width} × {pairing.Height}"),
            Foreground = new SolidColorBrush(Color.FromRgb(166, 181, 170)),
            Margin = new Thickness(0, 3, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "连接地址",
            Foreground = new SolidColorBrush(Color.FromRgb(166, 181, 170)),
            Margin = new Thickness(0, 10, 0, 3)
        });
        panel.Children.Add(new TextBox
        {
            Text = pairing.Addr,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 238, 154)),
            Background = new SolidColorBrush(Color.FromRgb(10, 16, 14)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 68, 59)),
            Padding = new Thickness(8, 5, 8, 5)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "配对码",
            Foreground = new SolidColorBrush(Color.FromRgb(166, 181, 170)),
            Margin = new Thickness(0, 12, 0, 2)
        });
        panel.Children.Add(new TextBlock
        {
            Text = pairing.Code,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 238, 154)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"有效期至 {DateTimeOffset.FromUnixTimeSeconds(pairing.Expires).ToLocalTime():HH:mm:ss}",
            Foreground = new SolidColorBrush(Color.FromRgb(166, 181, 170)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 10)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "主机证书 SHA-256 指纹",
            Foreground = new SolidColorBrush(Color.FromRgb(166, 181, 170)),
            Margin = new Thickness(0, 2, 0, 3)
        });
        panel.Children.Add(new TextBox
        {
            Text = pairing.Fingerprint,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(230, 238, 234)),
            Background = new SolidColorBrush(Color.FromRgb(10, 16, 14)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 68, 59)),
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(8, 5, 8, 5)
        });

        var close = new Button
        {
            Content = "关闭",
            Padding = new Thickness(18, 9, 18, 9),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(210, 238, 154)),
            Foreground = new SolidColorBrush(Color.FromRgb(23, 32, 24)),
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
    }

    private static string pairLine(string label, string value) => $"{label}：{value}";
}
