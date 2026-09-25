using System.Windows;
using System.Windows.Media;

namespace Nibble.Controls;

/// <summary>An indeterminate loading bar built from chunky pixel blocks.</summary>
public sealed class PixelLoader : FrameworkElement
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(PixelLoader),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnIsActiveChanged));

    public static readonly DependencyProperty BlockProperty = DependencyProperty.Register(
        nameof(Block), typeof(double), typeof(PixelLoader),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(PixelLoader),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(PixelLoader),
        new FrameworkPropertyMetadata(Brushes.Lime, FrameworkPropertyMetadataOptions.AffectsRender));

    private double _head;
    private bool _hooked;

    public PixelLoader()
    {
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        SnapsToDevicePixels = true;
        IsVisibleChanged += (_, __) => SyncHook();
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double Block
    {
        get => (double)GetValue(BlockProperty);
        set => SetValue(BlockProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public Brush Brush
    {
        get => (Brush)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var loader = (PixelLoader)d;
        loader.SyncHook();
        if ((bool)e.NewValue) loader._head = 0;
        loader.InvalidateVisual();
    }

    private void SyncHook()
    {
        var shouldRun = IsActive && IsVisible;
        if (shouldRun == _hooked) return;
        _hooked = shouldRun;
        if (shouldRun) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (!IsActive || !IsVisible)
        {
            SyncHook();
            return;
        }
        _head += 0.0135;
        if (_head > 1.35) _head = -0.35;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!IsActive) return;

        var w = Math.Round(ActualWidth);
        var h = Math.Round(ActualHeight);
        if (w < 4 || h < 1) return;

        var block = Math.Max(4, Math.Round(Block));
        var gap = Math.Max(1, Math.Round(Gap));
        var count = (int)Math.Ceiling((w + gap) / (block + gap));
        var headPos = _head * (count + 4);
        var trail = 5;

        for (var i = 0; i < trail; i++)
        {
            var idx = (int)Math.Floor(headPos) - i;
            if (idx < 0 || idx >= count) continue;
            var alpha = 1.0 - i * 0.17;
            if (alpha <= 0.05) continue;
            var brush = Brush.Clone();
            brush.Opacity = alpha;
            brush.Freeze();

            // Squash and stretch along the march: the head is full size, the tail tapers
            // in both axes so the snake looks like it is stretching forward.
            var taper = 1.0 - i * 0.14;
            var slot = block + gap;
            var drawn = Math.Min(block * (0.55 + 0.45 * taper), w - idx * slot);
            var height = Math.Max(1.0, Math.Round(h * (0.45 + 0.55 * taper)));
            var x = idx * slot + (block - drawn) / 2;
            var y = (h - height) / 2;
            if (drawn <= 0) continue;
            dc.DrawRectangle(brush, null, new Rect(x, y, drawn, height));
        }
    }
}
