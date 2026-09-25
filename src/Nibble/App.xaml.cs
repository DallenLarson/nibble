using System.IO;
using System.Windows;
using System.Windows.Threading;
using Nibble.Services;

namespace Nibble;

public partial class App : Application
{
    public static Settings Settings { get; private set; } = new();

    private SingleInstance? _instance;
    private MainWindow? _normal;
    private readonly List<MainWindow> _private = [];

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
            Shutdown();
            return;
        }

        // ... and what an uninstaller calls, before it deletes the files.
        if (Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--unregister-browser", StringComparison.OrdinalIgnoreCase)))
        {
            BrowserRegistration.Unregister();
            Shutdown();
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
        else ShowBrowser(request.Url);
    }

    /// <summary>Opens the normal window, or focuses the one that is already there.</summary>
    private void ShowBrowser(string? url = null)
    {
        if (_normal is not null)
        {
            _normal.Activate();
            if (url is not null) _normal.OpenUrlInNewTab(url, activate: true);
            return;
        }

        _normal = new MainWindow(privateWindow: false);
        MainWindow = _normal;
        // Shutdown is managed here rather than by OnMainWindowClose: a private window is
        // allowed to outlive the last normal one (as in every other browser), so closing
        // the normal window must not take the process with it.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _normal.Closed += (_, _) =>
        {
            _normal = null;
            if (_private.Count == 0) Shutdown();
        };
        _normal.Show();

        if (url is not null) _normal.OpenUrlInNewTab(url, activate: true);
    }

    /// <summary>A private window: its own window, its own throwaway engine profile.</summary>
    public MainWindow OpenPrivateWindow(string? url = null)
    {
        var window = new MainWindow(privateWindow: true);
        _private.Add(window);
        window.Closed += (_, _) =>
        {
            _private.Remove(window);
            if (_private.Count == 0 && _normal is null) Shutdown();
        };

        window.Show();
        if (url is not null) window.OpenUrlInNewTab(url, activate: true);
        return window;
    }

    public bool HasPrivateWindows => _private.Count > 0;

    /// <summary>Handles a command line that arrived from a second launch.</summary>
    private void OnCommandLine(string[] args)
    {
        var request = LaunchRequest.Parse(args);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (request.Private) OpenPrivateWindow(request.Url);
            else if (request.Url is null && request.NewWindow) ShowBrowser();
            else ShowBrowser(request.Url);
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
