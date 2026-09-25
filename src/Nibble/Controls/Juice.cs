using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nibble.Controls;

/// <summary>
/// Squash and stretch for the whole shell, in one place.
///
/// Deformations preserve volume (scaleX * scaleY ≈ 1, so a squashed button gets wider as
/// it gets shorter) and animate only RenderTransform and Opacity — both live on the
/// compositor, so nothing here triggers layout or a re-render of the page.
///
/// Motion respects the Windows "show animations" setting: with animations off, every
/// helper snaps straight to its resting state.
/// </summary>
public static class Juice
{
    /// <summary>Depth of the default press squash (12% shorter, 12% wider).</summary>
    public const double PressDepth = 0.12;

    /// <summary>Gentle hover stretch: leans towards the pointer without moving.</summary>
    public const double HoverDepth = 0.025;

    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    // =====================================================================
    //  transforms
    // =====================================================================

    /// <summary>
    /// Returns the element's squash transform, creating one only if it does not already
    /// have one. This must be idempotent: a popup's card is reused every time the menu
    /// opens, and a version that kept wrapping new transforms around old ones left each
    /// previous squash baked into the chain - which is why a menu looked worse the more
    /// it was opened.
    /// </summary>
    public static ScaleTransform EnsureScale(FrameworkElement element)
    {
        if (element.RenderTransformOrigin != new Point(0.5, 0.5))
            element.RenderTransformOrigin = new Point(0.5, 0.5);

        if (element.RenderTransform is TransformGroup group)
        {
            if (group.Children.OfType<ScaleTransform>().FirstOrDefault() is { } found) return found;
            var added = new ScaleTransform(1, 1);
            group.Children.Add(added);
            return added;
        }

        if (element.RenderTransform is ScaleTransform existing) return existing;

        var scale = new ScaleTransform(1, 1);
        if (element.RenderTransform is Transform other && other != Transform.Identity)
        {
            var composed = new TransformGroup();
            composed.Children.Add(other);
            composed.Children.Add(scale);
            element.RenderTransform = composed;
        }
        else
        {
            element.RenderTransform = scale;
        }

        return scale;
    }

