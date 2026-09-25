using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Nibble.Controls;
using Nibble.Services;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Nibble;

public partial class MainWindow : Window
{
    public ObservableCollection<ZTab> Tabs { get; } = [];

    private static readonly JsonSerializerOptions PageJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private CoreWebView2Environment? _env;
    private readonly TaskCompletionSource _envReady = new();
    private ZTab? _active;
    private readonly Stack<(string Url, string Title)> _closed = [];
    private readonly Dictionary<string, ImageSource?> _faviconCache = [];
    private readonly List<Download> _downloads = [];
    private readonly List<Toast> _toasts = [];
    private DispatcherTimer? _napTimer;
    private DispatcherTimer? _saveTimer;
    private bool _omniSuppress;
    private FrameworkElement? _popupAnchor;
    private readonly Native.SubclassProc _pageClickProc;
    private static readonly IntPtr PageClickSubclassId = 1;
    private List<OmniItem> _omniItems = [];
    private readonly List<Button> _omniRows = [];
    private int _omniSel = -1;
    private List<CommandResult> _paletteResults = [];
    private List<BrowserCommand> _commands = [];
    private readonly List<Button> _paletteRows = [];
    private int _paletteSel;
    private bool _fullscreen;
    private bool _forceQuit;
    private double _popupCardWidth = 330;
    private Rect? _windowedBounds;
    private bool _wasMaximized;

    /// <summary>
    /// A private window: the engine runs an in-private (off-the-record) profile for every
    /// tab in it, nothing is written to the profile on disk, and nothing that happens here
    /// reaches history, session restore, quick tiles or the address-bar suggestions.
    /// </summary>
    private readonly bool _private;
    private bool _findStarted;
    private string _findTerm = string.Empty;
    private int _findCount;
    private int _findIndex;

    private sealed record OmniItem(string Title, string Subtitle, string Url, bool IsSearch);
    private sealed record Tile(string Url, string Title, string Icon);

    private sealed class Download
    {
        public required CoreWebView2DownloadOperation Operation { get; init; }
        public Toast? Toast { get; set; }
        public string Name { get; init; } = "download";
        public bool Finished { get; set; }
    }

    private sealed class Toast
    {
        public required UIElement Card { get; init; }
        public Rectangle? Progress { get; init; }
        public DispatcherTimer? Timer { get; set; }
    }

