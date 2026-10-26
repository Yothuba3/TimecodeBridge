<#
.SYNOPSIS
  TimecodeBridge v3(Host)の Windows 通し確認。smoke.sh の Windows 版で、jq・pgrep・python は要らない(pwsh 7)。
  起動 → Web が ready → OSC を発火して受信 → 送信ログと画面を確認 → 撮影、を行い SMOKE PASS / SMOKE FAIL で終わる。
  -LoopbackDevice を付けると 2 つ目の Host でその出力へ LTC を生成し、1 つ目がその出力のループバック取り込みで受信して、
  LTC の時刻でキューが発火し OSC が届くところまで確かめる(VB-Audio Virtual Cable などの聞こえない出力を指定すること)。
.EXAMPLE
  pwsh tools/tcb3/smoke-windows.ps1                                        # Debug ビルド
  pwsh tools/tcb3/smoke-windows.ps1 -Exe publish\TimecodeBridge3.exe       # 発行物(CI)
  pwsh tools/tcb3/smoke-windows.ps1 -LoopbackDevice "CABLE In 16ch"        # LTC 生成 → ループバック受信も
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\src\TimecodeBridge.Host\bin\Debug\net8.0\TimecodeBridge3.exe'),
    [int]$Port = 47310,
    [int]$OscPort = 9110,
    [string]$RunDir = (Join-Path ([IO.Path]::GetTempPath()) 'tcb3-smoke'),
    [string]$LoopbackDevice = '',
    [int]$ReadyTimeoutSec = 60,
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
$script:fails = 0
function Ok([string]$m) { Write-Host "ok   $m" }
function Ng([string]$m) { Write-Host "NG   $m"; $script:fails++ }

function Api([int]$port, [string]$method, [string]$path, [string]$body = $null, [string]$contentType = 'application/json; charset=utf-8') {
    $uri = "http://127.0.0.1:$port$path"
    if ($null -eq $body) { return Invoke-RestMethod -Uri $uri -Method $method -TimeoutSec 30 }
    return Invoke-RestMethod -Uri $uri -Method $method -TimeoutSec 30 -ContentType $contentType -Body ([Text.Encoding]::UTF8.GetBytes($body))
}
function Cmd([int]$port, [string]$name, $arguments = $null) {
    $msg = @{ command = $name }
    if ($null -ne $arguments) { $msg.args = $arguments }
    return Api $port POST /command ($msg | ConvertTo-Json -Depth 10 -Compress)
}
function Eval([int]$port, [string]$js) { return (Api $port POST /eval $js 'text/plain; charset=utf-8').result }
function State([int]$port) { return (Api $port GET /state).state }
function Clock([int]$port) { return Api $port GET /clock }

