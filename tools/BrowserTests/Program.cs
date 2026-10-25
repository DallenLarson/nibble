using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using Microsoft.Web.WebView2.Core;
using Nibble;
using Nibble.Services;

internal static class Program
{
    static int failures;
    static void Check(string name, bool passed)
    {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
        if (!passed) failures++;
    }
    static object? Call(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    static async Task Until(Func<Task<bool>> predicate, int timeout = 15000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < end) { if (await predicate()) return; await Task.Delay(100); }
        throw new TimeoutException("Browser condition did not settle.");
    }
    [STAThread]
    static int Main(string[] args)
    {
        var profile = Path.Combine(Path.GetTempPath(), "NibbleBrowserTests-" + Guid.NewGuid());
        Environment.SetEnvironmentVariable("NIBBLE_PROFILE", profile);
        Directory.CreateDirectory(profile);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Nibble;component/Themes/Light.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Nibble;component/Themes/Controls.xaml", UriKind.Relative) });
        var window = new MainWindow(restoreSession: false) { ShowActivated = false, ShowInTaskbar = false, Left = -10000 };
        app.DispatcherUnhandledException += (_, e) => { Console.WriteLine(e.Exception); e.Handled = true; failures++; app.Shutdown(1); };
        window.Show();
        window.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await Until(() => Task.FromResult(window.Tabs.Count > 0 && window.Tabs[0].IsReady && !window.Tabs[0].IsLoading));
                var tab = window.Tabs[0];
                var core = tab.Core!;
                async Task<string> Js(string code) => await core.ExecuteScriptAsync(code);
                async Task Capture(string name)
                {
                    await Task.Delay(150);
                    await using var file = File.Create(Path.Combine(profile, name + ".png"));
                    await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, file);
                }
                async Task Go(string url)
                {
                    var done = new TaskCompletionSource();
                    void Completed(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
                    { if (e.NavigationId == tab.Navigation.Id) done.TrySetResult(); }
                    core.NavigationCompleted += Completed;
                    try { Call(window, "Navigate", tab, url); await done.Task.WaitAsync(TimeSpan.FromSeconds(25)); }
                    finally { core.NavigationCompleted -= Completed; }
                    await Task.Delay(250);
                }
                await Until(async () => await Js("!!document.querySelector('.planner-links')") == "true");
                Check("home offers notes and calendar", await Js("document.querySelectorAll('.planner-links a').length") == "2");

                var background = (ZTab)Call(window, "NewTab", "about:blank", false)!;
                await Until(() => Task.FromResult(background.IsReady && !background.IsLoading));
                var strip = (ItemsControl)window.FindName("TabStrip");
                strip.UpdateLayout();
                var tabAnchor = (FrameworkElement)strip.ItemContainerGenerator.ContainerFromItem(background);
                var tabMenu = (StackPanel)window.FindName("MenuList");
                void RightClickTab()
                {
                    Call(window, "Tab_PreviewMouseRightButtonUp", tabAnchor,
                        new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                            { RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent });
                }
                Button MenuItem(string label) => tabMenu.Children.OfType<Button>()
                    .Single(b => AutomationProperties.GetName(b) == label);
                RightClickTab();
                MenuItem("Mute tab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => Task.FromResult(background.Core!.IsMuted && background.IsMuted));
                Check("right-click mute targets background tab without switching tabs",
                    tab.IsActive && !background.IsActive && !core.IsMuted);
                background.Core!.Navigate("data:text/html,<title>Muted navigation</title>");
                await Until(() => Task.FromResult(background.Title == "Muted navigation"));
                Check("tab stays muted after navigation", background.Core.IsMuted && background.IsMuted);
                RightClickTab();
                MenuItem("Unmute tab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => Task.FromResult(!background.Core.IsMuted && !background.IsMuted));
                Check("right-click offers unmute and restores audio", !core.IsMuted);
                Call(window, "ToggleMuteSite");
                await Until(() => Task.FromResult(core.IsMuted && tab.IsMuted));
                Check("existing mute command updates tab indicator state", !background.Core.IsMuted);
                Call(window, "ToggleMuteSite");
                await Until(() => Task.FromResult(!core.IsMuted && !tab.IsMuted));
                Call(window, "CloseTab", background, false);

                var omni = (TextBox)window.FindName("Omni");
                Call(window, "ShowSuggestions", "example.com");
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                Call(window, "Omni_PreviewKeyDown", omni, key);
                var rows = (List<Button>)typeof(MainWindow).GetField("_omniRows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                var row = rows[0]; row.ApplyTemplate();
                var panel = (Nibble.Controls.PixelPanel)row.Template.FindName("panel", row);
                Check("arrow selection is visibly highlighted", Equals(panel.Fill, row.Background) && Equals(row.Background, window.FindResource("AccentSoft")));
                Call(window, "HideSuggestions");

                await Go("nibble://notes");
                await Until(async () => await Js("document.getElementById('status').textContent") == "\"Saved on this device.\"");
                await Js("document.getElementById('newNote').click();document.getElementById('noteTitle').value='Browser test';document.getElementById('noteTitle').dispatchEvent(new Event('input'));document.getElementById('noteBody').value='Saved text <script>literal</script>';document.getElementById('noteBody').dispatchEvent(new Event('input'))");
                await Until(() => Task.FromResult(PlannerStore.Shared.Read().Notes.Any(n => n.Body.Contains("literal"))));
                Check("notes bridge saves real input", PlannerStore.Shared.Read().Notes.Single().Title == "Browser test");
                await Go("about:blank");
                await Go("nibble://notes");
                await Until(async () => (await Js("document.getElementById('noteBody').value")).Contains("literal"));
                Check("notes survive page reload", true);
                await Capture("notes");
                // Keep the fixture on the selected day so editing works on any test date.
                await Js("document.getElementById('calendarTab').click();document.getElementById('newEvent').click();document.getElementById('eventTitle').value='Meeting';document.getElementById('eventForm').requestSubmit()");
                await Until(() => Task.FromResult(PlannerStore.Shared.Read().Events.Count == 1));
                Check("calendar saves dated time range", PlannerStore.Shared.Read().Events[0].Start.EndsWith("T09:00") &&
                    PlannerStore.Shared.Read().Events[0].End.EndsWith("T10:00"));
                await Js("document.getElementById('nextMonth').click();document.getElementById('prevMonth').click()");
                Check("calendar renders a full month grid", await Js("document.querySelectorAll('#days button').length") == "42");
                await Capture("calendar");
                window.Width = 680;
                await Capture("calendar-narrow");
                Check("calendar fits a narrow window", await Js("document.documentElement.scrollWidth<=innerWidth") == "true");
                window.Width = 1220;
                await Js("document.querySelector('.event-row').click();document.getElementById('eventTitle').value='Updated meeting';document.getElementById('eventForm').requestSubmit()");
                await Until(() => Task.FromResult(PlannerStore.Shared.Read().Events.Single().Title == "Updated meeting"));
                Check("editing updates an existing event", PlannerStore.Shared.Read().Events.Count == 1);
                await Js("document.querySelector('.event-row').click();document.getElementById('deleteEvent').click();document.getElementById('removeItem').click()");
                await Until(() => Task.FromResult(PlannerStore.Shared.Read().Events.Count == 0));
                Check("confirmed event deletion persists", true);

                var html = Path.Combine(profile, "fixture.html");
                await File.WriteAllTextAsync(html, "<html><body><h1>PDF test document</h1><p>This file should open inside Nibble.</p></body></html>");
                await Go(new Uri(html).AbsoluteUri);
                // A web page must not be able to read the notebook through the host bridge.
                await Js("window.plannerLeak=false;chrome.webview.addEventListener('message',e=>{if(e.data.type==='planner-state')window.plannerLeak=true});chrome.webview.postMessage({type:'planner',action:'load'})");
                await Task.Delay(250);
                Check("untrusted pages cannot access planner", await Js("window.plannerLeak") == "false");
                var pdf = Path.Combine(profile, "Test document.pdf");
                Check("PDF fixture created", await core.PrintToPdfAsync(pdf));
                await Go(pdf);
                await Until(async () => await Js("!!document.querySelector('embed[type=\"application/pdf\"]')") == "true");
                Check("local PDF is displayed by the engine", !tab.IsBroken && core.Source.EndsWith("Test%20document.pdf"));

                using var server = new FixtureServer { Pdf = await File.ReadAllBytesAsync(pdf) };
                await Go(server.Url + "/test.pdf");
                await Until(async () => await Js("!!document.querySelector('embed[type=\"application/pdf\"]')") == "true");
                Check("online PDF opens inside Nibble", !tab.IsBroken);
                await Capture("pdf");
                await Go(server.Url + "/ok");
                await Js("window.retainedState='still here'");
                foreach (var kind in new[] { CoreWebView2ProcessFailedKind.UtilityProcessExited,
                    CoreWebView2ProcessFailedKind.GpuProcessExited, CoreWebView2ProcessFailedKind.FrameRenderProcessExited,
                    CoreWebView2ProcessFailedKind.RenderProcessUnresponsive })
                {
                    Call(window, "HandleProcessFailure", tab, core, kind);
                    await Task.Delay(100);
                    Check($"{kind} preserves the page and its state", !tab.IsBroken &&
                        core.Source == server.Url + "/ok" && await Js("window.retainedState") == "\"still here\"");
                }
                Call(window, "HandleProcessFailure", tab, core, CoreWebView2ProcessFailedKind.RenderProcessExited);
                await Until(() => Task.FromResult(Pages.IsErrorPage(core.Source)));
                Check("main renderer failure still shows recovery page", tab.IsBroken);
                await Go(server.Url + "/ok");

                var menu = (StackPanel)window.FindName("MenuList");
                string DownloadLabel() => menu.Children.OfType<Button>().Select(AutomationProperties.GetName)
                    .FirstOrDefault(n => n.StartsWith("progress.bin")) ?? "";
                ProgressBar? DownloadBar() => menu.Children.OfType<Button>().Select(b => b.Content)
                    .OfType<StackPanel>().SelectMany(p => p.Children.OfType<ProgressBar>()).FirstOrDefault();
                CoreWebView2DownloadOperation? transfer = null;
                void SaveDownload(object? sender, CoreWebView2DownloadStartingEventArgs e)
                {
                    transfer = e.DownloadOperation;
                    e.ResultFilePath = Path.Combine(profile, Guid.NewGuid() + ".bin");
                    e.Handled = true;
                }
                core.DownloadStarting += SaveDownload;
                foreach (var route in new[] { "/progress", "/unknown-size", "/interrupted" })
                {
                    transfer = null;
                    Call(window, "Navigate", tab, server.Url + route);
                    await Until(() => Task.FromResult(transfer is not null && transfer.BytesReceived > 0));
                    Call(window, "Downloads_Click", window, new RoutedEventArgs());
                    await Until(() => Task.FromResult(DownloadBar() is { Visibility: Visibility.Visible }));
                    if (route == "/unknown-size")
                        Check("unknown download size shows indeterminate progress and bytes", DownloadBar()!.IsIndeterminate && DownloadLabel().Contains("received"));
                    else
                        Check("downloads menu shows percent and received/total bytes", DownloadLabel().Contains('%') && DownloadLabel().Contains(" / ") && DownloadBar()!.Value > 0);
                    var initial = DownloadLabel();
                    await Until(() => Task.FromResult(DownloadLabel() != initial));
                    Check("download progress updates while menu stays open", true);
                    if (route == "/progress")
                    {
                        var visual = (FrameworkElement)((System.Windows.Controls.Primitives.Popup)window.FindName("MenuPopup")).Child;
                        visual.UpdateLayout();
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                            (int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight),
                            96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(visual);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var image = File.Create(Path.Combine(profile, "download-progress.png"));
                        encoder.Save(image);
                    }
                    await Until(() => Task.FromResult(transfer!.State != CoreWebView2DownloadState.InProgress));
                    await Task.Delay(100);
                    Check(route + " has correct final status", route == "/interrupted"
                        ? DownloadLabel().Contains("Interrupted") && !DownloadLabel().Contains("Open file")
                        : DownloadLabel().Contains("Completed") && DownloadLabel().Contains("Open file"));
                    Check("finished download hides progress bar", DownloadBar()!.Visibility == Visibility.Collapsed);
                    Call(window, "ClosePopups", false);
                    Call(window, "Downloads_Click", window, new RoutedEventArgs());
                    Check("reopening downloads preserves final status", DownloadLabel().Contains(route == "/interrupted" ? "Interrupted" : "Completed"));
                    Call(window, "ClosePopups", false);
                }
                core.DownloadStarting -= SaveDownload;
                await Js("document.getElementById('link').click()");
                await Until(() => Task.FromResult(!tab.IsLoading && core.Source == server.Url + "/ok"));
                Check("clicked redirect preserves its destination", !tab.IsBroken);
                Call(window, "Navigate", tab, server.Url + "/slow");
                await Task.Delay(100);
                await Go(server.Url + "/missing");
                await Task.Delay(1700);
                Check("superseded navigation cannot replace a newer HTTP response", !tab.IsBroken && core.Source == server.Url + "/missing");
                var downloadSeen = false;
                core.DownloadStarting += (_, e) => { downloadSeen = true; e.Cancel = true; };
                Call(window, "Navigate", tab, server.Url + "/download");
                await Until(() => Task.FromResult(downloadSeen));
                await Task.Delay(300);
                Check("download handoff is not a connection failure", !tab.IsBroken && !Pages.IsErrorPage(core.Source));
                await Go(server.Url + "/reset");
                await Until(() => Task.FromResult(tab.IsBroken));
                Check("real connection failure still shows an error for the right URL", tab.Url == server.Url + "/reset");

                await Go(new Uri(html).AbsoluteUri);
                var slow = Path.Combine(profile, "redirect.html");
                await File.WriteAllTextAsync(slow, "<html><body><script>location.replace('fixture.html')</script></body></html>");
                await Go(new Uri(slow).AbsoluteUri);
                await Until(() => Task.FromResult(core.Source == new Uri(html).AbsoluteUri));
                Check("redirects keep the destination page", !tab.IsBroken);

                var privateWindow = new MainWindow(privateWindow: true, restoreSession: false, startUrl: "nibble://notes")
                    { Left = -10000, ShowActivated = false, ShowInTaskbar = false };
                privateWindow.Show();
                await Until(() => Task.FromResult(privateWindow.Tabs.Count > 0 && privateWindow.Tabs[0].IsReady));
                var privateCore = privateWindow.Tabs[0].Core!;
                await Until(async () => (await privateCore.ExecuteScriptAsync("document.getElementById('storage')?.textContent")).Contains("Private window"));
                Check("private notebook does not expose saved notes", await privateCore.ExecuteScriptAsync("document.querySelectorAll('#noteList button').length") == "0");
                await privateCore.ExecuteScriptAsync("document.getElementById('newNote').click();document.getElementById('noteTitle').value='Private thought';document.getElementById('noteTitle').dispatchEvent(new Event('input'))");
                await Until(async () => (await privateCore.ExecuteScriptAsync("document.getElementById('status').textContent")).Contains("Kept in this private window"));
                Check("private edits never reach persistent planner", PlannerStore.Shared.Read().Notes.All(n => n.Title != "Private thought"));
                foreach (var t in privateWindow.Tabs) t.View.Dispose();
                privateWindow.Hide();

                if (args.Contains("--web"))
                {
                    await Go("https://www.google.com/search?q=nibble+browser");
                    Check("Google results are not replaced by Nibble errors", !tab.IsBroken && !Pages.IsErrorPage(core.Source));
                    await Go("https://docs.google.com/document/");
                    Check("Google Docs navigation is not replaced by Nibble errors", !tab.IsBroken && !Pages.IsErrorPage(core.Source));
                    Console.WriteLine("Google Docs final host: " + new Uri(core.Source).Host + " (signed-out test profile)");
                }
                Console.WriteLine($"Browser integration failures: {failures}. Test profile: {profile}");
            }
            catch (Exception e) { failures++; Console.WriteLine(e); }
            finally
            {
                foreach (var tab in window.Tabs) tab.View.Dispose();
                app.Shutdown(failures == 0 ? 0 : 1);
            }
        }));
        return app.Run();
    }
}
