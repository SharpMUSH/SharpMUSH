// Converted from ColumnModule.fs — module SharpMUSH.MarkupString.ColumnModule
using System;
using System.Text.RegularExpressions;

namespace SharpMUSH.MarkupString.ColumnModule;

public enum Justification { Left, Center, Right, Full, Paragraph }

[Flags]
public enum ColumnOptions
{
    Default    = 0,
    Repeat     = 1,
    MergeToLeft  = 2,
    MergeToRight = 4,
    NoFill     = 8,
    Truncate   = 16,
    TruncateV2 = 32,
    NoColSep   = 64,
}

public record ColumnSpec(int Width, Justification Justification, ColumnOptions Options, string Ansi);

public static partial class ColumnSpecParser
{
    [GeneratedRegex(@"^([<>=_\-])?(\d+)([\.`'$xX#]*)(?:\((.+)\))?$")]
    private static partial Regex WidthPattern();

    public static ColumnSpec Parse(string spec)
    {
        var m = WidthPattern().Match(spec);
        if (!m.Success)
            throw new ArgumentException($"Invalid column specification: {spec}");

        Justification justification = m.Groups[1].Success
            ? m.Groups[1].Value switch
            {
                "<" => Justification.Left,
                "=" => Justification.Paragraph,
                ">" => Justification.Right,
                "_" => Justification.Full,
                "-" => Justification.Center,
                _   => Justification.Left,
            }
            : Justification.Left;

        ColumnOptions options = ColumnOptions.Default;
        if (m.Groups[3].Success)
            foreach (char c in m.Groups[3].Value)
                options |= c switch
                {
                    '.'  => ColumnOptions.Repeat,
                    '`'  => ColumnOptions.MergeToLeft,
                    '\'' => ColumnOptions.MergeToRight,
                    '$'  => ColumnOptions.NoFill,
                    'x'  => ColumnOptions.Truncate,
                    'X'  => ColumnOptions.TruncateV2,
                    '#'  => ColumnOptions.NoColSep,
                    _    => ColumnOptions.Default,
                };

        int width = int.Parse(m.Groups[2].Value);
        string ansi = m.Groups[4].Success ? m.Groups[4].Value : string.Empty;

        return new ColumnSpec(width, justification, options, ansi);
    }

    public static System.Collections.Generic.List<ColumnSpec> ParseList(string spec)
    {
        var result = new System.Collections.Generic.List<ColumnSpec>();
        foreach (var token in spec.Split(' '))
            result.Add(Parse(token));
        return result;
    }
}
