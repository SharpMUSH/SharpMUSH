// Converted from Markup.fs — namespace MarkupString.MarkupImplementation
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json.Serialization;
using ANSILibrary;

namespace MarkupString.MarkupImplementation;

// ── Struct records ─────────────────────────────────────────────────────────────

/// <summary>
/// Describes the ANSI/terminal formatting for an AnsiMarkup span.
/// </summary>
public readonly record struct AnsiStructure
{
    public AnsiColor Foreground    { get; init; }
    public AnsiColor Background    { get; init; }
    public string?  LinkText       { get; init; }
    public string?  LinkUrl        { get; init; }
    public bool     Blink          { get; init; }
    public bool     Bold           { get; init; }
    public bool     Clear          { get; init; }
    public bool     Faint          { get; init; }
    public bool     Inverted       { get; init; }
    public bool     Italic         { get; init; }
    public bool     Overlined      { get; init; }
    public bool     Underlined     { get; init; }
    public bool     StrikeThrough  { get; init; }
}

/// <summary>
/// Describes an HTML tag and optional attributes for an HtmlMarkup span.
/// </summary>
public readonly record struct HtmlStructure
{
    public string  TagName    { get; init; }
    public string? Attributes { get; init; }
}

// ── IMarkup interface ─────────────────────────────────────────────────────────

/// <summary>
/// Defines how to wrap a text segment in a given output format.
/// Implemented by NeutralMarkup, AnsiMarkup and HtmlMarkup.
/// </summary>
[JsonDerivedType(typeof(NeutralMarkup), "Neutral")]
[JsonDerivedType(typeof(AnsiMarkup),    "Ansi")]
[JsonDerivedType(typeof(HtmlMarkup),    "Html")]
public interface IMarkup
{
    string Wrap(string text);
    string WrapAndRestore(string text, IMarkup outerMarkup);
    string WrapAs(string format, string text);
    string WrapAndRestoreAs(string format, string text, IMarkup outerMarkup);
    string Prefix { get; }
    string Postfix { get; }
    string Optimize(string text);
}

// ── NeutralMarkup ─────────────────────────────────────────────────────────────

public sealed class NeutralMarkup : IMarkup
{
    public static readonly NeutralMarkup Instance = new();

    public string Prefix  => string.Empty;
    public string Postfix => string.Empty;
    public string Wrap(string text)                                     => text;
    public string WrapAndRestore(string text, IMarkup _)                => text;
    public string WrapAs(string _format, string text)                   => text;
    public string WrapAndRestoreAs(string _format, string text, IMarkup _) => text;
    public string Optimize(string text)                                 => text;
}

// ── AnsiMarkup ────────────────────────────────────────────────────────────────

public sealed class AnsiMarkup : IMarkup
{
    public AnsiStructure Details { get; }

    public AnsiMarkup(AnsiStructure details) => Details = details;

    /// <summary>Factory with all optional named parameters matching the F# Create signature.</summary>
    public static AnsiMarkup Create(
        AnsiColor? foreground  = null,
        AnsiColor? background  = null,
        string?    linkText    = null,
        string?    linkUrl     = null,
        bool       blink       = false,
        bool       bold        = false,
        bool       clear       = false,
        bool       faint       = false,
        bool       inverted    = false,
        bool       italic      = false,
        bool       overlined   = false,
        bool       underlined  = false,
        bool       strikeThrough = false)
    {
        return new AnsiMarkup(new AnsiStructure
        {
            Foreground    = foreground  ?? AnsiColor.NoAnsi.Instance,
            Background    = background  ?? AnsiColor.NoAnsi.Instance,
            LinkText      = linkText    ?? string.Empty,
            LinkUrl       = linkUrl     ?? string.Empty,
            Blink         = blink,
            Bold          = bold,
            Clear         = clear,
            Faint         = faint,
            Inverted      = inverted,
            Italic        = italic,
            Overlined     = overlined,
            Underlined    = underlined,
            StrikeThrough = strikeThrough,
        });
    }

    /// <summary>Returns CSS class names for non-color formatting attributes.</summary>
    public static IReadOnlyList<string> HtmlClassNames(AnsiStructure d)
    {
        var list = new List<string>();
        if (d.Bold)          list.Add("ms-bold");
        if (d.Faint)         list.Add("ms-faint");
        if (d.Italic)        list.Add("ms-italic");
        if (d.Underlined)    list.Add("ms-underline");
        if (d.StrikeThrough) list.Add("ms-strike");
        if (d.Overlined)     list.Add("ms-overline");
        if (d.Blink)         list.Add("ms-blink");
        return list;
    }

    private static string? ColorToHex(AnsiColor color) => color switch
    {
        AnsiColor.NoAnsi          => null,
        AnsiColor.RGB c           => $"#{c.Value.R:x2}{c.Value.G:x2}{c.Value.B:x2}",
        AnsiColor.ANSI bytes =>
            (ANSI.AnsiToRgb(bytes.Value)) is var rgb
                ? $"#{rgb.R:x2}{rgb.G:x2}{rgb.B:x2}"
                : null,
        _ => throw new NotSupportedException()
    };

