using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nibble.Controls;
using Nibble.Services;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Nibble;

/// <summary>
/// First-run setup: accent color, name, search engine — with a live preview of each answer.
/// Reopened from the menu as "Personalize Nibble".
/// </summary>
public partial class OnboardingWindow : Window
{
    public sealed record Result(string AccentHex, string UserName, string EngineId, string Clock, bool Completed);

    private readonly bool _editing;
    private readonly UIElement?[] _cache = new UIElement?[4];
    private readonly List<Button> _swatches = [];
    private readonly List<VectorIcon> _swatchChecks = [];
    private readonly List<Button> _engineCards = [];
    private readonly List<VectorIcon> _engineChecks = [];
    private readonly List<Button> _clockTiles = [];
    private readonly List<VectorIcon> _clockChecks = [];
    private readonly List<Rectangle> _blocks = [];

    private int _step;
    private string _accent;
    private string _name;
    private string _engine;
    private string _clock;
    private TextBlock? _greetingName;
    private Run? _previewDigits;
    private Run? _previewMeridiem;
    private DispatcherTimer? _previewTick;
    private UIElement? _current;

    public Result Outcome { get; private set; }

    public OnboardingWindow(string accentHex, string userName, string engineId, string clock, bool editing)
    {
        InitializeComponent();

        _editing = editing;
        _accent = accentHex;
        _name = userName;
        _engine = engineId;
        _clock = ClockFormat.Normalize(clock);
        Outcome = new Result(accentHex, userName, engineId, _clock, false);

        // The wordmark already says "Nibble", so the label only names the step collection.
        HeaderLabel.Text = editing ? "P E R S O N A L I Z E" : "S E T U P";
        SkipButton.Content = editing ? "Cancel" : "Skip";

        SourceInitialized += (_, _) => Native.RoundCorners(this);
        PreviewKeyDown += OnPreviewKeyDown;

        BuildProgress();
        ShowStep(0, forward: true, animate: false);
    }

