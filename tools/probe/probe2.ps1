
& 'C:\Program Files\Tailscale\tailscale.exe' serve --bg --yes --https=443 http://127.0.0.1:17645 2>&1 | Out-String
Write-Output ('exit=' + $LASTEXITCODE)
& 'C:\Program Files\Tailscale\tailscale.exe' serve status --json