    /// <summary>Renders AnsiStructure as an HTML span with inline style + CSS classes.</summary>
    public static string WrapAsHtmlClass(AnsiStructure d, string text)
    {
        var (fg, bg) = d.Inverted ? (d.Background, d.Foreground) : (d.Foreground, d.Background);

        var styles = new List<string>();
        if (ColorToHex(fg) is string fgHex) styles.Add($"color: {fgHex}");
        if (ColorToHex(bg) is string bgHex) styles.Add($"background-color: {bgHex}");

        var classes = HtmlClassNames(d);

        // Wrap hyperlink
        string inner = text;
        if (d.LinkUrl is { Length: > 0 } url)
            inner = $"<a href=\"{WebUtility.HtmlEncode(url)}\">{text}</a>";

        string styleAttr = styles.Count > 0 ? $" style=\"{string.Join("; ", styles)}\"" : "";
        string classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";

        return (styleAttr.Length == 0 && classAttr.Length == 0)
            ? inner
            : $"<span{styleAttr}{classAttr}>{inner}</span>";
    }

    public static string WrapAsPueblo(AnsiStructure d, string text)
    {
        var (fg, bg) = d.Inverted ? (d.Background, d.Foreground) : (d.Foreground, d.Background);
        string t = text;
        if (d.StrikeThrough) t = $"<S>{t}</S>";
        if (d.Overlined)     t = $"<SPAN STYLE=\"text-decoration: overline\">{t}</SPAN>";
        if (d.Underlined)    t = $"<U>{t}</U>";
        if (d.Italic)        t = $"<I>{t}</I>";
        if (d.Bold)          t = $"<B>{t}</B>";
        if (d.LinkUrl is { Length: > 0 } url)
            t = $"<A HREF=\"{WebUtility.HtmlEncode(url)}\">{t}</A>";
        if (ColorToHex(bg) is string bgHex) t = $"<SPAN STYLE=\"background-color: {bgHex}\">{t}</SPAN>";
        if (ColorToHex(fg) is string fgHex) t = $"<FONT COLOR=\"{fgHex}\">{t}</FONT>";
        return t;
    }

    public static string WrapAsBBCode(AnsiStructure d, string text)
    {
        var fg = d.Inverted ? d.Background : d.Foreground;
        string t = text;
        if (d.StrikeThrough) t = $"[s]{t}[/s]";
        if (d.Underlined)    t = $"[u]{t}[/u]";
        if (d.Italic)        t = $"[i]{t}[/i]";
        if (d.Bold)          t = $"[b]{t}[/b]";
        if (d.LinkUrl is { Length: > 0 } url)
            t = $"[url={url}]{t}[/url]";
        if (ColorToHex(fg) is string fgHex) t = $"[color={fgHex}]{t}[/color]";
        return t;
    }

    public static string WrapAsMxp(AnsiStructure d, string text)
    {
        var (fg, bg) = d.Inverted ? (d.Background, d.Foreground) : (d.Foreground, d.Background);
        string t = text;
        if (d.StrikeThrough) t = $"<S>{t}</S>";
        if (d.Underlined)    t = $"<U>{t}</U>";
        if (d.Italic)        t = $"<I>{t}</I>";
        if (d.Bold)          t = $"<B>{t}</B>";
        if (d.LinkUrl is { Length: > 0 } url)
            t = $"<SEND HREF=\"{WebUtility.HtmlEncode(url)}\">{t}</SEND>";
        string? fgHex = ColorToHex(fg);
        string? bgHex = ColorToHex(bg);
        t = (fgHex, bgHex) switch
        {
            ({ } f, { } b) => $"<COLOR FORE=\"{f}\" BACK=\"{b}\">{t}</COLOR>",
            ({ } f, null)  => $"<COLOR FORE=\"{f}\">{t}</COLOR>",
            (null, { } b)  => $"<COLOR BACK=\"{b}\">{t}</COLOR>",
            _              => t,
        };
        return t;
    }

    public static ANSIString ApplyDetails(AnsiStructure d, string text)
    {
        var s = text.ToANSI();
        if (d.LinkUrl is { Length: > 0 } url)  s = s.LinkANSI(url);
        if (d.Foreground is not AnsiColor.NoAnsi) s = s.ColorANSI(d.Foreground);
        if (d.Background is not AnsiColor.NoAnsi) s = s.BackgroundANSI(d.Background);
        if (d.Blink)          s = s.BlinkANSI();
        if (d.Bold)           s = s.BoldANSI();
        if (d.Faint)          s = s.FaintANSI();
        if (d.Italic)         s = s.ItalicANSI();
        if (d.Overlined)      s = s.OverlinedANSI();
        if (d.Underlined)     s = s.UnderlinedANSI();
        if (d.StrikeThrough)  s = s.StrikeThroughANSI();
        if (d.Inverted)       s = s.InvertedANSI();
        if (d.Clear)          s = s.ClearANSI();
        return s;
    }

