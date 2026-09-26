using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Nibble.Controls;
using Nibble.Services;

namespace Nibble;

/// <summary>
/// A window a page opened for itself, which is the shape a sign-in flow needs. Google - and
/// anything built on Firebase - signs in inside it and hands the result back through
/// window.opener and shared storage. Neither of those survives being turned into a tab, which
/// is exactly what "unable to process request due to missing initial state" means. The window
/// shares the engine profile of the window that opened it, so cookies and storage line up,
/// and a window opened from a private window stays in-private.
/// </summary>
public sealed class PopupWindow : Window
{
    private readonly WebView2 _view = new();
    private readonly TextBlock _title = new();
    private readonly MainWindow _owner;

    public PopupWindow(MainWindow owner, bool privateWindow, CoreWebView2WindowFeatures features)
    {
        _owner = owner;

        WindowStyle = WindowStyle.None;
        Background = (Brush)FindResource("WindowBg");
        MinWidth = 320;
        MinHeight = 240;
        Owner = owner;
        Title = "Nibble";
        FontFamily = (FontFamily)FindResource("UIFont");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        // A sign-in window is a small window: honour the size the page asked for, and let the
        // user resize it, but do not chase the exact screen position it asked for.
        if (features.HasSize) Width = Math.Clamp(features.Width, 360, 1200);
        else Width = 520;
        if (features.HasSize) Height = Math.Clamp(features.Height, 320, 900);
        else Height = 680;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        Content = BuildShell();
        SourceInitialized += (_, _) =>
        {
            Native.RoundCorners(this);
            Native.SetDarkFrame(this, Theme.IsDark);
        };
    }

    public CoreWebView2? CoreWebView2 => _view.CoreWebView2;

    private UIElement BuildShell()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Background = (Brush)FindResource("ChromeBg"), Margin = new Thickness(6, 0, 6, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); } catch { /* a rejected drag is not an error */ }
        };

        var mark = new VectorIcon
        {
            Data = Icons.Globe,
            Size = 13,
            Foreground = (Brush)FindResource("Ink3"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0)
        };
        Grid.SetColumn(mark, 0);
        header.Children.Add(mark);

        _title.Text = "Nibble";
        _title.FontSize = 12;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.Foreground = (Brush)FindResource("Ink2");
        _title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_title, 1);
        header.Children.Add(_title);

        var close = new Button
        {
            Style = (Style)FindResource("TabCloseButton"),
            Width = 26,
            Height = 26,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Close this window",
            Content = new VectorIcon
            {
                Data = Icons.Close,
                Size = 14,
                Foreground = (Brush)FindResource("Ink2")
            }
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        header.Children.Add(close);

        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        _view.DefaultBackgroundColor = Theme.IsDark
            ? System.Drawing.Color.FromArgb(255, 0x1C, 0x1C, 0x1E)
            : System.Drawing.Color.FromArgb(255, 0xFF, 0xFF, 0xFF);
        Grid.SetRow(_view, 1);
        grid.Children.Add(_view);

        return grid;
    }

    /// <summary>
    /// Builds the engine view. Everything a page can see has to be set up before this window is
    /// handed to the opener as its window.open result - WebView2 ignores later changes - so the
    /// caller awaits this before assigning NewWindow.
    /// </summary>
    public async Task PrepareAsync(CoreWebView2Environment environment, bool privateWindow, string? userAgent,
        Action<CoreWebView2, CoreWebView2NewWindowRequestedEventArgs> route)
    {
        if (privateWindow)
        {
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            await _view.EnsureCoreWebView2Async(environment, options);
        }
        else
        {
            await _view.EnsureCoreWebView2Async(environment);
        }

        var core = _view.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsPasswordAutosaveEnabled = false;

        // Match the window that opened it, compatibility mode included.
        try
        {
            if (userAgent is { Length: > 0 }) core.Settings.UserAgent = userAgent;
        }
        catch
        {
            // Older runtime: the engine's own agent is fine.
        }

        try { await core.AddScriptToExecuteOnDocumentCreatedAsync(Pages.BridgeScript); }
        catch { /* only powers keyboard shortcuts inside pages */ }

        core.DocumentTitleChanged += (_, _) => ShowTitle(core.DocumentTitle);
        core.WindowCloseRequested += (_, _) => Close();
        core.NewWindowRequested += (_, e) => route(core, e);
    }

    private void ShowTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        _title.Text = title;
        Title = $"{title} - Nibble";
    }
}
