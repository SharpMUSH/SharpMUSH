// Converted from ANSI.fs — ANSILibrary namespace
// Original: https://github.com/WilliamRagstad/ANSIConsole/blob/main/ANSIConsole/
using System;
using System.Drawing;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ANSILibrary;

// ── Discriminated union: AnsiColor ────────────────────────────────────────────

/// <summary>Represents a terminal color: 24-bit RGB, a raw ANSI byte sequence, or absent.</summary>
[JsonDerivedType(typeof(AnsiColor.RGB), "RGB")]
[JsonDerivedType(typeof(AnsiColor.ANSI), "ANSI")]
[JsonDerivedType(typeof(AnsiColor.NoAnsi), "NoAnsi")]
public abstract record AnsiColor
{
    private AnsiColor() { }

    /// <summary>24-bit RGB color.</summary>
    public sealed record RGB(Color Value) : AnsiColor;

    /// <summary>Raw ANSI SGR byte sequence.</summary>
    public sealed record ANSI(byte[] Value) : AnsiColor;

    /// <summary>No color (inherit / default terminal color).</summary>
    public sealed record NoAnsi : AnsiColor
    {
        /// <summary>Singleton instance.</summary>
        public static readonly NoAnsi Instance = new();
        [JsonConstructor]
        public NoAnsi() { }
    }
}

// ── ANSI SGR helpers ──────────────────────────────────────────────────────────

/// <summary>Low-level ANSI SGR code builders and color-code constants.</summary>
public static class ANSI
{
    private const string ESC = "\u001b";
    private const string CSI = ESC + "[";

    private static string SGR(params byte[] codes) =>
        CSI + string.Join(";", Array.ConvertAll(codes, c => c.ToString())) + "m";

    public static readonly string Clear       = SGR(0);
    public static readonly string Bold        = SGR(1);
    public static readonly string Faint       = SGR(2);
    public static readonly string Italic      = SGR(3);
    public static readonly string Underlined  = SGR(4);
    public static readonly string Blink       = SGR(5);
    public static readonly string Inverted    = SGR(7);
    public static readonly string StrikeThrough = SGR(9);
    public static readonly string Overlined   = SGR(53);

    public static string Foreground(AnsiColor color) => color switch
    {
        AnsiColor.RGB rgb   => SGR(38, 2, rgb.Value.R, rgb.Value.G, rgb.Value.B),
        AnsiColor.ANSI ansi => SGR(ansi.Value),
        AnsiColor.NoAnsi    => string.Empty,
        _ => throw new NotSupportedException()
    };

    public static string Background(AnsiColor color) => color switch
    {
        AnsiColor.RGB rgb   => SGR(48, 2, rgb.Value.R, rgb.Value.G, rgb.Value.B),
        AnsiColor.ANSI ansi => SGR(ansi.Value),
        AnsiColor.NoAnsi    => string.Empty,
        _ => throw new NotSupportedException()
    };

    public static string Hyperlink(string text, string link) =>
        $"\u001b]8;;{link}\a{text}\u001b]8;;\a";

    /// <summary>
    /// Converts standard ANSI color codes (30–37, 40–47, 90–97, 100–107) to RGB.
    /// Uses the standard VGA color palette.
    /// </summary>
    public static Color AnsiToRgb(byte[] ansiBytes)
    {
        int colorCode = ansiBytes.Length switch
        {
            1 => ansiBytes[0],
            2 => ansiBytes[1],
            _ => 0
        };

        return colorCode switch
        {
            30  => Color.FromArgb(0, 0, 0),
            31  => Color.FromArgb(170, 0, 0),
            32  => Color.FromArgb(0, 170, 0),
            33  => Color.FromArgb(170, 85, 0),
            34  => Color.FromArgb(0, 0, 170),
            35  => Color.FromArgb(170, 0, 170),
            36  => Color.FromArgb(0, 170, 170),
            37  => Color.FromArgb(170, 170, 170),
            40  => Color.FromArgb(0, 0, 0),
            41  => Color.FromArgb(170, 0, 0),
            42  => Color.FromArgb(0, 170, 0),
            43  => Color.FromArgb(170, 85, 0),
            44  => Color.FromArgb(0, 0, 170),
            45  => Color.FromArgb(170, 0, 170),
            46  => Color.FromArgb(0, 170, 170),
            47  => Color.FromArgb(170, 170, 170),
            90  => Color.FromArgb(85, 85, 85),
            91  => Color.FromArgb(255, 85, 85),
            92  => Color.FromArgb(85, 255, 85),
            93  => Color.FromArgb(255, 255, 85),
            94  => Color.FromArgb(85, 85, 255),
            95  => Color.FromArgb(255, 85, 255),
            96  => Color.FromArgb(85, 255, 255),
            97  => Color.FromArgb(255, 255, 255),
            100 => Color.FromArgb(85, 85, 85),
            101 => Color.FromArgb(255, 85, 85),
            102 => Color.FromArgb(85, 255, 85),
            103 => Color.FromArgb(255, 255, 85),
            104 => Color.FromArgb(85, 85, 255),
            105 => Color.FromArgb(255, 85, 255),
            106 => Color.FromArgb(85, 255, 255),
            107 => Color.FromArgb(255, 255, 255),
            _   => Color.FromArgb(0, 0, 0),
        };
    }
}

