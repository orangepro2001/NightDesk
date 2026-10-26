$ErrorActionPreference = 'Stop'
$path = 'C:\dev\NightDesk\src\NightDesk\RemoteServer.cs'
$text = [System.IO.File]::ReadAllText($path)

function Swap([string]$old, [string]$new) {
    if (-not $text.Contains($old)) { throw "anchor not found: $($old.Substring(0,[Math]::Min(60,$old.Length)))" }
    $script:text = $text.Replace($old, $new)
}

Swap "using System.Net;
using System.Text.Json;" "using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Connections;"

Swap "public const int DefaultPort = 17645;" "public const int DefaultPort = 17645;
    public int Port => port;"

Swap "            bool pairingRequest = context.Request.Path.Equals(""/api/v1/pair"", StringComparison.Ordinal) &&
                                  HttpMethods.IsPost(context.Request.Method);
            if (context.Request.Path.StartsWithSegments(""/api"") && !pairingRequest &&" "            bool pairingRequest = context.Request.Path.Equals(""/api/v1/pair"", StringComparison.Ordinal) &&
                                  HttpMethods.IsPost(context.Request.Method);
            bool healthRequest = context.Request.Path.Equals(""/api/v1/health"", StringComparison.Ordinal) &&
                                 HttpMethods.IsGet(context.Request.Method);
            if (context.Request.Path.StartsWithSegments(""/api"") && !pairingRequest && !healthRequest &&"

Swap "        next.MapGet(""/"", () => Results.Content(html, ""text/html; charset=utf-8""));" "        next.MapGet(""/"", () => Results.Content(html, ""text/html; charset=utf-8""));
        // Unauthenticated liveness probe: proves the local listener is NightDesk, reveals no settings or tokens.
        next.MapGet(""/api/v1/health"", () => Results.Json(new { ok = true, app = ""nightdesk"", version = typeof(RemoteServer).Assembly.GetName().Version?.ToString(3) ?? ""0"" }));"

Swap "        try { await next.StartAsync(); app = next; }
        catch { await next.DisposeAsync(); throw; }
    }" "        try { await next.StartAsync(); app = next; }
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
    public static async Task<bool> IsNightDeskListeningAsync(int port, TimeSpan? timeout = null)
    {
        try
        {
            using var http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(2) };
            using var response = await http.GetAsync($""http://127.0.0.1:{port}/api/v1/health"");
            if (!response.IsSuccessStatusCode) return false;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(""app"", out var app)
                && app.ValueKind == JsonValueKind.String
                && app.GetString() == ""nightdesk"";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException) { return false; }
    }"

Swap "    public static (string Ticket, string DeviceName) ParsePairRequest(JsonElement root)" "    public static (string Ticket, string DeviceName) ParsePairRequest(JsonElement root)"

[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "patched RemoteServer.cs"