$script:hosts = @()
function Start-Host([int]$port, [string]$name) {
    $dir = Join-Path $RunDir $name
    New-Item -ItemType Directory -Force (Join-Path $dir 'data') | Out-Null
    # 試験用インスタンスが利用者の設定(最近使ったプロジェクト)を書き換えないよう、設定保存先を分ける
    $env:TIMECODEBRIDGE_AUTOMATION_PORT = "$port"
    $env:TIMECODEBRIDGE_DATA_DIR = Join-Path $dir 'data'
    $env:TIMECODEBRIDGE_BRIDGE_TRACE = '1'
    try {
        $p = Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath) -PassThru `
            -RedirectStandardOutput (Join-Path $dir 'host.log') -RedirectStandardError (Join-Path $dir 'host.err.log')
    }
    finally {
        Remove-Item Env:TIMECODEBRIDGE_AUTOMATION_PORT, Env:TIMECODEBRIDGE_DATA_DIR, Env:TIMECODEBRIDGE_BRIDGE_TRACE -ErrorAction SilentlyContinue
    }
    $script:hosts += $p
    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($p.HasExited) {
            Get-Content (Join-Path $dir 'host.log'), (Join-Path $dir 'host.err.log') -ErrorAction SilentlyContinue | Select-Object -Last 40 | Write-Host
            throw "$name(port $port)が起動直後に終了しました(exit $($p.ExitCode))"
        }
        try { if ((Api $port GET /health).webReady) { Ok "$name 起動 pid=$($p.Id) port=$port"; return $p } } catch { }
        Start-Sleep -Milliseconds 500
    }
    Get-Content (Join-Path $dir 'host.log') -ErrorAction SilentlyContinue | Select-Object -Last 40 | Write-Host
    throw "$name(port $port)が $ReadyTimeoutSec 秒待っても Web ready になりません"
}

# ウィンドウを PrintWindow(PW_RENDERFULLCONTENT)で撮る。前面化しないので、他のウィンドウに隠れていても WebView の中身が写る
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Tcb3Window {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
}
'@
function Save-Shot([Diagnostics.Process]$p, [string]$out) {
    [void][Tcb3Window]::SetProcessDpiAwarenessContext([IntPtr]-4)
    $p.Refresh()
    $h = $p.MainWindowHandle
    if ($h -eq [IntPtr]::Zero) { Ng "撮影: ウィンドウが見つかりません"; return }
    $r = New-Object Tcb3Window+RECT
    [void][Tcb3Window]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object Drawing.Bitmap ([Math]::Max(1, $r.R - $r.L)), ([Math]::Max(1, $r.B - $r.T))
    $g = [Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    $ok = [Tcb3Window]::PrintWindow($h, $hdc, 2)
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save($out, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    if ($ok) { Ok "撮影 $out" } else { Ng "撮影: PrintWindow が失敗しました" }
}

# OSC を 1 通受けてアドレスを返す(届かなければ $null)。ループバックだけに束縛するのでファイアウォールの確認は出ない
function Receive-Osc([Net.Sockets.UdpClient]$udp, [int]$timeoutMs) {
    $udp.Client.ReceiveTimeout = $timeoutMs
    $from = New-Object Net.IPEndPoint ([Net.IPAddress]::Any), 0
    try { $bytes = $udp.Receive([ref]$from) } catch [Net.Sockets.SocketException] { return $null }
    $end = [Array]::IndexOf($bytes, [byte]0)
    return [Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Max(0, $end))
}

$exePath = (Resolve-Path $Exe).Path
# WebView2 のユーザーデータが実行ファイルの隣に作られていないか(Program Files に入れると書けず表示できない)。以前の実行の残りは数えない
$besideExe = "$exePath.WebView2"
$besideExeExisted = Test-Path $besideExe
New-Item -ItemType Directory -Force $RunDir | Out-Null
$udp = New-Object Net.Sockets.UdpClient (New-Object Net.IPEndPoint ([Net.IPAddress]::Loopback), $OscPort)
try {
    $main = Start-Host $Port 'main'

    $s = State $Port
    if ($s.uiCapabilities.platform -eq 'windows') { Ok 'uiCapabilities.platform=windows' } else { Ng "platform=$($s.uiCapabilities.platform)" }
    if ((Eval $Port "document.body.innerText.includes('キューリスト')") -eq 'true') { Ok 'Web UI が描画されている' } else { Ng 'Web UI に「キューリスト」がありません' }
    if ($besideExeExisted -or -not (Test-Path $besideExe)) { Ok 'WebView2 のユーザーデータを実行ファイルの隣に作らない' } else { Ng "WebView2 のユーザーデータが実行ファイルの隣にできた: $besideExe" }

    # ホストとキューを登録し、手動発火した OSC が届くか
    $hid = (Cmd $Port host.add @{ host = @{ name = 'smoke'; ipAddress = '127.0.0.1'; port = $OscPort } }).data.id
    if ($hid) { Ok "host.add $hid" } else { Ng 'host.add 失敗' }
    $ping = Cmd $Port host.ping @{ id = $hid }
    if ($ping.ok -and $ping.data.reachable) { Ok "host.ping $($ping.data.latencyMs)ms" } else { Ng "host.ping: $($ping | ConvertTo-Json -Compress)" }
    $cue = @{ name = 'smoke-manual'; triggerTime = '00:00:10:00'; oscAddress = '/smoke/manual'; targetHostIds = @($hid); arguments = @(@{ type = 'int32'; value = 1 }, @{ type = 'string'; value = '日本語' }) }
    $cid = (Cmd $Port cue.add @{ cue = $cue }).data.id
    if ($cid) { Ok "cue.add $cid" } else { Ng 'cue.add 失敗' }
    $fire = Cmd $Port cue.fire @{ id = $cid }
    if ($fire.ok -and $fire.data.sentCount -eq 1) { Ok 'cue.fire sentCount=1' } else { Ng "cue.fire: $($fire | ConvertTo-Json -Compress)" }
    $addr = Receive-Osc $udp 5000
    if ($addr -eq '/smoke/manual') { Ok "OSC 受信 $addr" } else { Ng "OSC が届かない(受信: $addr)" }
    Start-Sleep -Milliseconds 500
    $s = State $Port
    if ($s.logs | Where-Object { $_.success -and $_.message -like '/smoke/manual*' }) { Ok '送信ログに成功が残る' } else { Ng '送信ログに /smoke/manual がありません' }
    if ((Eval $Port "document.body.innerText.includes('smoke-manual')") -eq 'true') { Ok 'キューが画面に出ている' } else { Ng 'キューが画面に出ていません' }

    if ($LoopbackDevice) {
        # 2 つ目の Host で LTC を生成してその出力へ流し、1 つ目はその出力のループバック取り込みで受ける
        $gen = Start-Host ($Port + 1) 'gen'
        $out = (State ($Port + 1)).generator.outputDevices | Where-Object { $_.name -like "*$LoopbackDevice*" } | Select-Object -First 1
        $in = (State $Port).receive.devices | Where-Object { $_.loopback -and $_.name -like "*$LoopbackDevice*" } | Select-Object -First 1
        if (-not $out -or -not $in) { throw "デバイス '$LoopbackDevice' の出力またはループバック入力が見つかりません" }
        [void](Cmd ($Port + 1) mode.set @{ mode = 'generate' })
        [void](Cmd ($Port + 1) generator.configure @{ startTime = '01:00:00:00'; frameRate = '30'; outputDeviceId = $out.id; volume = 0.8 })
        [void](Cmd ($Port + 1) generator.start)
        [void](Cmd $Port receive.selectDevice @{ deviceId = $in.id })
        $deadline = (Get-Date).AddSeconds(15)
        do { Start-Sleep -Milliseconds 500; $t = (State $Port).transport } while ($t.status -ne 'receiving' -and (Get-Date) -lt $deadline)
        if ($t.status -eq 'receiving') { Ok "LTC 受信 ($($in.name))" } else { Ng "LTC を受信できない: $($t.statusText) $($t.detailText)" }
        $c1 = Clock $Port; Start-Sleep -Seconds 2; $c2 = Clock $Port
        if ($c2.totalFrames - $c1.totalFrames -ge 45 -and $c2.frameRate -eq '30') { Ok "時計が進む $($c1.display) → $($c2.display) ($($c2.frameRate)fps)" } else { Ng "時計: $($c1.display) → $($c2.display) $($c2.frameRate)" }
        # 誤り率は受信開始からの累計で、開始直後に弾かれる数フレームが最初の数秒は大きく効くので、5 秒以上受けてから見る
        Start-Sleep -Seconds 3
        $err = (State $Port).transport.signalErrorRatePercent
        if ($null -ne $err -and $err -lt 2) { Ok "誤り率 $([Math]::Round($err, 2))%" } else { Ng "誤り率 $err%" }

        # 受信した LTC の時刻でキューが発火するか(3 秒後)
        $f = (Clock $Port).totalFrames + 90
        $trig = '{0:00}:{1:00}:{2:00}:{3:00}' -f [Math]::Floor($f / 108000), ([Math]::Floor($f / 1800) % 60), ([Math]::Floor($f / 30) % 60), ($f % 30)
        $lcue = @{ name = 'smoke-ltc'; triggerTime = $trig; frameRate = '30'; oscAddress = '/smoke/ltc'; targetHostIds = @($hid) }
        [void](Cmd $Port cue.add @{ cue = $lcue })
        $addr = Receive-Osc $udp 10000
        if ($addr -eq '/smoke/ltc') { Ok "LTC $trig で発火した OSC を受信" } else { Ng "LTC 発火の OSC が届かない(受信: $addr)" }
        Save-Shot $gen (Join-Path $RunDir 'smoke-gen.png')
    }

    Start-Sleep -Milliseconds 500
    Save-Shot $main (Join-Path $RunDir 'smoke.png')
}
catch {
    Ng $_.Exception.Message
}
finally {
    $udp.Dispose()
    if (-not $Keep) { foreach ($p in $script:hosts) { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } } }
}

if ($script:fails -eq 0) { Write-Host 'SMOKE PASS'; exit 0 } else { Write-Host "SMOKE FAIL ($script:fails)"; exit 1 }
