using System.Globalization;

namespace Nibble.Services;

/// <summary>
/// How the new tab clock reads: 24-hour, 12-hour, or whatever Windows is set to.
/// The preference is stored as a string so "follow Windows" survives being saved.
/// </summary>
public static class ClockFormat
{
    public const string Auto = "auto";
    public const string TwentyFour = "24";
    public const string Twelve = "12";

    public static string Normalize(string? preference) => preference switch
    {
        TwentyFour => TwentyFour,
        Twelve => Twelve,
        _ => Auto
    };

    /// <summary>True when the clock should read 14:05 rather than 2:05 PM.</summary>
    public static bool Is24Hour(string? preference) => Normalize(preference) switch
    {
        TwentyFour => true,
        Twelve => false,
        _ => CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H')
    };

    /// <summary>Menu and command-bar order: 24-hour, 12-hour, match Windows.</summary>
    public static string Next(string? preference) => Normalize(preference) switch
    {
        TwentyFour => Twelve,
        Twelve => Auto,
        _ => TwentyFour
    };

    public static string Label(string? preference) => Normalize(preference) switch
    {
        TwentyFour => "24-hour clock",
        Twelve => "12-hour clock",
        _ => "Clock follows Windows"
    };

    /// <summary>The digits, padded so the pixel clock never jitters as time passes.</summary>
    public static string Digits(DateTime when, bool use24Hour) =>
        use24Hour
            ? when.ToString("HH:mm", CultureInfo.InvariantCulture)
            : when.ToString("hh:mm", CultureInfo.InvariantCulture);

    /// <summary>"AM" / "PM", or empty on a 24-hour clock.</summary>
    public static string Meridiem(DateTime when, bool use24Hour) =>
        use24Hour ? string.Empty : when.ToString("tt", CultureInfo.InvariantCulture);

    /// <summary>A whole sample reading, e.g. "14:05" or "02:05 PM".</summary>
    public static string Sample(DateTime when, bool use24Hour)
    {
        var digits = Digits(when, use24Hour);
        var suffix = Meridiem(when, use24Hour);
        return suffix.Length == 0 ? digits : $"{digits} {suffix}";
    }
}