    public MainWindow(bool privateWindow = false)
    {
        InitializeComponent();
        DataContext = this;
        _private = privateWindow;
        _pageClickProc = OnPageWindowMessage;

        if (_private)
        {
            PrivatePill.Visibility = Visibility.Visible;
            Title = "Private — Nibble";
        }

        RestorePlacement();

        Loaded += OnLoaded;
        Closing += OnClosing;
        Deactivated += (_, _) => ClosePopups();
        SizeChanged += (_, _) =>
        {
            PositionToasts();
            UpdateTabWidths();
            if (FindPopup.IsOpen) PositionFindBar();
            // An open menu follows the window: anchoring to the shell means a resize would
            // otherwise leave it sitting wherever it was, over the wrong thing.
            if (MenuPopup.IsOpen && _popupAnchor is not null) PositionPopup(MenuPopup, _popupAnchor, _popupCardWidth);
        };
        PreviewKeyDown += OnWindowPreviewKeyDown;
        Shell.PreviewMouseDown += OnShellPreviewMouseDown;
        Omni.AddHandler(PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((_, _) => ShowSuggestions(Omni.Text)), true);

        Tabs.CollectionChanged += (_, _) => UpdateTabWidths();
        TabScroll.PreviewMouseWheel += (_, e) =>
        {
            TabScroll.ScrollToHorizontalOffset(TabScroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        };

        // A WPF popup grabs the mouse while it is open, which swallows clicks in the
        // window underneath (so the button that opened it could never close it again).
        // We handle dismissal ourselves, so drop the capture as soon as one opens.
        foreach (var popup in new[] { SuggestionsPopup, MenuPopup, PalettePopup })
        {
            popup.Opened += (_, _) =>
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Mouse.Capture(null)));
        }
    }

    // =====================================================================
    //  startup
    // =====================================================================

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Store.LoadAll();
        AdBlocker.Enabled = App.Settings.Blocker;
        Pages.Ensure();

        Native.RoundCorners(this);
        Native.SetDarkFrame(this, Theme.IsDark);

        _napTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _napTimer.Tick += (_, _) => NapIdleTabs();
        _napTimer.Start();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (_, _) =>
        {
            Store.SaveHistory();
            Store.SaveStats();
        };
        _saveTimer.Start();

        await InitEnvironmentAsync();
        RestoreOrStartFresh();
        UpdateShieldChip();
        UpdateTabWidths();

        var who = App.Settings.UserName;
        if (_private)
        {
            PrivatePill.Visibility = Visibility.Visible;
            ExplainPrivateWindow();
        }
        else
        {
            ShowToast(string.IsNullOrWhiteSpace(who) ? "Welcome to Nibble" : $"Welcome back, {who}",
                "Ctrl+K commands · Ctrl+T new tab · Ctrl+L address bar",
                Icons.Sparkle, null, 6000);
        }
    }

    private async Task InitEnvironmentAsync()
    {
        // Trim the engine down to browsing: no extensions, no sync, no shopping helpers.
        // Note: CalculateNativeWinOcclusion is deliberately NOT disabled, so the engine
        // stops compositing while its window is occluded (saves CPU/battery).
        var arguments = string.Join(' ',
            "--disable-extensions", "--disable-sync", "--no-first-run", "--no-default-browser-check",
            "--disable-background-networking", "--disable-component-update", "--disable-domain-reliability",
            "--disable-breakpad", "--disable-search-engine-choice-screen", "--disk-cache-size=268435456",
            // Privacy switches, on in every window:
            //   --no-pings                     no <a ping> hyperlink-audit beacons
            //   --dns-prefetch-disable         no speculative DNS lookups for links you never click
            //   --force-webrtc-ip-handling      WebRTC cannot hand a page your LAN address
            "--no-pings", "--dns-prefetch-disable",
            "--force-webrtc-ip-handling-policy=default_public_interface_only",
            "--disable-features=msEdgeShoppingAssistant,msEdgeIdentityIntegration,msEdgeSidebar," +
            "msEdgeAutofillAssistant,msEdgeCollections,msEdgeWorkspaces," +
            // no translate download, no Cast/mDNS device discovery on your network,
            // no background model or hint downloads
            "Translate,MediaRouter,OptimizationHints,OptimizationGuideModelDownloading,AutofillServerCommunication");

        try
        {
            // Ask the runtime directly: a clear "install this" screen beats an exception
            // three layers down when the machine has no WebView2 at all.
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString(null);

            var options = new CoreWebView2EnvironmentOptions(arguments);
            _env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(Store.DataDir, "WebView2"),
                options);
            _envReady.TrySetResult();
        }
        catch (Exception ex)
        {
            ShowEngineMissing(ex);
            _envReady.TrySetException(ex);
        }
    }

    /// <summary>Turns "the engine is not there" into something a person can act on.</summary>
    private void ShowEngineMissing(Exception ex)
    {
        Loader.IsActive = false;
        EngineErrorText.Text = ex is WebView2RuntimeNotFoundException
            ? "engine not found on this PC"
            : ex.GetType().Name.ToUpperInvariant();
        EngineMissingPanel.Visibility = Visibility.Visible;
        Store.LogError($"engine unavailable: {ex.GetType().Name}: {ex.Message}");
    }

    private void EngineInstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Store.LogError($"could not open the WebView2 download page: {ex.Message}");
        }
    }

    private void RestoreOrStartFresh()
    {
        // A private window always starts clean: no restored session, no leftover tabs.
        var session = App.Settings.RestoreSession && !_private ? Store.LoadSession() : null;
        var urls = session?.Urls
            .Where(u => !string.IsNullOrWhiteSpace(u) && !u.Contains(Pages.Host, StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList() ?? [];

        if (urls.Count == 0)
        {
            NewTab(Urls.Home, true);
            return;
        }

        var index = Math.Clamp(session!.Active, 0, urls.Count - 1);
        for (var i = 0; i < urls.Count; i++) NewTab(urls[i], i == index);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            if (!_forceQuit && !_private)
            {
                var urls = Tabs
                    .Where(t => !t.IsBroken)
                    .Select(t => t.IsNewTab ? Urls.Home : t.Url)
                    .Where(u => !u.Contains(Pages.Host, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Store.SaveSession(urls, Math.Max(0, _active is null ? 0 : Tabs.IndexOf(_active)));
                Store.SaveHistory();
                Store.SaveStats();
            }

            if (!_fullscreen) SavePlacement();
        }
        catch
        {
            // Never block shutdown.
        }

        // Let go of the engine immediately so its processes leave with us, and make sure
        // the process really ends: closing must never be something a page can stall.
        foreach (var tab in Tabs.ToList())
        {
            try
            {
                Native.StopWatchingClicks(tab.PageHandle, _pageClickProc, PageClickSubclassId);
                tab.View.Dispose();
            }
            catch
            {
                // Already gone.
            }
        }

        if (_forceQuit)
        {
            Environment.Exit(0);
            return;
        }

        // Only the last window gets to end the process. With a private window still open,
        // closing the normal one has to leave the browser running - this watchdog used to
        // be armed by every window, so closing the normal window killed the private one
        // with it about a second and a half later.
        var lastWindow = Application.Current is null ||
            !Application.Current.Windows.OfType<Window>().Any(w => !ReferenceEquals(w, this));
        if (!lastWindow) return;

        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        watchdog.Tick += (_, _) =>
        {
            watchdog.Stop();
            Environment.Exit(0);
        };
        watchdog.Start();
    }

    /// <summary>Ends the process now: no session flush, no waiting on the engine.</summary>
    private void ForceQuit()
    {
        _forceQuit = true;
        try
        {
            Close();
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    // =====================================================================
    //  tabs
    // =====================================================================

    private ZTab NewTab(string url, bool activate = true)
    {
        var view = new WebView2
        {
            Visibility = Visibility.Collapsed,
            DefaultBackgroundColor = Theme.IsDark
                ? System.Drawing.Color.FromArgb(255, 0x1C, 0x1C, 0x1E)
                : System.Drawing.Color.FromArgb(255, 0xFF, 0xFF, 0xFF)
        };

        var tab = new ZTab(view);
        ContentHost.Children.Add(view);
        Tabs.Add(tab);

        if (activate) Activate(tab);
        _ = InitTabAsync(tab, url);

        // Stretch the new tab into the strip once its container exists.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement container)
                Juice.PopIn(container, 0.06, 0, 340);
        }));

        return tab;
    }

    private async Task InitTabAsync(ZTab tab, string url)
    {
        try
        {
            await _envReady.Task;
            if (_private && _env is not null)
            {
                // A real off-the-record profile: cookies, cache, storage and history for
                // this window live in memory and are thrown away when it closes. The
                // normal profile on disk is never touched.
                var options = _env.CreateCoreWebView2ControllerOptions();
                options.IsInPrivateModeEnabled = true;
                await tab.View.EnsureCoreWebView2Async(_env, options);
            }
            else
            {
                await tab.View.EnsureCoreWebView2Async(_env);
            }
        }
        catch (Exception)
        {
            tab.Title = "Engine unavailable";
            tab.IsBroken = true;
            ShowToast("WebView2 could not start",
                "Nibble needs the Microsoft Edge WebView2 runtime, which ships with Windows 10 and 11.",
                Icons.Close, null, 9000);
            return;
        }

        Configure(tab);
        Navigate(tab, url);
    }

    private async void Configure(ZTab tab)
    {
        var core = tab.View.CoreWebView2;
        tab.Core = core;

        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = true;
        settings.AreDevToolsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = true;
        settings.IsSwipeNavigationEnabled = true;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;

        try
        {
            core.Profile.PreferredColorScheme = Theme.IsDark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;
        }
        catch
        {
            // Older runtime: pages simply follow the OS setting.
        }

        // Remember the engine's own user agent, then optionally present as plain Chrome
        // (some sites treat embedded browser engines as bots).
        tab.DefaultUserAgent = settings.UserAgent;
        try
        {
            if (App.Settings.ChromeUserAgent && tab.DefaultUserAgent is { Length: > 0 })
                settings.UserAgent = ChromeLikeUserAgent(tab.DefaultUserAgent);
        }
        catch
        {
            // Not fatal: the default agent is fine.
        }

        // (Built-in pages are loaded from file URLs; no virtual host mapping needed.)
        try { await core.AddScriptToExecuteOnDocumentCreatedAsync(Pages.BridgeScript); }
        catch { /* optional: only powers keyboard shortcuts inside pages */ }

        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

        core.WebResourceRequested += (_, e) => OnWebResourceRequested(tab, core, e);
        core.NavigationStarting += (_, e) => OnNavigationStarting(tab, core, e);
        core.SourceChanged += (_, e) => OnSourceChanged(tab, core, e);
        core.NavigationCompleted += (_, e) => OnNavigationCompleted(tab, core, e);
        core.DocumentTitleChanged += (_, e) => OnTitleChanged(tab, core, e);
        core.FaviconChanged += (_, e) => OnFaviconChanged(tab, core, e);
        core.HistoryChanged += (_, e) => OnHistoryChanged(tab, core, e);
        core.NewWindowRequested += (_, e) => OnNewWindowRequested(tab, core, e);
        core.WebMessageReceived += (_, e) => OnWebMessage(tab, core, e);
        core.DownloadStarting += (_, e) => OnDownloadStarting(tab, core, e);
        core.ProcessFailed += (_, e) => OnProcessFailed(tab, core, e);
        core.PermissionRequested += (_, e) => OnPermissionRequested(tab, core, e);
        core.ContainsFullScreenElementChanged += (_, _) => SetFullscreen(core.ContainsFullScreenElement);

        // A page must never hold the browser hostage. Closing a tab or the window is
        // unconditional: a beforeunload "leave site?" prompt is accepted for you rather
        // than blocking the close (the engine would otherwise show its own dialog).
        core.ScriptDialogOpening += (_, e) =>
        {
            if (e.Kind != CoreWebView2ScriptDialogKind.Beforeunload) return;
            e.Accept();
            Store.LogError($"bypassed a beforeunload prompt on {tab.Url}");
        };

        // Clicks on the page surface go to the engine's own window, so watch that window
        // directly: this covers clicks inside embedded frames too, where the page's
        // JavaScript bridge cannot see them.
        tab.PageHandle = tab.View.Handle;
        if (!Native.WatchClicks(tab.PageHandle, _pageClickProc, PageClickSubclassId))
            Store.LogError($"page click watch unavailable for {tab.Url}");

        // Clicking into the page hands it focus; dismiss menus in that case as well.
        tab.View.GotFocus += (_, _) => ClosePopups();
    }

    /// <summary>Closes any open menu when the user clicks or scrolls the page.</summary>
    private IntPtr OnPageWindowMessage(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        // WM_LBUTTONDOWN / WM_RBUTTONDOWN / WM_MBUTTONDOWN / WM_MOUSEWHEEL
        if (msg is 0x0201 or 0x0204 or 0x0207 or 0x020A &&
            (SuggestionsPopup.IsOpen || MenuPopup.IsOpen || PalettePopup.IsOpen))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ClosePopups()));
        }

        return Native.ContinueDefault(hWnd, msg, wParam, lParam);
    }

    private void Activate(ZTab? tab)
    {
        if (tab is null || !Tabs.Contains(tab)) return;

        foreach (var t in Tabs)
        {
            var active = ReferenceEquals(t, tab);
            t.IsActive = active;
            t.View.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (active) t.LastActive = DateTime.Now;
        }

        _active = tab;
        Wake(tab);

        _omniSuppress = true;
        Omni.Text = DisplayUrl(tab);
        _omniSuppress = false;

        UpdateSecurityIcon(tab);
        UpdateStar(tab);
        UpdateNavButtons(tab);
        UpdateShieldChip();
        HideFindBar();

        var suffix = _private ? " — Nibble (private)" : " — Nibble";
        Title = tab.Title == "New tab" ? (_private ? "Nibble (private)" : "Nibble") : tab.Title + suffix;
        if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement container)
            Juice.Pulse(container, 0.06, 320);

        tab.View.Focus();
        ScrollActiveIntoView();
    }

    private void CloseTab(ZTab? tab, bool remember = true)
    {
        if (tab is null || !Tabs.Contains(tab)) return;

        if (remember && !tab.IsNewTab && !tab.IsBroken) _closed.Push((tab.Url, tab.Title));

        if (!_private) Store.TabsClosed++;
        var index = Tabs.IndexOf(tab);
        var container = TabStrip.ItemContainerGenerator.ContainerFromItem(tab) as FrameworkElement;

        void Remove()
        {
            Tabs.Remove(tab);
            tab.View.Visibility = Visibility.Collapsed;
            ContentHost.Children.Remove(tab.View);
            Native.StopWatchingClicks(tab.PageHandle, _pageClickProc, PageClickSubclassId);
            try { tab.View.Dispose(); } catch { }

            if (Tabs.Count == 0)
            {
                NewTab(Urls.Home, true);
                return;
            }

            if (ReferenceEquals(tab, _active)) Activate(Tabs[Math.Clamp(index, 0, Tabs.Count - 1)]);

            // Follow-through: the surviving tabs settle in a little wave.
            SettleTabs();
        }

        // Squash flat before it disappears, then take it out of the strip.
        if (container is not null) Juice.SquashOut(container, Remove, 0.16, 150);
        else Remove();
    }

    /// <summary>Neighbours bounce back after the strip changes shape.</summary>
    private async void SettleTabs()
    {
        for (var i = 0; i < Tabs.Count; i++)
        {
            if (i > 0) await Task.Delay(28);
            if (TabStrip.ItemContainerGenerator.ContainerFromItem(Tabs[i]) is FrameworkElement container)
                Juice.Pulse(container, 0.05, 340);
        }
    }

    private void ReopenClosedTab()
    {
        if (_closed.Count == 0)
        {
            ShowToast("Nothing to reopen", "Tabs you close stay available for this session.",
                Icons.Reload, null, 2600);
            return;
        }

        var (url, title) = _closed.Pop();
        var tab = NewTab(url, true);
        tab.Title = title;
    }

    private void CycleTab(int delta)
    {
        if (Tabs.Count < 2 || _active is null) return;
        var index = (Tabs.IndexOf(_active) + delta + Tabs.Count) % Tabs.Count;
        Activate(Tabs[index]);
    }

    private void ScrollActiveIntoView()
    {
        try
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromItem(_active) is FrameworkElement container)
                container.BringIntoView();
        }
        catch
        {
            // Scrolling is cosmetic.
        }
    }

    private void Wake(ZTab tab)
    {
        if (tab.Core is null) return;
        try
        {
            if (tab.IsSleeping)
            {
                tab.Core.Resume();
                tab.Core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                tab.IsSleeping = false;
            }
        }
        catch
        {
            // The engine wakes up on the next navigation anyway.
        }
    }

    private async void NapIdleTabs()
    {
        var after = TimeSpan.FromSeconds(Math.Max(15, App.Settings.SuspendSeconds));
        var napped = 0;
        var before = EngineWorkingSet();

        foreach (var tab in Tabs)
        {
            if (ReferenceEquals(tab, _active) || tab.Core is null || tab.IsSleeping) continue;
            if (DateTime.Now - tab.LastActive < after) continue;

            try
            {
                tab.Core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                await tab.Core.TrySuspendAsync();
                tab.IsSleeping = tab.Core.IsSuspended;
                if (tab.IsSleeping) napped++;
            }
            catch
            {
                // Best effort only.
            }
        }

        if (napped == 0) return;

        // Let the engine actually release the renderers before measuring, so the number we
        // show is observed rather than guessed.
        await Task.Delay(1200);
        var freed = Math.Max(0, before - EngineWorkingSet());
        Store.MemoryFreed += freed;
        Store.MemoryFreedSession += freed;

        if (freed < 32L * 1024 * 1024) return;
        ShowToast($"Nibble freed {Urls.FormatBytes(freed)}",
            $"by sleeping {napped} tab{(napped == 1 ? "" : "s")}  \u00b7  {Store.MemoryFreedSession / (1024 * 1024):N0} MB this session",
            Icons.Sparkle, null, 4200);
    }

    /// <summary>Private memory of the engine processes this browser owns.</summary>
    private long EngineWorkingSet()
    {
        if (_env is null) return 0;
        long total = 0;
        try
        {
            foreach (var info in _env.GetProcessInfos())
            {
                try { total += System.Diagnostics.Process.GetProcessById(info.ProcessId).PrivateMemorySize64; }
                catch { /* process may have exited */ }
            }
        }
        catch
        {
            return 0;
        }
        return total;
    }

    // =====================================================================
    //  navigation
    // =====================================================================

    private static string DisplayUrl(ZTab tab) => tab.IsNewTab ? string.Empty : tab.Url;

    private static bool IsErrorPage(string uri) => Pages.IsErrorPage(uri);

    private void Navigate(ZTab tab, string input)
    {
        var target = Urls.Resolve(input, App.Settings.SearchEngine);
        if (target.Equals(Urls.Home, StringComparison.OrdinalIgnoreCase))
        {
            tab.Url = Urls.Home;
            tab.IsBroken = false;
            tab.Core?.Navigate(Pages.NewTabPage);
            return;
        }

        tab.Url = target;
        tab.IsBroken = false;
        tab.Core?.Navigate(target);
    }

    private void SearchNow(ZTab tab, string query)
    {
        var engine = Urls.EngineFor(App.Settings.SearchEngine);
        Navigate(tab, string.Format(engine.Query, Uri.EscapeDataString(query)));
    }

    private void OnNavigationStarting(ZTab tab, CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        tab.IsLoading = true;
        // Inline fallbacks land on about:blank; those must not clear the broken state.
        if (!IsErrorPage(e.Uri) && !e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            tab.IsBroken = false;
        ClosePopups();

        if (ReferenceEquals(tab, _active))
        {
            Loader.IsActive = true;
            UpdateNavButtons(tab);
        }
    }

    private void OnSourceChanged(ZTab tab, CoreWebView2 sender, CoreWebView2SourceChangedEventArgs e)
    {
        var uri = sender.Source;
        if (Pages.IsNewTabPage(uri)) tab.Url = Urls.Home;
        else if (!IsErrorPage(uri) && !uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) tab.Url = uri;

        if (!ReferenceEquals(tab, _active)) return;

        if (!Omni.IsKeyboardFocusWithin)
        {
            _omniSuppress = true;
            Omni.Text = DisplayUrl(tab);
            _omniSuppress = false;
        }

        UpdateSecurityIcon(tab);
        UpdateStar(tab);
    }

    private async void OnNavigationCompleted(ZTab tab, CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        tab.IsLoading = false;
        if (ReferenceEquals(tab, _active)) Loader.IsActive = false;

        if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
        {
            var failed = tab.Url;
            var onErrorPage = IsErrorPage(sender.Source);
            Store.LogError($"navigation failed: {e.WebErrorStatus} url={failed} source={sender.Source}");

            // Our own pages must never bounce back into themselves: if the virtual host
            // can't serve them (or we are already showing the error page), render the
            // built-in page inline instead.
            if (onErrorPage || failed.Contains(Pages.Host, StringComparison.OrdinalIgnoreCase))
            {
                FallbackToInlinePage(sender, failed, e.WebErrorStatus);
                tab.IsBroken = true;
                if (ReferenceEquals(tab, _active)) Loader.IsActive = false;
                UpdateNavButtons(tab);
                return;
            }

            // If the engine actually rendered something, that beats our error page.
            if (await HasRenderedContentAsync(sender))
            {
                tab.IsBroken = false;
                ShowToast("That page complained",
                    $"{Urls.PrettyHost(failed)} reported a problem, but part of it loaded.",
                    Icons.Sparkle, null, 4200);
                UpdateNavButtons(tab);
                return;
            }

            tab.IsBroken = true;
            tab.Title = "This page didn't load";
            sender.Navigate(Pages.ErrorUrl(failed, FailureReason(failed, e.WebErrorStatus), e.WebErrorStatus.ToString()));
            return;
        }

        // Private windows deliberately write no history and no page counters.
        if (!_private && !tab.IsNewTab && !tab.IsBroken && !IsErrorPage(sender.Source) &&
            !sender.Source.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            Store.Record(tab.Url, string.IsNullOrWhiteSpace(tab.Title) ? Urls.PrettyHost(tab.Url) : tab.Title);
            Store.PagesVisited++;
        }

        UpdateNavButtons(tab);
        if (ReferenceEquals(tab, _active)) UpdateShieldChip();
    }

    /// <summary>Renders a built-in page from memory when it can't be served from disk.</summary>
    private static void FallbackToInlinePage(CoreWebView2 core, string failed, CoreWebView2WebErrorStatus status)
    {
        try
        {
            var isNewTab = failed.Equals(Urls.Home, StringComparison.OrdinalIgnoreCase) ||
                           failed.Contains($"{Pages.Host}/newtab", StringComparison.OrdinalIgnoreCase);

            core.NavigateToString(isNewTab
                ? Pages.NewTabHtml
                : Pages.ErrorHtml(failed, FailureReason(failed, status), status.ToString()));

            Store.LogError($"built-in page rendered inline ({failed}, {status})");
        }
        catch (Exception ex)
        {
            Store.LogError($"inline fallback failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>True when the failed document still has readable text on screen.</summary>
    private static async Task<bool> HasRenderedContentAsync(CoreWebView2 core)
    {
        try
        {
            var raw = await core.ExecuteScriptAsync(
                "(function(){var b=document.body;if(!b)return 0;return ((b.innerText||'').length);})()");
            return int.TryParse(raw?.Trim().Trim('"'), out var length) && length > 40;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Turns an engine error into something a person can act on.</summary>
    private static string FailureReason(string url, CoreWebView2WebErrorStatus status)
    {
        var lower = url.ToLowerInvariant();
        if (lower.Contains("/sorry/") || lower.Contains("captcha") || lower.Contains("/challenge") ||
            lower.Contains("cdn-cgi/challenge") || lower.Contains("hcaptcha") || lower.Contains("recaptcha"))
        {
            return "This site asked the browser to prove you're human, and that verification page didn't load. " +
                   "Try again, or open the address in your usual browser.";
        }

        return status switch
        {
            CoreWebView2WebErrorStatus.HostNameNotResolved => "That address doesn't exist. Check the spelling and try again.",
            CoreWebView2WebErrorStatus.Disconnected => "You're offline. Reconnect, then try again.",
            CoreWebView2WebErrorStatus.Timeout => "The site took too long to answer.",
            CoreWebView2WebErrorStatus.CannotConnect => "The site refused the connection.",
            CoreWebView2WebErrorStatus.ConnectionAborted => "The connection was cut off before the page arrived.",
            CoreWebView2WebErrorStatus.ConnectionReset => "The connection was reset before the page arrived.",
            CoreWebView2WebErrorStatus.ServerUnreachable => "The server could not be reached.",
            CoreWebView2WebErrorStatus.RedirectFailed => "The site's redirect went in circles.",
            CoreWebView2WebErrorStatus.CertificateExpired => "The site's security certificate has expired.",
            CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect => "The site's certificate doesn't match this address.",
            CoreWebView2WebErrorStatus.CertificateIsInvalid => "The site's security certificate isn't valid.",
            CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired => "This address needs a username and password.",
            CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired => "Your proxy needs a username and password.",
            _ => "The connection dropped before the page arrived. Give it another try in a moment."
        };
    }

    /// <summary>Strips the engine's "Edg/…" marker so the agent reads as plain Chrome.</summary>
    private static string ChromeLikeUserAgent(string engineUserAgent)
    {
        var chrome = System.Text.RegularExpressions.Regex
            .Replace(engineUserAgent, @"\s*Edg/[^\s]+", string.Empty)
            .Replace("  ", " ")
            .Trim();
        return chrome.Contains("Chrome/", StringComparison.Ordinal)
            ? chrome
            : engineUserAgent;
    }

    private void OnTitleChanged(ZTab tab, CoreWebView2 sender, object e)
    {
        if (tab.IsBroken) return;

        var title = sender.DocumentTitle;
        tab.Title = string.IsNullOrWhiteSpace(title) ? Urls.PrettyHost(tab.Url) : title.Trim();
        if (ReferenceEquals(tab, _active))
            Title = _private ? $"{tab.Title} — Nibble (private)" : $"{tab.Title} — Nibble";
    }

    private async void OnFaviconChanged(ZTab tab, CoreWebView2 sender, object e)
    {
        try
        {
            var uri = sender.FaviconUri;
            if (!string.IsNullOrEmpty(uri) && _faviconCache.TryGetValue(uri, out var cached))
            {
                tab.Favicon = cached;
                tab.HasFavicon = cached is not null;
                return;
            }

            using var stream = await sender.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream is null)
            {
                Store.LogError($"favicon: engine returned no image for {tab.Url}");
                return;
            }

            // The engine hands back a forward-only stream that WPF cannot decode in place
            // (it would stay "downloading" and Freeze() throws), so buffer it first.
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = buffer;
            bitmap.EndInit();
            try { bitmap.Freeze(); } catch { /* decodable but not freezable; still usable */ }

            if (!string.IsNullOrEmpty(uri)) _faviconCache[uri] = bitmap;
            tab.Favicon = bitmap;
            tab.HasFavicon = true;
        }
        catch (Exception ex)
        {
            Store.LogError($"favicon failed for {tab.Url}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnHistoryChanged(ZTab tab, CoreWebView2 sender, object e)
    {
        if (ReferenceEquals(tab, _active)) UpdateNavButtons(tab);
    }

    private void OnNewWindowRequested(ZTab tab, CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        NewTab(e.Uri, true);
    }

    private void OnProcessFailed(ZTab tab, CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        tab.IsBroken = true;
        tab.Title = "This page stopped";
        Store.LogError($"process failed: {e.ProcessFailedKind} url={tab.Url}");
        try
        {
            sender.Navigate(Pages.ErrorUrl(tab.Url,
                "The part of the browser that draws this page stopped. Reloading usually brings it back.",
                e.ProcessFailedKind.ToString()));
        }
        catch { /* nothing else to do */ }
    }

    private void OnDownloadStarting(ZTab tab, CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var operation = e.DownloadOperation;
        var name = "download";
        try { name = Path.GetFileName(operation.ResultFilePath); } catch { }

        var download = new Download { Operation = operation, Name = name };
        download.Toast = ShowToast("Downloading", name, Icons.Download, null, 0, showProgress: true);
        _downloads.Insert(0, download);

        void Update()
        {
            if (download.Toast?.Progress is not { } bar) return;
            var total = operation.TotalBytesToReceive ?? 0UL;
            var ratio = total > 0 ? Math.Clamp(operation.BytesReceived / (double)total, 0.0, 1.0) : 0.06;
            bar.Width = 262 * ratio;
        }

        operation.BytesReceivedChanged += (_, _) => Update();
        operation.StateChanged += (_, _) =>
        {
            Update();
            if (operation.State == CoreWebView2DownloadState.Completed)
            {
                download.Finished = true;
                RemoveToast(download.Toast);
                ShowToast("Downloaded", name, Icons.Download,
                    () => OpenPath(operation.ResultFilePath), 6500);
            }
            else if (operation.State == CoreWebView2DownloadState.Interrupted)
            {
                download.Finished = true;
                RemoveToast(download.Toast);
                ShowToast("Download failed", name, Icons.Close, null, 5000);
            }
        };
    }

    private void OnWebResourceRequested(ZTab tab, CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var context = e.ResourceContext.ToString();
        var uri = e.Request.Uri;
        var topHost = tab.IsNewTab ? null : Urls.PrettyHost(tab.Url);
        if (!AdBlocker.ShouldBlock(uri, context, topHost)) return;

        try
        {
            var saved = AdBlocker.EstimateSavedBytes(context);
            tab.BlockedCount++;
            tab.SavedBytes += saved;
            Store.BlockedTotal++;
            Store.SavedBytesTotal += saved;

            var host = new Uri(uri).Host;
            if (tab.BlockedHosts.Count < 40 && !tab.BlockedHosts.Contains(host)) tab.BlockedHosts.Add(host);

            if (ReferenceEquals(tab, _active)) UpdateShieldChip();

            e.Response = sender.Environment.CreateWebResourceResponse(
                new MemoryStream(), 200, "OK", "Content-Type: text/plain");
        }
        catch
        {
            // If blocking fails, let the request through untouched.
        }
    }

    // =====================================================================
    //  page -> shell messages
    // =====================================================================

    private void OnWebMessage(ZTab tab, CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try { raw = e.WebMessageAsJson; }
        catch { return; }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeElement)) return;

            string Text(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() ?? string.Empty
                    : string.Empty;

            switch (typeElement.GetString())
            {
                case "ready":
                    SendInit(tab);
                    break;
                case "query":
                    SendPageSuggestions(tab, Text("text"));
                    break;
                case "navigate":
                    Navigate(tab, Text("text"));
                    break;
                case "search":
                    SearchNow(tab, Text("text"));
                    break;
                case "open":
                case "retry":
                    Navigate(tab, Text("url"));
                    break;
                case "external":
                    OpenPath(Text("url"));
                    break;
                case "click":
                    ClosePopups();
                    break;
                case "copy":
                    try
                    {
                        Clipboard.SetText(Text("url"));
                        ShowToast("Address copied", Text("url"), Icons.Check, null, 2200);
                    }
                    catch
                    {
                        // The clipboard can be busy; nothing else to do.
                    }
                    break;
                case "back":
                    GoBack(tab);
                    break;
                case "escape":
                    tab.Core?.Stop();
                    Loader.IsActive = false;
                    break;
                case "key":
                    TryShortcut(Text("key"),
                        root.TryGetProperty("ctrl", out var c) && c.ValueKind == JsonValueKind.True,
                        root.TryGetProperty("shift", out var s) && s.ValueKind == JsonValueKind.True,
                        root.TryGetProperty("alt", out var a) && a.ValueKind == JsonValueKind.True);
                    break;
            }
        }
        catch
        {
            // Unknown message shapes are ignored on purpose.
        }
    }

    private string Version =>
        typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "1.0";

    private void SendInit(ZTab tab)
    {
        if (tab.Core is null) return;
        var payload = new
        {
            Type = "init",
            Dark = Theme.IsDark,
            Blocked = Store.BlockedTotal,
            SavedText = Urls.FormatBytes(Store.SavedBytesTotal),
            Launches = Store.Launches,
            Version,
            Engine = Urls.EngineFor(App.Settings.SearchEngine).Name,
            Accent = Accent.CurrentHex,
            AccentSoft = Accent.SoftHex,
            AccentInk = Accent.InkHex,
            Name = App.Settings.UserName,
            Clock24 = ClockFormat.Is24Hour(App.Settings.Clock),
            Private = _private,
            Theme = Themes.PageMessage(),
            Tiles = QuickTiles()
        };
        tab.Core.PostWebMessageAsJson(JsonSerializer.Serialize(payload, PageJson));
    }

    private void BroadcastInit()
    {
        foreach (var tab in Tabs)
        {
            try
            {
                if (tab.IsNewTab) SendInit(tab);
            }
            catch
            {
                // Ignore tabs that are mid-teardown.
            }
        }
    }

    private List<Tile> QuickTiles()
    {
        // A private window does not advertise where you have been: no tiles at all.
        if (_private) return [];

        var tiles = new List<Tile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bookmark in Store.Bookmarks)
        {
            if (tiles.Count >= 6) break;
            if (seen.Add(bookmark.Url)) tiles.Add(MakeTile(bookmark.Url, bookmark.Title));
        }

        foreach (var entry in Store.History)
        {
            if (tiles.Count >= 6) break;
            if (entry.Url.Contains(Pages.Host, StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(entry.Url)) tiles.Add(MakeTile(entry.Url, entry.Title));
        }

        return tiles;
    }

    private static Tile MakeTile(string url, string title) =>
        new(url, string.IsNullOrWhiteSpace(title) ? Urls.PrettyHost(url) : title, IconFor(url));

    private static string IconFor(string url)
    {
        var host = Urls.PrettyHost(url).ToLowerInvariant();
        if (host.Contains("youtu")) return "play";
        if (host.Contains("wikipedia")) return "globe";
        if (host.Contains("github") || host.Contains("gitlab") || host.Contains("stackoverflow")) return "code";
        if (host.Contains("ycombinator") || host.Contains("news")) return "bolt";
        if (host.Contains("reddit") || host.Contains("twitter") || host == "x.com") return "chat";
        if (host.Contains("spotify") || host.Contains("soundcloud") || host.Contains("music")) return "music";
        if (host.Contains("map")) return "pin";
        return "globe";
    }

    private void SendPageSuggestions(ZTab tab, string text)
    {
        if (tab.Core is null) return;

        var engine = Urls.EngineFor(App.Settings.SearchEngine);
        var items = new List<object>();
        text = text.Trim();

        if (text.Length > 0 && !Urls.LooksLikeAddress(text))
            items.Add(new { Kind = "search", Title = $"Search {engine.Name}", Subtitle = text, Url = string.Empty });

        // Private windows never suggest from your history: the page would learn where you
        // have been even if the browser itself keeps no record.
        if (!_private)
        {
            foreach (var entry in Store.Suggest(text, 5))
                items.Add(new
                {
                    Kind = "history",
                    Title = string.IsNullOrWhiteSpace(entry.Title) ? Urls.PrettyHost(entry.Url) : entry.Title,
                    Subtitle = Urls.PrettyHost(entry.Url),
                    Url = entry.Url
                });
        }

        tab.Core.PostWebMessageAsJson(JsonSerializer.Serialize(new { Type = "suggestions", Items = items }, PageJson));
    }

    /// <summary>Used by <see cref="Themes.Apply"/>, which lives outside this window.</summary>
    public void SendThemeToPages() => SendTheme();

    private void SendTheme()
    {
        var json = JsonSerializer.Serialize(new
        {
            Type = "theme",
            Dark = Theme.IsDark,
            Accent = Accent.CurrentHex,
            AccentSoft = Accent.SoftHex,
            AccentInk = Accent.InkHex,
            Name = App.Settings.UserName,
            Clock24 = ClockFormat.Is24Hour(App.Settings.Clock),
            Theme = Themes.PageMessage()
        }, PageJson);
        foreach (var tab in Tabs)
        {
            try
            {
                tab.Core?.PostWebMessageAsJson(json);
                if (tab.Core is not null)
                {
                    tab.Core.Profile.PreferredColorScheme = Theme.IsDark
                        ? CoreWebView2PreferredColorScheme.Dark
                        : CoreWebView2PreferredColorScheme.Light;
                }

                tab.View.DefaultBackgroundColor = Theme.IsDark
                    ? System.Drawing.Color.FromArgb(255, 0x1C, 0x1C, 0x1E)
                    : System.Drawing.Color.FromArgb(255, 0xFF, 0xFF, 0xFF);
            }
            catch
            {
                // Ignore per-tab failures.
            }
        }
    }

    // =====================================================================
    //  omnibox
    // =====================================================================

    private void UpdateSecurityIcon(ZTab tab)
    {
        var url = tab.Url;
        var https = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var http = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        SecIcon.Data = https || http ? Icons.Lock : Icons.Sparkle;
        // Use resource references (not resolved brushes) so icons re-tint with the theme.
        SecIcon.SetResourceReference(VectorIcon.ForegroundProperty, https ? "Accent" : http ? "Amber" : "Ink3");
        SecIcon.ToolTip = https
            ? "Encrypted connection (https)"
            : http
                ? "Not encrypted (http)"
                : "Nibble";
    }

    private void UpdateStar(ZTab tab)
    {
        var saved = Store.IsBookmarked(tab.Url);
        StarIcon.SetResourceReference(VectorIcon.ForegroundProperty, saved ? "Accent" : "Ink2");
        if (saved) Juice.Pulse(StarIcon, 0.35, 460);
        StarButton.ToolTip = saved ? "Remove bookmark (Ctrl+D)" : "Bookmark this page (Ctrl+D)";
    }

    private void UpdateNavButtons(ZTab tab)
    {
        var core = tab.Core;
        BackButton.IsEnabled = core?.CanGoBack ?? false;
        ForwardButton.IsEnabled = core?.CanGoForward ?? false;
    }

    private void UpdateShieldChip()
    {
        ShieldCount.Text = _active is null ? "0" : _active.BlockedCount.ToString();
        ShieldButton.ToolTip = _active is null
            ? "Tracker shield"
            : $"Tracker shield — {_active.BlockedCount} blocked here · about {Urls.FormatBytes(_active.SavedBytes)} saved";
    }

    private void ShowSuggestions(string text)
    {
        var engine = Urls.EngineFor(App.Settings.SearchEngine);
        var items = new List<OmniItem>();
        text = text.Trim();

        if (text.Length == 0)
        {
            if (!_private)
            {
                items.AddRange(Store.History.Take(6).Select(h =>
                    new OmniItem(string.IsNullOrWhiteSpace(h.Title) ? Urls.PrettyHost(h.Url) : h.Title,
                        Urls.PrettyHost(h.Url), h.Url, false)));
            }
        }
        else
        {
            items.Add(Urls.LooksLikeAddress(text)
                ? new OmniItem($"Go to {text}", "open the address", Urls.Resolve(text, App.Settings.SearchEngine), false)
                : new OmniItem($"Search {engine.Name}", text, Urls.Resolve(text, App.Settings.SearchEngine), true));

            if (!_private)
            {
                items.AddRange(Store.Suggest(text, 6)
                    .Where(h => !h.Url.Equals(text, StringComparison.OrdinalIgnoreCase))
                    .Select(h => new OmniItem(
                        string.IsNullOrWhiteSpace(h.Title) ? Urls.PrettyHost(h.Url) : h.Title,
                        Urls.PrettyHost(h.Url), h.Url, false)));
            }
        }

        _omniItems = items;
        _omniRows.Clear();
        _omniSel = -1;
        SuggestionList.Children.Clear();

        foreach (var item in items)
        {
            var row = new Button
            {
                Style = (Style)FindResource("PopupRowButton"),
                Padding = new Thickness(10, 8, 10, 8)
            };

            AutomationProperties.SetName(row, item.Subtitle.Length > 0
                ? $"{item.Title}, {item.Subtitle}"
                : item.Title);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new VectorIcon
            {
                Data = item.IsSearch ? Icons.Search : Icons.Sparkle,
                Size = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            icon.SetResourceReference(VectorIcon.ForegroundProperty, "Ink3");

            var title = new TextBlock
            {
                Text = item.Title,
                FontSize = 13,
                Foreground = (Brush)FindResource("Ink"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            var subtitle = new TextBlock
            {
                Text = item.Subtitle,
                FontSize = 11,
                Foreground = (Brush)FindResource("Ink3"),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 220,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Grid.SetColumn(icon, 0);
            Grid.SetColumn(title, 1);
            Grid.SetColumn(subtitle, 2);
            grid.Children.Add(icon);
            grid.Children.Add(title);
            grid.Children.Add(subtitle);

            row.Content = grid;
            var captured = item;
            row.Click += (_, _) => CommitSuggestion(captured);

            _omniRows.Add(row);
            SuggestionList.Children.Add(row);
        }

        if (items.Count == 0)
        {
            HideSuggestions();
            return;
        }

        HighlightOmni();
        SuggestionsPopup.Width = Math.Max(440, OmniFrame.ActualWidth);
        SuggestionsPopup.IsOpen = true;
        if (SuggestionsPopup.Child is FrameworkElement card) Juice.PopIn(card, 0.045, -5, 300);
    }

    private void HighlightOmni()
    {
        var selected = (Brush)FindResource("AccentSoft");
        for (var i = 0; i < _omniRows.Count; i++)
            _omniRows[i].Background = i == _omniSel ? selected : Brushes.Transparent;
    }

    private void HideSuggestions()
    {
        SuggestionsPopup.IsOpen = false;
        _omniItems = [];
        _omniRows.Clear();
        _omniSel = -1;
    }

    private void CommitSuggestion(OmniItem item)
    {
        HideSuggestions();
        if (_active is null) return;

        if (item.IsSearch) SearchNow(_active, item.Subtitle);
        else Navigate(_active, item.Url);

        _active.View.Focus();
    }

    private void CommitOmniboxText()
    {
        if (_active is null) return;
        var text = Omni.Text.Trim();
        HideSuggestions();
        if (text.Length == 0) return;
        Navigate(_active, text);
        _active.View.Focus();
    }

    private void FocusOmnibox()
    {
        _omniSuppress = true;
        if (_active is not null) Omni.Text = DisplayUrl(_active);
        _omniSuppress = false;

        Omni.Focus();
        Omni.SelectAll();
        ShowSuggestions(Omni.Text);
    }

    // =====================================================================
    //  shortcuts (shared by WPF focus and the in-page bridge)
    // =====================================================================

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (TryShortcut(KeyName(key), ctrl, shift, alt)) e.Handled = true;
    }

    private static string KeyName(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9) return ((int)key - (int)Key.D0).ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return ((int)key - (int)Key.NumPad0).ToString();
        return key switch
        {
            Key.OemPlus or Key.Add => "=",
            Key.OemMinus or Key.Subtract => "-",
            _ => key.ToString().ToLowerInvariant()
        };
    }

    private bool TryShortcut(string rawKey, bool ctrl, bool shift, bool alt)
    {
        var key = rawKey.ToLowerInvariant();

        switch (key)
        {
            case "t" when ctrl && shift: ReopenClosedTab(); return true;
            case "n" when ctrl && shift: NewPrivateWindow(); return true;
            case "t" when ctrl: NewTab(Urls.Home, true); return true;
            case "n" when ctrl: NewTab(Urls.Home, true); return true;
            case "w" when ctrl: CloseTab(_active); return true;
            case "l" when ctrl: FocusOmnibox(); return true;
            case "d" when ctrl: ToggleBookmark(); return true;
            case "k" when ctrl: OpenPalette(); return true;
            case "r" when ctrl: ReloadActive(); return true;
            case "f" when ctrl: ShowFindBar(); return true;
            case "p" when ctrl: PrintPage(); return true;
            case "f3": RunFind(forward: !(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))); return true;
            case "tab" when ctrl && shift: CycleTab(-1); return true;
            case "tab" when ctrl: CycleTab(1); return true;
            case "f5": ReloadActive(); return true;
            case "f6": FocusOmnibox(); return true;
            case "f11": SetFullscreen(!_fullscreen); return true;
            case "escape":
                if (FindPopup.IsOpen)
                {
                    HideFindBar();
                    return true;
                }
                if (_fullscreen)
                {
                    SetFullscreen(false);
                    return true;
                }
                _active?.Core?.Stop();
                Loader.IsActive = false;
                ClosePopups();
                return true;
            case "arrowleft" when alt: GoBack(_active); return true;
            case "arrowright" when alt: GoForward(_active); return true;
            case "=" or "+" when ctrl: Zoom(0.1); return true;
            case "-" when ctrl: Zoom(-0.1); return true;
            case "0" when ctrl: Zoom(0, reset: true); return true;
        }

        if (ctrl && key.Length == 1 && char.IsDigit(key[0]))
        {
            var index = key[0] == '9' ? Tabs.Count - 1 : key[0] - '1';
            if (index >= 0 && index < Tabs.Count)
            {
                Activate(Tabs[index]);
                return true;
            }
        }

        return false;
    }

    private void SetFullscreen(bool on)
    {
        if (_fullscreen == on) return;
        _fullscreen = on;

        RowHeader.Height = new GridLength(on ? 0 : 42);
        RowTools.Height = new GridLength(on ? 0 : 50);
        RowLoader.Height = new GridLength(on ? 0 : 3);

        var hwnd = new WindowInteropHelper(this).Handle;

        if (on)
        {
            _wasMaximized = WindowState == WindowState.Maximized;
            _windowedBounds = new Rect(Left, Top, Width, Height);

            // Fill the monitor itself (taskbar included); fall back to maximizing.
            WindowState = WindowState.Normal;
            if (!Native.CoverMonitor(hwnd)) WindowState = WindowState.Maximized;
            return;
        }

        if (_wasMaximized)
        {
            WindowState = WindowState.Maximized;
        }
        else if (_windowedBounds is { } bounds)
        {
            WindowState = WindowState.Normal;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        _windowedBounds = null;
    }

    // =====================================================================
    //  popups
    // =====================================================================

    /// <summary>Squashes the open menus out of view, then closes them.</summary>
    private void ClosePopups(bool animate = true)
    {
        _popupAnchor = null;

        foreach (var popup in new[] { SuggestionsPopup, MenuPopup, PalettePopup })
        {
            if (!popup.IsOpen) continue;

            if (!animate || popup.Child is not FrameworkElement card)
            {
                popup.IsOpen = false;
                continue;
            }

            var target = popup;
            Juice.SquashOut(card, () => target.IsOpen = false, 0.08, 130);
        }
    }

    private void OpenPopup(Popup popup, FrameworkElement target, double cardWidth = 330)
    {
        _popupAnchor = target;
        _popupCardWidth = cardWidth;
        // Anchored to the shell rather than to the button so a right-hand popup
        // always stays inside the window instead of hanging off the edge.
        popup.PlacementTarget = Shell;
        popup.Placement = PlacementMode.Relative;
        popup.Width = cardWidth + 32;

        PositionPopup(popup, target, cardWidth);
        popup.IsOpen = true;

        // The anchor is measured before the popup exists, so a window that has only just
        // changed size can leave it placed against a stale layout - measured once at 6 px
        // over its own button's bottom edge. Re-running after everything has laid out makes
        // that impossible instead of rare.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!popup.IsOpen) return;
            PositionPopup(popup, target, cardWidth);
        }));

        if (popup.Child is FrameworkElement card) Juice.PopIn(card, 0.045, -6, 320);
    }

    /// <summary>Where an open popup sits: below its button, never over it.</summary>
    private void PositionPopup(Popup popup, FrameworkElement target, double cardWidth)
    {
        var origin = target.TransformToAncestor(Shell).Transform(new Point(0, 0));
        var rightAligned = origin.X + target.ActualWidth - cardWidth - 16;
        var limit = Math.Max(8, Shell.ActualWidth - cardWidth - 24);

        popup.HorizontalOffset = Math.Clamp(rightAligned, 8, limit);

        // The popup window is bigger than its card: the card carries a margin so its drop
        // shadow has room. Dropping the window onto the anchor button put that *invisible*
        // margin over the button - the pointer over the bottom half of the hamburger then
        // belonged to the popup window instead of the button, so the hover state dropped
        // out, the cursor flipped between the button's hand and the popup's arrow, and the
        // button could not be clicked to close the menu it had opened. Start the window at
        // the anchor's bottom edge: card top = bottom + 8 (the card's own top margin), with
        // nothing of the popup over the button.
        popup.VerticalOffset = origin.Y + target.ActualHeight + PopupShadowInset;

        // A long menu gets its own scroll area so it always fits *inside the window*. If it
        // did not fit, Windows would slide the popup back up to stay on screen - straight
        // over the button again. The 90 is the fixed part of the card around the list
        // (title, paddings, the card's own shadow margin), plus a little slack.
        MenuScroll.MaxHeight = Math.Max(120, Shell.ActualHeight - popup.VerticalOffset - 90);

        popup.HorizontalOffset = SnapToPixel(popup.HorizontalOffset);
        popup.VerticalOffset = SnapToPixel(popup.VerticalOffset);
    }

    /// <summary>
    /// Rounds a coordinate to a whole device pixel. Popups are their own visual tree, so
    /// the window's layout rounding never reaches them: without this, a card can land on
    /// a half pixel and its text renders soft or doubled.
    /// </summary>
    private double SnapToPixel(double value)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) return Math.Round(value);
        return Math.Round(value * scale) / scale;
    }

    /// <summary>Clicks anywhere outside an open menu close it, like every other browser.</summary>
    private void OnShellPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!SuggestionsPopup.IsOpen && !MenuPopup.IsOpen && !PalettePopup.IsOpen) return;

        // Clicks inside a popup arrive in that popup's own window, so anything reaching
        // here is outside it — except the button that opened it, which toggles instead.
        if (_popupAnchor is not null && Contains(_popupAnchor, e.GetPosition(this))) return;

        ClosePopups();
    }

    /// <summary>True when this anchor already owns the open menu, so clicking it closes.</summary>
    private bool PopupIsOpenFor(FrameworkElement anchor) =>
        MenuPopup.IsOpen && ReferenceEquals(_popupAnchor, anchor);

    private Button PopupRow(string? icon, string label, string hint, Action? action, bool selected = false, bool danger = false)
    {
        var row = new Button
        {
            Style = (Style)FindResource("PopupRowButton"),
            Padding = new Thickness(10, 8, 10, 8)
        };

        // The row's content is a panel of icons and text, so without this a screen reader
        // (and Windows UI Automation) would see an unnamed button.
        AutomationProperties.SetName(row, hint.Length > 0 ? $"{label}, {hint}" : label);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new VectorIcon
        {
            Data = selected ? Icons.Check : icon ?? Icons.Sparkle,
            Size = selected ? 17 : 16,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        glyph.SetResourceReference(VectorIcon.ForegroundProperty, danger ? "Pink" : selected ? "Accent" : "Ink2");

        var text = new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            Foreground = (Brush)FindResource(danger ? "Pink" : "Ink"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var hintText = new TextBlock
        {
            Text = hint,
            FontSize = 11,
            Foreground = (Brush)FindResource("Ink3"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 175
        };

        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(hintText, 2);
        grid.Children.Add(glyph);
        grid.Children.Add(text);
        grid.Children.Add(hintText);

        row.Content = grid;
        if (action is not null) row.Click += (_, _) => action();
        return row;
    }

    private void Separator(Panel host, ResourceDictionary _) =>
        host.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)FindResource("Hairline"),
            Margin = new Thickness(8, 6, 8, 6)
        });

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (PopupIsOpenFor(MenuButton))
        {
            ClosePopups();
            return;
        }
        OpenMainMenu();
    }

    private void OpenMainMenu()
    {
        MenuTitle.Text = "NIBBLE · MENU";
        MenuList.Children.Clear();

        MenuList.Children.Add(PopupRow(Icons.Plus, "New tab", "Ctrl+T", () => { ClosePopups(); NewTab(Urls.Home, true); }));
        MenuList.Children.Add(PopupRow(Icons.Shield, "New private window",
            "Ctrl+Shift+N · nothing is saved", () => NewPrivateWindow()));
        MenuList.Children.Add(PopupRow(Icons.Sparkle, "Command palette", "Ctrl+K", () => { ClosePopups(); OpenPalette(); }));
        MenuList.Children.Add(PopupRow(Icons.Reload, "Reopen closed tab", "Ctrl+Shift+T", () => { ClosePopups(); ReopenClosedTab(); }));
        MenuList.Children.Add(PopupRow(Icons.Search, "Find in page", "Ctrl+F", () => { ClosePopups(); ShowFindBar(); }));
        MenuList.Children.Add(PopupRow(Icons.Copy, "Print", "Ctrl+P", () => { ClosePopups(); PrintPage(); }));
        MenuList.Children.Add(PopupRow(Icons.Star, "Bookmark this page", "Ctrl+D", () => { ClosePopups(); ToggleBookmark(); }));
        MenuList.Children.Add(PopupRow(Icons.Lock, "Copy address", "", () =>
        {
            ClosePopups();
            if (_active is not null && !_active.IsNewTab) Clipboard.SetText(_active.Url);
        }));

        Separator(MenuList, Resources);
        MenuList.Children.Add(PopupRow(Icons.Palette, "Personalize Nibble…",
            $"color · name · {Urls.EngineFor(App.Settings.SearchEngine).Name}", () =>
            {
                ClosePopups();
                OpenPersonalize();
            }));
        MenuList.Children.Add(PopupRow(Icons.Theme, "Light theme", "", () => { ClosePopups(); SetTheme("Light"); },
            App.Settings.Theme == "Light"));
        MenuList.Children.Add(PopupRow(Icons.Theme, "Dark theme", "", () => { ClosePopups(); SetTheme("Dark"); },
            App.Settings.Theme == "Dark"));
        MenuList.Children.Add(PopupRow(Icons.Theme, "Match Windows", "", () => { ClosePopups(); SetTheme("System"); },
            App.Settings.Theme == "System"));
        MenuList.Children.Add(PopupRow(Icons.Theme, $"Clock: {ClockWord()}", "click to switch",
            () => { ClosePopups(); SetClock(ClockFormat.Next(App.Settings.Clock)); }));
        MenuList.Children.Add(PopupRow(Icons.Palette, "Theme shop\u2026",
            Themes.Current?.Name ?? "built-in themes", () =>
            {
                ClosePopups();
                OpenThemeShop();
            }));

        Separator(MenuList, Resources);
        foreach (var engine in Urls.Engines)
        {
            var chosen = engine;
            MenuList.Children.Add(PopupRow(Icons.Search, chosen.Name + " search", "", () =>
            {
                App.Settings.SearchEngine = chosen.Id;
                Store.SaveSettings(App.Settings);
                ClosePopups();
                BroadcastInit();
                ShowToast("Search engine", chosen.Name, Icons.Search, null, 2000);
            }, App.Settings.SearchEngine == chosen.Id));
        }

        Separator(MenuList, Resources);
        MenuList.Children.Add(PopupRow(Icons.Shield, "Tracker shield",
            AdBlocker.Enabled ? "on" : "off", () =>
            {
                ToggleShield();
                ClosePopups();
            }, AdBlocker.Enabled));

        MenuList.Children.Add(PopupRow(Icons.Globe, "Report as plain Chrome",
            App.Settings.ChromeUserAgent ? "on" : "off", () =>
            {
                ClosePopups();
                ToggleChromeUserAgent();
            }, App.Settings.ChromeUserAgent));

        MenuList.Children.Add(PopupRow(Icons.Shield, "Site permissions",
            App.Settings.AllowSitePermissions ? "allowed" : "blocked", () =>
            {
                ClosePopups();
                ToggleSitePermissions();
            }, App.Settings.AllowSitePermissions));

        foreach (var option in new[] { (Seconds: 15, Label: "15 seconds"), (Seconds: 30, Label: "30 seconds"), (Seconds: 60, Label: "1 minute"), (Seconds: 86400, Label: "never") })
        {
            var picked = option;
            MenuList.Children.Add(PopupRow(Icons.Minimize, $"Nap idle tabs after {picked.Label}", "", () =>
            {
                App.Settings.SuspendSeconds = picked.Seconds;
                Store.SaveSettings(App.Settings);
                ClosePopups();
            }, App.Settings.SuspendSeconds == picked.Seconds));
        }

        MenuList.Children.Add(PopupRow(Icons.Sparkle, "Restore tabs on launch",
            App.Settings.RestoreSession ? "on" : "off", () =>
            {
                App.Settings.RestoreSession = !App.Settings.RestoreSession;
                Store.SaveSettings(App.Settings);
                ClosePopups();
            }, App.Settings.RestoreSession));

        Separator(MenuList, Resources);
        MenuList.Children.Add(PopupRow(Icons.Sparkle, "Memory", MemoryReadout(), null));
        MenuList.Children.Add(PopupRow(Icons.Close, "Clear browsing history", $"{Store.History.Count} entries", () =>
        {
            Store.History.Clear();
            Store.SaveHistory();
            ClosePopups();
            ShowToast("History cleared", "Fresh start.", Icons.Check, null, 2400);
        }));
        MenuList.Children.Add(PopupRow(Icons.Close, "Clear data and reset\u2026",
            "delete everything and restart", () =>
            {
                ClosePopups(animate: false);
                OpenResetConfirm();
            }, danger: true));
        MenuList.Children.Add(PopupRow(Icons.Home, "Open Nibble data folder", "", () =>
        {
            ClosePopups();
            OpenPath(Store.DataDir);
        }));
        MenuList.Children.Add(PopupRow(Icons.Globe, "Set as default browser",
            BrowserRegistration.IsDefaultBrowser() ? "already is" : "opens Windows settings", () =>
            {
                ClosePopups();
                MakeDefaultBrowser();
            }, BrowserRegistration.IsDefaultBrowser()));
        MenuList.Children.Add(PopupRow(Icons.Copy, "Copy diagnostics",
            "version · engine · profile", () => { ClosePopups(); CopyDiagnostics(); }));
        MenuList.Children.Add(PopupRow(Icons.Check, $"Nibble {Version}", "WebView2 engine", null));

        OpenPopup(MenuPopup, MenuButton);
    }

    private void Shield_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        if (PopupIsOpenFor(ShieldButton))
        {
            ClosePopups();
            return;
        }

        MenuTitle.Text = "TRACKER SHIELD";
        MenuList.Children.Clear();

        MenuList.Children.Add(PopupRow(Icons.Shield, $"{_active.BlockedCount} blocked on this tab",
            $"~{Urls.FormatBytes(_active.SavedBytes)}", null));
        MenuList.Children.Add(PopupRow(Icons.Sparkle, $"{Urls.FormatBytes(Store.SavedBytesTotal)} saved all time",
            $"{Store.BlockedTotal:n0} total", null));

        Separator(MenuList, Resources);
        if (_active.BlockedHosts.Count == 0)
        {
            MenuList.Children.Add(PopupRow(Icons.Check, "Nothing blocked on this page", "", null));
        }
        else
        {
            foreach (var host in _active.BlockedHosts.Take(12))
                MenuList.Children.Add(PopupRow(Icons.Shield, host, "blocked", null));
            if (_active.BlockedHosts.Count > 12)
                MenuList.Children.Add(PopupRow(null, $"and {_active.BlockedHosts.Count - 12} more…", "", null));
        }

        Separator(MenuList, Resources);
        MenuList.Children.Add(PopupRow(Icons.Shield, AdBlocker.Enabled ? "Turn shield off" : "Turn shield on", "", () =>
        {
            ToggleShield();
            ClosePopups();
        }));

        OpenPopup(MenuPopup, ShieldButton);
    }

    private void ToggleShield()
    {
        AdBlocker.Enabled = !AdBlocker.Enabled;
        App.Settings.Blocker = AdBlocker.Enabled;
        Store.SaveSettings(App.Settings);
        UpdateShieldChip();
        ShowToast(AdBlocker.Enabled ? "Shield on" : "Shield off",
            AdBlocker.Enabled ? "Ads and trackers are being dropped." : "Everything loads now.",
            Icons.Shield, null, 2600);
    }

    private void Downloads_Click(object sender, RoutedEventArgs e)
    {
        if (PopupIsOpenFor(DownloadsButton))
        {
            ClosePopups();
            return;
        }
        MenuTitle.Text = "DOWNLOADS";
        MenuList.Children.Clear();

        if (_downloads.Count == 0)
        {
            MenuList.Children.Add(PopupRow(Icons.Download, "No downloads yet", "this session", null));
        }
        else
        {
            foreach (var download in _downloads.Take(10))
            {
                var item = download;
                MenuList.Children.Add(PopupRow(Icons.Download, item.Name,
                    item.Finished ? "open" : "in progress",
                    item.Finished ? () => { ClosePopups(); OpenPath(item.Operation.ResultFilePath); } : null));
            }
        }

        Separator(MenuList, Resources);
        MenuList.Children.Add(PopupRow(Icons.Home, "Show downloads folder", "", () =>
        {
            ClosePopups();
            OpenPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        }));

        OpenPopup(MenuPopup, DownloadsButton);
    }

    private void Theme_Click(object sender, RoutedEventArgs e) => SetTheme(Theme.IsDark ? "Light" : "Dark");

    private void SetTheme(string theme)
    {
        App.Settings.Theme = theme;
        Store.SaveSettings(App.Settings);
        // a theme's colours stay on top of whichever palette the person picks
        Theme.Apply(theme, Themes.Current?.Colors);
        Accent.Apply(Themes.Current is { Accent.Length: > 0 } current ? current.Accent : App.Settings.AccentColor);
        Native.SetDarkFrame(this, Theme.IsDark);
        SendTheme();
        ShowToast(Theme.IsDark ? "Night pixel mode" : "Daylight mode",
            theme == "System" ? "Following Windows." : "Saved to your settings.",
            Icons.Theme, null, 2200);
    }

    // =====================================================================
    //  personalization (accent, name, search engine)
    // =====================================================================

    /// <summary>Short word for the clock row, e.g. "24-hour" or "Windows".</summary>
    private static string ClockWord() => ClockFormat.Normalize(App.Settings.Clock) switch
    {
        ClockFormat.TwentyFour => "24-hour",
        ClockFormat.Twelve => "12-hour",
        _ => WindowsClockWord()
    };

    private static string WindowsClockWord() =>
        $"Windows ({(ClockFormat.Is24Hour(ClockFormat.Auto) ? "24-hour" : "12-hour")})";

    private void SetClock(string preference)
    {
        App.Settings.Clock = ClockFormat.Normalize(preference);
        Store.SaveSettings(App.Settings);
        SendTheme();

        var readings24 = ClockFormat.Is24Hour(App.Settings.Clock);
        var now = DateTime.Now;
        var body = App.Settings.Clock == ClockFormat.Auto
            ? $"matching Windows — {ClockFormat.Sample(now, readings24)}"
            : $"the new tab clock now reads {ClockFormat.Sample(now, readings24)}";
        ShowToast(ClockFormat.Label(App.Settings.Clock), body, Icons.Theme, null, 2400);
    }

    /// <summary>The theme shop: browse what is installed, apply it, import and export.</summary>
    private void OpenThemeShop()
    {
        ClosePopups();
        var shop = new ThemesWindow { Owner = this };
        shop.ShowDialog();
        UpdateShieldChip();
    }

    private void OpenPersonalize()
    {
        ClosePopups();

        var previous = (Accent: App.Settings.AccentColor, Name: App.Settings.UserName,
            Engine: App.Settings.SearchEngine, Clock: App.Settings.Clock);
        var wizard = new OnboardingWindow(previous.Accent, previous.Name, previous.Engine, previous.Clock,
            editing: true) { Owner = this };
        wizard.ShowDialog();

        var outcome = wizard.Outcome;
        if (!outcome.Completed)
        {
            // Cancelled after live-previewing colors: put everything back.
            Accent.Apply(previous.Accent);
            return;
        }

        App.Settings.AccentColor = outcome.AccentHex;
        App.Settings.UserName = outcome.UserName;
        App.Settings.SearchEngine = outcome.EngineId;
        App.Settings.Clock = ClockFormat.Normalize(outcome.Clock);
        Store.SaveSettings(App.Settings);
        Accent.Apply(App.Settings.AccentColor);
        BroadcastInit();
        SendTheme();

        var engine = Urls.EngineFor(App.Settings.SearchEngine);
        ShowToast("Personalized",
            string.IsNullOrWhiteSpace(outcome.UserName)
                ? $"Accent updated · {engine.Name}"
                : $"{outcome.UserName} · accent updated · {engine.Name}",
            Icons.Palette, null, 3000);
    }

    /// <summary>
    /// Some sites treat embedded browser engines as bots. This presents a plain Chrome
    /// user agent instead of the engine's default one.
    /// </summary>
    private void ToggleChromeUserAgent()
    {
        App.Settings.ChromeUserAgent = !App.Settings.ChromeUserAgent;
        Store.SaveSettings(App.Settings);

        foreach (var tab in Tabs)
        {
            if (tab.Core is null || tab.DefaultUserAgent is null) continue;
            try
            {
                tab.Core.Settings.UserAgent = App.Settings.ChromeUserAgent
                    ? ChromeLikeUserAgent(tab.DefaultUserAgent)
                    : tab.DefaultUserAgent;
            }
            catch
            {
                // Ignore per-tab failures.
            }
        }

        ShowToast(App.Settings.ChromeUserAgent ? "Compatibility on" : "Compatibility off",
            App.Settings.ChromeUserAgent
                ? "Nibble reports a plain Chrome agent. Reload a page to apply it."
                : "Back to the standard engine agent. Reload a page to apply it.",
            Icons.Globe, null, 3600);
    }

    private string MemoryReadout()
    {
        try
        {
            var shell = Process.GetCurrentProcess().WorkingSet64;
            long engine = 0;
            var count = 0;

            if (_env is not null)
            {
                foreach (var info in _env.GetProcessInfos())
                {
                    count++;
                    try { engine += Process.GetProcessById(info.ProcessId).WorkingSet64; }
                    catch { /* process may have just exited */ }
                }
            }

            var napping = Tabs.Count(t => t.IsSleeping);
            return count == 0
                ? $"shell {Urls.FormatBytes(shell)}"
                : $"{Urls.FormatBytes(shell)} shell · {count} procs {Urls.FormatBytes(engine)}" +
                  (napping > 0 ? $" · {napping} napping" : string.Empty);
        }
        catch
        {
            return "unavailable";
        }
    }

    // =====================================================================
    //  command palette
    // =====================================================================

    private void OpenPalette()
    {
        _popupAnchor = null;
        BuildCommands();
        PaletteQuery.Text = string.Empty;
        FilterPalette(string.Empty);
        PalettePopup.PlacementTarget = Shell;
        PalettePopup.Placement = PlacementMode.Center;
        PalettePopup.IsOpen = true;
        if (PalettePopup.Child is FrameworkElement card) Juice.PopIn(card, 0.04, 8, 320);

        // A popup's content lives in its own visual tree, so focus only sticks once the
        // popup has had a layout pass - otherwise typing lands in the address bar.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            PaletteQuery.Focus();
            Keyboard.Focus(PaletteQuery);
            PaletteQuery.SelectAll();
        }));
    }

    /// <summary>Everything the command bar can do, rebuilt on each open so labels stay true.</summary>
    private void BuildCommands()
    {
        var pinned = _active?.IsPinned == true;
        var muted = _active?.Core?.IsMuted == true;
        var engine = Urls.EngineFor(App.Settings.SearchEngine).Name;

        _commands =
        [
            new BrowserCommand("New tab", "Ctrl+T", Icons.Plus, () => NewTab(Urls.Home, true), "open"),
            new BrowserCommand("New private window", "Ctrl+Shift+N", Icons.Shield, () => NewPrivateWindow(),
                "incognito", "private", "porn", "secret", "no history"),
            new BrowserCommand("Close tab", "Ctrl+W", Icons.Close, () => CloseTab(_active), "shut"),
            new BrowserCommand("Find in page", "Ctrl+F", Icons.Search, ShowFindBar, "search text", "find"),
            new BrowserCommand("Print", "Ctrl+P", Icons.Copy, PrintPage, "paper", "pdf"),
            new BrowserCommand("Close duplicate tabs", "keep one of each", Icons.Copy, CloseDuplicateTabs, "duplicate", "dedupe", "tidy"),
            new BrowserCommand("Close other tabs", "", Icons.Close, CloseOtherTabs, "others", "focus"),
            new BrowserCommand("Close tabs to the right", "", Icons.Forward, CloseTabsToRight, "right", "prune"),
            new BrowserCommand("Reopen closed tab", "Ctrl+Shift+T", Icons.Reload, ReopenClosedTab, "undo close"),
            new BrowserCommand(pinned ? "Unpin this tab" : "Pin this tab", "pinned tabs shrink to their icon", Icons.Pin, TogglePinTab, "pin", "unpin", "sticky"),
            new BrowserCommand("Duplicate this tab", "", Icons.Copy, DuplicateActiveTab, "clone"),
            new BrowserCommand("Bookmark this page", "Ctrl+D", Icons.Star, ToggleBookmark, "favourite", "favorite", "save"),
            new BrowserCommand("Copy address", "", Icons.Lock, CopyAddress, "url", "link"),
            new BrowserCommand("Reload", "Ctrl+R", Icons.Reload, ReloadActive, "refresh"),
            new BrowserCommand("Home", "", Icons.Home, HomeTab, "new tab page", "start"),
            new BrowserCommand("Next tab", "Ctrl+Tab", Icons.Forward, () => CycleTab(1), "cycle"),
            new BrowserCommand("Previous tab", "Ctrl+Shift+Tab", Icons.Back, () => CycleTab(-1), "cycle back"),
            new BrowserCommand(muted ? "Unmute this site" : "Mute this site", "", muted ? Icons.Volume : Icons.Mute, ToggleMuteSite, "silence", "sound", "audio"),
            new BrowserCommand("Zoom in", "Ctrl+=", Icons.Plus, () => Zoom(0.1), "bigger"),
            new BrowserCommand("Zoom out", "Ctrl+-", Icons.Minimize, () => Zoom(-0.1), "smaller"),
            new BrowserCommand("Reset zoom", "Ctrl+0", Icons.Maximize, () => Zoom(0, reset: true), "100%"),
            new BrowserCommand("Sleep background tabs now", "hands memory back", Icons.Sparkle, NapOthersNow, "nap", "memory", "sleep", "free ram"),
            new BrowserCommand("Tracker shield", AdBlocker.Enabled ? "on" : "off", Icons.Shield, ToggleShield, "ads", "privacy", "block"),
            new BrowserCommand("Site permissions", App.Settings.AllowSitePermissions ? "allowed" : "blocked", Icons.Shield,
                ToggleSitePermissions, "camera", "microphone", "location", "notifications"),
            new BrowserCommand("Set as default browser", BrowserRegistration.IsDefaultBrowser() ? "already is" : "opens Windows settings",
                Icons.Globe, MakeDefaultBrowser, "default", "associate", "links"),
            new BrowserCommand("Copy diagnostics", "versions and paths only", Icons.Copy, CopyDiagnostics, "support", "bug", "log"),
            new BrowserCommand("Compatibility: report as plain Chrome", App.Settings.ChromeUserAgent ? "on" : "off", Icons.Globe, ToggleChromeUserAgent, "user agent", "blocked site"),
            new BrowserCommand(Theme.IsDark ? "Switch to light mode" : "Switch to dark mode", "", Icons.Theme, () => SetTheme(Theme.IsDark ? "Light" : "Dark"), "appearance", "night"),
            new BrowserCommand("Personalize Nibble", $"color · name · {engine}", Icons.Palette, OpenPersonalize, "accent", "name", "search engine", "setup"),
            new BrowserCommand("Theme shop", Themes.Current?.Name ?? "built-in themes", Icons.Palette, OpenThemeShop,
                "theme", "themes", "skin", "shop", "minecraft", "water", "custom"),
            new BrowserCommand("Clock style", ClockFormat.Label(App.Settings.Clock), Icons.Theme,
                () => SetClock(ClockFormat.Next(App.Settings.Clock)), "24-hour", "12-hour", "military time", "clock", "time"),
            new BrowserCommand("Copy theme code", "share your look", Icons.Copy, CopyThemeCode, "share", "export", "theme"),
            new BrowserCommand("Nibble stats", "pages · trackers · memory freed", Icons.Hash, ShowStats, "stats", "year", "wrapped", "numbers"),
            new BrowserCommand("Import from another browser", "Chrome · Edge · Brave · Firefox", Icons.Download, ImportFromBrowsers, "migrate", "import", "chrome", "edge", "firefox", "bookmarks"),
            new BrowserCommand("Downloads", "", Icons.Download, () => Downloads_Click(this, new RoutedEventArgs()), "files"),
            new BrowserCommand("Full screen", "F11", Icons.Maximize, () => SetFullscreen(!_fullscreen), "zen"),
            new BrowserCommand("Force quit Nibble", "skips saving, nothing can block it", Icons.Close, ForceQuit, "force quit", "kill", "exit", "terminate"),
            new BrowserCommand("Clear data and reset", "wipe the profile and restart", Icons.Close, OpenResetConfirm,
                "reset", "wipe", "clear data", "factory reset", "fresh start", "start over"),
            new BrowserCommand("Clear browsing history", $"{Store.History.Count} entries", Icons.Close, ClearHistory, "wipe", "privacy"),
            new BrowserCommand("Open data folder", Store.DataDir, Icons.Home, () => OpenPath(Store.DataDir), "profile", "files"),
            new BrowserCommand("Memory usage", MemoryReadout(), Icons.Sparkle, () => ShowToast("Memory", MemoryReadout(), Icons.Sparkle, null, 3400), "ram")
        ];
    }

    private void FilterPalette(string text)
    {
        var query = text.Trim();
        PaletteList.Children.Clear();
        _paletteRows.Clear();
        _paletteResults = [];

        // A pasted theme code is offered as an action rather than searched for.
        if (query.StartsWith("nibble-theme:", StringComparison.OrdinalIgnoreCase))
        {
            var code = query;
            _paletteResults.Add(new CommandResult(
                ResultKind.Command, "Apply theme code", code, "apply", Icons.Palette, code,
                new BrowserCommand("Apply theme code", "apply", Icons.Palette, () => ApplyThemeCode(code))));
        }
        else
        {
            _paletteResults = CommandEngine.Query(query, BuildCommandInputs());
        }

        RenderPaletteRows();
    }

    private CommandInputs BuildCommandInputs() => new()
    {
        Tabs = Tabs.Select(t => (t.Title, t.Url, (object)t)).ToList(),
        History = Store.History,
        Bookmarks = Store.Bookmarks,
        RecentlyClosed = _closed.Select(entry => (entry.Title, entry.Url)).ToList(),
        Commands = _commands,
        SearchEngineId = App.Settings.SearchEngine
    };

    private void RenderPaletteRows()
    {
        _paletteSel = _paletteResults.Count > 0 ? 0 : -1;
        foreach (var result in _paletteResults)
        {
            var row = PaletteRow(result);
            _paletteRows.Add(row);
            PaletteList.Children.Add(row);
        }

        if (_paletteResults.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No matches \u00b7 try > for commands, @tabs, @history, @bookmarks, @closed",
                FontSize = 11.5,
                Margin = new Thickness(12, 10, 12, 12)
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Ink3");
            PaletteList.Children.Add(empty);
        }

        HighlightPalette();
    }

    /// <summary>One command-bar row: real favicon for tabs, icon otherwise, note on the right.</summary>
    private Button PaletteRow(CommandResult result)
    {
        var row = new Button
        {
            Style = (Style)FindResource("PopupRowButton"),
            Padding = new Thickness(10, 7, 10, 7)
        };

        AutomationProperties.SetName(row, result.Note.Length > 0
            ? $"{result.Title}, {result.Note}"
            : result.Title);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement glyph;
        if (result.Tag is ZTab { Favicon: not null } tab && tab.Favicon is ImageSource favicon)
        {
            glyph = new Image
            {
                Source = favicon,
                Width = 15,
                Height = 15,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }
        else
        {
            var icon = new VectorIcon
            {
                Data = result.Icon,
                Size = 16,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            icon.SetResourceReference(VectorIcon.ForegroundProperty, "Ink2");
            glyph = icon;
        }
        Grid.SetColumn(glyph, 0);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = result.Title,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        stack.Children.Add(title);

        if (!string.IsNullOrWhiteSpace(result.Subtitle))
        {
            var subtitle = new TextBlock
            {
                Text = result.Subtitle,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                MaxWidth = 340,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Ink3");
            stack.Children.Add(subtitle);
        }
        Grid.SetColumn(stack, 1);

        var isValue = result.Kind is ResultKind.Calculator or ResultKind.Conversion;
        var note = new TextBlock
        {
            Text = result.Note,
            FontSize = isValue ? 14 : 11,
            FontWeight = isValue ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 170,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, isValue ? "Accent" : "Ink3");
        Grid.SetColumn(note, 2);

        grid.Children.Add(glyph);
        grid.Children.Add(stack);
        grid.Children.Add(note);
        row.Content = grid;
        row.Click += (_, _) =>
        {
            PalettePopup.IsOpen = false;
            RunResult(result);
        };
        return row;
    }

    private void RunResult(CommandResult result)
    {
        switch (result.Kind)
        {
            case ResultKind.Command:
                if (result.Tag is BrowserCommand command) command.Run();
                break;

            case ResultKind.Tab:
                if (result.Tag is ZTab tab && Tabs.Contains(tab))
                {
                    Activate(tab);
                    tab.View.Focus();
                }
                break;

            case ResultKind.Calculator:
            case ResultKind.Conversion:
                try { Clipboard.SetText(result.Note); } catch { /* clipboard busy */ }
                ShowToast("Copied", result.Note,
                    result.Kind == ResultKind.Calculator ? Icons.Calculator : Icons.Ruler, null, 2600);
                break;

            default:
                if (_active is not null && result.Payload.Length > 0) Navigate(_active, result.Payload);
                break;
        }
    }

    // =====================================================================
    //  command implementations
    // =====================================================================

    private void CloseDuplicateTabs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<ZTab>();
        foreach (var tab in Tabs)
        {
            var key = tab.IsNewTab ? Urls.Home : tab.Url;
            if (!seen.Add(key)) duplicates.Add(tab);
        }

        if (duplicates.Count == 0)
        {
            ShowToast("No duplicate tabs", "Every tab is doing its own thing.", Icons.Check, null, 2400);
            return;
        }

        foreach (var tab in duplicates) CloseTab(tab, remember: false);
        ShowToast($"{duplicates.Count} duplicate tab{(duplicates.Count == 1 ? "" : "s")} closed",
            "The first of each pair survived.", Icons.Copy, null, 2800);
    }

    private void CloseOtherTabs()
    {
        if (_active is null) return;
        var others = Tabs.Where(t => !ReferenceEquals(t, _active) && !t.IsPinned).ToList();
        foreach (var tab in others) CloseTab(tab, remember: false);
        ShowToast(others.Count == 0 ? "Nothing else to close" : $"Closed {others.Count} tab{(others.Count == 1 ? "" : "s")}",
            _active.Title, Icons.Close, null, 2400);
    }

    private void CloseTabsToRight()
    {
        if (_active is null) return;
        var index = Tabs.IndexOf(_active);
        var right = Tabs.Skip(index + 1).Where(t => !t.IsPinned).ToList();
        foreach (var tab in right) CloseTab(tab, remember: false);
        ShowToast(right.Count == 0 ? "Nothing to the right" : $"Closed {right.Count} tab{(right.Count == 1 ? "" : "s")} to the right",
            _active.Title, Icons.Forward, null, 2400);
    }

    private void TogglePinTab()
    {
        if (_active is null) return;
        var tab = _active;
        tab.IsPinned = !tab.IsPinned;
        if (tab.IsPinned) Tabs.Move(Tabs.IndexOf(tab), 0);
        UpdateTabWidths();
        ShowToast(tab.IsPinned ? "Tab pinned" : "Tab unpinned",
            tab.IsPinned ? "It stays first, as just its icon." : "Back to a normal tab.",
            Icons.Pin, null, 2200);
    }

    private void DuplicateActiveTab()
    {
        if (_active is null) return;
        NewTab(_active.IsNewTab ? Urls.Home : _active.Url, true);
    }

    private void ToggleMuteSite()
    {
        if (_active?.Core is null) return;
        var muted = !_active.Core.IsMuted;
        _active.Core.IsMuted = muted;
        ShowToast(muted ? "Site muted" : "Site unmuted", _active.Title,
            muted ? Icons.Mute : Icons.Volume, null, 2200);
    }

    private void NapOthersNow()
    {
        foreach (var tab in Tabs.Where(t => !ReferenceEquals(t, _active)))
            tab.LastActive = DateTime.Now.AddHours(-1);
        NapIdleTabs();
    }

    private void HomeTab()
    {
        if (_active is not null) Navigate(_active, Urls.Home);
    }

    private void CopyAddress()
    {
        if (_active is null || _active.IsNewTab) return;
        try { Clipboard.SetText(_active.Url); } catch { return; }
        ShowToast("Address copied", _active.Url, Icons.Lock, null, 2200);
    }

    private void ClearHistory()
    {
        Store.History.Clear();
        Store.SaveHistory();
        BroadcastInit();
        ShowToast("History cleared", "Quick tiles and suggestions are clear too.", Icons.Check, null, 2600);
    }

    private void ShowStats()
    {
        MenuTitle.Text = "NIBBLE \u00b7 STATS";
        MenuList.Children.Clear();

        var year = DateTime.Now.Year;
        MenuList.Children.Add(PopupRow(Icons.Hash, $"You traveled {Store.PagesVisited:N0} pages in {year}", "", null));
        MenuList.Children.Add(PopupRow(Icons.Pin, $"{Tabs.Count} tabs open now \u00b7 {Tabs.Count(t => t.IsPinned)} pinned", "", null));
        MenuList.Children.Add(PopupRow(Icons.Reload, $"{Store.TabsClosed:N0} tabs closed", "", null));
        MenuList.Children.Add(PopupRow(Icons.Sparkle, $"{Urls.FormatBytes(Store.MemoryFreed)} handed back by napping tabs", "", null));
        MenuList.Children.Add(PopupRow(Icons.Shield, $"{Store.BlockedTotal:N0} trackers blocked", $"~{Urls.FormatBytes(Store.SavedBytesTotal)} never downloaded", null));
        MenuList.Children.Add(PopupRow(Icons.Theme, $"{Store.Launches} launches", "", null));

        Separator(MenuList, Resources);
        MenuList.Children.Add(PopupRow(Icons.Copy, "Copy stats card", "paste it anywhere", () =>
        {
            ClosePopups();
            var card = $"Nibble {year}: {Store.PagesVisited:N0} pages \u00b7 {Store.BlockedTotal:N0} trackers blocked \u00b7 " +
                       $"{Urls.FormatBytes(Store.MemoryFreed)} memory handed back by napping tabs";
            try { Clipboard.SetText(card); } catch { }
            ShowToast("Stats copied", card, Icons.Hash, null, 3200);
        }));

        OpenPopup(MenuPopup, MenuButton, 380);
    }

    private void CopyThemeCode()
    {
        var code = $"nibble-theme:{App.Settings.AccentColor}:{App.Settings.Theme}";
        try { Clipboard.SetText(code); } catch { return; }
        ShowToast("Theme code copied", "Paste it into Ctrl+K on another machine.", Icons.Palette, null, 3600);
    }

    private void ApplyThemeCode(string code)
    {
        var parts = code.Trim().Split(':');
        if (parts.Length < 2)
        {
            ShowToast("That theme code looks incomplete", code, Icons.Close, null, 2600);
            return;
        }

        var accent = Accent.ToHex(Accent.Parse(parts[1]));
        var theme = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : App.Settings.Theme;

        App.Settings.AccentColor = accent;
        App.Settings.Theme = theme;
        Store.SaveSettings(App.Settings);
        Theme.Apply(theme);
        Accent.Apply(accent);
        Native.SetDarkFrame(this, Theme.IsDark);
        SendTheme();
        BroadcastInit();
        ShowToast("Theme applied", $"{accent} \u00b7 {theme}", Icons.Palette, null, 3000);
    }

    private void ImportFromBrowsers()
    {
        var found = Migrator.Detect();

        MenuTitle.Text = "IMPORT FROM ANOTHER BROWSER";
        MenuList.Children.Clear();

        if (found.Count == 0)
        {
            MenuList.Children.Add(PopupRow(Icons.Offline, "No other browser profiles found", "", null));
        }

        foreach (var browser in found)
        {
            var captured = browser;
            MenuList.Children.Add(PopupRow(Icons.Download, $"Import from {captured.Name}", captured.Summary, () =>
            {
                ClosePopups();
                RunImport(captured);
            }));
        }

        Separator(MenuList, Resources);
        MenuList.Children.Add(PopupRow(Icons.Check, "Imported: bookmarks, search engine", "", null));
        MenuList.Children.Add(PopupRow(Icons.Offline, "Not yet: history, cookies, autofill, passwords, open tabs",
            "needs deeper profile work", null));

        OpenPopup(MenuPopup, MenuButton, 400);
    }

    private void RunImport(DetectedBrowser browser)
    {
        var result = Migrator.Import(browser);
        var engine = result.SearchEngine is null ? string.Empty : $" \u00b7 search engine: {Urls.EngineFor(result.SearchEngine).Name}";
        ShowToast($"Imported from {browser.Name}",
            $"{result.Bookmarks:N0} bookmarks{engine} \u00b7 skipped: {string.Join(", ", result.Skipped)}",
            Icons.Download, null, 5200);
        BroadcastInit();
    }

    // =====================================================================
    //  clear data and reset
    // =====================================================================

    /// <summary>Confirmation first: this one really cannot be undone.</summary>
    private void OpenResetConfirm()
    {
        MenuTitle.Text = "CLEAR DATA AND RESET";
        MenuList.Children.Clear();

        var warning = new TextBlock
        {
            Text = "This deletes your whole Nibble profile and restarts the browser like a fresh " +
                   "install: history, bookmarks, open tabs, settings, cookies, cache and site " +
                   "storage. Your other browsers are untouched.",
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 2, 12, 12),
            MaxWidth = 340
        };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
        MenuList.Children.Add(warning);

        MenuList.Children.Add(PopupRow(Icons.Close, "Yes, wipe everything and restart", "cannot be undone", () =>
        {
            ClosePopups(animate: false);
            RunFullReset();
        }, danger: true));

        MenuList.Children.Add(PopupRow(Icons.Back, "Cancel", "", () => ClosePopups()));

        OpenPopup(MenuPopup, MenuButton, 380);
    }

    /// <summary>
    /// Flags the wipe, lets go of the engine, then relaunches. The new instance performs
    /// the deletion before it starts an engine of its own, so it comes up on the setup screen.
    /// </summary>
    private void RunFullReset()
    {
        Store.BeginFullReset();

        // Release the engine first: its profile cannot be deleted while it is running.
        _forceQuit = true;
        foreach (var tab in Tabs.ToList())
        {
            try
            {
                Native.StopWatchingClicks(tab.PageHandle, _pageClickProc, PageClickSubclassId);
                tab.View.Dispose();
            }
            catch
            {
                // Already gone.
            }
        }

        // Let the engine processes actually exit so the new instance is not fighting a lock.
        // Breaks out the moment the last one is gone, so the usual relaunch is immediate.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (EngineWorkingSet() == 0) break;
            Thread.Sleep(100);
        }

        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? Store.DataDir
                });
            }
            else
            {
                Store.LogError("reset: could not resolve the executable path to relaunch");
            }
        }
        catch (Exception ex)
        {
            Store.LogError($"reset relaunch failed: {ex.GetType().Name}: {ex.Message}");
        }

        Environment.Exit(0);
    }

    private void HighlightPalette()
    {
        var selected = (Brush)FindResource("AccentSoft");
        for (var i = 0; i < _paletteRows.Count; i++)
            _paletteRows[i].Background = i == _paletteSel ? selected : Brushes.Transparent;
    }

    // =====================================================================
    //  toasts
    // =====================================================================

    private Toast ShowToast(string title, string subtitle, string icon, Action? action, int milliseconds,
        bool showProgress = false)
    {
        var card = new PixelPanel
        {
            Fill = (Brush)FindResource("CardBg"),
            Stroke = (Brush)FindResource("HairlineStrong"),
            StrokeThickness = 1,
            CornerSteps = 3,
            CornerStep = 3,
            Margin = new Thickness(0, 0, 0, 10),
            Effect = (Effect)FindResource("PopupShadow")
        };

        var grid = new Grid { Margin = new Thickness(13, 11, 13, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconElement = new VectorIcon
        {
            Data = icon,
            Size = 17,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 11, 0)
        };
        iconElement.SetResourceReference(VectorIcon.ForegroundProperty, "Accent");
        Grid.SetColumn(iconElement, 0);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Ink"),
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11.5,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = (Brush)FindResource("Ink2"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        Grid.SetColumn(stack, 1);

        Rectangle? progress = null;
        if (showProgress)
        {
            var track = new Grid
            {
                Height = 4,
                Margin = new Thickness(0, 9, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            track.Children.Add(new Rectangle { Fill = (Brush)FindResource("Track"), Height = 4 });
            progress = new Rectangle
            {
                Fill = (Brush)FindResource("Accent"),
                Height = 4,
                Width = 12,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            track.Children.Add(progress);
            stack.Children.Add(track);
        }

        grid.Children.Add(iconElement);
        grid.Children.Add(stack);
        card.Child = grid;

        var toast = new Toast { Card = card, Progress = progress };

        if (action is not null)
        {
            var button = new Button
            {
                Style = (Style)FindResource("PopupRowButton"),
                Content = card,
                ToolTip = "Click to open"
            };
            button.Click += (_, _) =>
            {
                RemoveToast(toast);
                action();
            };
            toast = new Toast { Card = button, Progress = progress };
        }

        ToastList.Children.Add(toast.Card);
        _toasts.Add(toast);

        if (milliseconds > 0)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => RemoveToast(toast);
            timer.Start();
            toast.Timer = timer;
        }

        PositionToasts();
        if (toast.Card is FrameworkElement element) Juice.PopIn(element, 0.05, 12, 360);
        return toast;
    }

    private void RemoveToast(Toast? toast)
    {
        if (toast is null || !_toasts.Contains(toast)) return;
        toast.Timer?.Stop();
        _toasts.Remove(toast);

        if (toast.Card is FrameworkElement element)
        {
            Juice.SquashOut(element, () =>
            {
                ToastList.Children.Remove(toast.Card);
                PositionToasts();
            }, 0.1, 150);
        }
        else
        {
            ToastList.Children.Remove(toast.Card);
            PositionToasts();
        }
    }

    private void PositionToasts()
    {
        try
        {
            ToastPopup.PlacementTarget = Shell;
            ToastPopup.Placement = PlacementMode.Relative;
            ToastList.Measure(new Size(330, double.PositiveInfinity));
            ToastPopup.HorizontalOffset = Math.Max(0, Shell.ActualWidth - 330 - 18);
            ToastPopup.VerticalOffset = Math.Max(0, Shell.ActualHeight - ToastList.DesiredSize.Height - 18);
        }
        catch
        {
            // Positioning is cosmetic.
        }
    }

    // =====================================================================
    //  native hit testing: drag, double-click zoom, Windows 11 snap layouts
    // =====================================================================

    private const int WmNcHitTest = 0x0084;
    private const int HtCaption = 2;
    private const int HtClient = 1;

    /// <summary>
    /// Room the menu card keeps around itself inside its popup window so the drop shadow
    /// has somewhere to fall. The top strip is invisible and only ever a problem when the
    /// popup is placed too close to the button it belongs to - see OpenPopup. The offset
    /// below the anchor also carries a few pixels of clearance, so device-pixel rounding
    /// (measured at 2 px on a 1000x620 window) cannot creep back over the button's edge.
    /// </summary>
    private const double PopupShadowInset = 12;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest || RowHeader.Height.Value <= 0) return IntPtr.Zero;

        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var point = PointFromScreen(new Point(x, y));

        // Note: the maximize button deliberately does NOT report HTMAXBUTTON. That one
        // value is what makes Windows 11 hover its snap-layouts flyout over the button,
        // which reads as a second, smaller button. Snap still works by dragging a window
        // to an edge, or with Win+Z.
        //
        // Anything clickable claims the client area outright. Saying nothing here used to
        // hand the traffic-light buttons to the window's 6 px resize border, so their outer
        // few pixels answered HTTOP/HTRIGHT: the pointer flipped between an arrow and a
        // resize cursor as it crossed them, and a drag there resized the window instead of
        // pressing the button. Measured before the fix: minimize top=12, maximize top=12,
        // close top=12 right=11.
        if (FindAncestor<ButtonBase>(InputHitTest(point) as DependencyObject) is not null ||
            IsOverTab(point) ||
            Contains(WindowButtons, point))
        {
            handled = true;
            return HtClient;
        }

        if (Contains(DragRegion, point))
        {
            handled = true;
            return HtCaption;
        }

        return IntPtr.Zero;
    }

    private bool IsOverTab(Point point)
    {
        foreach (var tab in Tabs)
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement container &&
                Contains(container, point))
                return true;
        }
        return false;
    }

    /// <summary>Tabs stretch to fill the strip when only a few are open, then shrink and scroll.</summary>
    private void UpdateTabWidths()
    {
        if (Tabs.Count == 0) return;

        var count = Tabs.Count;
        var available = TabScroll.ActualWidth - NewTabButton.ActualWidth - 12;
        if (available <= 120) available = 760; // before first layout pass

        var width = Math.Clamp((available - count * 6) / count, 112, 320);
        foreach (var tab in Tabs) tab.TabWidth = tab.IsPinned ? 46 : width;
    }

    private bool Contains(FrameworkElement element, Point point)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        try
        {
            var origin = element.TransformToAncestor(this).Transform(new Point(0, 0));
            return new Rect(origin, element.RenderSize).Contains(point);
        }
        catch
        {
            return false;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    // =====================================================================
    //  toolbar handlers
    // =====================================================================

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Shift-click is the force-quit gesture: it skips the session flush entirely.
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            ForceQuit();
            return;
        }

        Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTab(Urls.Home, true);

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack(_active);

    private void Forward_Click(object sender, RoutedEventArgs e) => GoForward(_active);

    private void Reload_Click(object sender, RoutedEventArgs e) => ReloadActive();

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (_active is not null) Navigate(_active, Urls.Home);
    }

    private void Bookmark_Click(object sender, RoutedEventArgs e) => ToggleBookmark();

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is ZTab tab) CloseTab(tab);
    }

    private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ZTab tab) return;
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;
        if (!ReferenceEquals(tab, _active)) Activate(tab);
    }

    private void Tab_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if ((sender as FrameworkElement)?.DataContext is ZTab tab) CloseTab(tab);
    }

    private void Tab_MouseEnter(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ZTab tab) tab.LastActive = DateTime.Now;
    }

    private void Tab_MouseLeave(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ZTab tab) tab.LastActive = DateTime.Now;
    }

    private void Omni_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AnimateUnderline(1);
        Juice.Stretch(OmniFrame, 0.009, 190);
        ShowSuggestions(Omni.Text);
    }

    private void Omni_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AnimateUnderline(0);
        Juice.Release(OmniFrame, 0.006, 300);
        if (_active is null) return;
        _omniSuppress = true;
        Omni.Text = DisplayUrl(_active);
        _omniSuppress = false;
    }

    private void Omni_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_omniSuppress) return;
        if (Omni.IsKeyboardFocusWithin) ShowSuggestions(Omni.Text);
    }

    private void Omni_MouseWheel(object sender, MouseWheelEventArgs e) => e.Handled = false;

    private void Omni_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                if (_omniSel >= 0 && _omniSel < _omniItems.Count) CommitSuggestion(_omniItems[_omniSel]);
                else CommitOmniboxText();
                break;

            case Key.Down:
                e.Handled = true;
                if (_omniRows.Count > 0)
                {
                    _omniSel = Math.Min(_omniSel + 1, _omniRows.Count - 1);
                    HighlightOmni();
                }
                break;

            case Key.Up:
                e.Handled = true;
                if (_omniRows.Count > 0)
                {
                    _omniSel = Math.Max(_omniSel - 1, 0);
                    HighlightOmni();
                }
                break;

            case Key.Escape:
                e.Handled = true;
                HideSuggestions();
                if (_active is not null)
                {
                    _omniSuppress = true;
                    Omni.Text = DisplayUrl(_active);
                    _omniSuppress = false;
                    _active.View.Focus();
                }
                break;
        }
    }

    private void AnimateUnderline(double to)
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        OmniUnderlineScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(200)) { EasingFunction = easing });
        OmniUnderline.BeginAnimation(OpacityProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(200)));
    }

    private void PaletteQuery_TextChanged(object sender, TextChangedEventArgs e) =>
        FilterPalette(PaletteQuery.Text);

    private void PaletteQuery_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                PalettePopup.IsOpen = false;
                _active?.View.Focus();
                break;

            case Key.Down:
                e.Handled = true;
                if (_paletteRows.Count > 0)
                {
                    _paletteSel = Math.Min(_paletteSel + 1, _paletteRows.Count - 1);
                    HighlightPalette();
                }
                break;

            case Key.Up:
                e.Handled = true;
                if (_paletteRows.Count > 0)
                {
                    _paletteSel = Math.Max(_paletteSel - 1, 0);
                    HighlightPalette();
                }
                break;

            case Key.Enter:
                e.Handled = true;
                var chosen = _paletteRows.ElementAtOrDefault(_paletteSel);
                if (chosen is not null)
                {
                    PalettePopup.IsOpen = false;
                    chosen.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, chosen));
                }
                break;
        }
    }

    // =====================================================================
    //  helpers
    // =====================================================================

    private void GoBack(ZTab? tab)
    {
        if (tab?.Core is { CanGoBack: true } core) core.GoBack();
    }

    private void GoForward(ZTab? tab)
    {
        if (tab?.Core is { CanGoForward: true } core) core.GoForward();
    }

    private void ReloadActive()
    {
        if (_active is null) return;
        if (_active.IsBroken) Navigate(_active, _active.Url);
        else _active.Core?.Reload();
    }

    private void ToggleBookmark()
    {
        if (_active is null || _active.IsNewTab) return;
        var added = Store.ToggleBookmark(_active.Url, _active.Title);
        UpdateStar(_active);
        ShowToast(added ? "Bookmarked" : "Bookmark removed", _active.Title,
            added ? Icons.Star : Icons.Close, null, 2200);
    }

    private void Zoom(double delta, bool reset = false)
    {
        if (_active is null) return;
        var value = reset ? 1.0 : Math.Clamp(_active.View.ZoomFactor + delta, 0.25, 3.0);
        _active.View.ZoomFactor = value;
        ShowToast($"Zoom {(int)Math.Round(value * 100)}%", string.Empty, Icons.Search, null, 1400);
    }

    // =====================================================================
    //  private windows
    // =====================================================================

    /// <summary>True when this window is the private kind.</summary>
    public bool IsPrivate => _private;

    /// <summary>
    /// Opens a URL from outside the window: a link handed over by another app, or a
    /// command line that arrived while Nibble was already running.
    /// </summary>
    public void OpenUrlInNewTab(string url, bool activate = true)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Activate();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            NewTab(url, activate);
        }));
    }

    private void NewPrivateWindow(string? url = null)
    {
        ClosePopups(animate: false);
        if (Application.Current is App app) app.OpenPrivateWindow(url);
    }

    /// <summary>Explains what a private window does and does not hide, once, on open.</summary>
    private void ExplainPrivateWindow()
    {
        ShowToast("Private window",
            "No history, no cookies kept, nothing written to disk. Your network can still see which sites you reach.",
            Icons.Shield, null, 9000);
    }

    // =====================================================================
    //  site permissions
    // =====================================================================

    /// <summary>
    /// Nibble has no permission prompt of its own, so the honest default is "no": a page
    /// cannot reach the camera, microphone or location behind your back. Turning site
    /// permissions on in the menu allows them for every site, which the toast says plainly.
    /// </summary>
    private void OnPermissionRequested(ZTab tab, CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        var allow = App.Settings.AllowSitePermissions;
        e.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
        e.Handled = true;

        var host = Urls.PrettyHost(tab.Url);
        var what = e.PermissionKind switch
        {
            CoreWebView2PermissionKind.Camera => "camera",
            CoreWebView2PermissionKind.Microphone => "microphone",
            CoreWebView2PermissionKind.Geolocation => "location",
            CoreWebView2PermissionKind.Notifications => "notifications",
            CoreWebView2PermissionKind.ClipboardRead => "clipboard",
            CoreWebView2PermissionKind.MultipleAutomaticDownloads => "automatic downloads",
            CoreWebView2PermissionKind.Autoplay => "autoplay",
            _ => e.PermissionKind.ToString().ToLowerInvariant()
        };

        if (allow)
        {
            ShowToast($"Allowed {what}", $"{host} asked and site permissions are on.", Icons.Shield, null, 3200);
        }
        else
        {
            ShowToast($"Blocked {what}",
                $"{host} asked for {what}. Menu → *Site permissions* if you want to allow it.",
                Icons.Shield, null, 5200);
        }
    }

    private void ToggleSitePermissions()
    {
        App.Settings.AllowSitePermissions = !App.Settings.AllowSitePermissions;
        Store.SaveSettings(App.Settings);
        ShowToast(App.Settings.AllowSitePermissions ? "Site permissions on" : "Site permissions blocked",
            App.Settings.AllowSitePermissions
                ? "Sites may now use the camera, microphone, location and notifications without asking."
                : "Sites are refused the camera, microphone, location and notifications.",
            Icons.Shield, null, 3600);
    }

    // =====================================================================
    //  find in page (Ctrl+F) and print (Ctrl+P)
    // =====================================================================

    private void ShowFindBar()
    {
        if (_active?.Core is null) return;
        HideSuggestions();
        PositionFindBar();
        FindPopup.IsOpen = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Mouse.Capture(null)));
        FindBox.Focus();
        FindBox.SelectAll();
        Juice.PopIn(FindBar, 0.04, -6, 220);
        UpdateFindCount();
    }

    private void HideFindBar()
    {
        if (!FindPopup.IsOpen) return;
        FindPopup.IsOpen = false;
        _ = ClearFindSelectionAsync();
        _active?.View.Focus();
    }

    /// <summary>Pins the find bar to the top-right of the page area.</summary>
    private void PositionFindBar()
    {
        var top = RowHeader.Height.Value + RowTools.Height.Value + 12;
        var right = Math.Max(18, Shell.ActualWidth - 400 - 18);
        FindPopup.HorizontalOffset = right;
        FindPopup.VerticalOffset = top;
    }

    private async void RunFind(bool forward)
    {
        var core = _active?.Core;
        if (core is null) return;

        var term = FindBox.Text;
        if (term.Length == 0)
        {
            await ClearFindSelectionAsync();
            FindCount.Text = string.Empty;
            return;
        }

        try
        {
            if (!_findStarted || !string.Equals(term, _findTerm, StringComparison.Ordinal))
            {
                _findTerm = term;
                _findCount = await CountMatchesAsync(core, term);
                _findIndex = 0;
                _findStarted = true;
            }

            if (_findCount > 0 && await JumpToMatchAsync(core, term, backwards: !forward))
                _findIndex = forward
                    ? (_findIndex % _findCount) + 1
                    : ((_findIndex - 2 + _findCount) % _findCount) + 1;
        }
        catch (Exception ex)
        {
            Store.LogError($"find failed: {ex.GetType().Name}: {ex.Message}");
        }

        UpdateFindCount();
    }

    private void UpdateFindCount()
    {
        if (!FindPopup.IsOpen) return;
        FindCount.Text = _findCount switch
        {
            <= 0 => FindBox.Text.Length == 0 ? string.Empty : "0 found",
            _ => $"{Math.Max(1, _findIndex)}/{_findCount}"
        };
    }

    private void FindBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                RunFind(forward: (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
                break;
            case Key.Escape:
                e.Handled = true;
                HideFindBar();
                break;
        }
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _findStarted = false;
        if (FindBox.Text.Length == 0)
        {
            FindCount.Text = string.Empty;
            _ = ClearFindSelectionAsync();
            return;
        }
        RunFind(forward: true);
    }

    // ---------------------------------------------------------------------
    //  find implementation
    //
    //  Deliberately plain DOM work rather than CoreWebView2Find: a find session
    //  started with SuppressDefaultFindDialog measured 0 matches on the engine shipped
    //  here (match count 0, active index -1, on a page whose text plainly contained the
    //  term), and the app cannot ship a search that reports nothing. window.find is the
    //  long-standing Chromium behaviour for "select and scroll to this text"; the count
    //  comes from the same visible text the person is looking at.
    // ---------------------------------------------------------------------

    private async Task<int> CountMatchesAsync(CoreWebView2 core, string term)
    {
        var script =
            "(() => {" +
            $"const term = {JsonSerializer.Serialize(term)};" +
            "const body = document.body ? document.body.innerText : '';" +
            "if (!term || !body) return '0';" +
            "const hay = body.toLowerCase(), needle = term.toLowerCase();" +
            "let count = 0, at = 0;" +
            "while ((at = hay.indexOf(needle, at)) !== -1) { count++; at += needle.length; }" +
            "return String(count);" +
            "})();";

        return int.TryParse(await EvaluateAsync(core, script), out var count) ? count : 0;
    }

    private async Task<bool> JumpToMatchAsync(CoreWebView2 core, string term, bool backwards)
    {
        var script =
            "(() => { try {" +
            $"return window.find({JsonSerializer.Serialize(term)}, false, {(backwards ? "true" : "false")}, true, false, true, false) ? '1' : '0';" +
            "} catch (e) { return '0'; } })();";

        return await EvaluateAsync(core, script) == "1";
    }

    private async Task ClearFindSelectionAsync()
    {
        var core = _active?.Core;
        if (core is null) return;
        await EvaluateAsync(core, "(() => { const s = window.getSelection(); if (s) s.removeAllRanges(); return '1'; })();");
    }

    /// <summary>Runs a snippet and unwraps the JSON string the engine hands back.</summary>
    private static async Task<string> EvaluateAsync(CoreWebView2 core, string script)
    {
        try
        {
            var raw = await core.ExecuteScriptAsync(script);
            if (string.IsNullOrEmpty(raw) || raw == "null") return string.Empty;
            return raw.Length > 1 && raw[0] == '"' ? JsonSerializer.Deserialize<string>(raw) ?? string.Empty : raw;
        }
        catch (Exception ex)
        {
            Store.LogError($"script failed: {ex.GetType().Name}: {ex.Message}");
            return string.Empty;
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => RunFind(forward: true);

    private void FindPrev_Click(object sender, RoutedEventArgs e) => RunFind(forward: false);

    private void FindClose_Click(object sender, RoutedEventArgs e) => HideFindBar();

    private void PrintPage()
    {
        var core = _active?.Core;
        if (core is null) return;
        try
        {
            core.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        }
        catch (Exception ex)
        {
            Store.LogError($"print failed: {ex.GetType().Name}: {ex.Message}");
            ShowToast("Could not print", "The engine refused to open its print dialog.", Icons.Close, null, 3200);
        }
    }

    // =====================================================================
    //  window placement memory
    // =====================================================================

    private void RestorePlacement()
    {
        var s = App.Settings;
        if (s.WindowWidth < 620 || s.WindowHeight < 460) return;

        var saved = new Rect(s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight);
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        // Only trust a rectangle that is still (mostly) on a screen you actually have:
        // unplugging a monitor must not put the window somewhere invisible.
        var visible = Rect.Intersect(saved, virtualScreen);
        if (visible.IsEmpty || visible.Width < 200 || visible.Height < 150) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = s.WindowWidth;
        Height = s.WindowHeight;
        Left = _private ? s.WindowLeft + 28 : s.WindowLeft;
        Top = _private ? s.WindowTop + 28 : s.WindowTop;

        if (s.WindowMaximized && !_private) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        try
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : (RestoreBounds.IsEmpty ? new Rect(Left, Top, Width, Height) : RestoreBounds);

            App.Settings.WindowWidth = bounds.Width;
            App.Settings.WindowHeight = bounds.Height;
            App.Settings.WindowLeft = bounds.Left;
            App.Settings.WindowTop = bounds.Top;
            App.Settings.WindowMaximized = WindowState == WindowState.Maximized;
            Store.SaveSettings(App.Settings);
        }
        catch
        {
            // Placement is a nicety, never a reason to fail a close.
        }
    }

    /// <summary>
    /// Registers Nibble as a browser and hands the choice to Windows: since Windows 10
    /// no app is allowed to make itself the default, only the user can, in Settings.
    /// </summary>
    private void MakeDefaultBrowser()
    {
        var exe = Environment.ProcessPath;
        if (!BrowserRegistration.Register(exe))
        {
            ShowToast("Could not register Nibble",
                "Windows refused the registry entries, so it will not appear in the browser list.",
                Icons.Close, null, 4200);
            return;
        }

        if (BrowserRegistration.IsDefaultBrowser())
        {
            ShowToast("Nibble is your default browser", "Web links open here.", Icons.Check, null, 2600);
            return;
        }

        BrowserRegistration.OpenDefaultAppsSettings();
        ShowToast("Pick Nibble in Settings",
            "Windows only lets you choose your own default browser — Nibble is now in that list.",
            Icons.Globe, null, 7000);
    }

    /// <summary>
    /// Versions and paths, never content: no history, no bookmarks, no URLs. This is the
    /// text to paste into a bug report.
    /// </summary>
    private void CopyDiagnostics()
    {
        string engine;
        try { engine = CoreWebView2Environment.GetAvailableBrowserVersionString(null); }
        catch { engine = "not installed"; }

        var privateWindows = Application.Current.Windows.OfType<MainWindow>().Count(w => w.IsPrivate);
        var text = new StringBuilder()
            .AppendLine($"Nibble {Version}")
            .AppendLine($"engine: WebView2 {engine}")
            .AppendLine($"windows: {Application.Current.Windows.Count} ({privateWindows} private)")
            .AppendLine($"theme: {App.Settings.Theme} · clock: {ClockFormat.Label(App.Settings.Clock)}")
            .AppendLine($"shield: {(AdBlocker.Enabled ? "on" : "off")} · site permissions: {(App.Settings.AllowSitePermissions ? "allowed" : "blocked")}")
            .AppendLine($"profile: {Store.DataDir}")
            .AppendLine($"log: {Path.Combine(Store.DataDir, "nibble.log")}")
            .ToString();

        try
        {
            Clipboard.SetText(text);
            ShowToast("Diagnostics copied", "Versions and paths only — no history, no URLs.", Icons.Copy, null, 3600);
        }
        catch
        {
            ShowToast("Could not copy", "The clipboard was busy.", Icons.Close, null, 2600);
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Nothing useful to do if the shell refuses.
        }
    }
}
