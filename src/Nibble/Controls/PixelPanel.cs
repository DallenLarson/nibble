using System.Windows;
using System.Windows.Media;

namespace Nibble.Controls;

/// <summary>
/// A container that paints a stair-stepped ("pixel rounded") rectangle instead of a
/// smooth rounded one: Apple-clean geometry, drawn in discrete pixel steps.
/// </summary>
public sealed class PixelPanel : System.Windows.Controls.Decorator
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(PixelPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(PixelPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(PixelPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerStepsProperty = DependencyProperty.Register(
        nameof(CornerSteps), typeof(int), typeof(PixelPanel),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerStepProperty = DependencyProperty.Register(
        nameof(CornerStep), typeof(double), typeof(PixelPanel),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public PixelPanel()
    {
        SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public int CornerSteps
    {
        get => (int)GetValue(CornerStepsProperty);
        set => SetValue(CornerStepsProperty, value);
    }

    public double CornerStep
    {
        get => (double)GetValue(CornerStepProperty);
        set => SetValue(CornerStepProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return new Size(0, 0);
        var inset = Math.Max(0, StrokeThickness);
        var available = new Size(
            Math.Max(0, constraint.Width - inset * 2),
            Math.Max(0, constraint.Height - inset * 2));
        Child.Measure(available);
        return new Size(Child.DesiredSize.Width + inset * 2, Child.DesiredSize.Height + inset * 2);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var inset = Math.Max(0, StrokeThickness);
        Child?.Arrange(new Rect(inset, inset,
            Math.Max(0, finalSize.Width - inset * 2),
            Math.Max(0, finalSize.Height - inset * 2)));
        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = Math.Round(ActualWidth);
        var h = Math.Round(ActualHeight);
        if (w <= 1 || h <= 1) return;

        var steps = Math.Max(0, CornerSteps);
        var step = Math.Max(1.0, Math.Round(CornerStep));
        var corner = Math.Min(steps * step, Math.Min(w, h) / 2.0 - 1);

        var geometry = BuildGeometry(w - 1, h - 1, corner, step);
        if (geometry is null) return;

        dc.PushTransform(new TranslateTransform(0.5, 0.5));
        if (Fill is not null) dc.DrawGeometry(Fill, null, geometry);
        if (Stroke is not null && StrokeThickness > 0)
        {
            var pen = new Pen(Stroke, StrokeThickness) { LineJoin = PenLineJoin.Miter };
            dc.DrawGeometry(null, pen, geometry);
        }
        dc.Pop();
    }

    /// <summary>Builds a rectangle whose four corners are staircases instead of arcs.</summary>
    public static Geometry? BuildGeometry(double w, double h, double corner, double step)
    {
        if (w <= 0 || h <= 0) return null;
        if (corner <= 0) return new RectangleGeometry(new Rect(0, 0, w, h));

        var n = Math.Max(1, (int)Math.Floor(corner / step));
        var c = n * step;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(c, 0), true, true);
            ctx.LineTo(new Point(w - c, 0), true, false);
            for (var i = 1; i <= n; i++) ctx.LineTo(new Point(w - c + i * step, i * step), true, false);
            ctx.LineTo(new Point(w, h - c), true, false);
            for (var i = 1; i <= n; i++) ctx.LineTo(new Point(w - i * step, h - c + i * step), true, false);
            ctx.LineTo(new Point(c, h), true, false);
            for (var i = 1; i <= n; i++) ctx.LineTo(new Point(c - i * step, h - i * step), true, false);
            ctx.LineTo(new Point(0, c), true, false);
            for (var i = 1; i <= n; i++) ctx.LineTo(new Point(i * step, c - i * step), true, false);
        }
        geo.Freeze();
        return geo;
    }
}
