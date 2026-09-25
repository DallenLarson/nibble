using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nibble.Controls;

// Renders a contact sheet of every icon Nibble uses, so the set can be checked without
// launching the browser (which needs the WebView2 profile).
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var outPath = args.Length > 0 ? args[0] : "icon-set.png";

        // ---- diagnostic: do icons inherit the theme brush, or fall back to black? ----
        var app = new Application();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Nibble;component/Themes/{theme}.xaml", UriKind.Absolute)
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Nibble;component/Themes/Controls.xaml", UriKind.Absolute)
            });

            var icon = new VectorIcon { Data = Icons.Back, Size = 18 };
            var button = new Button { Style = (Style)app.Resources["IconButton"], Content = icon };
            var loose = new VectorIcon { Data = Icons.Back, Size = 18 };
            var probe = new StackPanel();
            probe.Children.Add(button);
            probe.Children.Add(loose);
            probe.Measure(new Size(200, 200));
            probe.Arrange(new Rect(0, 0, 200, 200));
            probe.UpdateLayout();

            var ink2 = app.TryFindResource("Ink2") as SolidColorBrush;
            Console.WriteLine($"{theme}: theme Ink2={ink2?.Color} | icon in button={Describe(icon)} | loose icon={Describe(loose)}");
            var tints = new[] { "CloseTint", "MinTint", "MaxTint", "Hover" }
                .Select(key => $"{key}={(app.TryFindResource(key) as SolidColorBrush)?.Color.ToString() ?? "MISSING"}");
            Console.WriteLine($"{theme}: window-control hovers -> {string.Join(", ", tints)}");
        }

        static string Describe(VectorIcon icon) =>
            icon.Foreground is SolidColorBrush brush ? brush.Color.ToString() : icon.Foreground.ToString();

        var ink = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F));
        var muted = new SolidColorBrush(Color.FromRgb(0x86, 0x86, 0x8B));
        var accent = new SolidColorBrush(Color.FromRgb(0xA3, 0xE6, 0x35));

        var entries = new (string Name, string Data, bool Filled, Brush Brush, double Size, double VbW, double VbH)[]
        {
            ("back", Icons.Back, false, ink, 20, 24, 24),
            ("forward", Icons.Forward, false, ink, 20, 24, 24),
            ("reload", Icons.Reload, false, ink, 20, 24, 24),
            ("home", Icons.Home, false, ink, 20, 24, 24),
            ("close", Icons.Close, false, ink, 20, 24, 24),
            ("plus", Icons.Plus, false, ink, 20, 24, 24),
            ("min", Icons.Minimize, false, ink, 20, 24, 24),
            ("max", Icons.Maximize, false, ink, 20, 24, 24),
            ("menu", Icons.Menu, false, ink, 20, 24, 24),
            ("search", Icons.Search, false, ink, 20, 24, 24),
            ("star", Icons.Star, false, ink, 20, 24, 24),
            ("star filled", Icons.Star, true, accent, 20, 24, 24),
            ("lock", Icons.Lock, false, ink, 20, 24, 24),
            ("shield", Icons.Shield, true, accent, 20, 24, 24),
            ("globe", Icons.Globe, false, ink, 20, 24, 24),
            ("download", Icons.Download, false, ink, 20, 24, 24),
            ("theme", Icons.Theme, false, ink, 20, 24, 24),
            ("sparkles", Icons.Sparkle, false, ink, 20, 24, 24),
            ("check", Icons.Check, false, ink, 20, 24, 24),
            ("user", Icons.Person, false, ink, 20, 24, 24),
            ("palette", Icons.Palette, false, ink, 20, 24, 24),
            ("retry", Icons.Retry, false, ink, 20, 24, 24),
            ("external", Icons.External, false, ink, 20, 24, 24),
            ("copy", Icons.Copy, false, ink, 20, 24, 24),
            ("offline", Icons.Offline, false, muted, 20, 24, 24),
            ("google", Icons.BrandGoogle, true, new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4)), 24, 24, 24),
            ("duckduckgo", Icons.BrandDuckDuckGo, true, new SolidColorBrush(Color.FromRgb(0xDE, 0x58, 0x33)), 24, 24, 24),
            ("brave", Icons.BrandBrave, true, new SolidColorBrush(Color.FromRgb(0xFB, 0x54, 0x2B)), 24, 24, 24),
            ("bing", Icons.BrandBing, true, new SolidColorBrush(Color.FromRgb(0x00, 0x83, 0x73)), 24, 256, 388)
        };

        const int columns = 6;
        const double cell = 104;
        var rows = (int)Math.Ceiling(entries.Length / (double)columns);
        var width = columns * cell;
        var height = rows * cell + 58;

        var root = new Grid { Width = width, Height = height };
        root.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

        var title = new TextBlock
        {
            Text = "Nibble icon set — Lucide (ISC) for UI, Simple Icons / SVG Logos (CC0) for brand marks",
            FontSize = 13,
            Foreground = ink,
            Margin = new Thickness(18, 16, 18, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        root.Children.Add(title);

        var grid = new UniformGrid { Columns = columns, Rows = rows, Margin = new Thickness(10, 48, 10, 0) };
        foreach (var entry in entries)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new VectorIcon
            {
                Data = entry.Data,
                Size = entry.Size,
                Filled = entry.Filled,
                ViewBoxWidth = entry.VbW,
                ViewBoxHeight = entry.VbH,
                Foreground = entry.Brush,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = entry.Name,
                FontSize = 10.5,
                Foreground = muted,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            grid.Children.Add(stack);
        }
        root.Children.Add(grid);

        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outPath);
        encoder.Save(stream);

        Console.WriteLine($"wrote {outPath} ({(int)width}x{(int)height}, {entries.Length} icons)");
        return 0;
    }
}
