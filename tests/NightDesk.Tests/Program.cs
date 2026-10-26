using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NightDesk;
using NightDesk.Tests;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

if (args.Contains("-control"))
{
    await FakeNxHost.RunAsync(args);
    return;
}

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
void RejectUnauthorized(Action action, string name) { try { action(); } catch (UnauthorizedAccessException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
Check(new DisplayMode(1920,1080,60).Validate() == new DisplayMode(1920,1080,60), "valid display mode");
var mainXaml = FindRepoFile(Path.Combine("src", "NightDesk", "MainWindow.xaml"));
Check(!File.ReadAllText(mainXaml).Contains("`r`n", StringComparison.Ordinal), "XAML has no literal escaped newline sequence");
var layoutXml = System.Xml.Linq.XDocument.Load(mainXaml);
System.Xml.Linq.XNamespace layoutNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
System.Xml.Linq.XNamespace nameNs = "http://schemas.microsoft.com/winfx/2006/xaml";
foreach (var fields in new[] { new[] { "NxListenBox", "NxDisplayBox", "NxFpsBox" }, new[] { "NxViewerAddressBox", "NxViewerCodeBox", "NxViewerFingerprintBox" } })
{
    var columns = fields.Select(name => (int?)layoutXml.Descendants().Single(e => (string?)e.Attribute(nameNs + "Name") == name).Parent!.Attribute("Grid.Column") ?? 0).ToArray();
    Check(columns.SequenceEqual(new[] { 0, 1, 2 }), "remote form fields occupy distinct columns: " + fields[0]);
}
Check(layoutXml.Descendants(layoutNs + "TabItem").Count() == 5, "desktop UI has five functional pages");
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
     Check(healthDoc.RootElement.GetProperty("version").GetString() == typeof(RemoteServer).Assembly.GetName().Version?.ToString(3), "health endpoint reports build version");
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
// ---- go-nx sidecar options, protocol, trust parsing and process lifecycle ----
Reject(() => new NxHostOptions("127.0.0.1", "primary", 10, "state").Validate(), "nx listen requires a port");
Reject(() => new NxHostOptions("127.0.0.1:7000", "primary", 0, "state").Validate(), "nx fps must be positive");
Reject(() => new NxHostOptions("127.0.0.1:7000", "primary", 61, "state").Validate(), "nx fps must stay below 61");
Reject(() => new NxHostOptions("127.0.0.1:7000", "bad\u0001display", 10, "state").Validate(), "nx display rejects control characters");
Reject(() => new NxHostOptions("127.0.0.1:7000", "primary", 10, " ").Validate(), "nx state directory required");
string nxState = Path.Combine(Path.GetTempPath(), "nx state");
string nxDevice = """\\.\\DISPLAY6""";
var nxOptions = new NxHostOptions("127.0.0.1:7000", nxDevice, 10, nxState);
var nxArguments = NxHostClient.BuildArguments(nxOptions);
Check(nxArguments.SequenceEqual(new[] { "-listen", "127.0.0.1:7000", "-display", nxDevice, "-fps", "10", "-state-dir", nxState, "-control", "stdio" }), "nx-host arguments are exact");
var nxReady = NxHostEvent.Parse("""{"event":"ready","listen":"127.0.0.1:7000","width":1920,"height":1080,"fps":10,"requireAuth":true}""");
Check(nxReady.Event == "ready" && nxReady.Width == 1920 && nxReady.RequireAuth == true, "parse ready event");
Check(!NxHostEvent.TryParse("""{"event":""}""", out _, out _), "reject event without a name");
Check(!NxHostEvent.TryParse("not-json", out _, out _), "reject malformed event json");
Check(NxHostProtocol.Pair() == """{"cmd":"pair","seconds":300}""", "pair command json");
Check(NxHostProtocol.Pair(0).Contains("\"seconds\":1"), "pair seconds clamp low");
Check(NxHostProtocol.Pair(9999).Contains("\"seconds\":3600"), "pair seconds clamp high");
Check(NxHostProtocol.Fps(0).Contains("\"value\":1"), "fps clamp low");
Check(NxHostProtocol.Fps(99).Contains("\"value\":60"), "fps clamp high");
Check(NxHostProtocol.Untrust("abc") == """{"cmd":"untrust","fingerprint":"abc"}""", "untrust command json");
var trustedDevice = NxTrustedDevice.Create("fingerprint", "192.168.1.5:5555 2026-10-27 01:20");
Check(trustedDevice.Endpoint == "192.168.1.5:5555" && trustedDevice.PairedAt == "2026-10-27 01:20", "parse trusted ipv4 entry");
var trustedV6 = NxTrustedDevice.Create("fingerprint", "[::1]:5555 2026-10-27 01:20");
Check(trustedV6.Endpoint == "[::1]:5555", "parse trusted ipv6 entry");
var trustedRaw = NxTrustedDevice.Create("fingerprint", "not-a-timestamp");
Check(trustedRaw.Endpoint == "not-a-timestamp" && trustedRaw.PairedAt == "", "keep unparseable trust value visible");
Check(NxTrustedDevice.From(null).Count == 0, "empty trust list has no devices");

var nxFingerprint = new string('A', 64);
var nxPayload = new NxPairingPayload("100.100.1.2:7000", nxFingerprint, "123456", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(), "PC-TEST", 1920, 1080);
nxPayload.Validate();
using (var qrJson = JsonDocument.Parse(nxPayload.ToJson()))
{
    Check(qrJson.RootElement.GetProperty("kind").GetString() == "nightdesk-nx", "NX QR kind");
    Check(qrJson.RootElement.GetProperty("fingerprint").GetString() == nxFingerprint.ToLowerInvariant(), "NX QR lowercases fingerprint");
    Check(qrJson.RootElement.GetProperty("addr").GetString() == "100.100.1.2:7000", "NX QR carries reachable address");
}
Check(NxPairingPayload.ResolveAddress("100.100.1.2:7000", null) == "100.100.1.2:7000", "NX explicit reachable address");
Check(NxPairingPayload.ResolveAddress("0.0.0.0:7000", "100.100.1.2") == "100.100.1.2:7000", "NX wildcard uses Tailscale IPv4");
Check(NxPairingPayload.WithHost("0.0.0.0:7000", "100.100.1.2") == "100.100.1.2:7000", "NX fills Tailscale host and preserves port");
Reject(() => NxPairingPayload.ResolveAddress("127.0.0.1:7000", "100.100.1.2"), "NX QR rejects loopback sidecar");
Check(NxPairingPayload.TryParseTailscaleIPv4("100.64.0.1", out _), "Tailscale IPv4 lower bound");
Check(NxPairingPayload.TryParseTailscaleIPv4("100.127.255.254", out _), "Tailscale IPv4 upper bound");
Check(!NxPairingPayload.TryParseTailscaleIPv4("100.63.255.255", out _), "reject address below Tailscale range");
Check(!NxPairingPayload.TryParseTailscaleIPv4("100.128.0.1", out _), "reject address above Tailscale range");
Check(!NxPairingPayload.TryParseTailscaleIPv4("fd7a:115c:a1e0::1", out _), "reject Tailscale IPv6 as NX IPv4");
using (var tailscaleStatus = JsonDocument.Parse("""{"BackendState":"Running","Self":{"TailscaleIPs":["fd7a:115c:a1e0::1","100.88.9.7"]}}"""))
    Check(TailscaleService.ExtractIPv4(tailscaleStatus.RootElement) == "100.88.9.7", "extract Tailscale IPv4 from status");
Reject(() => new NxPairingPayload("100.100.1.2:7000", nxFingerprint, "123456", DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(), "PC", 1920, 1080).Validate(), "NX QR rejects expired pairing");
Reject(() => new NxPairingPayload("192.168.1.2:7000", nxFingerprint, "123456", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(), "PC", 1920, 1080).Validate(), "NX QR rejects non-Tailscale address");
Reject(() => new NxPairingPayload("100.100.1.2:7000", nxFingerprint, "123456", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(), "PC", 63, 1080).Validate(), "NX QR rejects too-small frame");
Reject(() => new NxPairingPayload("100.100.1.2:7000", nxFingerprint, "123456", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(), "PC", 1920, 16385).Validate(), "NX QR rejects too-large frame");

var viewerTarget = NxViewerTarget.Parse("100.100.2.3:7001");
Check(viewerTarget.Address == "100.100.2.3:7001" && viewerTarget.Port == 7001, "viewer parses IPv4 and explicit port");
Check(NxViewerTarget.Parse("host.tailnet.ts.net").Port == 7000, "viewer defaults to UDP 7000");
Check(NxViewerTarget.Parse("[fd7a:115c:a1e0::2]:7000").Address == "[fd7a:115c:a1e0::2]:7000", "viewer parses bracketed IPv6");
Reject(() => NxViewerTarget.Parse("https://host:7000"), "viewer rejects URL syntax");
Reject(() => NxViewerTarget.Parse("host:0"), "viewer rejects zero port");
Reject(() => NxViewerTarget.Parse("fd00::1:7000"), "viewer requires brackets for ambiguous IPv6");
Check(NxViewerSecurity.NormalizeFingerprint(new string('A', 64).Insert(2, ":")) == new string('a', 64), "viewer normalizes partial fingerprint separators");
Reject(() => NxViewerSecurity.NormalizeFingerprint("xyz"), "viewer rejects malformed fingerprint");
byte[] helloPacket = NxViewerWire.Hello("123456");
Check(helloPacket.Length == 8 && helloPacket[0] == NxViewerWire.MsgHello && helloPacket[1] == 6, "viewer encodes hello");
byte[] inputPacket = NxViewerWire.Input(9, NxViewerInputKind.MouseDown, 10, 20, 1);
Check(inputPacket.Length == 22 && BitConverter.ToUInt64(inputPacket, 1) == 9 && inputPacket[9] == NxViewerInputKind.MouseDown &&
      BitConverter.ToInt32(inputPacket, 10) == 10 && BitConverter.ToInt32(inputPacket, 18) == 1, "viewer encodes input action");
Reject(() => NxViewerWire.Text(0, new string('x', 1025)), "viewer rejects oversized text action");
byte[] receiptPacket = new byte[10]; receiptPacket[0] = NxViewerWire.MsgReceipt;
BitConverter.GetBytes(77UL).CopyTo(receiptPacket, 1); receiptPacket[9] = 1;
Check(NxViewerWire.TryReadReceipt(receiptPacket, out ulong receiptOperation, out byte receiptStatus) &&
      receiptOperation == 77 && receiptStatus == 1, "viewer decodes receipt");
byte[] solidFrame = new byte[64 * 64 * 4];
byte[] solidTile = new byte[13]; solidTile[4] = 0; BitConverter.GetBytes(4u).CopyTo(solidTile, 5);
solidTile[9] = 1; solidTile[10] = 2; solidTile[11] = 3; solidTile[12] = 255;
IReadOnlyList<NxViewerRect> solidDirty = NxViewerTileDecoder.Apply(solidFrame, 64, 64, solidTile);
Check(solidDirty.Count == 1 && solidDirty[0] == new NxViewerRect(0, 0, 64, 64) &&
      solidFrame[0] == 1 && solidFrame[1] == 2 && solidFrame[2] == 3 && solidFrame[3] == 255,
      "viewer decodes a solid BGRA tile");
byte[] truncatedTile = new byte[9]; BitConverter.GetBytes(4u).CopyTo(truncatedTile, 5);
bool truncatedRejected = false;
try { _ = NxViewerTileDecoder.Apply(solidFrame, 64, 64, truncatedTile); }
catch (InvalidDataException) { truncatedRejected = true; }
Check(truncatedRejected, "viewer rejects a truncated tile");

string viewerStateDir = Path.Combine(Path.GetTempPath(), "nightdesk-viewer-unit-" + Guid.NewGuid().ToString("N"));
try
{
    using var identityOne = NxViewerIdentity.LoadOrCreate(viewerStateDir);
    using var identityTwo = NxViewerIdentity.LoadOrCreate(viewerStateDir);
    Check(NxViewerSecurity.ComputeFingerprint(identityOne) == NxViewerSecurity.ComputeFingerprint(identityTwo),
        "viewer identity is stable");
    var pinStore = NxViewerPinStore.Load(viewerStateDir);
    Check(pinStore.Get(viewerTarget) is null, "viewer pin store starts empty");
    pinStore.Set(viewerTarget, nxFingerprint.ToLowerInvariant());
    Check(NxViewerPinStore.Load(viewerStateDir).Get(viewerTarget) == nxFingerprint.ToLowerInvariant(),
        "viewer pin store persists a host fingerprint");
    Check(pinStore.Remove(viewerTarget) && NxViewerPinStore.Load(viewerStateDir).Get(viewerTarget) is null,
        "viewer pin store removes a host fingerprint");
}
finally
{
    if (Directory.Exists(viewerStateDir)) Directory.Delete(viewerStateDir, true);
}
string desktopUpdateDir = Path.Combine(Path.GetTempPath(), "nightdesk-desktop-update-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(desktopUpdateDir);
try
{
    byte[] nightDeskBytes = Encoding.UTF8.GetBytes("fake-nightdesk-exe-1.6.0");
    byte[] hostBytes = Encoding.UTF8.GetBytes("fake-nx-host-0.2.0");
    var packageMetadata = new WindowsPackageMetadata
    {
        SchemaVersion = 1,
        PackageName = DesktopUpdateService.PackageName,
        Version = "1.6.0",
        VersionCode = 10600,
        Files =
        [
            new WindowsPackageFile { Name = "NightDesk.exe", Size = nightDeskBytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(nightDeskBytes)).ToLowerInvariant() },
            new WindowsPackageFile { Name = "nx-host.exe", Size = hostBytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(hostBytes)).ToLowerInvariant() }
        ]
    };
    byte[] zipBytes;
    using (var archiveBuffer = new MemoryStream())
    {
        using (var zip = new ZipArchive(archiveBuffer, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("release.json").Open(), new UTF8Encoding(false)))
                writer.Write(JsonSerializer.Serialize(packageMetadata));
            using (var output = zip.CreateEntry("NightDesk.exe").Open()) output.Write(nightDeskBytes);
            using (var output = zip.CreateEntry("nx-host.exe").Open()) output.Write(hostBytes);
        }
        zipBytes = archiveBuffer.ToArray();
    }
    byte[] zipHash = SHA256.HashData(zipBytes);
    string updateManifest = JsonSerializer.Serialize(new WindowsUpdateRelease
    {
        SchemaVersion = 1,
        PackageName = DesktopUpdateService.PackageName,
        Version = "1.6.0",
        VersionCode = 10600,
        Arch = "win-x64",
        Size = zipBytes.Length,
        Sha256 = Convert.ToHexString(zipHash).ToLowerInvariant(),
        PackagePath = DesktopUpdateService.LatestPackagePath,
        Notes = "desktop updater test"
    });
    using (var updater = new DesktopUpdateService(desktopUpdateDir,
               new HttpClient(new StubHttpHandler(updateManifest, zipBytes)), new Version(1, 5, 0)))
    {
        WindowsUpdateRelease? release = updater.CheckAndDownloadAsync().GetAwaiter().GetResult();
        Check(release?.Version == "1.6.0", "desktop updater discovers newer release");
        Check(updater.Ready()?.Release.VersionCode == 10600 &&
              File.Exists(Path.Combine(updater.ReadyDirectory, "NightDesk.exe")) &&
              File.Exists(Path.Combine(updater.ReadyDirectory, "nx-host.exe")),
            "desktop updater verifies and stages both executables");
    }
    using (var current = new DesktopUpdateService(desktopUpdateDir,
               new HttpClient(new StubHttpHandler(updateManifest, zipBytes)), new Version(1, 6, 0)))
    {
        Check(current.CheckAndDownloadAsync().GetAwaiter().GetResult() is null &&
              current.Ready() is null,
            "desktop updater clears a pending package once current");
    }
    string badManifest = updateManifest.Replace(Convert.ToHexString(zipHash).ToLowerInvariant(), new string('0', 64));
    bool desktopHashRejected = false;
    try
    {
        using var bad = new DesktopUpdateService(desktopUpdateDir,
            new HttpClient(new StubHttpHandler(badManifest, zipBytes)), new Version(1, 5, 0));
        bad.CheckAndDownloadAsync().GetAwaiter().GetResult();
    }
    catch (InvalidDataException) { desktopHashRejected = true; }
    Check(desktopHashRejected, "desktop updater rejects a package hash mismatch");
}
finally
{
    if (Directory.Exists(desktopUpdateDir)) Directory.Delete(desktopUpdateDir, true);
}
string locatorDir = Path.Combine(Path.GetTempPath(), "nightdesk-locator-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(locatorDir);
string locatorExe = Path.Combine(locatorDir, "nx-host.exe");
File.WriteAllText(locatorExe, "");
try
{
    Environment.SetEnvironmentVariable("NIGHTDESK_NX_HOST", locatorExe);
    Check(NxHostLocator.Resolve() == Path.GetFullPath(locatorExe), "locator honours environment override");
    Environment.SetEnvironmentVariable("NIGHTDESK_NX_HOST", Path.Combine(locatorDir, "missing.exe"));
    bool missingRejected = false;
    try { _ = NxHostLocator.Resolve(); } catch (FileNotFoundException) { missingRejected = true; }
    Check(missingRejected, "locator rejects missing override");
}
finally
{
    Environment.SetEnvironmentVariable("NIGHTDESK_NX_HOST", null);
    if (Directory.Exists(locatorDir)) Directory.Delete(locatorDir, true);
}

string fakeExe = Environment.ProcessPath ?? throw new Exception("test process path missing");
string nxDir = Path.Combine(Path.GetTempPath(), "nightdesk-nx-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(nxDir);
Environment.SetEnvironmentVariable("NIGHTDESK_NX_HOST", fakeExe);
try
{
    int nxPort = FreePort();
    var lifecycleOptions = new NxHostOptions($"127.0.0.1:{nxPort}", "primary", 10, nxDir);
    using (var client = new NxHostClient(fakeExe, lifecycleOptions))
    {
        var pairing = new TaskCompletionSource<NxHostEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.EventReceived += value => { if (value.Event == "pairing" && !string.IsNullOrEmpty(value.Code)) pairing.TrySetResult(value); };
        var started = client.StartAsync().GetAwaiter().GetResult();
        Check(started.Event == "ready" && started.Width == 1920, "fake sidecar reports ready");
        int childPid = client.ProcessId;
        client.SendAsync(NxHostProtocol.Pair(60)).GetAwaiter().GetResult();
        Check(pairing.Task.Wait(TimeSpan.FromSeconds(5)), "fake sidecar answers pairing");
        Check(pairing.Task.Result.Code == "123456" && pairing.Task.Result.ExpiresIn == 60, "pairing event parsed");
        client.Stop();
        Check(ProcessHasExited(childPid), "sidecar exits after stop command");
        bool duplicateStop = true;
        try { client.Stop(); client.Stop(); DisposeTwice(client); }
        catch { duplicateStop = false; }
        Check(duplicateStop, "client stop and dispose are idempotent");
    }

    using (var service = new NxHostService())
    {
        var started = service.StartAsync(lifecycleOptions).GetAwaiter().GetResult();
        Check(service.IsRunning && service.State == NxHostState.Running && started.Event == "ready", "service reaches running");
        var sameReady = service.StartAsync(lifecycleOptions).GetAwaiter().GetResult();
        Check(ReferenceEquals(started, sameReady), "service start is idempotent while running");
        service.RefreshStatusAsync().GetAwaiter().GetResult();
        Check(service.Trusted.Count == 0, "service tracks empty trust list");
        Check(service.StopForTopologyChangeAsync().GetAwaiter().GetResult(), "topology stop reports a running sidecar");
        Check(!service.IsRunning && service.State == NxHostState.Stopped, "service reports stopped after topology stop");
        Check(!service.StopForTopologyChangeAsync().GetAwaiter().GetResult(), "topology stop is a no-op when stopped");
        service.StopAsync().GetAwaiter().GetResult();
        Check(!service.IsRunning, "duplicate service stop is harmless");
        var restarted = service.StartAsync(lifecycleOptions).GetAwaiter().GetResult();
        Check(restarted.Event == "ready" && service.IsRunning, "service restarts after a clean stop");
        service.StopAsync().GetAwaiter().GetResult();
    }

    var malformedLogs = new List<string>();
    Environment.SetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_MALFORMED", "1");
    try
    {
        using var tolerant = new NxHostClient(fakeExe, lifecycleOptions);
        tolerant.Log += malformedLogs.Add;
        var ready = tolerant.StartAsync().GetAwaiter().GetResult();
        Check(ready.Event == "ready" && malformedLogs.Any(line => line.Contains("invalid event", StringComparison.Ordinal)),
            "malformed stdout line is logged without breaking ready");
        tolerant.Stop();
    }
    finally { Environment.SetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_MALFORMED", null); }

    string trustFingerprint = new string('a', 64);
    Environment.SetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_TRUSTED", trustFingerprint);
    try
    {
        using var trustedService = new NxHostService();
        var statusSeen = new TaskCompletionSource<NxHostEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        trustedService.EventReceived += value => { if (value.Event == "status") statusSeen.TrySetResult(value); };
        trustedService.StartAsync(lifecycleOptions).GetAwaiter().GetResult();
        trustedService.RefreshStatusAsync().GetAwaiter().GetResult();
        bool sawStatus = statusSeen.Task.Wait(TimeSpan.FromSeconds(5));
        Check(sawStatus, "service receives status event");
        Check(statusSeen.Task.Result.Trusted?.Count == 1, "status event carries seeded trust entry");
        Check(WaitUntil(() => trustedService.Trusted.Count == 1, TimeSpan.FromSeconds(5)), "service refreshes seeded trust list");
        Check(trustedService.Trusted[0].Endpoint == "100.100.1.2:5555", "trusted endpoint survives status parsing");
        trustedService.UntrustAsync(trustFingerprint).GetAwaiter().GetResult();
        Check(WaitUntil(() => trustedService.Trusted.Count == 0, TimeSpan.FromSeconds(5)), "untrust emits a fresh status event");
        trustedService.StopAsync().GetAwaiter().GetResult();
    }
    finally { Environment.SetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_TRUSTED", null); }

    string exitOnceMarker = Path.Combine(nxDir, "exit-after-ready-once.marker");
    Environment.SetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_EXIT_AFTER_READY_ONCE", exitOnceMarker);
    try
    {
        using var recovering = new NxHostService();
        recovering.StartAsync(lifecycleOptions).GetAwaiter().GetResult();
        Check(WaitUntil(() => !recovering.IsRunning && recovering.State == NxHostState.Stopped, TimeSpan.FromSeconds(5)),
            "unexpected clean exit clears the stale running state");
        var recovered = recovering.StartAsync(lifecycleOptions).GetAwaiter().GetResult();
        Check(recovered.Event == "ready" && recovering.IsRunning, "service can start a fresh sidecar after unexpected exit");
        recovering.StopAsync().GetAwaiter().GetResult();
    }
    finally { Environment.SetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_EXIT_AFTER_READY_ONCE", null); }

    Environment.SetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_MODE", "exit");
    bool earlyExitRejected = false;
    try
    {
        using var broken = new NxHostClient(fakeExe, lifecycleOptions);
        try { broken.StartAsync().GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { earlyExitRejected = true; }
    }
    finally { Environment.SetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_MODE", null); }
    Check(earlyExitRejected, "sidecar exiting before ready fails startup");

    Environment.SetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_MODE", "exit");
    try
    {
        using var faultedService = new NxHostService();
        bool faulted = false;
        try { faultedService.StartAsync(lifecycleOptions).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { faulted = true; }
        Check(faulted && !faultedService.IsRunning && faultedService.State == NxHostState.Faulted,
            "service clears ownership and reports fault on early exit");
    }
    finally { Environment.SetEnvironmentVariable("NIGHTDESK_FAKE_NX_HOST_MODE", null); }
}
finally
{
    Environment.SetEnvironmentVariable("NIGHTDESK_NX_HOST", null);
    if (Directory.Exists(nxDir)) Directory.Delete(nxDir, true);
}

passed += await NxViewerIntegration.VerifyAsync(FindRepoFile(Path.Combine("src", "NightDesk", "Assets", "nx-host", "nx-host.exe")));
Console.WriteLine($"{passed} tests passed. No display state changed.");



static void DisposeTwice(IDisposable value)
{
    value.Dispose();
    value.Dispose();
}

static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
{
    var watch = Stopwatch.StartNew();
    while (watch.Elapsed < timeout)
    {
        if (condition()) return true;
        Thread.Sleep(25);
    }
    return condition();
}

static string FindRepoFile(string relativePath)
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
    }
    throw new FileNotFoundException("Repository file not found.", relativePath);
}

static bool ProcessHasExited(int pid)
{
    try
    {
        using var process = Process.GetProcessById(pid);
        return process.HasExited;
    }
    catch (ArgumentException) { return true; }
}

internal sealed class StubHttpHandler(string manifest, byte[] package) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string uri = request.RequestUri?.AbsoluteUri ?? "";
        byte[] body = uri.EndsWith("/nightdesk/api/v1/windows-update", StringComparison.Ordinal)
            ? Encoding.UTF8.GetBytes(manifest)
            : uri.EndsWith("/nightdesk/windows/latest.zip", StringComparison.Ordinal) ? package : [];
        var response = new HttpResponseMessage(body.Length == 0 ? System.Net.HttpStatusCode.NotFound : System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        };
        if (body.Length > 0 && uri.EndsWith(".zip", StringComparison.Ordinal))
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        return Task.FromResult(response);
    }
}
