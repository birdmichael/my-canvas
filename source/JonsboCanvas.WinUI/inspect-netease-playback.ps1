param([string]$Expression = '')

$ErrorActionPreference = 'Stop'

function Get-CdpTarget {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $targets = @(Invoke-RestMethod 'http://127.0.0.1:38476/json/list' -TimeoutSec 1)
            $page = $targets | Where-Object { $_.type -eq 'page' -and $_.webSocketDebuggerUrl } | Select-Object -First 1
            if ($page) { return $page }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    throw 'No NetEase CDP page target is available.'
}

function Send-CdpCommand([System.Net.WebSockets.ClientWebSocket]$Socket, [int]$Id, [string]$Method, $Parameters) {
    $command = @{ id = $Id; method = $Method; params = $Parameters } | ConvertTo-Json -Depth 8 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($command)
    $segment = [ArraySegment[byte]]::new($bytes)
    $Socket.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true,
        [Threading.CancellationToken]::None).GetAwaiter().GetResult()
}

function Receive-CdpResponse([System.Net.WebSockets.ClientWebSocket]$Socket, [int]$Id) {
    $buffer = New-Object byte[] 65536
    while ($true) {
        $stream = [IO.MemoryStream]::new()
        try {
            do {
                $result = $Socket.ReceiveAsync([ArraySegment[byte]]::new($buffer),
                    [Threading.CancellationToken]::None).GetAwaiter().GetResult()
                $stream.Write($buffer, 0, $result.Count)
            } while (-not $result.EndOfMessage)
            $json = [Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
            if ($json.id -eq $Id) { return $json }
        } finally {
            $stream.Dispose()
        }
    }
}

function Evaluate-Cdp([System.Net.WebSockets.ClientWebSocket]$Socket, [int]$Id, [string]$Expression) {
    Send-CdpCommand $Socket $Id 'Runtime.evaluate' @{
        expression = $Expression
        returnByValue = $true
        awaitPromise = $true
    }
    return Receive-CdpResponse $Socket $Id
}

$target = Get-CdpTarget
$socket = [System.Net.WebSockets.ClientWebSocket]::new()
$socket.Options.KeepAliveInterval = [Threading.Timeout]::InfiniteTimeSpan
try {
    $socket.ConnectAsync([uri]$target.webSocketDebuggerUrl,
        [Threading.CancellationToken]::None).GetAwaiter().GetResult()

    if ($Expression) {
        (Evaluate-Cdp $socket 99 $Expression).result.result.value
        return
    }

    $install = @"
(()=>{
  if(typeof window.jonsboPlayback!=='function') return 'binding-unavailable';
  if(!window.__jonsboProbeOriginal){
    window.__jonsboProbeOriginal=window.jonsboPlayback;
    window.jonsboPlayback=(payload)=>{
      window.__jonsboProbePayload=payload;
      return window.__jonsboProbeOriginal(payload);
    };
  }
  return 'probe-installed';
})()
"@
    (Evaluate-Cdp $socket 1 $install).result.result.value
    Start-Sleep -Seconds 3
    $payload = (Evaluate-Cdp $socket 2 'window.__jonsboProbePayload||""').result.result.value
    (Evaluate-Cdp $socket 3 '(()=>{if(window.__jonsboProbeOriginal){window.jonsboPlayback=window.__jonsboProbeOriginal;delete window.__jonsboProbeOriginal;}return "restored";})()').result.result.value
    $payload
} finally {
    $socket.Abort()
    $socket.Dispose()
}
