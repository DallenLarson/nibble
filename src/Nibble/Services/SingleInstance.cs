using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Nibble.Services;

/// <summary>
/// One Nibble per user. A second launch (a link from another app, an installer shortcut,
/// a stray double-click) hands its command line to the browser that is already running
/// through a named pipe and exits, so links arrive as tabs instead of two processes
/// fighting over the same engine profile.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Nibble.Browser.Instance";
    private const string PipeName = "Nibble.Browser.Commands";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>True when this process owns the browser; false means hand off and quit.</summary>
    public bool IsFirst { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        IsFirst = created;
    }

    /// <summary>Hands a command line to the running instance. False when nothing is listening.</summary>
    public static bool Send(IEnumerable<string> args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.WriteLine(string.Join('\n', args.Where(a => !a.Contains('\n'))));
            writer.Flush();
            return true;
        }
        catch
        {
            // No browser listening (still starting, or wedged): fall through and let the
            // caller start its own window rather than losing the link entirely.
            return false;
        }
    }

    /// <summary>Runs the listening loop on a background thread until the app exits.</summary>
    public void Listen(Action<string[]> onArgs)
    {
        var thread = new Thread(() =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    server.WaitForConnection();

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = reader.ReadToEnd();
                    if (text.Length == 0) continue;

                    var args = text.Split('\n').Where(a => a.Length > 0).ToArray();
                    if (args.Length > 0) onArgs(args);
                }
                catch
                {
                    // A broken pipe is not interesting; wait a beat and keep listening.
                    Thread.Sleep(250);
                }
            }
        })
        {
            IsBackground = true,
            Name = "Nibble command line"
        };

        thread.Start();
    }

    public void Dispose()
    {
        try { _stop.Cancel(); } catch { }
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}

/// <summary>What a command line is asking Nibble to do.</summary>
public sealed record LaunchRequest(string? Url, bool Private, bool NewWindow)
{
    /// <summary>Reads Windows' own command line: flags, then an optional URL.</summary>
    public static LaunchRequest Parse(IEnumerable<string> argv)
    {
        var url = (string?)null;
        var isPrivate = false;
        var newWindow = false;

        foreach (var raw in argv)
        {
            var arg = raw.Trim();
            if (arg.Length == 0) continue;

            switch (arg.ToLowerInvariant())
            {
                case "--private":
                case "-private":
                case "--incognito":
                case "-incognito":
                    isPrivate = true;
                    continue;
                case "--new-window":
                case "-new-window":
                    newWindow = true;
                    continue;
                case "--":
                    continue;
            }

            // The shell hands us a URL as the first non-flag argument.
            if (url is null && !arg.StartsWith('-')) url = arg;
        }

        return new LaunchRequest(url, isPrivate, newWindow);
    }
}
