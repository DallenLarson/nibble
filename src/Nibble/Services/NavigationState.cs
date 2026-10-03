using Microsoft.Web.WebView2.Core;

namespace Nibble.Services;

/// <summary>Correlates engine events; an older completion must never replace a newer page.</summary>
public sealed class NavigationState
{
    public ulong Id { get; private set; }
    public string Url { get; private set; } = "";
    public bool HasDocument { get; private set; }
    public bool IsDownload { get; private set; }

    public void Start(ulong id, string url)
    {
        if (Id != id) { HasDocument = false; IsDownload = false; }
        Id = id;
        Url = url;
    }

    public void Content(ulong id, bool errorPage)
    {
        if (id == Id) HasDocument = !errorPage;
    }

    public void Download(string url)
    {
        if (string.Equals(url, Url, StringComparison.OrdinalIgnoreCase)) IsDownload = true;
    }

    public bool ShouldShowError(ulong id, bool success, CoreWebView2WebErrorStatus error, int httpStatus) =>
        id == Id && !success && !HasDocument && !IsDownload && httpStatus < 400 &&
        error is not (CoreWebView2WebErrorStatus.OperationCanceled or CoreWebView2WebErrorStatus.ConnectionAborted);
}
