using System.IO;
using System.Windows;
using System.Windows.Threading;
using Nibble.Services;

namespace Nibble;

public partial class App : Application
{
    public static Settings Settings { get; private set; } = new();

    private SingleInstance? _instance;
    private readonly List<MainWindow> _windows = [];

    /// <summary>
    /// The tabs of windows that have already closed during this run. The last window to close
    /// is the one that writes the session, so without this the tabs of a window closed earlier
    /// would be dropped on the floor - two windows, closed one at a time, would come back as
    /// one.
    /// </summary>
    private readonly List<string> _retiredTabs = [];

    public IReadOnlyList<string> RetiredTabs => _retiredTabs;

    public void RetireTabs(IEnumerable<string> tabs) => _retiredTabs.AddRange(tabs);

    /// <summary>Every browser window, in the order they were opened.</summary>
    public IReadOnlyList<MainWindow> BrowserWindows => _windows;

    /// <summary>True while another browser window is still open - what lets the last one close the process.</summary>
    public bool HasOtherWindows(MainWindow window) =>
        _windows.Any(w => !ReferenceEquals(w, window));

    /// <summary>True while a normal (not private) window is open, so a link reuses it.</summary>
    public MainWindow? NormalWindow => _windows.FirstOrDefault(w => !w.IsPrivate);

