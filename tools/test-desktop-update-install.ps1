[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('nightdesk-installer-test-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $testRoot 'source'
$target = Join-Path $testRoot 'target'
$project = Join-Path $testRoot 'fake-app'
New-Item -ItemType Directory -Path $source,$target,$project | Out-Null
[IO.File]::WriteAllText((Join-Path $project 'Fake.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><AssemblyName>NightDesk</AssemblyName></PropertyGroup></Project>')
[IO.File]::WriteAllText((Join-Path $project 'Program.cs'), 'File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "restarted.txt"), "isolated synthetic update test");')
& dotnet publish (Join-Path $project 'Fake.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $source
if ($LASTEXITCODE -ne 0) { throw 'Cannot build synthetic test EXE' }
[IO.File]::WriteAllText((Join-Path $source 'nx-host.exe'), 'synthetic new host - never executed')
[IO.File]::WriteAllText((Join-Path $target 'NightDesk.exe'), 'synthetic old app')
[IO.File]::WriteAllText((Join-Path $target 'nx-host.exe'), 'synthetic old host')
$files = foreach ($name in @('NightDesk.exe','nx-host.exe')) {
    $path = Join-Path $source $name
    @{name=$name;size=(Get-Item -LiteralPath $path).Length;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}
}
[IO.File]::WriteAllText((Join-Path $source 'package.json'), (@{files=@($files)} | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
$code = [IO.File]::ReadAllText((Join-Path $root 'src\NightDesk\DesktopUpdateService.cs'))
$match = [regex]::Match($code, '(?s)private const string InstallScript = """\r?\n(.*?)\r?\n""";')
if (-not $match.Success) { throw 'Cannot extract actual install helper' }
$script = Join-Path $testRoot 'apply-update.ps1'
[IO.File]::WriteAllText($script, $match.Groups[1].Value, (New-Object Text.UTF8Encoding($false)))
$sleeper = Start-Process powershell.exe -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 3') -WindowStyle Hidden -PassThru
& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $script -TargetProcessId $sleeper.Id -SourceDirectory $source -TargetDirectory $target
if ($LASTEXITCODE -ne 0) { throw 'Actual install helper failed in isolated test' }
for ($i=0; $i -lt 50 -and -not (Test-Path -LiteralPath (Join-Path $target 'restarted.txt')); $i++) { Start-Sleep -Milliseconds 100 }
if (-not (Test-Path -LiteralPath (Join-Path $target 'restarted.txt'))) { throw 'Synthetic app did not restart' }
foreach ($name in @('NightDesk.exe','nx-host.exe')) {
    if ((Get-FileHash -LiteralPath (Join-Path $source $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target $name)).Hash) { throw "Installed file mismatch: $name" }
    if (-not (Test-Path -LiteralPath (Join-Path $target ($name + '.bak-update')))) { throw "Missing rollback backup: $name" }
}
'PASS isolated updater: waited for owned process, replaced both files, preserved backups, restarted synthetic WinExe.'
"Evidence: $testRoot"
