using System.Globalization;
using System.Text.RegularExpressions;

namespace Nibble.Services;

/// <summary>
/// The little things that make a command bar useful: arithmetic and unit conversion.
/// Deliberately hand-written (no eval, no expression library, no dependencies).
/// </summary>
public static partial class Calculator
{
    // =====================================================================
    //  arithmetic: + - * / % ^ ( ) and unary minus
    // =====================================================================

    public static bool TryEvaluate(string input, out double value)
    {
        value = 0;
        var text = input.Trim().TrimStart('=').Trim();
        if (text.Length == 0 || text.Length > 120) return false;
        if (!text.Any(char.IsDigit)) return false;
        if (text.Any(c => !"0123456789.+-*/%^() ,e".Contains(c))) return false;

        try
        {
            var parser = new Parser(text.Replace(",", ""));
            value = parser.ParseExpression();
            return parser.AtEnd && !double.IsNaN(value) && !double.IsInfinity(value);
        }
        catch
        {
            return false;
        }
    }

    private sealed class Parser(string text)
    {
        private int _index;

        public bool AtEnd => _index >= text.Length;

        public double ParseExpression()
        {
            var value = ParseTerm();
            while (!AtEnd)
            {
                SkipSpace();
                if (AtEnd) break;
                var c = text[_index];
                if (c is not ('+' or '-')) break;
                _index++;
                var right = ParseTerm();
                value = c == '+' ? value + right : value - right;
            }
            return value;
        }

        private double ParseTerm()
        {
            var value = ParsePower();
            while (!AtEnd)
            {
                SkipSpace();
                if (AtEnd) break;
                var c = text[_index];
                if (c is not ('*' or '/' or '%')) break;
                _index++;
                var right = ParsePower();
                value = c switch
                {
                    '*' => value * right,
                    '/' => right == 0 ? double.NaN : value / right,
                    _ => right == 0 ? double.NaN : value % right
                };
            }
            return value;
        }

        private double ParsePower()
        {
            var value = ParseUnary();
            SkipSpace();
            if (AtEnd || text[_index] != '^') return value;
            _index++;
            return Math.Pow(value, ParsePower());
        }

        private double ParseUnary()
        {
            SkipSpace();
            if (!AtEnd && text[_index] == '-')
            {
                _index++;
                return -ParseUnary();
            }
            if (!AtEnd && text[_index] == '+')
            {
                _index++;
                return ParseUnary();
            }
            return ParseAtom();
        }

        private double ParseAtom()
        {
            SkipSpace();
            if (!AtEnd && text[_index] == '(')
            {
                _index++;
                var inner = ParseExpression();
                SkipSpace();
                if (AtEnd || text[_index] != ')') throw new FormatException("missing )");
                _index++;
                return inner;
            }

            var start = _index;
            while (!AtEnd && (char.IsDigit(text[_index]) || text[_index] == '.')) _index++;
            if (_index == start) throw new FormatException("expected number");

            var slice = text[start.._index];
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException("bad number");

            // bare "e" suffix is not supported beyond exponent notation handled by TryParse
            return value;
        }

        private void SkipSpace()
        {
            while (!AtEnd && text[_index] == ' ') _index++;
        }
    }

    public static string Format(double value)
    {
        if (Math.Abs(value) >= 1e15 || (Math.Abs(value) < 1e-9 && value != 0))
            return value.ToString("0.###e+0", CultureInfo.InvariantCulture);

        var rounded = Math.Round(value, 10);
        return rounded == Math.Floor(rounded) && Math.Abs(rounded) < 1e15
            ? rounded.ToString("N0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.####", CultureInfo.InvariantCulture);
    }

    // =====================================================================
    //  unit conversion: "12 km to mi", "100 f in c", "3 tbsp to ml"
    // =====================================================================

    private sealed record Unit(string Family, double Factor)
    {
        public bool Affine => Family is "temp";
    }

    private static readonly Dictionary<string, Unit> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        // length (base: metre)
        ["mm"] = new("length", 0.001), ["cm"] = new("length", 0.01), ["m"] = new("length", 1),
        ["km"] = new("length", 1000), ["in"] = new("length", 0.0254), ["ft"] = new("length", 0.3048),
        ["yd"] = new("length", 0.9144), ["mi"] = new("length", 1609.344), ["nmi"] = new("length", 1852),
        // mass (base: kilogram)
        ["mg"] = new("mass", 1e-6), ["g"] = new("mass", 0.001), ["kg"] = new("mass", 1),
        ["oz"] = new("mass", 0.028349523125), ["lb"] = new("mass", 0.45359237), ["st"] = new("mass", 6.35029318),
        ["t"] = new("mass", 1000),
        // volume (base: litre)
        ["ml"] = new("volume", 0.001), ["l"] = new("volume", 1), ["tsp"] = new("volume", 0.00492892159),
        ["tbsp"] = new("volume", 0.0147867648), ["cup"] = new("volume", 0.2365882365),
        ["pt"] = new("volume", 0.473176473), ["qt"] = new("volume", 0.946352946), ["gal"] = new("volume", 3.785411784),
        // time (base: second)
        ["ms"] = new("time", 0.001), ["s"] = new("time", 1), ["sec"] = new("time", 1),
        ["min"] = new("time", 60), ["h"] = new("time", 3600), ["hr"] = new("time", 3600),
        ["d"] = new("time", 86400), ["wk"] = new("time", 604800),
        // data (base: byte, binary multiples - what a file manager reports)
        ["b"] = new("data", 1), ["kb"] = new("data", 1024), ["mb"] = new("data", 1024 * 1024),
        ["gb"] = new("data", 1024.0 * 1024 * 1024), ["tb"] = new("data", 1024.0 * 1024 * 1024 * 1024),
        // temperature (affine, handled separately)
        ["c"] = new("temp", 1), ["f"] = new("temp", 1), ["k"] = new("temp", 1)
    };

    [GeneratedRegex(@"^\s*(-?\d+(?:[.,]\d+)?)\s*°?\s*([a-zA-Z]{1,6})\s*(?:to|in|as|→|->)\s*°?\s*([a-zA-Z]{1,6})\s*$")]
    private static partial Regex ConversionPattern();

    public static bool TryConvert(string input, out string result)
    {
        result = string.Empty;
        var match = ConversionPattern().Match(input);
        if (!match.Success) return false;

        if (!double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            return false;

        var from = match.Groups[2].Value;
        var to = match.Groups[3].Value;
        if (!Units.TryGetValue(from, out var source) || !Units.TryGetValue(to, out var target)) return false;
        if (source.Family != target.Family) return false;

        var converted = source.Family == "temp"
            ? ConvertTemperature(amount, from.ToLowerInvariant(), to.ToLowerInvariant())
            : amount * source.Factor / target.Factor;

        var label = source.Family == "temp"
            ? $"°{to.ToUpperInvariant()}"
            : to.ToLowerInvariant();

        result = $"{Format(converted)} {label}";
        return true;
    }

    private static double ConvertTemperature(double value, string from, string to)
    {
        var celsius = from switch
        {
            "c" => value,
            "f" => (value - 32) * 5 / 9,
            "k" => value - 273.15,
            _ => value
        };

        return to switch
        {
            "c" => celsius,
            "f" => celsius * 9 / 5 + 32,
            "k" => celsius + 273.15,
            _ => celsius
        };
    }
}