    // ── IMarkup ────────────────────────────────────────────────────────────────

    public string Prefix  => string.Empty;
    public string Postfix => string.Empty.EndWithTrueClear().ToString();

    public string Optimize(string text) => Optimization.Optimize(text);

    public string Wrap(string text) =>
        ApplyDetails(Details, text).ToString().EndWithTrueClear().ToString();

    public string WrapAndRestore(string text, IMarkup outerMarkup)
    {
        var applied = ApplyDetails(Details, text).ToString();
        string restore = outerMarkup switch
        {
            AnsiMarkup am when am.Details.Equals(Details) =>
                string.Empty.ToANSI().ToString(),
            AnsiMarkup am =>
                BuildRestore(am.Details).ToString(),
            HtmlMarkup => string.Empty,
            _ => throw new Exception("Unknown markup type")
        };
        return applied + restore;
    }

    private static ANSIString BuildRestore(AnsiStructure d)
    {
        var s = string.Empty.ToANSI();
        if (d.Foreground is not AnsiColor.NoAnsi) s = s.ColorANSI(d.Foreground);
        if (d.Background is not AnsiColor.NoAnsi) s = s.BackgroundANSI(d.Background);
        if (d.Blink)          s = s.BlinkANSI();
        if (d.Bold)           s = s.BoldANSI();
        if (d.Faint)          s = s.FaintANSI();
        if (d.Italic)         s = s.ItalicANSI();
        if (d.Overlined)      s = s.OverlinedANSI();
        if (d.Underlined)     s = s.UnderlinedANSI();
        if (d.StrikeThrough)  s = s.StrikeThroughANSI();
        if (d.Inverted)       s = s.InvertedANSI();
        return s;
    }

    public string WrapAs(string format, string text) => format.ToLower() switch
    {
        "html"     => WrapAsHtmlClass(Details, text),
        "pueblo"   => WrapAsPueblo(Details, text),
        "bbcode"   => WrapAsBBCode(Details, text),
        "mxp"      => WrapAsMxp(Details, text),
        _          => Wrap(text),
    };

    public string WrapAndRestoreAs(string format, string text, IMarkup outerMarkup) =>
        format.ToLower() switch
        {
            "html"   => WrapAsHtmlClass(Details, text),
            "pueblo" => WrapAsPueblo(Details, text),
            "bbcode" => WrapAsBBCode(Details, text),
            "mxp"    => WrapAsMxp(Details, text),
            _        => WrapAndRestore(text, outerMarkup),
        };
}

// ── HtmlMarkup ────────────────────────────────────────────────────────────────

public sealed class HtmlMarkup : IMarkup
{
    public HtmlStructure Details { get; }

    public HtmlMarkup(HtmlStructure details) => Details = details;

    public static HtmlMarkup Create(string tagName, string? attributes = null) =>
        new(new HtmlStructure { TagName = tagName, Attributes = attributes });

    public static string WrapAsAnsi(HtmlStructure d, string text)
    {
        AnsiStructure? ansiDetails = d.TagName.ToLowerInvariant() switch
        {
            "b" or "strong"         => AnsiMarkup.Create(bold: true).Details,
            "i" or "em"             => AnsiMarkup.Create(italic: true).Details,
            "u"                     => AnsiMarkup.Create(underlined: true).Details,
            "s" or "strike" or "del"=> AnsiMarkup.Create(strikeThrough: true).Details,
            _                       => null,
        };
        return ansiDetails.HasValue
            ? AnsiMarkup.ApplyDetails(ansiDetails.Value, text).ToString().EndWithTrueClear().ToString()
            : text;
    }

    public string Prefix  => string.Empty;
    public string Postfix => string.Empty;

    public string Wrap(string text) => Details.Attributes is { } attrs
        ? $"<{Details.TagName} {attrs}>{text}</{Details.TagName}>"
        : $"<{Details.TagName}>{text}</{Details.TagName}>";

    public string WrapAndRestore(string text, IMarkup _) => Wrap(text);

    public string WrapAs(string format, string text) => format.ToLower() switch
    {
        "ansi"                          => WrapAsAnsi(Details, text),
        "bbcode" or "plaintext" or "plain" => text,
        _                               => Wrap(text),
    };

    public string WrapAndRestoreAs(string format, string text, IMarkup _) =>
        format.ToLower() switch
        {
            "ansi"                             => WrapAsAnsi(Details, text),
            "bbcode" or "plaintext" or "plain" => text,
            _                                  => Wrap(text),
        };

    public string Optimize(string text) => text;
}
