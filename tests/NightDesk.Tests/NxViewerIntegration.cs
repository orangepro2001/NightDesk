using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NightDesk;

namespace NightDesk.Tests;

/// <summary>
/// Starts the real nx-host.exe in its synthetic, log-only test mode. This never
/// captures the desktop and never calls SendInput.
/// </summary>
internal static class NxViewerIntegration
{
    public static async Task<int> VerifyAsync(string hostExecutable)
    {
        if (!File.Exists(hostExecutable)) throw new FileNotFoundException("nx-host.exe not found", hostExecutable);
        int passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }

        int port = FreeUdpPort();
        string root = Path.Combine(Path.GetTempPath(), "nightdesk-viewer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string hostState = Path.Combine(root, "host");
        string viewerState = Path.Combine(root, "viewer");
        var start = new ProcessStartInfo(hostExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[]
                 {
                     "-listen", $"127.0.0.1:{port}",
                     "-display", "primary",
                     "-fps", "5",
                     "-state-dir", hostState,
                     "-control", "stdio",
                     "-pair",
                     "-test-mode",
                     "-source", "pattern",
                     "-inject", "log"
                 })
            start.ArgumentList.Add(argument);

        using var host = Process.Start(start) ?? throw new InvalidOperationException("cannot start nx-host");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            string? identity = null;
            string? code = null;
            while (identity is null || code is null)
            {
                string line = await host.StandardOutput.ReadLineAsync(timeout.Token)
                    ?? throw new InvalidOperationException("nx-host exited before ready/pairing");
                using JsonDocument eventJson = JsonDocument.Parse(line);
                JsonElement rootElement = eventJson.RootElement;
                if (!rootElement.TryGetProperty("event", out JsonElement eventName)) continue;
                string name = eventName.GetString() ?? "";
                if (name == "ready") identity = rootElement.TryGetProperty("identity", out var id) ? id.GetString() : null;
                else if (name == "pairing") code = rootElement.TryGetProperty("code", out var pairing) ? pairing.GetString() : null;
                else if (name == "error") throw new InvalidOperationException(line);
            }
            Check(identity is { Length: 64 }, "real test-mode host reports a certificate identity");
            Check(code is { Length: 6 }, "real test-mode host opens a six-digit pairing window");

            var target = new NxViewerTarget("127.0.0.1", port);
            string wrong = code == "000000" ? "111111" : "000000";
            var wrongClose = new TaskCompletionSource<NxViewerClose>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (NxViewerSession denied = await NxViewerSession.ConnectAsync(target, wrong, "", viewerState))
            {
                denied.Closed += value => wrongClose.TrySetResult(value);
                denied.Start();
                NxViewerClose close = await wrongClose.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Check(close.Code == "bad-code" && close.Failed, "wrong pairing code fails closed with a classified error");
            }

            var firstFrame = new TaskCompletionSource<NxViewerFrameUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondFrame = new TaskCompletionSource<NxViewerFrameUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
            var receipt = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (NxViewerSession viewer = await NxViewerSession.ConnectAsync(target, code, identity, viewerState))
            {
                viewer.FrameReceived += frame =>
                {
                    firstFrame.TrySetResult(frame);
                    if (frame.Sequence > 1) secondFrame.TrySetResult(frame);
                };
                viewer.ReceiptReceived += (operation, ok) => { if (ok) receipt.TrySetResult(operation); };
                viewer.Start();

                NxViewerFrameUpdate first = await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Check(first.Width == 1920 && first.Height == 1080, "real host frame size decodes correctly");
                Check(first.Dirty.Count > 0 && first.Pixels.Length == first.Width * first.Height * 4,
                    "first full tile frame decodes into BGRA pixels");
                NxViewerFrameUpdate second = await secondFrame.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Check(second.Sequence > first.Sequence, "ACK advances the real host frame sequence");

                viewer.SendInputAsync(NxViewerInputKind.KeyDown, argument: 0x41);
                await receipt.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Check(true, "real host returns an input receipt through the C# viewer");
                viewer.SendInputAsync(NxViewerInputKind.KeyUp, argument: 0x41);
            }
            Check(NxViewerPinStore.Load(viewerState).Get(target) == identity,
                "viewer persists the verified host certificate fingerprint");

            try
            {
                await host.StandardInput.WriteLineAsync("{\"cmd\":\"stop\"}");
                await host.StandardInput.FlushAsync();
                await host.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            }
            catch { }
            return passed;
        }
        finally
        {
            try
            {
                if (!host.HasExited)
                {
                    host.Kill(entireProcessTree: true);
                    host.WaitForExit(5000);
                }
            }
            catch { }
        }
    }

    private static int FreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}