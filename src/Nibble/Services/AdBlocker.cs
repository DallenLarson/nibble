namespace Nibble.Services;

/// <summary>
/// A deliberately tiny request blocker: a compact host list for the worst ad and
/// tracker networks plus a few path patterns. Trivial cost, surprising payoff.
/// </summary>
public static class AdBlocker
{
    private static readonly string[] BlockedHosts =
    [
        "doubleclick.net", "googleadservices.com", "googlesyndication.com", "google-analytics.com",
        "googletagmanager.com", "googletagservices.com", "adservice.google.com", "analytics.google.com",
        "scorecardresearch.com", "quantserve.com", "criteo.com", "criteo.net", "outbrain.com",
        "taboola.com", "adsrvr.org", "adnxs.com", "rubiconproject.com", "pubmatic.com", "openx.net",
        "casalemedia.com", "smartadserver.com", "33across.com", "sharethrough.com", "teads.tv",
        "adform.net", "bidswitch.net", "bluekai.com", "demdex.net", "everesttech.net", "exelator.com",
        "hotjar.com", "mixpanel.com", "segment.io", "segment.com", "amplitude.com", "fullstory.com",
        "mouseflow.com", "crazyegg.com", "luckyorange.com", "newrelic.com", "nr-data.net",
        "bugsnag.com", "sentry.io", "branch.io", "appsflyer.com", "adjust.com", "kochava.com",
        "chartbeat.com", "parsely.com", "permutive.com", "moatads.com", "doubleverify.com",
        "adsafeprotected.com", "serving-sys.com", "sizmek.com", "flashtalking.com", "mathtag.com",
        "tremorhub.com", "yieldmo.com", "indexww.com", "spotxchange.com", "springserve.com",
        "inmobi.com", "mopub.com", "applovin.com", "unityads.unity3d.com", "vungle.com",
        "adcolony.com", "chartboost.com", "supersonicads.com", "ironsrc.com", "tapjoy.com",
        "facebook.net", "connect.facebook.net", "pixel.facebook.com", "bat.bing.com",
        "clarity.ms", "c.clarity.ms", "ads.linkedin.com", "px.ads.linkedin.com", "analytics.tiktok.com",
        "ads.pinterest.com", "ct.pinterest.com", "analytics.yahoo.com", "ads.yahoo.com",
        "adroll.com", "taboolasyndication.com", "revcontent.com", "mgid.com", "zergnet.com",
        "popads.net", "propellerads.com", "onclickads.net", "adcash.com", "exoclick.com",
        "trafficjunky.net", "juicyads.com", "hilltopads.net", "clickadu.com", "adsterra.com",
        "media.net", "bidvertiser.com", "infolinks.com", "adblade.com", "sonobi.com",
        "ads-twitter.com", "static.ads-twitter.com", "analytics.spotify.com", "log.byteoversea.com",
        "tracking.hubspot.com", "hs-analytics.net", "hs-scripts.com", "marketo.net", "mktoresp.com",
        "pardot.com", "salesforceiq.com", "6sense.com", "clearbit.com", "rb2b.com",
        "adtelligent.com", "smadex.com", "loopme.me", "thetradedesk.com", "adsrvr.org"
    ];

    private static readonly string[] BlockedPathBits =
    [
        "/ads/", "/adserver", "/advert", "/banner", "/pagead", "/popunder", "/prebid",
        "/analytics.js", "/gtag/js", "/tag.js", "/collect?", "/pixel?", "/beacon?",
        "/track?", "/tracking?", "/telemetry", "/metrics?"
    ];

    private static readonly HashSet<string> HostSet = new(BlockedHosts, StringComparer.OrdinalIgnoreCase);

    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Blocks only when the host matches a known ad/tracker network. Path patterns are
    /// skipped for the site you are actually looking at, so a site's own "/pixel?" or
    /// "/track?" endpoints are never mistaken for third-party trackers.
    /// </summary>
    public static bool ShouldBlock(string url, string resourceContext, string? topLevelHost = null)
    {
        if (!Enabled) return false;

        // Never block the page itself, and never block document navigations.
        if (string.Equals(resourceContext, "Document", StringComparison.OrdinalIgnoreCase)) return false;

        Uri uri;
        try { uri = new Uri(url); }
        catch { return false; }

        if (uri.Scheme is not ("http" or "https")) return false;

        var host = uri.Host;
        foreach (var blocked in HostSet)
        {
            if (host.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Generic patterns only apply to third-party sub-resources of script/beacon-ish types.
        if (resourceContext is "Script" or "XmlHttpRequest" or "Fetch" or "Image" or "Other")
        {
            if (!string.IsNullOrEmpty(topLevelHost) &&
                host.Equals(topLevelHost, StringComparison.OrdinalIgnoreCase))
                return false;

            var path = uri.PathAndQuery.ToLowerInvariant();
            foreach (var bit in BlockedPathBits)
            {
                if (path.Contains(bit, StringComparison.Ordinal)) return true;
            }
        }

        return false;
    }

    /// <summary>Rough payload estimate so the "saved" counter means something.</summary>
    public static long EstimateSavedBytes(string resourceContext) => resourceContext switch
    {
        "Script" => 52_000,
        "Stylesheet" => 11_000,
        "Image" => 24_000,
        "Media" => 380_000,
        "Font" => 30_000,
        "XmlHttpRequest" => 7_000,
        "Fetch" => 7_000,
        _ => 14_000
    };
}
