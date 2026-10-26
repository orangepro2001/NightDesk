using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        string output = Path.Combine(root, "artifacts", "ui-1.6.0");
        Directory.CreateDirectory(output);
        var xml = XDocument.Load(Path.Combine(root, "src", "NightDesk", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        xml.Root!.Attribute(x + "Class")!.Remove();
        xml.Root.Attribute("Icon")!.Remove();
        // A pure XAML preview: no code-behind, settings, display API, network or sidecar.
        string[] events = ["Click", "SelectionChanged", "Loaded", "Closing", "Checked", "Unchecked"];
        foreach (var element in xml.Descendants())
            foreach (var attribute in element.Attributes().Where(a => events.Contains(a.Name.LocalName)).ToArray())
                attribute.Remove();
        var window = (Window)XamlReader.Parse(xml.ToString());
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("MainTabs");
            foreach (int width in new[] { 980, 1180 })
            {
                window.Width = width;
                window.Height = 900;
                for (int page = 0; page < tabs.Items.Count; page++)
                {
                    tabs.SelectedIndex = page;
                    window.UpdateLayout();
                    if (page == 2)
                        AssertSeparate(window, "NxListenBox", "NxDisplayBox", "NxFpsBox");
                    if (page == 3)
                        AssertSeparate(window, "NxViewerAddressBox", "NxViewerCodeBox", "NxViewerFingerprintBox");
                    var content = (FrameworkElement)window.Content;
                    content.Measure(new Size(window.ActualWidth - 52, window.ActualHeight - 40));
                    window.UpdateLayout();
                    var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    image.Render(window);
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(image));
                    string path = Path.Combine(output, $"desktop-{width}-page-{page + 1}.png");
                    using var file = File.Create(path);
                    png.Save(file);
                    Console.WriteLine($"PASS layout {width} page {page + 1}: {((TabItem)tabs.Items[page]).Header}");
                }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("Pure XAML layout smoke passed; no application services started.");
        return 0;
    }

    private static void AssertSeparate(Window window, params string[] names)
    {
        double right = -1;
        foreach (string name in names)
        {
            var element = (FrameworkElement)window.FindName(name);
            var origin = element.TransformToAncestor(window).Transform(new Point(0, 0));
            if (element.ActualWidth < 60 || element.ActualHeight < 30 || origin.X < right - 0.5)
                throw new InvalidOperationException($"Overlapping or collapsed field: {name}");
            right = origin.X + element.ActualWidth;
            Console.WriteLine($"  {name}: x={origin.X:F0} width={element.ActualWidth:F0} height={element.ActualHeight:F0}");
        }
    }
}
