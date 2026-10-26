using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NightDesk;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
void RejectUnauthorized(Action action, string name) { try { action(); } catch (UnauthorizedAccessException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
Check(new DisplayMode(1920,1080,60).Validate() == new DisplayMode(1920,1080,60), "valid display mode");
Reject(() => new DisplayMode(0,1080,60).Validate(), "reject zero width");
Reject(() => new DisplayMode(1920,1080,0).Validate(), "reject zero refresh");
Reject(() => new DisplayMode(20000,1080,60).Validate(), "reject huge mode");
Check(HotkeySpec.Parse("Ctrl+Alt+F12").Key == 123, "parse global hotkey");
Check(HotkeySpec.Parse("Win+Shift+M").Modifiers == 12, "parse modifiers");
Reject(() => HotkeySpec.Parse("F12"), "reject unmodified hotkey");
Reject(() => HotkeySpec.Parse("Ctrl+Ctrl+F12"), "reject duplicate modifier");
Reject(() => HotkeySpec.Parse("Ctrl+Alt+Delete"), "reject invalid/reserved key");
var bearerToken = new string('a', 40);
Check(!Security.HasToken(null, bearerToken), "reject missing bearer");
Check(!Security.HasToken("Bearer " + bearerToken + " ", bearerToken), "reject trailing token chars");
Check(Security.HasToken("Bearer " + bearerToken, bearerToken), "accept exact bearer");
Check(!Security.HasToken("Bearer " + new string('b', 40), bearerToken), "reject wrong bearer");
Check(!Security.HasToken(bearerToken, bearerToken), "require bearer scheme");
Check(!Security.HasToken("Bearer short", "short"), "reject short bearer");
NativeDisplays.VerifyLayouts(); DisplayTopology.VerifyLayouts(); Check(true, "Windows native ABI layout sizes");
var hardware = new MonitorInfo("hardware", "Physical output", "PCI\\test", true, false, true);
var virtualOutput = new MonitorInfo("virtual", "GameViewer", "Root\\test", true, false, false);
Check(NativeDisplays.HasActiveHardwareOutput([hardware]), "active hardware permits display cleanup");
Check(!NativeDisplays.HasActiveHardwareOutput([virtualOutput]), "third-party virtual output cannot substitute for hardware");
Check(!NativeDisplays.HasActiveHardwareOutput([hardware with { Active = false }, virtualOutput]), "inactive hardware cannot permit display cleanup");
Check(!NativeDisplays.HasActiveHardwareOutput([hardware with { IsParsec = true }]), "Parsec output cannot substitute for hardware");
Check(!NativeDisplays.HasActiveHardwareOutput([]), "no outputs cannot permit display cleanup");
Check(NativeDisplays.HasActiveHardwareOutput([virtualOutput, hardware]), "mixed outputs retain independent hardware");
Check(TailscaleService.IsEmptyConfig("{}"), "empty Serve config");
Check(!TailscaleService.IsEmptyConfig("{\"Web\":{\"a\":{}}}"), "refuse existing Serve config");
Check(!TailscaleService.IsEmptyConfig("invalid"), "refuse malformed Serve config");
var paths = new byte[72]; var modes = new byte[128];
BitConverter.GetBytes(100u).CopyTo(paths, 28);
BitConverter.GetBytes(1u).CopyTo(paths, 32);
BitConverter.GetBytes(0x80000000u).CopyTo(paths, 36);
BitConverter.GetBytes(1u).CopyTo(modes, 0);
BitConverter.GetBytes(2u).CopyTo(modes, 64);
var snapshot = new RecoverySnapshot(DateTime.UtcNow, paths, modes);
DisplayTopology.ValidateSnapshot(snapshot); Check(true, "target index uses +32, not target id or output technology");
BitConverter.GetBytes(0u).CopyTo(paths, 32);
Reject(() => DisplayTopology.ValidateSnapshot(snapshot), "target index must refer to target mode");
BitConverter.GetBytes(1u).CopyTo(paths, 32);
BitConverter.GetBytes(2u).CopyTo(paths, 12);
Reject(() => DisplayTopology.ValidateSnapshot(snapshot), "source mode index bounds");
BitConverter.GetBytes(0u).CopyTo(paths, 12);
Reject(() => DisplayTopology.ValidateSnapshot(new RecoverySnapshot(DateTime.UtcNow, new byte[71], modes)), "reject incomplete native path");
const string ownServe = """{"TCP":{"443":{"HTTPS":true}},"Web":{"minipc-xoh4t.tail9f7f1f.ts.net:443":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:17645"}}}}}""";
using (var serve = JsonDocument.Parse(ownServe))
{
    Check(TailscaleService.HasOnlyNightDeskRoute(serve.RootElement), "documented Tailscale 1.104.1 Serve schema");
    Check(TailscaleService.HasOnlyNightDeskRoute(serve.RootElement, "minipc-xoh4t.tail9f7f1f.ts.net"), "own node route accepted");
    Check(!TailscaleService.HasOnlyNightDeskRoute(serve.RootElement, "other-node.tail9f7f1f.ts.net"), "route for another node is foreign");
}
Check(TailscaleService.HasOnlyNightDeskRoute(ownServe), "string overload adopts existing NightDesk route");
Check(!TailscaleService.HasOnlyNightDeskRoute("""{"TCP":{"443":{"HTTPS":true}},"Web":{"minipc-xoh4t.tail9f7f1f.ts.net:443":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:8080"}}}}}"""), "foreign proxy is not adopted");
Check(!TailscaleService.HasOnlyNightDeskRoute("""{"TCP":{"443":{"HTTPS":true}},"Web":{"minipc-xoh4t.tail9f7f1f.ts.net:443":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:17645"}},"Extra":{}}}}"""), "extra handler rejected");
Check(!TailscaleService.HasOnlyNightDeskRoute("""{"TCP":{"443":{"HTTPS":true}},"Web":{"minipc-xoh4t.tail9f7f1f.ts.net:443":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:17645"}}}},"AllowFunnel":{"minipc-xoh4t.tail9f7f1f.ts.net:443":true}}"""), "funnel route is not adopted");
Check(!TailscaleService.HasOnlyNightDeskRoute("""{"TCP":{"443":{"HTTPS":true}},"Web":{"minipc-xoh4t.tail9f7f1f.ts.net:443":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:17645"}},"/api":{"Proxy":"http://127.0.0.1:17645"}}}}"""), "second route is not adopted");
Check(!TailscaleService.HasOnlyNightDeskRoute("{}"), "empty config is not a NightDesk route");

var dir = Path.Combine(Path.GetTempPath(),"NightDesk-tests-" + Guid.NewGuid());
var updateDir = Path.Combine(Path.GetTempPath(), "NightDesk-update-tests-" + Guid.NewGuid());
var registryDir = Path.Combine(Path.GetTempPath(), "NightDesk-device-tests-" + Guid.NewGuid());
Directory.CreateDirectory(updateDir); Directory.CreateDirectory(registryDir);
try {
 var store = new SettingsStore(dir); var settings = store.Load();
 Check(store.Token.Length >= 40,"generate remote token");
 Check(store.LocalDevicesPath.EndsWith("trusted-devices.dat", StringComparison.Ordinal), "device registry path is local to settings");
 string originalToken = store.Token; store.Save(settings); Check((store.Load() is not null && store.Token == originalToken),"DPAPI token round trip");
 Check(!File.ReadAllText(Path.Combine(dir,"settings.json")).Contains(store.Token), "no cleartext token on disk");
 File.WriteAllText(Path.Combine(dir,"settings.json"),"bad json");
 bool failed=false; try {store.Load();}catch(Exception){failed=true;} Check(failed,"corrupt config fails closed");

 string registryPath = Path.Combine(registryDir, "trusted-devices.dat");
 var registry = new DeviceRegistry(registryPath);
 Reject(() => registry.Add(" ", Security.NewToken()), "reject blank device name");
 Reject(() => registry.Add(new string('a', 65), Security.NewToken()), "reject long device name");
 Reject(() => registry.Add("bad\u0001name", Security.NewToken()), "reject control character in device name");
 var direct = registry.Add("Test phone", "ndd1_" + Security.NewToken());
 Check(registry.List().Single().Id == direct.Id, "device registry add and list");
 Check(!File.ReadAllText(registryPath).Contains("ndd1_"), "device registry does not contain cleartext token");
 Check(registry.Revoke(direct.Id), "device registry revoke");
 Check(registry.List().Count == 0, "device registry revoke removes record");

 var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback,0); listener.Start(); var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
 int calls=0; var secret=Security.NewToken();
 var devices = new DeviceRegistry(registryPath);
 var authentication = new RemoteAuthenticator(() => secret, devices);
 var pairing = authentication.IssuePairing("https://minipc-xoh4t.tail9f7f1f.ts.net");
 using (var qr = JsonDocument.Parse(pairing.Payload)) {
     Check(qr.RootElement.GetProperty("v").GetInt32() == 1, "QR payload version");
     Check(qr.RootElement.GetProperty("kind").GetString() == "nightdesk-pair", "QR payload kind");
     Check(qr.RootElement.GetProperty("origin").GetString() == "https://minipc-xoh4t.tail9f7f1f.ts.net", "QR payload origin");
     Check(qr.RootElement.GetProperty("ticket").GetString()!.StartsWith("ndp1_", StringComparison.Ordinal), "QR payload one-time ticket");
     Check(qr.RootElement.GetProperty("expires").GetInt64() == pairing.ExpiresUtc.ToUnixTimeSeconds(), "QR payload expiry");
     Check(!pairing.Payload.Contains("ndd1_", StringComparison.Ordinal), "QR payload has no device token");
 }
 Reject(() => authentication.IssuePairing("http://minipc-xoh4t.tail9f7f1f.ts.net"), "pairing refuses non-HTTPS origin");
 Reject(() => authentication.IssuePairing("https://example.com"), "pairing refuses non-Tailscale origin");

 var clock = DateTimeOffset.UtcNow;
 var expiredRegistry = new DeviceRegistry(Path.Combine(registryDir, "expired-devices.dat"), () => clock);
 var expiredAuth = new RemoteAuthenticator(() => secret, expiredRegistry, TimeSpan.FromMinutes(5), () => clock);
 var expiredInvite = expiredAuth.IssuePairing("https://minipc-xoh4t.tail9f7f1f.ts.net");
 using (var expiredQr = JsonDocument.Parse(expiredInvite.Payload)) {
     string expiredTicket = expiredQr.RootElement.GetProperty("ticket").GetString()!;
     clock = clock.AddMinutes(6);
     RejectUnauthorized(() => expiredAuth.Pair(expiredTicket, "Expired phone"), "expired pairing ticket is rejected");
 }
 var onceRegistry = new DeviceRegistry(Path.Combine(registryDir, "once-devices.dat"));
 var onceAuth = new RemoteAuthenticator(() => secret, onceRegistry);
 var onceQr = onceAuth.IssuePairing("https://minipc-xoh4t.tail9f7f1f.ts.net");
 using (var onceDoc = JsonDocument.Parse(onceQr.Payload)) {
     string onceTicket = onceDoc.RootElement.GetProperty("ticket").GetString()!;
     PairingResult once = onceAuth.Pair(onceTicket, "Once phone");
     Check(once.Token.StartsWith("ndd1_", StringComparison.Ordinal), "pairing returns long-lived device token");
     RejectUnauthorized(() => onceAuth.Pair(onceTicket, "Replay phone"), "pairing ticket is single-use");
 }

 await using (var api = new RemoteServer(port, authentication, () => new { mode="local" }, (cmd)=> { calls++; return Task.FromResult<object>(new {ok=true}); }, new AndroidUpdateFeed(updateDir)))
 {
     await api.StartAsync();
     using var http=new HttpClient {BaseAddress=new Uri($"http://127.0.0.1:{port}")};
     using (var homepage = await http.GetAsync("/"))
     {
         Check(homepage.IsSuccessStatusCode && homepage.Content.Headers.ContentType?.MediaType == "text/html", "embedded remote page is served");
         Check((await homepage.Content.ReadAsStringAsync()).Contains("NightDesk"), "embedded remote page contains application UI");
     }
     Check((await http.GetAsync("/api/v1/status")).StatusCode==HttpStatusCode.Unauthorized,"status requires authentication");
     Check(calls==0,"unauthenticated does not call controller");
     Check((await http.GetAsync("/api/v1/android-update")).StatusCode==HttpStatusCode.Unauthorized,"desktop update feed requires authentication");
     Check((await http.GetAsync("/api/v1/android-apk?versionCode=1")).StatusCode==HttpStatusCode.Unauthorized,"desktop APK requires authentication");
     Check((await http.GetAsync("/api/v1/pair")).StatusCode==HttpStatusCode.Unauthorized,"GET pair cannot bypass authentication");
     Check((await http.PostAsync("/api/v1/pair", new StringContent("{}", Encoding.UTF8, "text/plain"))).StatusCode==HttpStatusCode.UnsupportedMediaType,"pair requires JSON content type");
     Check((await http.PostAsync("/api/v1/pair", new StringContent("not-json", Encoding.UTF8, "application/json"))).StatusCode==HttpStatusCode.BadRequest,"pair rejects malformed JSON");
     Check((await http.PostAsync("/api/v1/pair", new StringContent("{\"ticket\":\"ndp1_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\",\"deviceName\":\"Phone\"}", Encoding.UTF8, "application/json"))).StatusCode==HttpStatusCode.Unauthorized,"pair rejects unknown ticket");
     Check(calls==0,"pair failures never call controller");

     string ticket;
     using (var pairDoc = JsonDocument.Parse(pairing.Payload)) ticket = pairDoc.RootElement.GetProperty("ticket").GetString()!;
     Check((await http.PostAsync("/api/v1/pair", new StringContent(JsonSerializer.Serialize(new { ticket, deviceName = "  " }), Encoding.UTF8, "application/json"))).StatusCode==HttpStatusCode.BadRequest,"pair rejects blank device name before consuming ticket");
     HttpResponseMessage granted = await http.PostAsync("/api/v1/pair", new StringContent(JsonSerializer.Serialize(new { ticket, deviceName = "Pixel 9" }), Encoding.UTF8, "application/json"));
     Check(granted.StatusCode==HttpStatusCode.Created,"pair accepts one-time QR ticket");
     string deviceId; string deviceToken; string deviceName;
     using (var grantedDoc = JsonDocument.Parse(await granted.Content.ReadAsStringAsync())) {
         deviceId = grantedDoc.RootElement.GetProperty("deviceId").GetString()!;
         deviceToken = grantedDoc.RootElement.GetProperty("token").GetString()!;
         deviceName = grantedDoc.RootElement.GetProperty("deviceName").GetString()!;
     }
     Check(deviceToken.StartsWith("ndd1_", StringComparison.Ordinal) && deviceToken.Length >= 40, "pair response grants device token");
     Check(deviceName == "Pixel 9", "pair response normalizes device name");
     Check(devices.List().Single(x => x.Id == deviceId).Name == "Pixel 9", "paired device appears in desktop registry");
     Check(!File.ReadAllText(registryPath).Contains(deviceToken), "paired device token is not written in cleartext");
     Check((await http.PostAsync("/api/v1/pair", new StringContent(JsonSerializer.Serialize(new { ticket, deviceName = "Replay" }), Encoding.UTF8, "application/json"))).StatusCode==HttpStatusCode.Unauthorized,"used QR ticket cannot mint another token");
     using (var deviceRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status"))
     {
         deviceRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
         Check((await http.SendAsync(deviceRequest)).IsSuccessStatusCode,"device token authenticates status");
     }
     Check(authentication.RevokeDevice(deviceId), "desktop revokes selected device");
     using (var revokedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status"))
     {
         revokedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
         Check((await http.SendAsync(revokedRequest)).StatusCode==HttpStatusCode.Unauthorized,"revoked device token fails immediately");
     }
     Check(calls==0,"pair and revoke never invoke display controller");

     http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",secret);
     Check((await http.GetAsync("/api/v1/status")).IsSuccessStatusCode,"shared token remains compatible");
     Check((await http.GetAsync("/api/v1/off")).StatusCode==HttpStatusCode.MethodNotAllowed,"GET cannot mutate state");
     Check((await http.PostAsync("/api/v1/commands", new StringContent("{\"command\":\"shell\"}",Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.BadRequest,"unknown command refused");
     Check(calls==0,"invalid command never calls controller");
     Check((await http.PostAsync("/api/v1/commands",new StringContent("{\"command\":\"off\"}",Encoding.UTF8,"application/json"))).IsSuccessStatusCode,"known command dispatched");
     Check(calls==1,"dispatch once");
     Check((await http.PostAsync("/api/v1/commands",new StringContent("{\"command\":\"add\",\"width\":-1,\"height\":1080,\"refreshRate\":60}",Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.BadRequest,"invalid mode refused at boundary");
     Check(calls==1,"invalid mode never calls controller");
     Check((await http.GetAsync("/api/v1/android-update")).StatusCode==HttpStatusCode.NoContent,"no release is 204");
     var apkBytes = Encoding.UTF8.GetBytes("NightDesk isolated synthetic update fixture");
     var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(apkBytes)).ToLowerInvariant();
     var release = new AndroidRelease(1,"dev.nightdesk.android",1,"1.0.0",29,apkBytes.Length,digest,new string('a',64),"/api/v1/android-apk?versionCode=1","test fixture");
     File.WriteAllBytes(Path.Combine(updateDir,"nightdesk-1.apk"),apkBytes);
     void Manifest(AndroidRelease value) => File.WriteAllText(Path.Combine(updateDir,"manifest.json"),JsonSerializer.Serialize(value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
     Manifest(release);
     Check((await http.GetAsync("/api/v1/android-update")).IsSuccessStatusCode,"published manifest available without restart");
     Check((await http.GetByteArrayAsync(release.ApkPath)).SequenceEqual(apkBytes),"download exactly matches manifest bytes");
     Check((await http.GetAsync("/api/v1/android-apk?versionCode=2")).StatusCode==HttpStatusCode.Conflict,"stale version requests refresh");
     Manifest(release with { Size = release.Size + 1 });
     Check((await http.GetAsync("/api/v1/android-update")).StatusCode==HttpStatusCode.ServiceUnavailable,"incomplete publication unavailable");
     File.WriteAllText(Path.Combine(updateDir,"manifest.json"),"invalid");
     Check((await http.GetAsync("/api/v1/android-update")).StatusCode==HttpStatusCode.ServiceUnavailable,"invalid manifest fails closed");
     Manifest(release);
     Check(calls==1,"updates do not invoke display controller");
     secret=Security.NewToken(); Check((await http.GetAsync("/api/v1/status")).StatusCode==HttpStatusCode.Unauthorized,"rotation revokes prior shared token immediately");
 // 端口冲突与残留监听：必须转成可诊断异常，且失败后不破坏已有监听、可再次绑定
 int busyPort;
 { var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); probe.Start(); busyPort = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
 RemoteServer Make(int p) => new(p, authentication, () => new { mode = "local" }, _ => Task.FromResult<object>(new { ok = true }), new AndroidUpdateFeed(updateDir));
 await using (var primary = Make(busyPort))
 {
     await primary.StartAsync();
     await using var conflict = Make(busyPort);
     bool portRejected = false;
     try { await conflict.StartAsync(); } catch (RemotePortInUseException) { portRejected = true; }
     Check(portRejected, "busy port raises RemotePortInUseException");
     Check(await RemoteServer.IsNightDeskListeningAsync(busyPort), "health probe identifies a live NightDesk listener");
     using var portHttp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{busyPort}") };
     using var health = await portHttp.GetAsync("/api/v1/health");
     Check(health.IsSuccessStatusCode, "health endpoint answers without a token");
     using var healthDoc = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
     Check(healthDoc.RootElement.GetProperty("app").GetString() == "nightdesk", "health endpoint identifies the app");
     Check(healthDoc.RootElement.GetProperty("version").GetString()!.StartsWith("1.3.1"), "health endpoint reports build version");
     Check((await portHttp.GetAsync("/api/v1/status")).StatusCode == HttpStatusCode.Unauthorized, "health endpoint does not bypass auth elsewhere");
 }
 Check(!await RemoteServer.IsNightDeskListeningAsync(busyPort), "probe reports the port free after dispose");
 await using (var rebound = Make(busyPort))
 {
     await rebound.StartAsync();
     Check(await RemoteServer.IsNightDeskListeningAsync(busyPort), "port rebinds cleanly after rollback");
 }
 }
} finally { if (Directory.Exists(dir)) Directory.Delete(dir,true); if (Directory.Exists(updateDir)) Directory.Delete(updateDir,true); if (Directory.Exists(registryDir)) Directory.Delete(registryDir,true); }
// 旧版 NightDesk 没有 /api/v1/health：401 + Bearer + 远程页品牌仍应被识别，其它 401 服务不应误判。
static int FreePort() { var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); return port; }
static Microsoft.AspNetCore.Builder.WebApplication FakeListener(int port, string html) {
    var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateSlimBuilder(new Microsoft.AspNetCore.Builder.WebApplicationOptions { Args = Array.Empty<string>() });
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port));
    var app = builder.Build();
    app.Use(async (context, run) => {
        if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 401; context.Response.Headers.WWWAuthenticate = "Bearer"; return; }
        await run();
    });
    app.MapGet("/", () => Microsoft.AspNetCore.Http.Results.Content(html, "text/html; charset=utf-8"));
    return app;
}
{
    int legacyPort = FreePort();
    await using (var legacy = FakeListener(legacyPort, "<!doctype html><title>NightDesk · 远程桌面</title><script>fetch('/api/v1/status')</script>"))
    {
        await legacy.StartAsync();
        Check(await RemoteServer.IsNightDeskListeningAsync(legacyPort), "legacy NightDesk listener recognised despite 401");
        await legacy.StopAsync();
    }
    int foreignPort = FreePort();
    await using (var foreign = FakeListener(foreignPort, "<!doctype html><title>Other control panel</title>"))
    {
        await foreign.StartAsync();
        Check(!await RemoteServer.IsNightDeskListeningAsync(foreignPort), "unrelated 401 service is not mistaken for NightDesk");
        await foreign.StopAsync();
    }
}
Check(VddInstaller.HasEmbeddedPayload, "bundled Parsec VDD installer is embedded in the build");
string vddCache = Path.Combine(Path.GetTempPath(), "nightdesk-vdd-" + Guid.NewGuid().ToString("N"));
try
{
    string? payload = VddInstaller.FindPayload(vddCache);
    Check(payload is not null && File.Exists(payload), "embedded installer extracts to disk");
    Check(VddInstaller.ValidatePayload(payload!) is null, "bundled installer matches pinned SHA-256 and Parsec Cloud signature");
    Check(new FileInfo(payload!).Length == VddInstaller.PayloadSize, "bundled installer size matches expected value");
    string tampered = Path.Combine(vddCache, "tampered.exe");
    File.WriteAllBytes(tampered, new byte[VddInstaller.PayloadSize]);
    Check(VddInstaller.ValidatePayload(tampered) is not null, "tampered installer is rejected before execution");
    Check(VddInstaller.ValidatePayload(Path.Combine(vddCache, "missing.exe")) is not null, "missing installer is rejected");
}
finally { if (Directory.Exists(vddCache)) Directory.Delete(vddCache, true); }
Console.WriteLine($"{passed} tests passed. No display state changed.");


