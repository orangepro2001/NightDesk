using System.Text.Json;
using System.Windows;

namespace NightDesk;

public partial class App : Application
{
    private Mutex? singleInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            NativeDisplays.VerifyLayouts();
            DisplayTopology.VerifyLayouts();
            if (e.Args.Contains("--diagnose"))
            {
                var result = new Dictionary<string, object?> { ["utc"] = DateTime.UtcNow, ["remoteSession"] = SystemParameters.IsRemoteSession, ["monitors"] = NativeDisplays.Enumerate() };
                try { result["activeSources"] = DisplayTopology.ActiveSourceNames(DisplayTopology.Capture()); }
                catch (Exception ex) { result["topologyError"] = ex.Message; }
                using var vdd = new VddClient();
                result["otherManagerRunning"] = vdd.IsOtherManagerRunning();
                result["vddPayloadEmbedded"] = VddInstaller.HasEmbeddedPayload;
                result["vddPayloadSize"] = VddInstaller.HasEmbeddedPayload ? VddInstaller.PayloadSize : 0;
                try { vdd.EnsureOpen(); result["driverVersion"] = vdd.Version; }
                catch (Exception ex) { result["driverError"] = ex.Message; }
                int outputIndex = Array.IndexOf(e.Args, "--output");
                string output = outputIndex >= 0 && outputIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[outputIndex + 1]) : Path.Combine(AppContext.BaseDirectory, "diagnostics.json");
                File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(); return;
            }
            singleInstance = new Mutex(true, @"LocalNightDesk.Desktop", out bool created);
            if (!created) { MessageBox.Show("NightDesk 已在本用户会话中运行，请从系统托盘打开。", "NightDesk"); Shutdown(); return; }
            var store = new SettingsStore();
            var settings = store.Load();
            var window = new MainWindow(store, settings);
            MainWindow = window;
            window.Show();
            if (e.Args.Contains("--smoke"))
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                timer.Tick += async (_, _) => { timer.Stop(); await window.ExitAsync(); };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动失败，未更改显示设置。\n\n" + ex.Message, "NightDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { singleInstance?.Dispose(); base.OnExit(e); }
}
