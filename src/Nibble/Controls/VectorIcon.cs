using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;

namespace Nibble.Controls;

/// <summary>
/// Smooth vector icon. Path data comes from an open 24x24 stroke set (Lucide); the
/// shape is scaled to Size, keeping its own aspect ratio, and tinted with Foreground.
/// </summary>
public sealed class VectorIcon : FrameworkElement
{
    private static readonly ConcurrentDictionary<string, Geometry> Cache = new();

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(string), typeof(VectorIcon),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(VectorIcon),
        new FrameworkPropertyMetadata(18.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(VectorIcon),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ViewBoxWidthProperty = DependencyProperty.Register(
        nameof(ViewBoxWidth), typeof(double), typeof(VectorIcon),
        new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ViewBoxHeightProperty = DependencyProperty.Register(
        nameof(ViewBoxHeight), typeof(double), typeof(VectorIcon),
        new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(VectorIcon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(VectorIcon),
        new FrameworkPropertyMetadata(Brushes.Black,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public VectorIcon()
    {
        SnapsToDevicePixels = false;

        // Icons are plain FrameworkElements, so they do not inherit a Foreground from the
        // button around them. Default to the theme's secondary ink (which follows light
        // and dark mode) instead of the Brushes.Black fallback.
        SetResourceReference(ForegroundProperty, "Ink2");
    }

    public string Data
    {
        get => (string)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Width and height of the square slot the icon occupies.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double ViewBoxWidth
    {
        get => (double)GetValue(ViewBoxWidthProperty);
        set => SetValue(ViewBoxWidthProperty, value);
    }

    public double ViewBoxHeight
    {
        get => (double)GetValue(ViewBoxHeightProperty);
        set => SetValue(ViewBoxHeightProperty, value);
    }

    /// <summary>Fill the shape as well as stroke it (solid star, solid shield, brand marks).</summary>
    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = Math.Max(1, Size);
        return new Size(size, size);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var geometry = Resolve(Data);
        if (geometry is null) return;

        // Safety net: an icon with no explicit or inherited brush should still match the
        // theme instead of falling back to plain black (which is invisible in dark mode).
        var brush = Foreground;
        if (ReferenceEquals(brush, Brushes.Black) &&
            Application.Current?.TryFindResource("Ink") is Brush themeInk)
        {
            brush = themeInk;
        }
        var size = Math.Max(1, Size);
        var viewWidth = Math.Max(1, ViewBoxWidth);
        var viewHeight = Math.Max(1, ViewBoxHeight);

        var scale = Math.Min(size / viewWidth, size / viewHeight);
        var offsetX = (size - viewWidth * scale) / 2;
        var offsetY = (size - viewHeight * scale) / 2;

        var pen = new Pen(brush, Math.Max(0.5, StrokeThickness))
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();

        dc.PushTransform(new TranslateTransform(offsetX, offsetY));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(Filled ? brush : null, pen, geometry);
        dc.Pop();
        dc.Pop();
    }

    private static Geometry? Resolve(string data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        return Cache.GetOrAdd(data, key =>
        {
            var geometry = Geometry.Parse(key);
            geometry.Freeze();
            return geometry;
        });
    }
}
