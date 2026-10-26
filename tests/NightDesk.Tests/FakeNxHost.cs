using System.Text.Json;
using NightDesk;

namespace NightDesk.Tests;

/// <summary>
/// Minimal stand-in for nx-host.exe used by the lifecycle tests. It speaks the
/// same JSON-lines management protocol but never captures a screen or injects
/// input, so tests are safe on a live desktop.
/// </summary>
internal static class FakeNxHost
{
    public static async Task RunAsync(string[] args)
    {
        if (Environment.GetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_MODE") == "exit") return;

        string listen = Value(args, "-listen") ?? "127.0.0.1:7000";
        string display = Value(args, "-display") ?? "primary";
        int fps = int.TryParse(Value(args, "-fps"), out int parsed) ? parsed : 10;
        var trusted = new Dictionary<string, string>();
        string? seededFingerprint = Environment.GetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_TRUSTED");
        if (!string.IsNullOrWhiteSpace(seededFingerprint))
            trusted[seededFingerprint] = "100.100.1.2:5555 2026-10-27 01:20";

        if (Environment.GetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_MALFORMED") == "1")
            Console.Out.WriteLine("not-json");
        Emit(new { @event = "ready", version = "fake-0.1", pid = Environment.ProcessId, listen, display, width = 1920, height = 1080, dpi = 96, identity = "fakeidentity", fps, requireAuth = true });

        string? exitAfterReadyOnce = Environment.GetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_EXIT_AFTER_READY_ONCE");
        if (!string.IsNullOrWhiteSpace(exitAfterReadyOnce) && !File.Exists(exitAfterReadyOnce))
        {
            File.WriteAllText(exitAfterReadyOnce, "consumed");
            return;
        }

        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                string cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";
                switch (cmd)
                {
                    case "pair":
                        int seconds = root.TryGetProperty("seconds", out var s) ? s.GetInt32() : 300;
                        Emit(new { @event = "pairing", code = "123456", expiresIn = seconds });
                        break;
                    case "pair-cancel":
                        Emit(new { @event = "pairing", code = "", expiresIn = 0 });
                        break;
                    case "untrust":
                        string fp = root.TryGetProperty("fingerprint", out var f) ? f.GetString() ?? "" : "";
                        trusted.Remove(fp);
                        EmitStatus(fps, trusted, listen, display);
                        break;
                    case "fps":
                        fps = Math.Clamp(root.TryGetProperty("value", out var v) ? v.GetInt32() : fps, 1, 60);
                        EmitStatus(fps, trusted, listen, display);
                        break;
                    case "status":
                        EmitStatus(fps, trusted, listen, display);
                        break;
                    case "stop":
                        return;
                    default:
                        Emit(new { @event = "error", message = "unknown command " + cmd });
                        break;
                }
            }
            catch (JsonException ex)
            {
                Emit(new { @event = "error", message = "bad command: " + ex.Message });
            }
        }
    }

    private static void EmitStatus(int fps, Dictionary<string, string> trusted, string listen, string display)
    {
        Emit(new { @event = "status", fps, trusted, requireAuth = true, listen, display, width = 1920, height = 1080, addr = "", identity = "fakeidentity", message = "frames=0" });
    }

    private static string? Value(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void Emit(object value)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(value));
        Console.Out.Flush();
    }
}