// ── ANSIFormatting flags enum ─────────────────────────────────────────────────

[Flags]
public enum ANSIFormatting
{
    None         = 0,
    Bold         = 1,
    Faint        = 2,
    Italic       = 4,
    Underlined   = 8,
    Overlined    = 16,
    Blink        = 32,
    Inverted     = 64,
    StrikeThrough = 128,
    LowerCase    = 256,
    UpperCase    = 512,
    Clear        = 1024,
    TrueClear    = 2048,
}

// ── ANSIString ────────────────────────────────────────────────────────────────

/// <summary>
/// Immutable value-object wrapping a text string with optional ANSI styling.
/// </summary>
public sealed class ANSIString
{
    private readonly string _text;
    private readonly string? _hyperlink;
    private readonly AnsiColor? _colorForeground;
    private readonly AnsiColor? _colorBackground;
    private readonly double? _opacity;
    private readonly ANSIFormatting _formatting;

    public ANSIString(string text, string? hyperlink, AnsiColor? colorForeground,
                      AnsiColor? colorBackground, double? opacity, ANSIFormatting formatting)
    {
        _text = text;
        _hyperlink = hyperlink;
        _colorForeground = colorForeground;
        _colorBackground = colorBackground;
        _opacity = opacity;
        _formatting = formatting;
    }

    public ANSIString(string text)
        : this(text, null, null, null, null, ANSIFormatting.None) { }

    public string Text => _text;
    public string? Hyperlink => _hyperlink;
    public AnsiColor? ColorForeground => _colorForeground;
    public AnsiColor? ColorBackground => _colorBackground;
    public double? Opacity => _opacity;
    public ANSIFormatting Formatting => _formatting;

    public ANSIString AddFormatting(ANSIFormatting add)
    {
        var newFormatting = _formatting | add;
        if (newFormatting.HasFlag(ANSIFormatting.UpperCase) && newFormatting.HasFlag(ANSIFormatting.LowerCase))
            throw new ArgumentException("formatting cannot include both UpperCase and LowerCase!", nameof(add));
        return new ANSIString(_text, _hyperlink, _colorForeground, _colorBackground, _opacity, newFormatting);
    }

    public ANSIString RemoveFormatting(ANSIFormatting rem) =>
        new(_text, _hyperlink, _colorForeground, _colorBackground, _opacity, _formatting & ~rem);

    public ANSIString SetForegroundColor(AnsiColor color) =>
        new(_text, _hyperlink, color, _colorBackground, _opacity, _formatting);

    public ANSIString SetBackgroundColor(AnsiColor color) =>
        new(_text, _hyperlink, _colorForeground, color, _opacity, _formatting);

    public ANSIString SetOpacity(double opacity) =>
        new(_text, _hyperlink, _colorForeground, _colorBackground, opacity, _formatting);

    public ANSIString SetHyperlink(string link) =>
        new(_text, link, _colorForeground, _colorBackground, _opacity, _formatting);

    internal static AnsiColor Interpolate(Color fromC, Color toC, double percentage)
    {
        double pTo = percentage;
        double pFrom = 1.0 - percentage;

        static byte Blend(byte from, byte to, double pf, double pt) =>
            (byte)(from * pf + to * pt);

        int r = Blend(fromC.R, toC.R, pFrom, pTo);
        int g = Blend(fromC.G, toC.G, pFrom, pTo);
        int b = Blend(fromC.B, toC.B, pFrom, pTo);
        return new AnsiColor.RGB(Color.FromArgb(r, g, b));
    }

