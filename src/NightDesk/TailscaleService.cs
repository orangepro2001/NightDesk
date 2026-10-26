using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NightDesk;

public readonly record struct ServeBinding(string Fingerprint, bool Created);

public sealed class TailscaleService
{
    private readonly string executable;
    public TailscaleService()
    {
        executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("未找到 Tailscale。请先安装并登录 Windows Tailscale 客户端。", executable);
    }
    public static bool IsEmptyConfig(string config)
    {
        try
        {
            using var doc = JsonDocument.Parse(config);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.Null || p.Value.ValueKind == JsonValueKind.Object && !p.Value.EnumerateObject().Any());
        }
        catch (JsonException) { return false; }
    }
    public async Task<string> GetHttpsUrlAsync()
    {
        using var state = JsonDocument.Parse(await RunAsync("status", "--json"));
        if (state.RootElement.GetProperty("BackendState").GetString() != "Running") throw new InvalidOperationException("Tailscale 尚未连接。");
        string name = state.RootElement.GetProperty("Self").GetProperty("DNSName").GetString()?.TrimEnd('.') ?? "";
        if (!name.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase) || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '.')))
            throw new InvalidOperationException("本机没有有效的 Tailscale HTTPS DNS 地址。");
        return $"https://{name}/";
    }
    /// <summary>启用 443 入口；若现有 Serve 配置正好是本机的 NightDesk 路由，则直接接管，不要求旧指纹匹配。</summary>
    public async Task<string> EnableAsync() => (await EnsureServeAsync()).Fingerprint;

    /// <summary>幂等地保证 443 入口指向本机 NightDesk API；Created 表示本次是否新建了路由。</summary>
    public async Task<ServeBinding> EnsureServeAsync()
    {
        string host = new Uri(await GetHttpsUrlAsync()).Host;
        string before = await RunAsync("serve", "status", "--json");
        if (!IsEmptyConfig(before))
        {
            if (HasOnlyNightDeskRoute(before, host)) return new ServeBinding(Fingerprint(before), false);
            throw new InvalidOperationException("已有其它 Tailscale Serve 配置，未覆盖。请在终端运行 tailscale serve status 查看并自行处理。");
        }
        await RunAsync("serve", "--bg", "--yes", "--https=443", $"http://127.0.0.1:{RemoteServer.DefaultPort}");
        string after = await RunAsync("serve", "status", "--json");
        if (!HasOnlyNightDeskRoute(after, host)) throw new InvalidOperationException("Serve 返回的绑定与预期不符，请检查 Tailscale Serve 状态。");
        return new ServeBinding(Fingerprint(after), true);
    }
    /// <summary>只删除结构上精确等于本机 NightDesk 路由的 Serve 配置；指纹仅作记录，旧版本或丢失设置不应阻止关闭。</summary>
    public async Task DisableAsync()
    {
        string host = new Uri(await GetHttpsUrlAsync()).Host;
        string before = await RunAsync("serve", "status", "--json");
        if (IsEmptyConfig(before)) return;
        if (!HasOnlyNightDeskRoute(before, host)) throw new InvalidOperationException("Serve 配置已被外部修改，未删除任何绑定。请手动核查。");
        await RunAsync("serve", "--https=443", "off");
    }
    /// <summary>启动时若本机已有正确的 NightDesk 路由（例如指纹丢失），返回当前指纹以接管；否则返回 null。</summary>
    public async Task<string?> TryAdoptOwnedRouteAsync()
    {
        string host = new Uri(await GetHttpsUrlAsync()).Host;
        string config = await RunAsync("serve", "status", "--json");
        return !IsEmptyConfig(config) && HasOnlyNightDeskRoute(config, host) ? Fingerprint(config) : null;
    }
    public static bool HasOnlyNightDeskRoute(JsonElement root, string? expectedHost = null)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Web", out var web) || web.ValueKind != JsonValueKind.Object) return false;
        var hosts = web.EnumerateObject().ToArray();
        if (hosts.Length != 1 || !hosts[0].Name.EndsWith(".ts.net:443", StringComparison.OrdinalIgnoreCase)) return false;
        string host = hosts[0].Name[..^4];
        if (expectedHost is not null && !host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase)) return false;
        if (hosts[0].Value.ValueKind != JsonValueKind.Object || hosts[0].Value.EnumerateObject().Count() != 1 || !hosts[0].Value.TryGetProperty("Handlers", out var handlers) || handlers.ValueKind != JsonValueKind.Object) return false;
        var routes = handlers.EnumerateObject().ToArray();
        if (routes.Length != 1 || routes[0].Name != "/" || routes[0].Value.ValueKind != JsonValueKind.Object || routes[0].Value.EnumerateObject().Count() != 1 || !routes[0].Value.TryGetProperty("Proxy", out var proxy) || proxy.ValueKind != JsonValueKind.String) return false;
        if (proxy.GetString() != $"http://127.0.0.1:{RemoteServer.DefaultPort}") return false;
        if (root.TryGetProperty("AllowFunnel", out var funnel) && (funnel.ValueKind != JsonValueKind.Object || funnel.EnumerateObject().Any(x => x.Value.ValueKind != JsonValueKind.False))) return false;
        if (!root.TryGetProperty("TCP", out var tcp) || tcp.ValueKind != JsonValueKind.Object || tcp.EnumerateObject().Count() != 1 || !tcp.TryGetProperty("443", out var listener) || listener.ValueKind != JsonValueKind.Object || listener.EnumerateObject().Count() != 1 || !listener.TryGetProperty("HTTPS", out var tls) || tls.ValueKind != JsonValueKind.True) return false;
        return root.EnumerateObject().All(x => x.Name is "Web" or "TCP" or "AllowFunnel");
    }
    public static bool HasOnlyNightDeskRoute(string config, string? expectedHost = null)
    {
        try { using var doc = JsonDocument.Parse(config); return HasOnlyNightDeskRoute(doc.RootElement, expectedHost); }
        catch (JsonException) { return false; }
    }
    private static string Fingerprint(string value)
    {
        using var document = JsonDocument.Parse(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(document.RootElement))));
    }
    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(',', value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => JsonSerializer.Serialize(x.Name) + ":" + Canonical(x.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(',', value.EnumerateArray().Select(Canonical)) + "]",
        _ => value.GetRawText()
    };
    private async Task<string> RunAsync(params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 Tailscale CLI。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { throw new InvalidOperationException("Tailscale 操作超时；请检查登录与 HTTPS 设置。操作可能仍在处理，请核查 Serve 状态。"); }
        string stdout = await output, stderr = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException($"Tailscale 返回 {process.ExitCode}：{stderr.Trim()} {stdout.Trim()}");
        return stdout.Trim();
    }
}
