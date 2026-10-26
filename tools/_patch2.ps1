$path = 'C:\dev\NightDesk\src\NightDesk\RemoteServer.cs'
$text = [System.IO.File]::ReadAllText($path)
$addition = @"

/// <summary>Raised when the local API port is already taken, so callers can explain the real cause instead of a raw socket error.</summary>
public sealed class RemotePortInUseException : IOException
{
    public RemotePortInUseException(int port, Exception? inner = null)
        : base($"本地端口 {port} 已被占用，NightDesk 远程 API 无法监听。", inner) => Port = port;

    public int Port { get; }
}
"@
[System.IO.File]::WriteAllText($path, $text + $addition, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "appended exception"