    public override string ToString()
    {
        // Apply case transforms first
        string result = _text;
        if (_formatting.HasFlag(ANSIFormatting.UpperCase)) result = result.ToUpper();
        if (_formatting.HasFlag(ANSIFormatting.LowerCase)) result = result.ToLower();

        // Apply SGR formatting flags
        if (_formatting.HasFlag(ANSIFormatting.Bold))          result = ANSI.Bold + result;
        if (_formatting.HasFlag(ANSIFormatting.Faint))         result = ANSI.Faint + result;
        if (_formatting.HasFlag(ANSIFormatting.Italic))        result = ANSI.Italic + result;
        if (_formatting.HasFlag(ANSIFormatting.Underlined))    result = ANSI.Underlined + result;
        if (_formatting.HasFlag(ANSIFormatting.Overlined))     result = ANSI.Overlined + result;
        if (_formatting.HasFlag(ANSIFormatting.Blink))         result = ANSI.Blink + result;
        if (_formatting.HasFlag(ANSIFormatting.Inverted))      result = ANSI.Inverted + result;
        if (_formatting.HasFlag(ANSIFormatting.StrikeThrough)) result = ANSI.StrikeThrough + result;

        // Apply foreground color (with opacity interpolation if set)
        if (_opacity.HasValue && _colorForeground != null && _colorBackground != null)
        {
            if (_colorForeground is AnsiColor.RGB fg && _colorBackground is AnsiColor.RGB bg)
                result = ANSI.Foreground(Interpolate(bg.Value, fg.Value, _opacity.Value)) + result;
            else if (_colorForeground is AnsiColor.ANSI fa && _colorBackground is AnsiColor.ANSI ba)
            {
                var rgbA = ANSI.AnsiToRgb(fa.Value);
                var rgbB = ANSI.AnsiToRgb(ba.Value);
                result = ANSI.Foreground(Interpolate(rgbA, rgbB, _opacity.Value)) + result;
            }
            else if (_colorForeground is AnsiColor.ANSI fa2 && _colorBackground is AnsiColor.RGB rbg)
            {
                var rgbA = ANSI.AnsiToRgb(fa2.Value);
                result = ANSI.Foreground(Interpolate(rgbA, rbg.Value, _opacity.Value)) + result;
            }
            else if (_colorForeground is AnsiColor.RGB rfg && _colorBackground is AnsiColor.ANSI ba2)
            {
                var rgbB = ANSI.AnsiToRgb(ba2.Value);
                result = ANSI.Foreground(Interpolate(rgbB, rfg.Value, _opacity.Value)) + result;
            }
        }
        else if (_colorForeground != null)
        {
            result = ANSI.Foreground(_colorForeground) + result;
        }

        // Apply background color
        if (_colorBackground != null)
            result = ANSI.Background(_colorBackground) + result;

        // Apply hyperlink
        if (_hyperlink != null)
            result = ANSI.Hyperlink(result, _hyperlink);

        // Apply clear/trueclear
        if (_formatting.HasFlag(ANSIFormatting.TrueClear))
            result += ANSI.Clear;

        if (_formatting.HasFlag(ANSIFormatting.Clear))
            result = ANSI.Clear + result;

        return result;
    }
}

// ── StringExtensions ──────────────────────────────────────────────────────────

public static class StringExtensions
{
    public static ANSIString ToANSI(this string text) => new(text);

    public static ANSIString AddFormatting(this string text, ANSIFormatting formatting) =>
        text.ToANSI().AddFormatting(formatting);
    public static ANSIString AddFormattingANSI(this ANSIString text, ANSIFormatting formatting) =>
        text.AddFormatting(formatting);

