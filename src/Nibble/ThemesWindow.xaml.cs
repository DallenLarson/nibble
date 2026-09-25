using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nibble.Controls;
using Nibble.Services;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Nibble;

/// <summary>
/// The theme shop: every theme Nibble ships plus anything installed, as cards with a
/// miniature of the theme painted from its own colours. Apply, import, export, remove.
/// </summary>
public partial class ThemesWindow : Window
{
    private const int CardWidth = 268;

    public ThemesWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.RoundCorners(this);
        Loaded += (_, _) => Build();
    }

    private void Build()
    {
        BuiltInList.Children.Clear();
        InstalledList.Children.Clear();

        var themes = Themes.All();
        var builtIn = themes.Where(t => t.BuiltIn).ToList();
        var installed = themes.Where(t => !t.BuiltIn).ToList();

        foreach (var theme in builtIn) BuiltInList.Children.Add(Card(theme));

        InstalledLabel.Visibility = installed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var theme in installed) InstalledList.Children.Add(Card(theme));

        var current = Themes.CurrentId;
        var name = Themes.Find(current)?.Name;
        Subtitle.Text = name is null
            ? "Pick a look — it applies to the whole browser."
            : $"On now: {name} · {themes.Count} theme{(themes.Count == 1 ? "" : "s")} available";
    }

    // =====================================================================
    //  a card
    // =====================================================================

    private UIElement Card(ThemeManifest theme)
    {
        var card = new PixelPanel
        {
            Width = CardWidth,
            Margin = new Thickness(0, 0, 14, 14),
            CornerSteps = 3,
            CornerStep = 3,
            StrokeThickness = 1,
            Effect = FindResource("CardShadow") as System.Windows.Media.Effects.Effect
        };
        card.SetResourceReference(PixelPanel.FillProperty, "CardBg");
        card.SetResourceReference(PixelPanel.StrokeProperty,
            Themes.CurrentId == theme.Id ? "Accent" : "Hairline");

        var stack = new StackPanel();
        stack.Children.Add(Preview(theme));
        stack.Children.Add(Text(theme.Name, 14, "Ink", FontWeights.SemiBold, new Thickness(14, 10, 14, 0)));

        if (!string.IsNullOrWhiteSpace(theme.Tagline))
            stack.Children.Add(Text(theme.Tagline, 11.5, "Ink2", FontWeights.Normal, new Thickness(14, 3, 14, 0),
                wrap: true));

        stack.Children.Add(Text(
            string.IsNullOrWhiteSpace(theme.Author) ? "by Nibble" : $"by {theme.Author}",
            10.5, "Ink3", FontWeights.Normal, new Thickness(14, 6, 14, 0)));

        var applied = Themes.CurrentId == theme.Id;
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 12, 14, 14)
        };

        var apply = new Button
        {
            Content = applied ? "Applied" : "Apply",
            Style = (Style)FindResource(applied ? "GhostButton" : "PrimaryButton"),
            Height = 32,
            MinWidth = 96,
            IsEnabled = !applied
        };
        AutomationProperties.SetName(apply, $"Apply {theme.Name}");
        apply.Click += (_, _) => Apply(theme);
        actions.Children.Add(apply);

        if (!theme.BuiltIn)
        {
            var remove = new Button
            {
                Content = "Remove",
                Style = (Style)FindResource("GhostButton"),
                Height = 32,
                MinWidth = 90,
                Margin = new Thickness(8, 0, 0, 0)
            };
            AutomationProperties.SetName(remove, $"Remove {theme.Name}");
            remove.Click += (_, _) =>
            {
                if (Themes.Remove(theme.Id))
                {
                    Status($"Removed “{theme.Name}”.");
                    Build();
                }
            };
            actions.Children.Add(remove);
        }

        stack.Children.Add(actions);
        card.Child = stack;
        return card;
    }

    private static TextBlock Text(string value, double size, string resource, FontWeight weight,
        Thickness margin, bool wrap = false)
    {
        var block = new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = weight,
            Margin = margin,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, resource);
        return block;
    }

    /// <summary>A miniature of the theme's home page, painted from its own preview palette.</summary>
    private UIElement Preview(ThemeManifest theme)
    {
        var sky = Themes.Brush(theme.Preview.Sky) ?? Brushes.White;
        var ground = Themes.Brush(theme.Preview.Ground) ?? Brushes.Gray;
        var button = Themes.Brush(theme.Preview.Button) ?? Brushes.White;
        var ink = Themes.Brush(theme.Preview.Text) ?? Brushes.Black;
        var accent = Themes.Brush(theme.Preview.Accent) ?? Brushes.LimeGreen;

        var host = new Grid { Height = 132, ClipToBounds = true };
        host.Children.Add(new Rectangle { Fill = sky });

        // the ground band: the theme's own texture when it has one, flat colour otherwise
        var band = new Grid { Height = 44, VerticalAlignment = VerticalAlignment.Bottom };
        band.Children.Add(new Rectangle { Fill = ground });
        var texture = TextureImage(theme);
        if (texture is not null) band.Children.Add(texture);
        host.Children.Add(band);

        // a mock search bar and a mock button, so the card reads as the home page
        var bar = new Rectangle
        {
            Height = 22,
            Width = 168,
            RadiusX = 3,
            RadiusY = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -18, 0, 18),
            Fill = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0))
        };
        host.Children.Add(bar);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 58)
        };
        for (var i = 0; i < 3; i++)
        {
            var tile = new PixelPanel
            {
                Width = 40,
                Height = 30,
                Margin = new Thickness(0, 0, 7, 0),
                CornerSteps = 2,
                CornerStep = 3,
                StrokeThickness = 1,
                Fill = button,
                Stroke = accent
            };
            row.Children.Add(tile);
        }
        host.Children.Add(row);

        // a mock clock, in the theme's text colour
        var clock = new TextBlock
        {
            Text = "11:11",
            FontFamily = (FontFamily)FindResource("PixelFont"),
            FontSize = 20,
            Foreground = ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -46, 0, 40),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 6,
                ShadowDepth = 1,
                Opacity = 0.35,
                Color = Colors.Black
            }
        };
        host.Children.Add(clock);

        var accentBar = new Rectangle
        {
            Height = 3,
            Fill = accent,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        host.Children.Add(accentBar);
        return host;
    }

    private static Image? TextureImage(ThemeManifest theme)
    {
        if (string.IsNullOrWhiteSpace(theme.Preview.Texture)) return null;
        var path = Path.Combine(Pages.Folder, "themes", theme.Id,
            theme.Preview.Texture.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();

            var element = new Image
            {
                Source = image,
                Stretch = Stretch.UniformToFill,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            // block textures must stay crisp when they are scaled up
            RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.NearestNeighbor);
            return element;
        }
        catch
        {
            return null;
        }
    }

    // =====================================================================
    //  actions
    // =====================================================================

    private void Apply(ThemeManifest theme)
    {
        Themes.Apply(theme.Id);
        Status($"“{theme.Name}” applied.");
        Build();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Install a Nibble theme",
            Filter = "Nibble theme (*.json;*.nibbletheme)|*.json;*.nibbletheme|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var (ok, message) = Themes.Import(dialog.FileName);
        Status(message);
        if (ok) Build();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var theme = Themes.Current;
        if (theme is null)
        {
            Status("Switch to a theme first, then export it.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export theme",
            FileName = $"{theme.Id}.nibbletheme.json",
            Filter = "Nibble theme (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true) return;

        Status(Themes.Export(theme.Id, dialog.FileName)
            ? $"Exported “{theme.Name}” — edit the json, then install it back."
            : "Could not write that file.");
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Themes.Folder);
            Process.Start(new ProcessStartInfo(Themes.Folder) { UseShellExecute = true });
        }
        catch
        {
            Status("Could not open the themes folder.");
        }
    }

    private void Status(string message) => StatusText.Text = message;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
