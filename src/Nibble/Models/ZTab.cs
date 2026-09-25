using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Nibble.Services;

namespace Nibble;

/// <summary>One browser tab: its engine view, its metadata, and its nap state.</summary>
public sealed class ZTab : INotifyPropertyChanged
{
    public ZTab(WebView2 view) => View = view;

    public WebView2 View { get; }
    public CoreWebView2? Core { get; set; }

    /// <summary>The engine's original user agent, kept so compatibility mode can be undone.</summary>
    public string? DefaultUserAgent { get; set; }

    /// <summary>Window handle of the page surface, used to watch for clicks that dismiss menus.</summary>
    public IntPtr PageHandle { get; set; }


    /// <summary>Logical address shown in the omnibox (may be nibble://newtab).</summary>
    private string _url = Urls.Home;
    public string Url
    {
        get => _url;
        set
        {
            if (Set(ref _url, value)) Raise(nameof(IsNewTab));
        }
    }

    public bool IsNewTab => string.Equals(Url, Urls.Home, StringComparison.OrdinalIgnoreCase);

    private string _title = "New tab";
    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    private ImageSource? _favicon;
    public ImageSource? Favicon
    {
        get => _favicon;
        set => Set(ref _favicon, value);
    }

    private bool _hasFavicon;
    public bool HasFavicon
    {
        get => _hasFavicon;
        set => Set(ref _hasFavicon, value);
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => Set(ref _isLoading, value);
    }

    private bool _isSleeping;
    public bool IsSleeping
    {
        get => _isSleeping;
        set => Set(ref _isSleeping, value);
    }

    /// <summary>Pinned tabs sit first, shrink to their favicon, and are never auto-closed.</summary>
    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set => Set(ref _isPinned, value);
    }

    /// <summary>Width in device-independent units; grows when few tabs are open.</summary>
    private double _tabWidth = 200;
    public double TabWidth
    {
        get => _tabWidth;
        set => Set(ref _tabWidth, value);
    }

    public int BlockedCount { get; set; }
    public long SavedBytes { get; set; }
    public List<string> BlockedHosts { get; } = [];
    public DateTime LastActive { get; set; } = DateTime.Now;
    public bool IsBroken { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