    // =====================================================================
    //  chrome
    // =====================================================================

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                Advance();
                break;
            case Key.Escape:
                e.Handled = true;
                Cancel();
                break;
        }
    }

    private void BuildProgress()
    {
        for (var i = 0; i < 3; i++)
        {
            var block = new Rectangle
            {
                Width = 34,
                Height = 4,
                Margin = new Thickness(0, 0, 7, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                Opacity = 0.35
            };
            block.SetResourceReference(Rectangle.FillProperty, "Track");
            _blocks.Add(block);
            ProgressPanel.Children.Add(block);
        }
    }

    private void UpdateProgress()
    {
        for (var i = 0; i < _blocks.Count; i++)
        {
            var done = i <= Math.Min(_step, 2);
            var block = _blocks[i];
            block.SetResourceReference(Rectangle.FillProperty, done ? "Accent" : "Track");
            block.Opacity = done ? 1 : 0.35;
        }
    }

    private void UpdateFooter()
    {
        BackButton.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = _step == 3 ? "Start browsing" : "Next";
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 0) return;
        ShowStep(_step - 1, forward: false);
    }

    private void Next_Click(object sender, RoutedEventArgs e) => Advance();

    private void Advance()
    {
        if (_step >= 3)
        {
            Finish();
            return;
        }
        ShowStep(_step + 1, forward: true);
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Cancel();

    private void Cancel()
    {
        Outcome = new Result(_accent, _name, _engine, _clock, false);
        Close();
    }

    private void Finish()
    {
        Outcome = new Result(_accent, _name, _engine, _clock, true);
        Close();
    }

    // =====================================================================
    //  step mechanics
    // =====================================================================

    private void ShowStep(int index, bool forward, bool animate = true)
    {
        _step = Math.Clamp(index, 0, 3);
        var next = _cache[_step] ??= Build(_step);

        StepHost.Children.Clear();
        StepHost.Children.Add(next);
        _current = next;

        if (animate) AnimateIn(next, forward);
        else next.Opacity = 1;

        UpdateFooter();
        UpdateProgress();
    }

    private static void AnimateIn(UIElement element, bool forward)
    {
        // Steps slide in and settle with a volume-preserving stretch.
        if (element is FrameworkElement framework)
        {
            var squash = Juice.EnsureScale(framework);
            squash.ScaleX = 0.94;
            squash.ScaleY = 1.06;
            var stretch = new DoubleAnimation(1, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }
            };
            squash.BeginAnimation(ScaleTransform.ScaleXProperty, stretch, HandoffBehavior.SnapshotAndReplace);
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, stretch, HandoffBehavior.SnapshotAndReplace);
        }

        element.Opacity = 0;
        var transform = new TranslateTransform(forward ? 30 : -30, 0);
        if (element.RenderTransform is Transform existing && existing != Transform.Identity)
        {
            var group = new TransformGroup();
            group.Children.Add(existing);
            group.Children.Add(transform);
            element.RenderTransform = group;
        }
        else
        {
            element.RenderTransform = transform;
        }
        if (element is FrameworkElement fe) fe.RenderTransformOrigin = new Point(0.5, 0.5);

        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(230))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(330))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private UIElement Build(int step) => step switch
    {
        0 => BuildColorStep(),
        1 => BuildNameStep(),
        2 => BuildSearchStep(),
        _ => BuildReadyStep()
    };

    // =====================================================================
    //  step 1 — accent color
    // =====================================================================

    private UIElement BuildColorStep()
    {
        var stack = new StackPanel();
        stack.Children.Add(StepHeader(Icons.Palette, "Pick your color",
            "It becomes the accent all over Nibble: tabs, buttons, and your new tab page. You can change it later from the menu."));

        var grid = new UniformGrid { Rows = 3, Columns = 3, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (name, hex) in Accent.Palette) grid.Children.Add(BuildSwatch(name, hex));
        stack.Children.Add(grid);
        stack.Children.Add(BuildColorPreview());
        return stack;
    }

    private UIElement BuildSwatch(string name, string hex)
    {
        var color = Accent.Parse(hex);
        var selected = string.Equals(hex, _accent, StringComparison.OrdinalIgnoreCase);

        var check = new VectorIcon
        {
            Data = Icons.Check,
            Size = 24,
            StrokeThickness = 2.6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Accent.IsLight(color)
                ? Color.FromRgb(0x16, 0x25, 0x0A)
                : Colors.White),
            Visibility = selected ? Visibility.Visible : Visibility.Collapsed
        };

        var button = new Button
        {
            Style = (Style)FindResource("TileButton"),
            Width = 70,
            Height = 70,
            Margin = new Thickness(0, 0, 11, 11),
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(color),
            Content = check,
            ToolTip = name
        };
        AutomationProperties.SetName(button, $"Accent colour {name}");
        button.Click += (_, _) => PickAccent(hex);

        _swatches.Add(button);
        _swatchChecks.Add(check);
        return button;
    }

    private void PickAccent(string hex)
    {
        _accent = hex;
        Accent.Apply(hex);

        foreach (var check in _swatchChecks)
        {
            // swatch order matches the palette order
            var index = _swatchChecks.IndexOf(check);
            var selected = string.Equals(Accent.Palette[index].Hex, hex, StringComparison.OrdinalIgnoreCase);
            check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            if (selected && check.Parent is Button parent) Juice.Pulse(parent, 0.22, 460);
        }

        RefreshGreeting();
    }

    /// <summary>A miniature of the real browser chrome that re-tints instantly.</summary>
    private UIElement BuildColorPreview()
    {
        var card = new PixelPanel
        {
            Height = 104,
            Margin = new Thickness(0, 14, 0, 0),
            CornerSteps = 3,
            CornerStep = 3,
            StrokeThickness = 1
        };
        card.SetResourceReference(PixelPanel.FillProperty, "SubBg");
        card.SetResourceReference(PixelPanel.StrokeProperty, "Hairline");

        var grid = new Grid { Margin = new Thickness(18, 14, 18, 14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // mini tab strip
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        tabs.Children.Add(MiniTab("Your tab", active: true));
        tabs.Children.Add(MiniTab("Another", active: false));
        Grid.SetRow(tabs, 0);

        // mini toolbar
        var bar = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(MiniDot(Icons.Back));
        nav.Children.Add(MiniDot(Icons.Forward));
        Grid.SetColumn(nav, 0);

        var omni = new PixelPanel { Height = 26, Margin = new Thickness(12, 0, 12, 0), CornerSteps = 2, CornerStep = 3, StrokeThickness = 1 };
        omni.SetResourceReference(PixelPanel.FillProperty, "CardBg");
        omni.SetResourceReference(PixelPanel.StrokeProperty, "Hairline");
        var omniInner = new Grid { Margin = new Thickness(9, 0, 9, 0) };
        var urlBar = new Rectangle { Height = 5, Width = 150, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, RadiusX = 0, RadiusY = 0 };
        urlBar.SetResourceReference(Rectangle.FillProperty, "Ink3");
        omniInner.Children.Add(urlBar);
        omni.Child = omniInner;
        Grid.SetColumn(omni, 1);

        var shield = new PixelPanel { Height = 26, Width = 46, CornerSteps = 2, CornerStep = 3, StrokeThickness = 0 };
        shield.SetResourceReference(PixelPanel.FillProperty, "Accent");
        var shieldIcon = new VectorIcon
        {
            Data = Icons.Shield,
            Size = 13,
            Filled = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        shieldIcon.SetResourceReference(VectorIcon.ForegroundProperty, "AccentInk");
        shield.Child = shieldIcon;
        Grid.SetColumn(shield, 2);

        bar.Children.Add(nav);
        bar.Children.Add(omni);
        bar.Children.Add(shield);
        Grid.SetRow(bar, 1);

        // mini loading snake
        var loader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var width in new[] { 26.0, 26.0, 26.0 })
        {
            var block = new Rectangle { Height = 4, Width = width, Margin = new Thickness(0, 0, 6, 0) };
            block.SetResourceReference(Rectangle.FillProperty, "Accent");
            loader.Children.Add(block);
        }
        Grid.SetRow(loader, 2);

        grid.Children.Add(tabs);
        grid.Children.Add(bar);
        grid.Children.Add(loader);
        card.Child = grid;
        return card;
    }

    private PixelPanel MiniTab(string title, bool active)
    {
        var panel = new PixelPanel
        {
            Height = 26,
            Width = active ? 150 : 118,
            Margin = new Thickness(0, 0, 8, 0),
            CornerSteps = 2,
            CornerStep = 3,
            StrokeThickness = 1
        };
        panel.SetResourceReference(PixelPanel.FillProperty, active ? "CardBg" : "SubBg");
        panel.SetResourceReference(PixelPanel.StrokeProperty, active ? "HairlineStrong" : "Hairline");

        var stack = new Grid { Margin = new Thickness(9, 0, 9, 0) };
        stack.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stack.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var globe = new VectorIcon { Data = Icons.Globe, Size = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        globe.SetResourceReference(VectorIcon.ForegroundProperty, "Ink3");
        Grid.SetColumn(globe, 0);

        var label = new TextBlock
        {
            Text = title,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, active ? "Ink" : "Ink2");
        Grid.SetColumn(label, 1);

        var underline = new Rectangle { Height = 3, Width = 22, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
        underline.SetResourceReference(Rectangle.FillProperty, "Accent");
        underline.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(underline, 3);

        stack.Children.Add(globe);
        stack.Children.Add(label);
        stack.Children.Add(underline);
        panel.Child = stack;
        return panel;
    }

    private PixelPanel MiniDot(string iconData)
    {
        var panel = new PixelPanel { Width = 20, Height = 20, Margin = new Thickness(0, 0, 5, 0), CornerSteps = 2, CornerStep = 2, StrokeThickness = 0 };
        panel.SetResourceReference(PixelPanel.FillProperty, "SubBg");
        var icon = new VectorIcon { Data = iconData, Size = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(VectorIcon.ForegroundProperty, "Ink2");
        panel.Child = icon;
        return panel;
    }

    // =====================================================================
    //  step 2 — name
    // =====================================================================

    private UIElement BuildNameStep()
    {
        var stack = new StackPanel();
        stack.Children.Add(StepHeader(Icons.Person, "What should we call you?",
            "Nibble greets you by name on the new tab page \u2014 and reads the clock your way. " +
            "Both stay on this machine, and nothing is uploaded."));

        var frame = new PixelPanel { Height = 58, Style = (Style)FindResource("InputFrame"), CornerSteps = 2, CornerStep = 3 };
        var inner = new Grid { Margin = new Thickness(16, 0, 16, 0) };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var person = new VectorIcon { Data = Icons.Person, Size = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        person.SetResourceReference(VectorIcon.ForegroundProperty, "Ink3");
        Grid.SetColumn(person, 0);

        var box = new TextBox
        {
            Style = (Style)FindResource("OmniBox"),
            FontSize = 17,
            Text = _name,
            VerticalAlignment = VerticalAlignment.Center,
            MaxLength = 24
        };
        Grid.SetColumn(box, 1);
        AutomationProperties.SetName(box, "Your name");

        var placeholder = new TextBlock
        {
            Text = "Your name",
            FontSize = 17,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = _name.Length > 0 ? Visibility.Collapsed : Visibility.Visible
        };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "Ink3");
        Grid.SetColumn(placeholder, 1);

        inner.Children.Add(person);
        inner.Children.Add(box);
        inner.Children.Add(placeholder);
        frame.Child = inner;
        stack.Children.Add(frame);

        stack.Children.Add(BuildGreetingPreview());
        stack.Children.Add(BuildClockChoice());

        box.TextChanged += (_, _) =>
        {
            _name = box.Text.Trim();
            placeholder.Visibility = box.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            RefreshGreeting();
        };

        Loaded += (_, _) => box.Focus();
        return stack;
    }

    private UIElement BuildGreetingPreview()
    {
        var card = new PixelPanel
        {
            Height = 150,
            Margin = new Thickness(0, 16, 0, 0),
            CornerSteps = 3,
            CornerStep = 3,
            StrokeThickness = 1
        };
        card.SetResourceReference(PixelPanel.FillProperty, "SubBg");
        card.SetResourceReference(PixelPanel.StrokeProperty, "Hairline");

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

        var time = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("PixelFont"),
            FontSize = 34,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        time.SetResourceReference(TextBlock.ForegroundProperty, "Ink");

        // Same shape as the real page: padded digits, with AM/PM raised and smaller.
        _previewDigits = new Run(string.Empty);
        _previewMeridiem = new Run(string.Empty)
        {
            FontSize = 13,
            BaselineAlignment = BaselineAlignment.Superscript
        };
        _previewMeridiem.SetResourceReference(Run.ForegroundProperty, "Ink2");
        time.Inlines.Add(_previewDigits);
        time.Inlines.Add(_previewMeridiem);

        _greetingName = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("PixelFont"),
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var preview = new TextBlock
        {
            Text = "your name and clock, live on the new tab page",
            FontSize = 10.5,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        preview.SetResourceReference(TextBlock.ForegroundProperty, "Ink3");

        stack.Children.Add(time);
        stack.Children.Add(_greetingName);
        stack.Children.Add(preview);
        card.Child = stack;

        _previewTick ??= CreatePreviewTicker();
        _previewTick.Start();
        RefreshPreviewClock();
        RefreshGreeting();
        return card;
    }

    private DispatcherTimer CreatePreviewTicker()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => RefreshPreviewClock();
        Closed += (_, _) => timer.Stop();
        return timer;
    }

    // =====================================================================
    //  step 2b — the clock
    // =====================================================================

    /// <summary>Three tiles: 24-hour, 12-hour, or whatever Windows is set to.</summary>
    private UIElement BuildClockChoice()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 18, 4, 0) };

        var caption = new TextBlock
        {
            Text = "AND THE CLOCK UP THERE?",
            FontFamily = (FontFamily)FindResource("PixelFont"),
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 9)
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Ink3");
        stack.Children.Add(caption);

        var row = new UniformGrid { Rows = 1, Columns = 3, HorizontalAlignment = HorizontalAlignment.Stretch };
        row.Children.Add(ClockTile(ClockFormat.TwentyFour, "24-hour"));
        row.Children.Add(ClockTile(ClockFormat.Twelve, "12-hour"));
        row.Children.Add(ClockTile(ClockFormat.Auto, "Match Windows"));
        stack.Children.Add(row);

        RefreshClockTiles();
        return stack;
    }

    private Button ClockTile(string value, string caption)
    {
        var now = DateTime.Now;

        var chip = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

        var sample = new TextBlock
        {
            Text = ClockFormat.Sample(now, ClockFormat.Is24Hour(value)),
            FontFamily = (FontFamily)FindResource("PixelFont"),
            FontSize = 17,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        sample.SetResourceReference(TextBlock.ForegroundProperty, "Ink");

        var name = new TextBlock
        {
            Text = caption,
            FontSize = 11.5,
            Margin = new Thickness(0, 7, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");

        chip.Children.Add(sample);
        chip.Children.Add(name);

        var check = new VectorIcon
        {
            Data = Icons.Check,
            Size = 15,
            StrokeThickness = 2.3,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, 9, 0),
            Visibility = Visibility.Collapsed
        };
        check.SetResourceReference(VectorIcon.ForegroundProperty, "Accent");

        var host = new Grid();
        host.Children.Add(chip);
        host.Children.Add(check);

        var tile = new Button
        {
            Style = (Style)FindResource("TileButton"),
            Height = 76,
            Margin = new Thickness(0, 0, 10, 0),
            Background = (Brush)FindResource("CardBg"),
            BorderBrush = (Brush)FindResource("Hairline"),
            Content = host,
            Tag = value
        };
        AutomationProperties.SetName(tile, $"Clock {caption}");

        tile.Click += (_, _) =>
        {
            _clock = value;
            RefreshClockTiles();
            RefreshPreviewClock();
        };

        _clockTiles.Add(tile);
        _clockChecks.Add(check);
        return tile;
    }

    private void RefreshClockTiles()
    {
        for (var i = 0; i < _clockChecks.Count; i++)
        {
            var selected = string.Equals(_clockTiles[i].Tag as string, _clock, StringComparison.OrdinalIgnoreCase);
            _clockChecks[i].Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            _clockTiles[i].BorderBrush = (Brush)FindResource(selected ? "Accent" : "Hairline");
        }
    }

    private void RefreshPreviewClock()
    {
        if (_previewDigits is null || _previewMeridiem is null) return;

        var now = DateTime.Now;
        var use24 = ClockFormat.Is24Hour(_clock);
        _previewDigits.Text = ClockFormat.Digits(now, use24);
        var suffix = ClockFormat.Meridiem(now, use24);
        _previewMeridiem.Text = suffix.Length == 0 ? string.Empty : "  " + suffix;
    }

    private void RefreshGreeting()
    {
        if (_greetingName is null) return;

        var greeting = Greeting();
        if (_name.Length == 0)
        {
            _greetingName.Text = $"{greeting}, FRIEND";
            _greetingName.SetResourceReference(TextBlock.ForegroundProperty, "Ink3");
        }
        else
        {
            _greetingName.Text = $"{greeting}, {_name.ToUpperInvariant()}";
            _greetingName.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        }
    }

    private static string Greeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "GOOD MORNING" : hour < 18 ? "GOOD AFTERNOON" : "GOOD EVENING";
    }

    // =====================================================================
    //  step 3 — search engine
    // =====================================================================

    private UIElement BuildSearchStep()
    {
        var stack = new StackPanel();
        stack.Children.Add(StepHeader(Icons.Search, "Where should searches go?",
            "Type anything into the address bar and this is the engine Nibble uses. You can switch it anytime from the menu."));

        var grid = new UniformGrid { Rows = 2, Columns = 2, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var engine in Urls.Engines) grid.Children.Add(BuildEngineCard(engine));
        stack.Children.Add(grid);
        return stack;
    }

    private UIElement BuildEngineCard(Urls.Engine engine)
    {
        var selected = string.Equals(engine.Id, _engine, StringComparison.OrdinalIgnoreCase);
        var (mark, brand) = EngineMark(engine.Id);

        var tile = new PixelPanel { Width = 46, Height = 46, CornerSteps = 2, CornerStep = 3, StrokeThickness = 0 };
        tile.Fill = new SolidColorBrush(TintOf(brand));
        tile.Child = mark;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0) };
        var name = new TextBlock { Text = engine.Name, FontSize = 14, FontWeight = FontWeights.SemiBold };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var blurb = new TextBlock { Text = engine.Blurb, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        blurb.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
        text.Children.Add(name);
        text.Children.Add(blurb);

        var check = new VectorIcon
        {
            Data = Icons.Check,
            Size = 18,
            StrokeThickness = 2.4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Visibility = selected ? Visibility.Visible : Visibility.Collapsed
        };
        check.SetResourceReference(VectorIcon.ForegroundProperty, "Accent");

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(tile, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(check, 2);
        grid.Children.Add(tile);
        grid.Children.Add(text);
        grid.Children.Add(check);

        var button = new Button
        {
            Style = (Style)FindResource("TileButton"),
            Height = 92,
            Margin = new Thickness(0, 0, 13, 13),
            Background = (Brush)FindResource("CardBg"),
            BorderBrush = (Brush)FindResource("Hairline"),
            Content = grid
        };
        AutomationProperties.SetName(button, $"Search engine {engine.Name}");
        button.Click += (_, _) =>
        {
            _engine = engine.Id;
            for (var i = 0; i < _engineChecks.Count; i++)
                _engineChecks[i].Visibility = string.Equals(Urls.Engines[i].Id, engine.Id, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        };

        _engineCards.Add(button);
        _engineChecks.Add(check);
        return button;
    }

    private (UIElement Mark, Color Brand) EngineMark(string id) => id switch
    {
        "google" => (new VectorIcon
        {
            Data = Icons.BrandGoogle,
            Filled = true,
            Size = 26,
            Foreground = new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }, Color.FromRgb(0x42, 0x85, 0xF4)),
        "brave" => (new VectorIcon
        {
            Data = Icons.BrandBrave,
            Filled = true,
            Size = 26,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0x54, 0x2B)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }, Color.FromRgb(0xFB, 0x54, 0x2B)),
        "bing" => (new VectorIcon
        {
            Data = Icons.BrandBing,
            Filled = true,
            Size = 24,
            ViewBoxWidth = 256,
            ViewBoxHeight = 388,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x83, 0x73)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }, Color.FromRgb(0x00, 0x83, 0x73)),
        _ => (new VectorIcon
        {
            Data = Icons.BrandDuckDuckGo,
            Filled = true,
            Size = 26,
            Foreground = new SolidColorBrush(Color.FromRgb(0xDE, 0x58, 0x33)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }, Color.FromRgb(0xDE, 0x58, 0x33))
    };

    private static Color TintOf(Color brand)
    {
        var target = Theme.IsDark ? Color.FromRgb(0x2C, 0x2C, 0x2E) : Colors.White;
        var amount = Theme.IsDark ? 0.80 : 0.88;
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromRgb(Mix(brand.R, target.R), Mix(brand.G, target.G), Mix(brand.B, target.B));
    }

    // =====================================================================
    //  step 4 — ready
    // =====================================================================

    private UIElement BuildReadyStep()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

        var badge = new PixelPanel { Width = 72, Height = 72, CornerSteps = 3, CornerStep = 4, StrokeThickness = 0, HorizontalAlignment = HorizontalAlignment.Center };
        badge.SetResourceReference(PixelPanel.FillProperty, "Accent");
        var tick = new VectorIcon
        {
            Data = Icons.Check,
            Size = 34,
            StrokeThickness = 2.4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        tick.SetResourceReference(VectorIcon.ForegroundProperty, "AccentInk");
        badge.Child = tick;

        var badgeRow = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        badgeRow.Children.Add(badge);

        // sparkles around the badge, revealed in sequence
        var sparkleSpecs = new (double Angle, double Distance)[]
        {
            (200, 62), (250, 74), (300, 66), (20, 72), (70, 64)
        };
        for (var i = 0; i < sparkleSpecs.Length; i++)
        {
            var spec = sparkleSpecs[i];
            var radians = spec.Angle * Math.PI / 180.0;
            var sparkle = new VectorIcon
            {
                Data = Icons.Sparkle,
                Size = 12 + (i % 3) * 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Math.Cos(radians) * spec.Distance * 2, Math.Sin(radians) * spec.Distance, 0, 0),
                Opacity = 0
            };
            sparkle.SetResourceReference(VectorIcon.ForegroundProperty, "Accent");
            badgeRow.Children.Add(sparkle);

            var delay = TimeSpan.FromMilliseconds(160 + i * 90);
            sparkle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.9, TimeSpan.FromMilliseconds(320))
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        var title = new TextBlock
        {
            Text = _name.Length > 0 ? $"You're all set, {_name}." : "You're all set.",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 26, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");

        var hint = new TextBlock
        {
            Text = "Ctrl+K  commands   ·   Ctrl+T  new tab   ·   Ctrl+L  address bar",
            FontFamily = (FontFamily)FindResource("PixelFont"),
            FontSize = 9.5,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Ink3");

        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 26, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        chips.Children.Add(RecapChip(new SolidColorBrush(Accent.Parse(_accent)), "accent"));
        chips.Children.Add(RecapChip(null, _name.Length > 0 ? _name : "no name"));

        var engine = Urls.EngineFor(_engine);
        var (mark, _) = EngineMark(engine.Id);
        chips.Children.Add(RecapChip(null, engine.Name, mark));

        // badge entrance
        badge.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.7, 0.7);
        badge.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new BackEase { Amplitude = 0.9, EasingMode = EasingMode.EaseOut }
        });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new BackEase { Amplitude = 0.9, EasingMode = EasingMode.EaseOut }
        });

        stack.Children.Add(badgeRow);
        stack.Children.Add(title);
        stack.Children.Add(chips);
        stack.Children.Add(hint);

        // Offer the other browsers on this machine, right where "you're all set" lands.
        var detected = Migrator.Detect();
        if (detected.Count > 0)
        {
            var importHeading = new TextBlock
            {
                Text = "BRING YOUR STUFF OVER",
                FontFamily = (FontFamily)FindResource("PixelFont"),
                FontSize = 9,
                Margin = new Thickness(0, 26, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            importHeading.SetResourceReference(TextBlock.ForegroundProperty, "Ink3");
            stack.Children.Add(importHeading);

            var status = new TextBlock
            {
                FontSize = 11.5,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420
            };
            status.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");

            foreach (var browser in detected)
            {
                var captured = browser;
                var button = new Button
                {
                    Style = (Style)FindResource("GhostButton"),
                    Height = 34,
                    Margin = new Thickness(0, 0, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Content = $"Import {captured.Name}  \u00b7  {captured.Summary}"
                };
                button.Click += (_, _) =>
                {
                    var result = Migrator.Import(captured);
                    var engine = result.SearchEngine is null
                        ? string.Empty
                        : $" and set the search engine to {Urls.EngineFor(result.SearchEngine).Name}";
                    status.Text = $"Imported {result.Bookmarks:N0} bookmarks{engine}. " +
                                  $"Skipped for now: {string.Join(", ", result.Skipped)}.";
                };
                stack.Children.Add(button);
            }

            stack.Children.Add(status);
        }

        return stack;
    }

    private UIElement RecapChip(Brush? dot, string label, UIElement? mark = null)
    {
        var chip = new PixelPanel
        {
            Height = 40,
            Margin = new Thickness(0, 0, 10, 0),
            CornerSteps = 2,
            CornerStep = 3,
            StrokeThickness = 1
        };
        chip.SetResourceReference(PixelPanel.FillProperty, "SubBg");
        chip.SetResourceReference(PixelPanel.StrokeProperty, "Hairline");

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(13, 0, 15, 0)
        };

        if (dot is not null)
        {
            row.Children.Add(new Rectangle
            {
                Width = 14,
                Height = 14,
                Fill = dot,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0)
            });
        }

        if (mark is not null)
        {
            if (mark is FrameworkElement element) element.Margin = new Thickness(0, 0, 9, 0);
            row.Children.Add(mark);
        }

        var text = new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 150
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        row.Children.Add(text);

        chip.Child = row;
        return chip;
    }

    // =====================================================================
    //  shared bits
    // =====================================================================

    private UIElement StepHeader(string icon, string title, string subtitle)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 16) };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var glyph = new VectorIcon
        {
            Data = icon,
            Size = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        glyph.SetResourceReference(VectorIcon.ForegroundProperty, "Accent");
        row.Children.Add(glyph);

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        row.Children.Add(heading);

        var body = new TextBlock
        {
            Text = subtitle,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 540
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");

        stack.Children.Add(row);
        stack.Children.Add(body);
        return stack;
    }
}
