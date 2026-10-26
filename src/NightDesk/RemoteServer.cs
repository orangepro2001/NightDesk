using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Connections;

namespace NightDesk;

public sealed class RemoteServer : IAsyncDisposable
{
    public const int DefaultPort = 17645;
    public int Port => port;
    private WebApplication? app;
    private readonly int port;
    private readonly RemoteAuthenticator authentication;
    private readonly Func<object> status;
    private readonly Func<RemoteCommand, Task<object>> execute;
    private readonly string html;
    private readonly AndroidUpdateFeed updates;

    public RemoteServer(int port, RemoteAuthenticator authentication, Func<object> status, Func<RemoteCommand, Task<object>> execute, AndroidUpdateFeed? updates = null)
    {
        this.updates = updates ?? new AndroidUpdateFeed();
        this.port = port; this.authentication = authentication; this.status = status; this.execute = execute;
        using var stream = typeof(RemoteServer).Assembly.GetManifestResourceStream("NightDesk.remote.html")
            ?? throw new InvalidOperationException("远程页面资源缺失，请重新发布应用。");
        using var reader = new StreamReader(stream);
        html = reader.ReadToEnd();
    }

    public async Task StartAsync()
    {
        if (app is not null) return;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(IPAddress.Loopback, port);
            server.Limits.MaxRequestBodySize = 4096;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            server.Limits.MaxConcurrentConnections = 32;
        });
        var next = builder.Build();
        next.Use(async (context, run) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            bool pairingRequest = context.Request.Path.Equals("/api/v1/pair", StringComparison.Ordinal) &&
                                  HttpMethods.IsPost(context.Request.Method);
            bool healthRequest = context.Request.Path.Equals("/api/v1/health", StringComparison.Ordinal) &&
                                 HttpMethods.IsGet(context.Request.Method);
            if (context.Request.Path.StartsWithSegments("/api") && !pairingRequest && !healthRequest &&
                !authentication.IsAuthorized(context.Request.Headers.Authorization.ToString()))
            {
                context.Response.StatusCode = 401;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await context.Response.WriteAsJsonAsync(new { error = "需要有效的共享令牌或已绑定设备令牌。" });
                return;
            }
            await run();
        });
        next.MapGet("/", () => Results.Content(html, "text/html; charset=utf-8"));
        // Unauthenticated liveness probe: proves the local listener is NightDesk, reveals no settings or tokens.
        next.MapGet("/api/v1/health", () => Results.Json(new { ok = true, app = "nightdesk", version = typeof(RemoteServer).Assembly.GetName().Version?.ToString(3) ?? "0" }));
        next.MapGet("/api/v1/status", () => Results.Json(status()));
        next.MapGet("/api/v1/android-update", () =>
        {
            try
            {
                var release = updates.Read();
                if (release is null) return Results.NoContent();
                using var verified = updates.OpenVerified(release);
                return Results.Json(release);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            { return Results.Json(new { error = "Android 更新发布暂不可用，请联系电脑端。" }, statusCode: 503); }
        });
        next.MapGet("/api/v1/android-apk", (HttpContext context) =>
        {
            try
            {
                var release = updates.Read();
                if (release is null) return Results.NotFound();
                if (context.Request.Query.Count != 1 || context.Request.Query["versionCode"].ToString() != release.VersionCode.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    return Results.Conflict(new { error = "版本已变化，请重新检查更新。" });
                return Results.Stream(updates.OpenVerified(release), "application/vnd.android.package-archive", $"NightDesk-{release.VersionName}.apk");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            { return Results.Json(new { error = "Android 更新文件暂不可用。" }, statusCode: 503); }
        });
        next.MapGet("/api/v1/{invalid}", (string invalid) => Results.StatusCode(405));
        next.MapPost("/api/v1/pair", async (HttpContext context) =>
        {
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            try
            {
                using var document = await JsonDocument.ParseAsync(context.Request.Body, new JsonDocumentOptions { MaxDepth = 4 }, context.RequestAborted);
                (string ticket, string deviceName) = ParsePairRequest(document.RootElement);
                PairingResult pairing = authentication.Pair(ticket, deviceName);
                return Results.Json(new RemotePairingGrant(pairing.DeviceId, pairing.DeviceName, pairing.Token), statusCode: 201);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            { return Results.Json(new { error = ex.Message }, statusCode: 400); }
            catch (PairingRejectedException ex)
            {
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return Results.Json(new { error = ex.Message }, statusCode: 401);
            }
            catch (InvalidOperationException ex)
            { return Results.Json(new { error = ex.Message }, statusCode: 409); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return Results.Json(new { error = "无法保存受信任设备，请检查桌面端存储。" }, statusCode: 503); }
        });
        next.MapPost("/api/v1/commands", async (HttpContext context) =>
        {
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            try
            {
                using var document = await JsonDocument.ParseAsync(context.Request.Body, new JsonDocumentOptions { MaxDepth = 4 }, context.RequestAborted);
                RemoteCommand command = ParseCommand(document.RootElement);
                var response = await execute(command);
                return Results.Json(response);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or AggregateException)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex is JsonException or ArgumentException ? 400 : 409);
            }
        });
        try { await next.StartAsync(); app = next; }
        catch (Exception ex)
        {
            try { await next.DisposeAsync(); } catch { /* original failure wins */ }
            if (IsAddressInUse(ex)) throw new RemotePortInUseException(port, ex);
            throw;
        }
    }

    /// <summary>True when the failure chain contains a socket/ASP.NET address-in-use error.</summary>
    public static bool IsAddressInUse(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException socket && socket.SocketErrorCode == SocketError.AddressAlreadyInUse) return true;
            if (current is AddressInUseException) return true;
        }
        return false;
    }

    /// <summary>Probes the local port without credentials to tell a NightDesk listener apart from a foreign one.</summary>
    /// <summary>检测端口上是否为本机 NightDesk：新版直接回答健康检查；旧版（1.2.x 及更早）以 401 + 远程页品牌暴露自己。</summary>
    public static async Task<bool> IsNightDeskListeningAsync(int port, TimeSpan? timeout = null)
    {
        try
        {
            using var http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(2) };
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/v1/health");
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("app", out var app)
                    && app.ValueKind == JsonValueKind.String
                    && app.GetString() == "nightdesk";
            }
            if (response.StatusCode != HttpStatusCode.Unauthorized ||
                !response.Headers.WwwAuthenticate.Any(x => string.Equals(x.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)))
                return false;
            using var page = await http.GetAsync($"http://127.0.0.1:{port}/");
            if (!page.IsSuccessStatusCode) return false;
            string html = await page.Content.ReadAsStringAsync();
            return html.Contains("NightDesk", StringComparison.OrdinalIgnoreCase) && html.Contains("/api/v1/status", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException) { return false; }
    }
    public static (string Ticket, string DeviceName) ParsePairRequest(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("配对请求必须为 JSON 对象。");
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "ticket", "deviceName" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name)) throw new ArgumentException("配对请求含有未知或重复字段。");
        if (!root.TryGetProperty("ticket", out var ticketProperty) || ticketProperty.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("deviceName", out var nameProperty) || nameProperty.ValueKind != JsonValueKind.String)
            throw new ArgumentException("配对请求缺少 ticket 或 deviceName。");
        string ticket = ticketProperty.GetString()!;
        string deviceName = nameProperty.GetString()!;
        if (ticket.Length is < 32 or > 128 || ticket.Any(char.IsWhiteSpace)) throw new ArgumentException("配对票据格式无效。");
        if (string.IsNullOrWhiteSpace(deviceName) || deviceName.Trim().Length > 64 || deviceName.Any(char.IsControl))
            throw new ArgumentException("设备名称必须为 1–64 个可见字符。");
        return (ticket, deviceName.Trim());
    }

    public static RemoteCommand ParseCommand(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("命令必须为 JSON 对象。");
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "command", "width", "height", "refreshRate", "index", "enabled" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name)) throw new ArgumentException("命令含有未知或重复字段。");
        if (!root.TryGetProperty("command", out var nameProperty) || nameProperty.ValueKind != JsonValueKind.String)
            throw new ArgumentException("缺少 command 字段。");
        var name = nameProperty.GetString()!;
        if (name is not ("off" or "on" or "toggle" or "add" or "remove" or "prevent-sleep" or "restore"))
            throw new ArgumentException("不支持该命令。");
        int? width = Integer("width"), height = Integer("height"), refresh = Integer("refreshRate"), index = Integer("index");
        bool? enabled = null;
        if (root.TryGetProperty("enabled", out var enabledProperty))
        {
            if (enabledProperty.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("enabled 必须为布尔值。");
            enabled = enabledProperty.GetBoolean();
        }
        if (name == "add")
        {
            if (width is null || height is null || refresh is null) throw new ArgumentException("创建显示器需要 width、height、refreshRate。");
            _ = new DisplayMode(width.Value, height.Value, refresh.Value).Validate();
            if (index is not null || enabled is not null) throw new ArgumentException("创建命令包含多余字段。");
        }
        else if (width is not null || height is not null || refresh is not null) throw new ArgumentException("此命令不接受显示模式字段。");
        if (name == "remove") { if (index is null or < 0 or > 15) throw new ArgumentException("删除命令需要 0–15 的 index。"); }
        else if (index is not null) throw new ArgumentException("此命令不接受 index。");
        if (name == "prevent-sleep") { if (enabled is null) throw new ArgumentException("防休眠命令需要 enabled。"); }
        else if (enabled is not null) throw new ArgumentException("此命令不接受 enabled。");
        return new RemoteCommand(name, width, height, refresh, index, enabled);

        int? Integer(string property)
        {
            if (!root.TryGetProperty(property, out var value)) return null;
            if (!value.TryGetInt32(out int result)) throw new ArgumentException($"{property} 必须为整数。");
            return result;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (app is null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await app.StopAsync(timeout.Token); } catch (OperationCanceledException) { /* Dispose will tear down the listener. */ }
        await app.DisposeAsync(); app = null;
    }
}




/// <summary>Raised when the local API port is already taken, so callers can explain the real cause instead of a raw socket error.</summary>
public sealed class RemotePortInUseException : IOException
{
    public RemotePortInUseException(int port, Exception? inner = null)
        : base($"本地端口 {port} 已被占用，NightDesk 远程 API 无法监听。", inner) => Port = port;

    public int Port { get; }
}