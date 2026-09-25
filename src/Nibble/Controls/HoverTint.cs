using System.Windows;
using System.Windows.Media;

namespace Nibble.Controls;

/// <summary>
/// The fill a button uses while hovered. Window controls use it to take their
/// traffic-light colour (close red, minimize yellow, maximize green) without needing a
/// template per button - the shared template binds to this attached property.
/// </summary>
public static class HoverTint
{
    public static readonly DependencyProperty BrushProperty = DependencyProperty.RegisterAttached(
        "Brush", typeof(Brush), typeof(HoverTint), new PropertyMetadata(null));

    public static void SetBrush(DependencyObject element, Brush? value) => element.SetValue(BrushProperty, value);

    public static Brush? GetBrush(DependencyObject element) => (Brush?)element.GetValue(BrushProperty);
}
