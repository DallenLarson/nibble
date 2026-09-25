# Tiny offline HTTP server used to prove that a Nibble private window keeps its own
# cookie jar and leaves nothing on disk. It answers with the cookie-jar state in the
# page title, so a test can read the answer straight out of the window title.
#   /set?c=value   -> sets a 1-day cookie
#   /check         -> title says "cookie present" or "cookie none"
param([int]$Port = 8791, [int]$Seconds = 900)

$ErrorActionPreference = "Stop"
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
$deadline = (Get-Date).AddSeconds($Seconds)
$utf8 = [System.Text.Encoding]::UTF8
$ascii = [System.Text.Encoding]::ASCII

while ((Get-Date) -lt $deadline) {
    if (-not $listener.Pending()) { Start-Sleep -Milliseconds 60; continue }

    $client = $listener.AcceptTcpClient()
    try {
        $stream = $client.GetStream()
        $reader = [System.IO.StreamReader]::new($stream)
        $requestLine = $reader.ReadLine()
        $cookieHeader = ""
        while ($true) {
            $line = $reader.ReadLine()
            if ([string]::IsNullOrEmpty($line)) { break }
            if ($line -like "Cookie:*") { $cookieHeader = $line }
        }

        $path = if ($requestLine -match '^GET\s+(\S+)') { $matches[1] } else { "/" }
        $setCookie = ""
        $title = ""

        if ($path -like "/set*") {
            # /set?n=name&c=value — the name lets a test tell one jar from another.
            $name = if ($path -match 'n=([^&\s]+)') { $matches[1] } else { "nibbletest" }
            $value = if ($path -match 'c=([^&\s]+)') { $matches[1] } else { "value" }
            $setCookie = "Set-Cookie: $name=$value; Path=/; Max-Age=86400`r`n"
            $title = "set $name=$value"
        }
        else {
            # Report every cookie the jar offered, so one jar is distinguishable from another.
            $found = @()
            foreach ($pair in @($cookieHeader -split ';')) {
                # no anchor: the first pair still carries its "Cookie:" prefix
                if ($pair -match '(nibbletest|prvtoken)=([^\s;]*)') { $found += "$($matches[1])=$($matches[2])" }
            }
            $title = if ($found.Count -gt 0) { "cookie " + ($found -join " ") } else { "cookie none" }
        }

        $body = "<html><head><title>$title</title></head><body>$title</body></html>"
        $bodyBytes = $utf8.GetBytes($body)
        $headers = "HTTP/1.1 200 OK`r`nContent-Type: text/html; charset=utf-8`r`n" +
                   "Cache-Control: no-store`r`n$setCookie" +
                   "Content-Length: $($bodyBytes.Length)`r`nConnection: close`r`n`r`n"
        $headerBytes = $ascii.GetBytes($headers)
        $stream.Write($headerBytes, 0, $headerBytes.Length)
        $stream.Write($bodyBytes, 0, $bodyBytes.Length)
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
