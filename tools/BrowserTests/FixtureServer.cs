using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class FixtureServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    public byte[] Pdf { get; set; } = [];
    public string Url { get; }
    public FixtureServer()
    {
        listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        _ = Serve();
    }
    private async Task Serve()
    {
        try { while (true) { var client = await listener.AcceptTcpClientAsync(); _ = Respond(client); } }
        catch (Exception e) when (e is SocketException or ObjectDisposedException) { }
    }
    private async Task Respond(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new System.IO.StreamReader(stream, leaveOpen: true);
                var line = await reader.ReadLineAsync();
                var path = line?.Split(' ').ElementAtOrDefault(1) ?? "/";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                if (path == "/reset") return;
                if (path == "/slow") await Task.Delay(1500);
                var redirect = path == "/redirect";
                var pdf = path is "/test.pdf" or "/download";
                var code = redirect ? "302 Found" : path == "/missing" ? "404 Not Found" : "200 OK";
                var body = pdf ? Pdf : Encoding.UTF8.GetBytes("<html><body><h1>Fixture destination</h1><a href='/redirect' id='link'>Open redirect</a></body></html>");
                var headers = $"HTTP/1.1 {code}\r\nContent-Type: {(pdf ? "application/pdf" : "text/html")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n";
                if (redirect) headers += "Location: /ok\r\n";
                if (path == "/download") headers += "Content-Disposition: attachment; filename=test.pdf\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"));
                await stream.WriteAsync(body);
            }
            catch (Exception e) when (e is System.IO.IOException or ObjectDisposedException) { }
        }
    }
    public void Dispose() => listener.Stop();
}