    /// <summary>Clears any animation hold and returns the element to its resting shape.</summary>
    public static void Reset(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup group)
        {
            foreach (var scale in group.Children.OfType<ScaleTransform>())
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
            foreach (var slide in group.Children.OfType<TranslateTransform>())
            {
                slide.BeginAnimation(TranslateTransform.YProperty, null);
                slide.BeginAnimation(TranslateTransform.XProperty, null);
                slide.Y = 0;
                slide.X = 0;
            }
        }
        else if (element.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }

        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
    }

    // =====================================================================
    //  press: squash down, stretch past neutral, spring back
    // =====================================================================

    public static void Squash(FrameworkElement element, double amount = PressDepth, double milliseconds = 80)
    {
        var scale = EnsureScale(element);
        if (!Enabled)
        {
            scale.ScaleX = scale.ScaleY = 1;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1 + amount, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1 - amount, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Release(FrameworkElement element, double amount = PressDepth, double milliseconds = 420)
    {
        var scale = EnsureScale(element);
        if (!Enabled)
        {
            scale.ScaleX = scale.ScaleY = 1;
            return;
        }

        // Follow-through: overshoot slightly the other way, then settle.
        var spring = new BackEase { Amplitude = 1.15, EasingMode = EasingMode.EaseOut };
        var settle = new CubicEase { EasingMode = EasingMode.EaseOut };
        var mid = TimeSpan.FromMilliseconds(milliseconds * 0.42);
        var end = TimeSpan.FromMilliseconds(milliseconds);

        var scaleX = new DoubleAnimationUsingKeyFrames();
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1 - amount * 0.45, KeyTime.FromTimeSpan(mid)) { EasingFunction = spring });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(end)) { EasingFunction = settle });

        var scaleY = new DoubleAnimationUsingKeyFrames();
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1 + amount * 0.45, KeyTime.FromTimeSpan(mid)) { EasingFunction = spring });
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(end)) { EasingFunction = settle });

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Stretch(FrameworkElement element, double amount = HoverDepth, double milliseconds = 220)
    {
        var scale = EnsureScale(element);
        if (!Enabled)
        {
            scale.ScaleX = scale.ScaleY = 1;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1 + amount, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1 - amount, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
    }

    // =====================================================================
    //  entrances and exits
    // =====================================================================

    /// <summary>Pops in: squashed and slightly displaced, overshooting into place.</summary>
    public static void PopIn(FrameworkElement element, double amount = 0.05, double fromY = 7, double milliseconds = 340)
    {
        // Always start from a clean slate: the same card is reused on every open.
        Reset(element);

        if (!Enabled)
        {
            element.Opacity = 1;
            return;
        }

        var scale = EnsureScale(element);
        var slide = EnsureSlide(element);

        element.Opacity = 0;

        var spring = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut };
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(milliseconds);

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds * 0.6)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1 + amount, 1, duration) { EasingFunction = spring }, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1 - amount, 1, duration) { EasingFunction = spring }, HandoffBehavior.SnapshotAndReplace);
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(fromY, 0, duration) { EasingFunction = spring }, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Squashes flat and fades, then runs <paramref name="completed"/>.</summary>
    public static void SquashOut(FrameworkElement element, Action? completed = null, double amount = 0.14, double milliseconds = 160)
    {
        if (!Enabled)
        {
            element.Opacity = 0;
            completed?.Invoke();
            return;
        }

        var scale = EnsureScale(element);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var done = false;

        void Finish()
        {
            if (done) return;
            done = true;
            completed?.Invoke();
        }

        var fade = new DoubleAnimation(0, duration) { EasingFunction = ease };
        fade.Completed += (_, _) => Finish();
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1 + amount, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1 - amount * 1.4, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);

        // Safety net if the animation is interrupted (element unloaded, tab closed).
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds + 60)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Finish();
        };
        timer.Start();
    }

    /// <summary>A quick stretch-then-settle pulse, for state changes (bookmark saved, tab activated).</summary>
    public static void Pulse(FrameworkElement element, double amount = 0.22, double milliseconds = 480)
    {
        var scale = EnsureScale(element);
        if (!Enabled)
        {
            scale.ScaleX = scale.ScaleY = 1;
            return;
        }

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var spring = new BackEase { Amplitude = 1.4, EasingMode = EasingMode.EaseOut };

        // The two axes are slightly out of phase, which reads as follow-through.
        var scaleX = new DoubleAnimationUsingKeyFrames();
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1 + amount, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds * 0.3))) { EasingFunction = easeOut });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds))) { EasingFunction = spring });

        var scaleY = new DoubleAnimationUsingKeyFrames();
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1 - amount * 0.7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds * 0.34))) { EasingFunction = easeOut });
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds))) { EasingFunction = spring });

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY, HandoffBehavior.SnapshotAndReplace);
    }

    // =====================================================================
    //  attached behaviours: <Button c:Juice.SquashOnPress="True" />
    // =====================================================================

    public static readonly DependencyProperty SquashOnPressProperty = DependencyProperty.RegisterAttached(
        "SquashOnPress", typeof(bool), typeof(Juice), new PropertyMetadata(false, OnSquashOnPressChanged));

    public static void SetSquashOnPress(DependencyObject element, bool value) => element.SetValue(SquashOnPressProperty, value);

    public static bool GetSquashOnPress(DependencyObject element) => (bool)element.GetValue(SquashOnPressProperty);

    public static readonly DependencyProperty StretchOnHoverProperty = DependencyProperty.RegisterAttached(
        "StretchOnHover", typeof(bool), typeof(Juice), new PropertyMetadata(false, OnStretchOnHoverChanged));

    public static void SetStretchOnHover(DependencyObject element, bool value) => element.SetValue(StretchOnHoverProperty, value);

    public static bool GetStretchOnHover(DependencyObject element) => (bool)element.GetValue(StretchOnHoverProperty);

    private static void OnSquashOnPressChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not ButtonBase button) return;

        button.PreviewMouseLeftButtonDown -= OnPressDown;
        button.PreviewMouseLeftButtonUp -= OnPressUp;
        button.MouseLeave -= OnPressUp;
        button.LostMouseCapture -= OnPressUp;

        if (e.NewValue is not true) return;

        button.PreviewMouseLeftButtonDown += OnPressDown;
        button.PreviewMouseLeftButtonUp += OnPressUp;
        button.MouseLeave += OnPressUp;
        button.LostMouseCapture += OnPressUp;
    }

    private static void OnStretchOnHoverChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement element) return;

        element.MouseEnter -= OnHoverEnter;
        element.MouseLeave -= OnHoverLeave;

        if (e.NewValue is not true) return;

        element.MouseEnter += OnHoverEnter;
        element.MouseLeave += OnHoverLeave;
    }

    private static void OnPressDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element) Squash(element);
    }

    private static void OnPressUp(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (sender is ButtonBase { IsPressed: true }) return;
        Release(element);
    }

    private static void OnHoverEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element) Stretch(element);
    }

    private static void OnHoverLeave(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        // Coming out of a press, the release animation owns the transform.
        if (sender is ButtonBase { IsPressed: true }) return;
        Release(element, HoverDepth, 300);
    }

    /// <summary>Returns the element's slide transform, creating one only if it has none.</summary>
    public static TranslateTransform EnsureSlide(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup group &&
            group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } existing)
            return existing;

        var slide = new TranslateTransform(0, 0);
        if (element.RenderTransform is TransformGroup currentGroup)
        {
            currentGroup.Children.Add(slide);
            return slide;
        }

        var scale = EnsureScale(element);
        if (element.RenderTransform is TransformGroup composed)
        {
            composed.Children.Add(slide);
            return slide;
        }

        var rebuilt = new TransformGroup();
        if (element.RenderTransform is Transform current) rebuilt.Children.Add(current);
        rebuilt.Children.Add(slide);
        element.RenderTransform = rebuilt;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        _ = scale;
        return slide;
    }
}