    public static ANSIString Bold(this string text)    => text.ToANSI().AddFormatting(ANSIFormatting.Bold);
    public static ANSIString BoldANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.Bold);

    public static ANSIString Faint(this string text)   => text.ToANSI().AddFormatting(ANSIFormatting.Faint);
    public static ANSIString FaintANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.Faint);

    public static ANSIString Italic(this string text)  => text.ToANSI().AddFormatting(ANSIFormatting.Italic);
    public static ANSIString ItalicANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.Italic);

    public static ANSIString Underlined(this string text) => text.ToANSI().AddFormatting(ANSIFormatting.Underlined);
    public static ANSIString UnderlinedANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.Underlined);

    public static ANSIString Overlined(this string text) => text.ToANSI().AddFormatting(ANSIFormatting.Overlined);
    public static ANSIString OverlinedANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.Overlined);

    public static ANSIString Inverted(this string text) => text.ToANSI().AddFormatting(ANSIFormatting.Inverted);
    public static ANSIString InvertedANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.Inverted);

    public static ANSIString StrikeThrough(this string text) => text.ToANSI().AddFormatting(ANSIFormatting.StrikeThrough);
    public static ANSIString StrikeThroughANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.StrikeThrough);

    public static ANSIString UpperCase(this string text) => text.ToANSI().AddFormatting(ANSIFormatting.UpperCase);
    public static ANSIString UpperCaseANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.UpperCase);

    public static ANSIString LowerCase(this string text) => text.ToANSI().AddFormatting(ANSIFormatting.LowerCase);
    public static ANSIString LowerCaseANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.LowerCase);

    public static ANSIString NoClear(this ANSIString text) => text.RemoveFormatting(ANSIFormatting.Clear);

    public static ANSIString Clear(this string text)  => text.ToANSI().AddFormatting(ANSIFormatting.Clear);
    public static ANSIString ClearANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.Clear);

    public static ANSIString EndWithTrueClear(this string text) => text.ToANSI().AddFormatting(ANSIFormatting.TrueClear);
    public static ANSIString EndWithTrueClearANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.TrueClear);

    public static ANSIString Color(this string text, AnsiColor color) => text.ToANSI().SetForegroundColor(color);
    public static ANSIString ColorANSI(this ANSIString text, AnsiColor color) => text.SetForegroundColor(color);

    public static AnsiColor Rgb(Color color) => new AnsiColor.RGB(color);
    public static AnsiColor AnsiByte(byte color) => new AnsiColor.ANSI(new[] { color });
    public static AnsiColor AnsiBytes(byte[] color) => new AnsiColor.ANSI(color);

    public static ANSIString Background(this string text, AnsiColor color) => text.ToANSI().SetBackgroundColor(color);
    public static ANSIString BackgroundANSI(this ANSIString text, AnsiColor color) => text.SetBackgroundColor(color);

    public static ANSIString Opacity(this string text, int percent) =>
        text.ToANSI().SetOpacity(percent / 100.0);
    public static ANSIString OpacityANSI(this ANSIString text, int percent) =>
        text.SetOpacity(percent / 100.0);

    public static ANSIString Blink(this string text)  => text.ToANSI().AddFormatting(ANSIFormatting.Blink);
    public static ANSIString BlinkANSI(this ANSIString text) => text.AddFormatting(ANSIFormatting.Blink);

    public static ANSIString Link(this string text, string url) => text.ToANSI().SetHyperlink(url);
    public static ANSIString LinkANSI(this ANSIString text, string url) => text.SetHyperlink(url);
}

// ── Optimization ──────────────────────────────────────────────────────────────

/// <summary>
/// ANSI optimization functions for reducing redundant escape sequences.
/// </summary>
public static partial class Optimization
{
    [GeneratedRegex(@"(?<Pattern>(?:\u001b[^m]*m)+)(?<Body1>[^\u001b]+)\u001b\[0m\k<Pattern>(?<Body2>[^\u001b]+)\u001b\[0m")]
    private static partial Regex RepeatedPatternRegex();

    /// <summary>
    /// Optimizes repeated patterns like: [31mtext[0m[31mmore[0m → [31mtextmore[0m
    /// </summary>
    public static string OptimizeRepeatedPattern(string text)
    {
        while (true)
        {
            var m = RepeatedPatternRegex().Match(text);
            if (!m.Success) return text;
            text = RepeatedPatternRegex().Replace(text, "${Pattern}${Body1}${Body2}\u001b[0m");
        }
    }

    /// <summary>
    /// Optimizes repeated clear codes: ]0m]0m → ]0m
    /// </summary>
    public static string OptimizeRepeatedClear(string text) =>
        text.Replace("]0m]0m", "]0m");

    /// <summary>
    /// Removes duplicate consecutive escape codes, e.g. [31m[31m → [31m
    /// </summary>
    public static string OptimizeImpl(string text)
    {
        int currentIndex = 0;
        string currentEscapeCode = string.Empty;

        while (currentIndex < text.Length - 1)
        {
            int escapeCodeStartIndex = text.IndexOf("\u001b[", currentIndex, StringComparison.Ordinal);
            if (escapeCodeStartIndex == -1) break;

            int escapeCodeEndIndex = text.IndexOf("m", escapeCodeStartIndex, StringComparison.Ordinal);
            if (escapeCodeEndIndex == -1) break;

            string escapeCode = text.Substring(escapeCodeStartIndex, escapeCodeEndIndex - escapeCodeStartIndex + 1);
            if (escapeCode == currentEscapeCode)
            {
                text = text.Remove(escapeCodeStartIndex, escapeCodeEndIndex - escapeCodeStartIndex + 1);
                // do NOT advance currentIndex — re-check same position
            }
            else
            {
                currentIndex = escapeCodeEndIndex + 1;
                currentEscapeCode = escapeCode;
            }
        }

        return text;
    }

    /// <summary>
    /// Main optimization function that applies all optimizations to ANSI text.
    /// </summary>
    public static string Optimize(string text) =>
        OptimizeRepeatedClear(OptimizeRepeatedPattern(OptimizeImpl(text)));
}
