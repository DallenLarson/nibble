# Offline media server for the media/tab-nap probe. Serves a page that plays a real
# (audible) tone and reports once a second whether the audio is still advancing, so a test
# can prove what happened while the tab sat in the background.
#   /page      the test page (tools/MediaPage.html)
#   /tone.wav  60 s of 220 Hz sine, 8 kHz mono 16-bit PCM
#   /report    POST target; each body is appended to -Log as one JSON line
param(
    [int]$Port = 8797,
    [int]$Seconds = 900,
    [string]$Log = "",
    [string]$Page = ""
)

$ErrorActionPreference = "Stop"

# $PSScriptRoot is empty when this file is run as a background job, so fall back to the
# invocation's own path and, failing that, to the repository layout.
$here = if ($PSScriptRoot) { $PSScriptRoot } elseif ($MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path } else { (Get-Location).Path }
if (-not $Log) { $Log = Join-Path $here "..\scratch\media\samples.jsonl" }
if (-not $Page) { $Page = Join-Path $here "MediaPage.html" }

$logDir = Split-Path -Parent $Log
if ($logDir -and -not (Test-Path $logDir)) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }

# ---- the tone: a real signal, because Chromium only calls a page "audible" when the
# ---- samples handed to the output device are actually non-silent.
# A half-million samples is far too slow in interpreted PowerShell, so build it compiled.
Add-Type -TypeDefinition @"
using System;
public static class MediaTone
{
    public static byte[] Make(int rate, int seconds, double frequency, short amplitude)
    {
        int samples = rate * seconds;
        int dataBytes = samples * 2;
        byte[] wav = new byte[dataBytes + 44];
        Action<int, string> put = (offset, text) =>
            Array.Copy(System.Text.Encoding.ASCII.GetBytes(text), 0, wav, offset, text.Length);
        put(0, "RIFF");
        BitConverter.GetBytes(36 + dataBytes).CopyTo(wav, 4);
        put(8, "WAVEfmt ");
        BitConverter.GetBytes(16).CopyTo(wav, 16);
        BitConverter.GetBytes((short)1).CopyTo(wav, 20);
        BitConverter.GetBytes((short)1).CopyTo(wav, 22);
        BitConverter.GetBytes(rate).CopyTo(wav, 24);
        BitConverter.GetBytes(rate * 2).CopyTo(wav, 28);
        BitConverter.GetBytes((short)2).CopyTo(wav, 32);
        BitConverter.GetBytes((short)16).CopyTo(wav, 34);
        put(36, "data");
        BitConverter.GetBytes(dataBytes).CopyTo(wav, 40);
        for (int i = 0; i < samples; i++)
        {
            double value = amplitude * Math.Sin(2 * Math.PI * frequency * i / rate);
            short sample = (short)value;
            wav[44 + i * 2] = (byte)(sample & 0xFF);
            wav[45 + i * 2] = (byte)((sample >> 8) & 0xFF);
        }
        return wav;
    }
}
"@
$wav = [MediaTone]::Make(8000, 60, 220, 12000)

# Three pages: the media probe, and the opener/popup pair used to prove that window.open
# hands back a real window with a real opener (which is what a sign-in flow needs).
$pages = @{
    "/page"     = $Page
    "/popup"    = Join-Path (Split-Path -Parent $Page) "Popup.html"
    "/popupopener" = Join-Path (Split-Path -Parent $Page) "PopupOpener.html"
    "/auto"     = Join-Path (Split-Path -Parent $Page) "PopupOnLoad.html"
    "/one"      = Join-Path (Split-Path -Parent $Page) "Drag1.html"
    "/two"      = Join-Path (Split-Path -Parent $Page) "Drag2.html"
    "/three"    = Join-Path (Split-Path -Parent $Page) "Drag3.html"
}
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
$deadline = (Get-Date).AddSeconds($Seconds)
$ascii = [System.Text.Encoding]::ASCII

while ((Get-Date) -lt $deadline) {
    if (-not $listener.Pending()) { Start-Sleep -Milliseconds 40; continue }
    $client = $listener.AcceptTcpClient()
    try {
        $stream = $client.GetStream()
        $reader = [System.IO.StreamReader]::new($stream)
        $requestLine = $reader.ReadLine()
        $rangeHeader = ""
        $length = 0
        while ($true) {
            $line = $reader.ReadLine()
            if ([string]::IsNullOrEmpty($line)) { break }
            if ($line -like "Range:*") { $rangeHeader = $line }
            if ($line -like "Content-Length:*") { $length = [int]($line -replace "[^0-9]", "") }
        }

        $method = if ($requestLine -match '^(\S+)') { $matches[1] } else { "GET" }
        $path = if ($requestLine -match '^\S+\s+(\S+)') { $matches[1] } else { "/" }

        if ($method -eq "POST" -and $path -like "/report*") {
            $buffer = New-Object char[] ([Math]::Max(1, $length))
            $read = 0
            while ($read -lt $length) {
                $n = $reader.Read($buffer, $read, $length - $read)
                if ($n -le 0) { break }
                $read += $n
            }
            $body = -join $buffer[0..([Math]::Max(0, $read - 1))]
            Add-Content -LiteralPath $Log -Value $body -Encoding UTF8
            $response = "HTTP/1.1 204 No Content`r`nConnection: close`r`n`r`n"
            $bytes = $ascii.GetBytes($response)
            $stream.Write($bytes, 0, $bytes.Length)
        }
        elseif ($path -like "/tone.wav*") {
            $start = 0
            $end = $wav.Length - 1
            $status = "200 OK"
            $extra = ""
            if ($rangeHeader -match "bytes=(\d+)-(\d*)") {
                $start = [int]$matches[1]
                if ($matches[2] -ne "") { $end = [Math]::Min([int]$matches[2], $wav.Length - 1) }
                $status = "206 Partial Content"
                $extra = "Content-Range: bytes $start-$end/$($wav.Length)`r`n"
            }
            $count = $end - $start + 1
            $headers = "HTTP/1.1 $status`r`nContent-Type: audio/wav`r`nAccept-Ranges: bytes`r`n" +
                       "$extra" + "Content-Length: $count`r`nCache-Control: no-store`r`nConnection: close`r`n`r`n"
            $headerBytes = $ascii.GetBytes($headers)
            $stream.Write($headerBytes, 0, $headerBytes.Length)
            $stream.Write($wav, $start, $count)
        }
        elseif ($pages.ContainsKey($path)) {
            $bytes = [System.IO.File]::ReadAllBytes($pages[$path])
            $headers = "HTTP/1.1 200 OK`r`nContent-Type: text/html; charset=utf-8`r`n" +
                       "Cache-Control: no-store`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n"
            $headerBytes = $ascii.GetBytes($headers)
            $stream.Write($headerBytes, 0, $headerBytes.Length)
            $stream.Write($bytes, 0, $bytes.Length)
        }
        else {
            $headers = "HTTP/1.1 404 Not Found`r`nContent-Length: 0`r`nConnection: close`r`n`r`n"
            $headerBytes = $ascii.GetBytes($headers)
            $stream.Write($headerBytes, 0, $headerBytes.Length)
        }
        $stream.Flush()
    }
    catch {
        # A dropped connection is not interesting for a test server.
    }
    finally {
        $client.Close()
    }
}

$listener.Stop()
