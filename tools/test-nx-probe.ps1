# Runs the go-nx host in strict test mode and drives it with nx-probe.
# Nothing here captures the desktop or calls SendInput: the host uses a
# synthetic 1920x1080 pattern and logs input instead of injecting it.
[CmdletBinding()]
param(
    [string]$HostExe = '',
    [string]$ProbeExe = '',
    [int]$Port = 0,
    [int]$Frames = 4,
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $HostExe) { $HostExe = Join-Path (Split-Path -Parent $scriptDir) 'src\NightDesk\Assets\nx-host\nx-host.exe' }
if (-not $ProbeExe) { $ProbeExe = Join-Path $scriptDir 'nx-probe.exe' }
foreach ($file in @($HostExe, $ProbeExe)) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing executable: $file" }
}

# Safety guard: bare test mode must resolve to the synthetic source and logged
# injector. Upstream changed this from an error to safe defaults; accepting the
# defaults makes the guard stronger because it verifies the process cannot fall
# through to GDI/SendInput.
$previousEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$guard = & $HostExe -test-mode 2>&1 | Out-String
$guardExit = $LASTEXITCODE
$ErrorActionPreference = $previousEap
if ($guardExit -ne 0 -or $guard -notmatch '"testMode":true' -or $guard -notmatch '"source":"pattern"' -or $guard -notmatch '"inputMode":"log"') {
    throw "Safety guard failed: -test-mode alone must select pattern/log and exit 0, got exit $guardExit. Output: $guard"
}
"Safety guard OK: -test-mode alone selects synthetic pattern and logged input."

if ($Port -le 0) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $Port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()
}
$addr = "127.0.0.1:$Port"

$psi = [Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $HostExe
$psi.Arguments = "-test-mode -source pattern -inject log -control stdio -pair -listen $addr"
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true

$host_ = [Diagnostics.Process]::new()
$host_.StartInfo = $psi
[void]$host_.Start()
$stderrTask = $host_.StandardError.ReadToEndAsync()

function Read-Stderr {
    if ($null -eq $stderrTask) { return '' }
    if (-not $stderrTask.IsCompleted) { return '<stderr still open>' }
    try { return $stderrTask.GetAwaiter().GetResult() } catch { return '<stderr unavailable>' }
}

function Read-HostEvent([int]$DeadlineMs = 15000) {
    $task = $host_.StandardOutput.ReadLineAsync()
    if (-not $task.Wait($DeadlineMs)) { throw "Timed out waiting for nx-host event. stderr: $(Read-Stderr)" }
    $line = $task.Result
    if ($null -eq $line) { throw "nx-host closed stdout. stderr: $(Read-Stderr)" }
    return ($line | ConvertFrom-Json)
}

$ready = $null
$pairing = $null
$events = @()
$deadline = [DateTime]::UtcNow.AddSeconds(15)
while ([DateTime]::UtcNow -lt $deadline -and ($null -eq $ready -or $null -eq $pairing)) {
    $ev = Read-HostEvent
    $events += $ev
    if ($ev.event -eq 'ready') { $ready = $ev }
    if ($ev.event -eq 'pairing') { $pairing = $ev }
}
if ($null -eq $ready -or $null -eq $pairing) { throw "Missing ready/pairing events: $($events | ConvertTo-Json -Compress)" }
if (-not $ready.testMode -or $ready.source -ne 'pattern' -or $ready.inputMode -ne 'log') {
    throw "Host is not in the expected test mode: $($ready | ConvertTo-Json -Compress)"
}
"Host ready: $addr display $($ready.display) $($ready.width)x$($ready.height) testMode=$($ready.testMode) source=$($ready.source) inputMode=$($ready.inputMode)"

try {
    $probeArgs = @('-addr', $addr, '-fingerprint', $ready.identity, '-code', $pairing.code,
        '-frames', "$Frames", '-timeout', "$($TimeoutSeconds)s", '-input', '-json')
    $probeOut = & $ProbeExe @probeArgs 2>&1 | Out-String
    $probeExit = $LASTEXITCODE
    $probeText = $probeOut.Trim()
    $probe = $null
    if ($probeText) {
        try { $probe = $probeText | ConvertFrom-Json } catch { }
    }
    if ($null -eq $probe) { throw "nx-probe produced no JSON (exit $probeExit): $probeText" }
    $probe | ConvertTo-Json -Depth 5

    if ($probeExit -ne 0) { throw "nx-probe failed with exit $probeExit" }
    if (-not $probe.connected) { throw "probe did not report a connection" }
    if ($probe.width -ne 1920 -or $probe.height -ne 1080) { throw "unexpected frame size $($probe.width)x$($probe.height)" }
    if ($probe.frames -lt $Frames) { throw "received $($probe.frames) frames, want $Frames" }
    if ($probe.receipts -lt 7) { throw "received $($probe.receipts) input receipts, want at least 7" }
    if ($probe.receiptFailures -ne 0) { throw "$($probe.receiptFailures) input receipts failed" }
    if ($probe.digestFirst -eq $probe.digestLast) { throw "frames did not animate" }
    "PASS: $($probe.frames) frames, $($probe.tiles) tile updates, $($probe.receipts) input receipts, dynamic content."
}
finally {
    if (-not $host_.HasExited) {
        try {
            $host_.StandardInput.WriteLine('{"cmd":"stop"}')
            $host_.StandardInput.Flush()
        } catch { }
        if (-not $host_.WaitForExit(5000)) {
            $host_.Kill()
            [void]$host_.WaitForExit(5000)
        }
    }
    $host_.StandardInput.Close()
    $exitCode = $host_.ExitCode
    "Host exit code: $exitCode"
    if ($exitCode -ne 0) {
        "Host stderr:`n$(Read-Stderr)"
        throw "nx-host exited with $exitCode"
    }
}
[Environment]::Exit(0)
