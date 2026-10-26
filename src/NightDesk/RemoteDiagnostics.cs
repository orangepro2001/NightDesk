using System.Text;

namespace NightDesk;

/// <summary>Append-only, token-free diagnostics for the remote-control pipeline.</summary>
public static class RemoteDiagnostics
{
    private static readonly object gate = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightDesk", "logs", "remote.log");

    public static void Log(string message)
    {
        try
        {
            lock (gate)
            {
                string path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Move(path, path + ".1", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}", new UTF8Encoding(false));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Log(string message, Exception ex) => Log(message + " :: " + Describe(ex));

    public static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
            parts.Add(current.GetType().Name + ": " + current.Message);
        return string.Join(" <- ", parts);
    }
}
