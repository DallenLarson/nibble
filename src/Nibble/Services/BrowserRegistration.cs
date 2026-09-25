using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Nibble.Services;

/// <summary>
/// Makes Nibble a first-class Windows browser: a ProgID for web pages, the Start-menu
/// internet-client capabilities Windows looks for, and registration under
/// RegisteredApplications. Windows 10 and 11 deliberately refuse to let an app make
/// itself the default — only the user can choose, in Settings — so Nibble registers
/// itself and then opens the right page there.
/// </summary>
public static class BrowserRegistration
{
    public const string ProgId = "NibbleHTML";
    private const string ClientPath = @"Software\Clients\StartMenuInternet\Nibble";
    private const string CapabilitiesPath = ClientPath + @"\Capabilities";
    private const string ClassesPath = @"Software\Classes\";

    /// <summary>True when Windows currently opens http/https with Nibble.</summary>
    public static bool IsDefaultBrowser()
    {
        return IsDefaultFor("http") && IsDefaultFor("https");
    }

    private static bool IsDefaultFor(string scheme)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}\UserChoice");
            return string.Equals(key?.GetValue("ProgId") as string, ProgId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the per-user registry entries that make Nibble offerable as a default
    /// browser. Everything lands under HKCU: no admin rights, nothing machine-wide.
    /// </summary>
    public static bool Register(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return false;
        var command = $"\"{exePath}\" -- \"%1\"";

        try
        {
            using (var prog = Registry.CurrentUser.CreateSubKey(ClassesPath + ProgId))
            {
                prog.SetValue(null, "Nibble HTML Document");
                prog.SetValue("FriendlyTypeName", "Nibble HTML Document");
                prog.SetValue("URL Protocol", string.Empty);
                using var icon = prog.CreateSubKey("DefaultIcon");
                icon.SetValue(null, $"{exePath},0");
                using var open = prog.CreateSubKey(@"shell\open\command");
                open.SetValue(null, command);
            }

            using (var client = Registry.CurrentUser.CreateSubKey(ClientPath))
            {
                client.SetValue(null, "Nibble");
                using var icon = client.CreateSubKey("DefaultIcon");
                icon.SetValue(null, $"{exePath},0");
                using var open = client.CreateSubKey(@"shell\open\command");
                open.SetValue(null, $"\"{exePath}\"");
            }

            using (var caps = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
            {
                caps.SetValue("ApplicationName", "Nibble");
                caps.SetValue("ApplicationDescription",
                    "A tiny, tasty pixel-perfect browser that rides on the WebView2 engine Windows already ships.");
                caps.SetValue("ApplicationIcon", $"{exePath},0");
                caps.SetValue("StartMenuInternet", "Nibble.exe");

                using var files = caps.CreateSubKey("FileAssociations");
                files.SetValue(".htm", ProgId);
                files.SetValue(".html", ProgId);
                files.SetValue(".xhtml", ProgId);

                using var urls = caps.CreateSubKey("URLAssociations");
                urls.SetValue("http", ProgId);
                urls.SetValue("https", ProgId);
            }

            using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                registered.SetValue("Nibble", CapabilitiesPath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Takes Nibble back out of Windows' browser list. Called by the uninstaller before the
    /// files go, so nothing points at a directory that will not exist a second later.
    /// The per-user profile is deliberately left alone: a person's history and settings are
    /// not the installer's to delete. Uninstall offers that separately.
    /// </summary>
    public static bool Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ClassesPath + ProgId, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(ClientPath, throwOnMissingSubKey: false);
            using var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true);
            registered?.DeleteValue("Nibble", throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Opens the Windows page where a person picks their default browser. The optional
    /// deep-link argument highlights Nibble on Windows 11; Windows 10 ignores it.
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=Nibble")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
            }
            catch
            {
                Store.LogError("could not open the Windows default-apps page");
            }
        }
    }
}
