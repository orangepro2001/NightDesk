using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NightDesk;

public sealed class SettingsStore
{
    private readonly string directory;
    private readonly string settingsPath;
    private readonly string recoveryPath;
    private readonly JsonSerializerOptions json = new() { WriteIndented = true };
    private string token = "";

    public SettingsStore(string? root = null)
    {
        directory = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightDesk");
        settingsPath = Path.Combine(directory, "settings.json");
        recoveryPath = Path.Combine(directory, "recovery.json");
    }

    public string Token => token;

    public AppSettings Load()
    {
        Directory.CreateDirectory(directory);
        if (!File.Exists(settingsPath))
        {
            token = Security.NewToken();
            var initial = new AppSettings { EncryptedToken = Dpapi.Protect(token) };
            Save(initial);
            return initial;
        }
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath), json)
            ?? throw new InvalidDataException("设置文件无有效内容。");
        try { token = Dpapi.Unprotect(settings.EncryptedToken); }
        catch (Exception ex) { throw new InvalidDataException("无法解密远程访问令牌。设置已拒绝加载，原文件保留。", ex); }
        _ = HotkeySpec.Parse(settings.Hotkey);
        _ = new DisplayMode(settings.Width, settings.Height, settings.RefreshRate).Validate();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        settings.EncryptedToken = Dpapi.Protect(token);
        AtomicWrite(settingsPath, JsonSerializer.Serialize(settings, json));
    }

    public void RotateToken(AppSettings settings)
    {
        token = Security.NewToken();
        Save(settings);
    }

    public void SaveRecovery(RecoverySnapshot snapshot) => AtomicWrite(recoveryPath, JsonSerializer.Serialize(snapshot, json));

    public RecoverySnapshot? LoadRecovery()
    {
        if (!File.Exists(recoveryPath)) return null;
        return JsonSerializer.Deserialize<RecoverySnapshot>(File.ReadAllText(recoveryPath), json)
            ?? throw new InvalidDataException("恢复快照无效；不会自动改动显示设置。");
    }

    public void ClearRecovery()
    {
        if (File.Exists(recoveryPath)) File.Delete(recoveryPath);
    }

    public string LocalSettingsPath => settingsPath;

    public string LocalDevicesPath => Path.Combine(directory, "trusted-devices.dat");

    public string LocalNxHostDirectory => Path.Combine(directory, "nx-host");

    private static void AtomicWrite(string path, string value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, value, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
}

internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    public static string Protect(string value) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
    public static string Unprotect(string value) => Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value), false));

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        IntPtr memory = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, memory, bytes.Length);
        var input = new DataBlob { Size = bytes.Length, Data = memory };
        DataBlob output;
        try
        {
            bool ok = protect
                ? CryptProtectData(ref input, "NightDesk remote token", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { _ = LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(memory); CryptographicOperations.ZeroMemory(bytes); }
    }
}