    protected override void OnStartup(StartupEventArgs e)
    {
        // The setup window closes before the browser window exists, so keep the app
        // alive explicitly until the real window is up.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var request = LaunchRequest.Parse(Environment.GetCommandLineArgs().Skip(1));

        // What an installer calls: register the browser bits and get out of the way.
        if (Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--register-browser", StringComparison.OrdinalIgnoreCase)))
        {
            BrowserRegistration.Register(Environment.ProcessPath);
            // Exit rather than Shutdown: an installer runs this with a timeout waiting on the
            // process handle, and every registry write above is already committed. Anything
            // that could keep a WPF app alive for another second is a liability here.
            Environment.Exit(0);
            return;
        }

        // ... and what an uninstaller calls, before it deletes the files.
        if (Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--unregister-browser", StringComparison.OrdinalIgnoreCase)))
        {
            BrowserRegistration.Unregister();
            Environment.Exit(0);
            return;
        }

        // What a script or a test calls: ask the release feed, fetch the installer if there is
        // one, write what happened to updates/last-check.json, and exit without a window. It is
        // answered here, before the single-instance hand-off, so it also works while the
        // browser is open.
        if (Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--check-updates", StringComparison.OrdinalIgnoreCase)))
        {
            Store.PerformPendingReset();
            Settings = Store.LoadSettings();
            RunUpdateCheckHeadless();
            return;
        }

        // One browser per user: a second launch opens a tab (or private window) in the
        // running one instead of a second process fighting over the engine profile.
        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            var argv = new List<string>();
            if (request.Private) argv.Add("--private");
            if (request.NewWindow) argv.Add("--new-window");
            if (request.Url is not null) argv.Add(request.Url);

            // If nothing is listening (an older build, or a wedged one) start a window of
            // our own rather than swallowing the click.
            if (SingleInstance.Send(argv))
            {
                Shutdown();
                return;
            }
        }

        // A pending reset is applied before anything reads the profile.
        Store.PerformPendingReset();
        Store.MigrateLegacy();
        Settings = Store.LoadSettings();

        // An update downloaded last time is applied now, before anything is on screen: the
        // installer replaces this build and starts the new one.
        Updater.CleanUp();
        if (Settings.Updates && Updater.PendingInstaller(out var pendingVersion) is { } pending &&
            Updater.ApplyAndExit(pending, pendingVersion))
        {
            Environment.Exit(0);
            return;
        }

        // The built-in pages carry the theme assets next to them, so lay those down first:
        // a theme is a folder the new tab page loads by relative path.
        Pages.Ensure();
        Themes.Refresh();
        Themes.Apply(Settings.ThemeId);

        DispatcherUnhandledException += (_, args) =>
        {
            TryLog(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => TryLog(args.ExceptionObject as Exception);

        base.OnStartup(e);

        _instance.Listen(OnCommandLine);
        Exit += (_, _) => _instance?.Dispose();

        if (!Settings.Onboarded)
        {
            var setup = new OnboardingWindow(Settings.AccentColor, Settings.UserName, Settings.SearchEngine,
                Settings.Clock, editing: false);
            setup.ShowDialog();
            Apply(setup.Outcome);
            Settings.Onboarded = true;
            Store.SaveSettings(Settings);
        }

        if (request.Private) OpenPrivateWindow(request.Url);
        else ShowBrowser(request.Url, newWindow: request.NewWindow);

        // A version that changed under the user gets said out loud, once, and the background
        // look for a newer one starts a few seconds later so a cold start stays a cold start.
        var previous = Settings.LastVersion;
        if (previous.Length > 0 &&
            !string.Equals(previous, Updater.Current.ToString(), StringComparison.Ordinal))
        {
            NormalWindow?.AnnounceUpdate(previous);
        }
        Settings.LastVersion = Updater.Current.ToString();
        Store.SaveSettings(Settings);

        NormalWindow?.ScheduleUpdateCheck(TimeSpan.FromSeconds(8));
    }

    /// <summary>
    /// The --check-updates path: no window, no engine, no waiting for a user. It asks the feed,
    /// fetches the installer when there is a newer release, writes what happened to
    /// updates/last-check.json, and exits with 0 when there is nothing to do, 2 when the feed
    /// could not be read and 3 when the download did not arrive intact.
    /// </summary>
    private void RunUpdateCheckHeadless()
    {
        var release = Updater.FetchAsync().GetAwaiter().GetResult();
        if (release is null)
        {
            Updater.WriteStatus(Settings.Updates, "unreachable", error: "the release feed could not be read");
            Environment.Exit(2);
            return;
        }

        Settings.LastUpdateCheck = DateTimeOffset.Now;
        Store.SaveSettings(Settings);

        if (!Updater.IsNewer(release.Version))
        {
            Updater.WriteStatus(Settings.Updates, "up-to-date", release);
            Environment.Exit(0);
            return;
        }

        var file = Updater.DownloadAsync(release).GetAwaiter().GetResult();
        Updater.WriteStatus(Settings.Updates, file is null ? "download-failed" : "ready", release, file,
            file is null ? "the installer did not arrive intact" : null);
        Environment.Exit(file is null ? 3 : 0);
    }


    /// <summary>Opens the normal window, or focuses the one that is already there.</summary>
    private void ShowBrowser(string? url = null, bool newWindow = false)
    {
        if (!newWindow && NormalWindow is { } existing)
        {
            existing.Activate();
            if (url is not null) existing.OpenUrlInNewTab(url, activate: true);
            return;
        }

        OpenWindow(url);
    }

    /// <summary>
    /// Opens a browser window and keeps the process alive until the last one closes.
    /// <paramref name="place"/> runs before the window is shown, so a window opened by
    /// dragging a tab out appears under the pointer instead of flashing somewhere else first.
    /// </summary>
    public MainWindow OpenWindow(string? url = null, bool privateWindow = false, Action<MainWindow>? place = null)
    {
        // The session belongs to the first window of a launch. Windows opened afterwards start
        // with the page they were asked for.
        var window = new MainWindow(privateWindow, restoreSession: _windows.Count == 0, startUrl: url);
        place?.Invoke(window);
        _windows.Add(window);

        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            if (_windows.Count == 0) Shutdown();
        };

        if (MainWindow is null) MainWindow = window;
        window.Show();
        return window;
    }

    /// <summary>A private window: its own window, its own throwaway engine profile.</summary>
    public MainWindow OpenPrivateWindow(string? url = null) => OpenWindow(url, privateWindow: true);

    public bool HasPrivateWindows => _windows.Any(w => w.IsPrivate);

    /// <summary>Handles a command line that arrived from a second launch.</summary>
    private void OnCommandLine(string[] args)
    {
        var request = LaunchRequest.Parse(args);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (request.Private) OpenPrivateWindow(request.Url);
            else ShowBrowser(request.Url, newWindow: request.NewWindow);
        }));
    }

    /// <summary>Applies a setup result to settings and the live theme.</summary>
    public static void Apply(OnboardingWindow.Result outcome)
    {
        Settings.AccentColor = outcome.AccentHex;
        Settings.UserName = outcome.UserName;
        Settings.SearchEngine = outcome.EngineId;
        Settings.Clock = ClockFormat.Normalize(outcome.Clock);
        Accent.Apply(Settings.AccentColor);
    }

    private static void TryLog(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Store.DataDir);
            File.AppendAllText(Path.Combine(Store.DataDir, "nibble.log"), $"[{DateTime.Now:u}] {ex}\n\n");
        }
        catch
        {
            // Logging must never take the browser down.
        }
    }
}
