// Converted from MarkupStringModule.fs — namespace MarkupString
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Drawing;
using ANSILibrary;
using MarkupString.MarkupImplementation;

namespace MarkupString;

// ── Simple enums (were F# single-case DUs with no payload) ────────────────────

public enum TrimType  { TrimStart, TrimEnd, TrimBoth }
public enum PadType   { Left, Right, Center, Full }
public enum TruncationType { Truncate, Overflow }

// ── RenderFormat — abstract sealed record DU ──────────────────────────────────

/// <summary>
/// Type-safe discriminated union for selecting a render format.
/// </summary>
public abstract record RenderFormat
{
    private RenderFormat() { }

    public sealed record Ansi      : RenderFormat { public static readonly Ansi Instance = new(); }
    public sealed record Html      : RenderFormat { public static readonly Html Instance = new(); }
    public sealed record PlainText : RenderFormat { public static readonly PlainText Instance = new(); }
    public sealed record Custom(
        Func<string, string> EncodeText,
        Func<IMarkup, string, string> ApplyMarkup) : RenderFormat;

    public IRenderStrategy ToStrategy() => this switch
    {
        Ansi      => MarkupStringModule.RenderStrategies.AnsiStrategy,
        Html      => MarkupStringModule.RenderStrategies.HtmlStrategy,
        PlainText => MarkupStringModule.RenderStrategies.PlainTextStrategy,
        Custom c  => new CustomRenderStrategy(c.EncodeText, c.ApplyMarkup),
        _         => throw new NotSupportedException(),
    };
}

// ── IRenderStrategy ───────────────────────────────────────────────────────────

public interface IRenderStrategy
{
    string EncodeText(string text);
    string ApplyMarkup(IMarkup markup, string text);
    string Prefix  { get; }
    string Postfix { get; }
    string Optimize(string text);
}

// ── Built-in strategy implementations ────────────────────────────────────────

internal sealed class AnsiRenderStrategy : IRenderStrategy
{
    public string EncodeText(string text) => text;
    public string ApplyMarkup(IMarkup markup, string text) => markup.WrapAs("ansi", text);
    public string Prefix  => string.Empty;
    public string Postfix => string.Empty.EndWithTrueClear().ToString();
    public string Optimize(string text) => Optimization.Optimize(text);
}

internal sealed class HtmlRenderStrategy : IRenderStrategy
{
    public string EncodeText(string text) => System.Net.WebUtility.HtmlEncode(text);
    public string ApplyMarkup(IMarkup markup, string text) => markup.WrapAs("html", text);
    public string Prefix  => string.Empty;
    public string Postfix => string.Empty;
    public string Optimize(string text) => text;
}

internal sealed class PlainTextRenderStrategy : IRenderStrategy
{
    public string EncodeText(string text) => text;
    public string ApplyMarkup(IMarkup _markup, string text) => text;
    public string Prefix  => string.Empty;
    public string Postfix => string.Empty;
    public string Optimize(string text) => text;
}

internal sealed class CustomRenderStrategy(
    Func<string, string> encodeText,
    Func<IMarkup, string, string> applyMarkup) : IRenderStrategy
{
    public string EncodeText(string text) => encodeText(text);
    public string ApplyMarkup(IMarkup markup, string t) => applyMarkup(markup, t);
    public string Prefix  => string.Empty;
    public string Postfix => string.Empty;
    public string Optimize(string text) => text;
}

internal sealed class NativeRenderStrategy(IMarkup firstMarkup) : IRenderStrategy
{
    public string EncodeText(string text) => text;
    public string ApplyMarkup(IMarkup markup, string text) => markup.Wrap(text);
    public string Prefix  => firstMarkup.Prefix;
    public string Postfix => firstMarkup.Postfix;
    public string Optimize(string text) => firstMarkup.Optimize(text);
}

// ── AttributeRun struct ───────────────────────────────────────────────────────

/// <summary>
/// Describes a contiguous range of characters that share the same markup attributes.
/// Runs are non-overlapping and ordered by Start position.
/// </summary>
public readonly record struct AttributeRun(int Start, int Length, ImmutableArray<IMarkup> Markups)
{
    public int End => Start + Length;
}

// ── RunStartComparer (private) ────────────────────────────────────────────────

file sealed class RunStartComparer : IComparer<AttributeRun>
{
    public static readonly RunStartComparer Instance = new();
    public int Compare(AttributeRun a, AttributeRun b) => a.Start.CompareTo(b.Start);
}

// ── ColorJsonConverter ─────────────────────────────────────────────────────────

public sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ColorTranslator.FromHtml(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
        writer.WriteStringValue($"#{value.R:X2}{value.G:X2}{value.B:X2}".ToLower());
}

// ── MarkupString class ────────────────────────────────────────────────────────

/// <summary>
/// A flat, attributed markup string inspired by NSAttributedString.
/// Fully immutable — text is a .NET string, runs are ImmutableArray&lt;AttributeRun&gt;.
/// </summary>
public sealed class MarkupString
{
    private readonly string _text;
    private readonly ImmutableArray<AttributeRun> _runs;

    // Lazily cached renders
    private readonly Lazy<string> _cachedToString;
    private readonly Lazy<string> _cachedAnsiRender;
    private readonly Lazy<string> _cachedHtmlRender;
    private readonly Lazy<string> _cachedPlainTextRender;

    public MarkupString(string text, ImmutableArray<AttributeRun> runs)
    {
        _text = text;
        _runs = runs;
        _cachedToString      = new Lazy<string>(() => NativeToString());
        _cachedAnsiRender    = new Lazy<string>(() => RenderWith(MarkupStringModule.RenderStrategies.AnsiStrategy));
        _cachedHtmlRender    = new Lazy<string>(() => RenderWith(MarkupStringModule.RenderStrategies.HtmlStrategy));
        _cachedPlainTextRender = new Lazy<string>(() => RenderWith(MarkupStringModule.RenderStrategies.PlainTextStrategy));
    }

    public string                   Text  => _text;
    public ImmutableArray<AttributeRun> Runs  => _runs;
    public int                      Length => _text.Length;

    public string ToPlainText() => _text;

    public override string ToString() => _cachedToString.Value;

    public string Render(string format) => format.ToLowerInvariant() switch
    {
        "html"               => _cachedHtmlRender.Value,
        "plaintext" or "plain" => _cachedPlainTextRender.Value,
        _                    => _cachedAnsiRender.Value,
    };

    public string Render(RenderFormat format) => format switch
    {
        RenderFormat.Ansi      => _cachedAnsiRender.Value,
        RenderFormat.Html      => _cachedHtmlRender.Value,
        RenderFormat.PlainText => _cachedPlainTextRender.Value,
        RenderFormat.Custom    => RenderWith(format.ToStrategy()),
        _                      => throw new NotSupportedException(),
    };

    public string RenderWith(IRenderStrategy strategy)
    {
        if (_runs.Length == 0)
            return strategy.EncodeText(_text);

        var sb = new StringBuilder(_text.Length * 2);
        bool hasAnyMarkup = false;

        foreach (var run in _runs)
        {
            string segment = strategy.EncodeText(_text.Substring(run.Start, run.Length));
            if (run.Markups.Length == 0)
            {
                sb.Append(segment);
            }
            else
            {
                string wrapped = segment;
                foreach (var markup in run.Markups)
                    wrapped = strategy.ApplyMarkup(markup, wrapped);
                sb.Append(wrapped);
                hasAnyMarkup = true;
            }
        }

        if (!hasAnyMarkup) return sb.ToString();

        string rendered = sb.ToString();
        string prefix = strategy.Prefix;
        string postfix = strategy.Postfix;
        if (prefix.Length > 0 || postfix.Length > 0)
            rendered = prefix + rendered + postfix;
        return strategy.Optimize(rendered);
    }

    public string EvaluateWith(Func<IMarkup?, string, string> evaluator)
    {
        var sb = new StringBuilder(_text.Length);
        foreach (var run in _runs)
        {
            string segment = _text.Substring(run.Start, run.Length);
            if (run.Markups.Length == 0)
            {
                sb.Append(evaluator(null, segment));
            }
            else
            {
                string result = segment;
                foreach (var markup in run.Markups)
                    result = evaluator(markup, result);
                sb.Append(result);
            }
        }
        return sb.ToString();
    }

    private string NativeToString()
    {
        IMarkup? firstMarkup = null;
        foreach (var run in _runs)
        {
            if (run.Markups.Length > 0) { firstMarkup = run.Markups[0]; break; }
        }
        return firstMarkup is null
            ? RenderWith(MarkupStringModule.RenderStrategies.PlainTextStrategy)
            : RenderWith(new NativeRenderStrategy(firstMarkup));
    }

    public override bool Equals(object? obj) => obj switch
    {
        MarkupString ms => _text.Equals(ms._text),
        string s        => _text.Equals(s),
        _               => false,
    };

    public override int GetHashCode() => _text.GetHashCode();
}

// ── MarkupStringModule static class (module-level functions) ──────────────────

public static partial class MarkupStringModule
{
    // ── Render strategy singletons ─────────────────────────────────────────────
    public static class RenderStrategies
    {
        public static readonly IRenderStrategy AnsiStrategy      = new AnsiRenderStrategy();
        public static readonly IRenderStrategy HtmlStrategy      = new HtmlRenderStrategy();
        public static readonly IRenderStrategy PlainTextStrategy = new PlainTextRenderStrategy();
    }

    public static IRenderStrategy ForFormat(RenderFormat format) => format.ToStrategy();

    // ── Private binary search helper ──────────────────────────────────────────

    private static int FindFirstOverlappingRunIndex(ImmutableArray<AttributeRun> runs, int position)
    {
        if (runs.Length == 0) return 0;
        var probe = new AttributeRun(position, 0, ImmutableArray<IMarkup>.Empty);
        int idx = runs.BinarySearch(probe, RunStartComparer.Instance);
        if (idx >= 0) return Math.Max(0, idx - 1);
        int insertionPoint = ~idx;
        return Math.Max(0, insertionPoint - 1);
    }

    // ── Construction ──────────────────────────────────────────────────────────

    public static MarkupString Single(string str)
    {
        if (str.Length == 0)
            return new MarkupString(string.Empty, ImmutableArray<AttributeRun>.Empty);
        return new MarkupString(str,
            ImmutableArray.Create(new AttributeRun(0, str.Length, ImmutableArray<IMarkup>.Empty)));
    }

    // F#-style lowercase alias kept for callers that use `single`
    public static MarkupString single(string? str) => str is null ? Empty() : Single(str);

    public static MarkupString Empty() =>
        new(string.Empty, ImmutableArray<AttributeRun>.Empty);
    public static MarkupString empty() => Empty();

    public static MarkupString MarkupSingle(IMarkup markup, string str)
    {
        var run = new AttributeRun(0, str.Length, ImmutableArray.Create(markup));
        return new MarkupString(str, ImmutableArray.Create(run));
    }
    public static MarkupString markupSingle((IMarkup, string) t) => MarkupSingle(t.Item1, t.Item2);

    public static MarkupString MarkupSingleMulti(ImmutableArray<IMarkup> markups, string str)
    {
        var run = new AttributeRun(0, str.Length, markups);
        return new MarkupString(str, ImmutableArray.Create(run));
    }
    public static MarkupString markupSingleMulti((ImmutableArray<IMarkup>, string) t) =>
        MarkupSingleMulti(t.Item1, t.Item2);

    // ── Core operations ────────────────────────────────────────────────────────

    public static string PlainText(MarkupString ams) => ams.ToPlainText();
    public static string plainText(MarkupString? ams) => ams?.ToPlainText() ?? string.Empty;

    public static int GetLength(MarkupString ams) => ams.Length;
    public static int getLength(MarkupString? ams) => ams?.Length ?? 0;

    public static MarkupString Concat(MarkupString a, MarkupString b)
    {
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        string combinedText = a.Text + b.Text;
        int offset = a.Text.Length;
        var builder = ImmutableArray.CreateBuilder<AttributeRun>(a.Runs.Length + b.Runs.Length);
        foreach (var run in a.Runs) builder.Add(run);
        foreach (var run in b.Runs)
            builder.Add(new AttributeRun(run.Start + offset, run.Length, run.Markups));
        return new MarkupString(combinedText, builder.ToImmutable());
    }
    public static MarkupString concat(MarkupString? a, MarkupString? b) => Concat(a ?? Empty(), b ?? Empty());

    public static MarkupString ConcatMany(IEnumerable<MarkupString> items)
    {
        var textSb = new StringBuilder();
        var runsBuilder = ImmutableArray.CreateBuilder<AttributeRun>();
        foreach (var item in items)
        {
            if (item.Length > 0)
            {
                int offset = textSb.Length;
                textSb.Append(item.Text);
                foreach (var run in item.Runs)
                    runsBuilder.Add(new AttributeRun(run.Start + offset, run.Length, run.Markups));
            }
        }
        return textSb.Length == 0
            ? Empty()
            : new MarkupString(textSb.ToString(), runsBuilder.ToImmutable());
    }
    public static MarkupString concatMany(IEnumerable<MarkupString> items) => ConcatMany(items);

    public static MarkupString Substring(int start, int length, MarkupString ams)
    {
        if (length <= 0 || start >= ams.Length) return Empty();
        int actualStart = Math.Max(0, start);
        int actualEnd   = Math.Min(ams.Length, actualStart + length);
        int actualLength = actualEnd - actualStart;
        string subText = ams.Text.Substring(actualStart, actualLength);
        int startIdx = FindFirstOverlappingRunIndex(ams.Runs, actualStart);

        var clippedRuns = new List<AttributeRun>();
        for (int i = startIdx; i < ams.Runs.Length; i++)
        {
            var run = ams.Runs[i];
            if (run.Start >= actualEnd) break;
            int runEnd = run.End;
            if (runEnd > actualStart)
            {
                int clippedStart  = Math.Max(run.Start, actualStart);
                int clippedEnd    = Math.Min(runEnd, actualEnd);
                int clippedLength = clippedEnd - clippedStart;
                if (clippedLength > 0)
                    clippedRuns.Add(new AttributeRun(clippedStart - actualStart, clippedLength, run.Markups));
            }
        }
        return new MarkupString(subText, ImmutableArray.CreateRange(clippedRuns));
    }
    public static MarkupString substring(int start, int length, MarkupString? ams) =>
        ams is null ? Empty() : Substring(start, length, ams);

    public static MarkupString[] Split(string delimiter, MarkupString ams)
    {
        if (ams.Length == 0) return Array.Empty<MarkupString>();
        string text = ams.Text;

        // Collect split positions
        var positions = new List<int>();
        int pos = 0;
        while (pos < text.Length)
        {
            int idx = text.IndexOf(delimiter, pos, StringComparison.Ordinal);
            if (idx < 0) break;
            positions.Add(idx);
            pos = delimiter.Length > 0 ? idx + delimiter.Length : idx + 1;
        }

        if (positions.Count == 0) return new[] { ams };

        var segments = new List<MarkupString>(positions.Count + 1);
        int lastPos = 0;
        foreach (int splitPos in positions)
        {
            segments.Add(Substring(lastPos, splitPos - lastPos, ams));
            lastPos = splitPos + delimiter.Length;
        }
        segments.Add(Substring(lastPos, text.Length - lastPos, ams));
        return segments.ToArray();
    }
    public static MarkupString[] split(string delimiter, MarkupString? ams) => ams is null ? Array.Empty<MarkupString>() : Split(delimiter, ams);

    public static MarkupString Trim(MarkupString ams, string trimChars, TrimType trimType)
    {
        string text = ams.Text;
        int len = text.Length;
        if (len == 0) return ams;

        int CountLeft(int i)
        {
            while (i < len && trimChars.Contains(text[i])) i++;
            return i;
        }
        int CountRight(int i)
        {
            while (i >= 0 && trimChars.Contains(text[i])) i--;
            return i + 1;
        }

        return trimType switch
        {
            TrimType.TrimStart =>
                CountLeft(0) is int l && l == 0 ? ams : Substring(l, len - l, ams),
            TrimType.TrimEnd =>
                CountRight(len - 1) is int r && r == len ? ams : Substring(0, r, ams),
            TrimType.TrimBoth =>
                (CountLeft(0), CountRight(len - 1)) is (int lt, int rb)
                    && lt == 0 && rb == len
                    ? ams
                    : Substring(lt, rb - lt, ams),
            _ => throw new NotSupportedException(),
        };
    }
    public static MarkupString trim(MarkupString ams, string trimChars, TrimType trimType) =>
        Trim(ams, trimChars, trimType);

    public static MarkupString Optimize(MarkupString ams)
    {
        if (ams.Runs.Length <= 1) return ams;

        static bool MarkupsEqual(ImmutableArray<IMarkup> a, ImmutableArray<IMarkup> b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;
        }

        var merged = new List<AttributeRun>();
        var current = ams.Runs[0];
        for (int i = 1; i < ams.Runs.Length; i++)
        {
            var next = ams.Runs[i];
            if (current.End == next.Start && MarkupsEqual(current.Markups, next.Markups))
                current = new AttributeRun(current.Start, current.Length + next.Length, current.Markups);
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);
        return new MarkupString(ams.Text, ImmutableArray.CreateRange(merged));
    }
    public static MarkupString optimize(MarkupString ams) => Optimize(ams);

    public static int IndexOf(MarkupString ams, string search) =>
        ams.Text.IndexOf(search, StringComparison.Ordinal);
    public static int indexOf(MarkupString? ams, string search) => ams is null ? -1 : IndexOf(ams, search);

    public static int IndexOfLast(MarkupString ams, string search) =>
        ams.Text.LastIndexOf(search, StringComparison.Ordinal);
    public static int indexOfLast(MarkupString ams, string search) => IndexOfLast(ams, search);

    public static MarkupString Apply(MarkupString ams, Func<string, string> transform)
    {
        string newText = transform(ams.Text);
        return newText.Length == ams.Text.Length
            ? new MarkupString(newText, ams.Runs)
            : new MarkupString(newText, ImmutableArray.Create(
                new AttributeRun(0, newText.Length, ImmutableArray<IMarkup>.Empty)));
    }
    public static MarkupString apply(MarkupString ams, Func<string, string> transform) =>
        Apply(ams, transform);

    public static MarkupString Remove(MarkupString ams, int index, int length)
    {
        if (length <= 0 || index >= ams.Length) return ams;
        var left  = Substring(0, index, ams);
        int rs    = index + length;
        var right = Substring(rs, ams.Length - rs, ams);
        return Concat(left, right);
    }
    public static MarkupString remove(MarkupString ams, int index, int length) =>
        Remove(ams, index, length);

    public static MarkupString Replace(MarkupString ams, MarkupString replacement, int index, int length)
    {
        if (index >= ams.Length)  return Concat(ams, replacement);
        if (index < 0)            return Concat(replacement, ams);
        var left       = Substring(0, index, ams);
        int rightStart = Math.Min(index + length, ams.Length);
        var right      = Substring(rightStart, ams.Length - rightStart, ams);
        return ConcatMany(new[] { left, replacement, right });
    }
    public static MarkupString replace(MarkupString ams, MarkupString replacement, int index, int length) =>
        Replace(ams, replacement, index, length);

    public static MarkupString Repeat(MarkupString ams, int count)
    {
        if (count <= 0) return Empty();
        if (count == 1) return ams;
        var acc     = Empty();
        var current = ams;
        int remaining = count;
        while (remaining > 0)
        {
            if (remaining % 2 == 1)
                acc = Concat(acc, current);
            current   = Concat(current, current);
            remaining /= 2;
        }
        return acc;
    }
    public static MarkupString repeat(MarkupString ams, int count) => Repeat(ams, count);

    private static MarkupString BuildPadding(MarkupString padStr, int exactLength)
    {
        if (exactLength <= 0 || padStr.Length == 0) return Empty();
        var textSb    = new StringBuilder(exactLength);
        var runsBldr  = ImmutableArray.CreateBuilder<AttributeRun>();
        int remaining = exactLength;
        while (remaining > 0)
        {
            int offset      = textSb.Length;
            int charsToTake = Math.Min(remaining, padStr.Text.Length);
            if (charsToTake == padStr.Text.Length)
            {
                textSb.Append(padStr.Text);
                foreach (var run in padStr.Runs)
                    runsBldr.Add(new AttributeRun(run.Start + offset, run.Length, run.Markups));
            }
            else
            {
                textSb.Append(padStr.Text, 0, charsToTake);
                foreach (var run in padStr.Runs)
                    if (run.Start < charsToTake)
                    {
                        int cl = Math.Min(run.Length, charsToTake - run.Start);
                        if (cl > 0)
                            runsBldr.Add(new AttributeRun(run.Start + offset, cl, run.Markups));
                    }
            }
            remaining -= charsToTake;
        }
        return new MarkupString(textSb.ToString(), runsBldr.ToImmutable());
    }

    public static MarkupString Pad(MarkupString ams, MarkupString padStr, int width, PadType padType, TruncationType truncType)
    {
        int lengthToPad = width - ams.Length;
        if (lengthToPad <= 0)
            return truncType == TruncationType.Overflow
                ? ams
                : lengthToPad == 0 ? ams : Substring(0, width, ams);

        switch (padType)
        {
            case PadType.Right:
            {
                var result = Concat(ams, BuildPadding(padStr, lengthToPad));
                return truncType == TruncationType.Truncate ? Substring(0, width, result) : result;
            }
            case PadType.Left:
            {
                var result = Concat(BuildPadding(padStr, lengthToPad), ams);
                return truncType == TruncationType.Truncate ? Substring(0, width, result) : result;
            }
            case PadType.Center:
            {
                int lp = lengthToPad / 2, rp = lengthToPad - lp;
                var result = ConcatMany(new[] { BuildPadding(padStr, lp), ams, BuildPadding(padStr, rp) });
                return truncType == TruncationType.Truncate ? Substring(0, width, result) : result;
            }
            case PadType.Full:
            {
                if (truncType == TruncationType.Truncate && ams.Length > width) return Substring(0, width, ams);
                if (truncType == TruncationType.Overflow && ams.Length > width) return ams;
                var wordArr = Split(" ", ams);
                int fences = Math.Max(wordArr.Length - 1, 0);
                if (fences == 0) return ams;
                int totalSpaces = fences + lengthToPad;
                var space = Single(" ");
                int minFenceWidth  = totalSpaces / fences;
                int thickerFences  = totalSpaces % fences;
                var fenceStr       = Repeat(space, minFenceWidth);
                var thickFenceStr  = Repeat(space, minFenceWidth + 1);
                var parts = new List<MarkupString>(wordArr.Length * 2 - 1);
                for (int i = 0; i < wordArr.Length; i++)
                {
                    if (i > 0) parts.Add(i <= thickerFences ? thickFenceStr : fenceStr);
                    parts.Add(wordArr[i]);
                }
                return ConcatMany(parts);
            }
            default: throw new NotSupportedException();
        }
    }
    public static MarkupString pad(MarkupString ams, MarkupString padStr, int width, PadType padType, TruncationType truncType) =>
        Pad(ams, padStr, width, padType, truncType);

    public static MarkupString MultipleWithDelimiter(MarkupString delimiter, IEnumerable<MarkupString> items)
    {
        var textSb   = new StringBuilder();
        var runsBldr = ImmutableArray.CreateBuilder<AttributeRun>();
        bool first = true;
        foreach (var item in items)
        {
            if (!first)
            {
                int offset = textSb.Length;
                textSb.Append(delimiter.Text);
                foreach (var run in delimiter.Runs)
                    runsBldr.Add(new AttributeRun(run.Start + offset, run.Length, run.Markups));
            }
            first = false;
            int itemOffset = textSb.Length;
            textSb.Append(item.Text);
            foreach (var run in item.Runs)
                runsBldr.Add(new AttributeRun(run.Start + itemOffset, run.Length, run.Markups));
        }
        return first ? Empty() : new MarkupString(textSb.ToString(), runsBldr.ToImmutable());
    }
    public static MarkupString multipleWithDelimiter(MarkupString? delimiter, IEnumerable<MarkupString?> items) =>
        MultipleWithDelimiter(delimiter ?? Empty(), items.Where(x => x != null).Select(x => x!));

    public static MarkupString InsertAt(MarkupString input, MarkupString insert, int index)
    {
        if (index <= 0) return Concat(insert, input);
        if (index >= input.Length) return Concat(input, insert);
        var before = Substring(0, index, input);
        var after  = Substring(index, input.Length - index, input);
        int runIndex = FindFirstOverlappingRunIndex(input.Runs, index);
        MarkupString wrappedInsert = insert;
        if (runIndex >= 0 && runIndex < input.Runs.Length)
        {
            var run = input.Runs[runIndex];
            if (run.Markups.Length > 0)
            {
                var insertRuns = ImmutableArray.CreateBuilder<AttributeRun>(insert.Runs.Length);
                foreach (var r in insert.Runs)
                {
                    var newMarkups = ImmutableArray.CreateBuilder<IMarkup>(r.Markups.Length + run.Markups.Length);
                    newMarkups.AddRange(r.Markups);
                    newMarkups.AddRange(run.Markups);
                    insertRuns.Add(new AttributeRun(r.Start, r.Length, newMarkups.ToImmutable()));
                }
                wrappedInsert = new MarkupString(insert.Text, insertRuns.ToImmutable());
            }
        }
        return ConcatMany(new[] { before, wrappedInsert, after });
    }
    public static MarkupString insertAt(MarkupString input, MarkupString insert, int index) =>
        InsertAt(input, insert, index);

    public static MarkupString Multiple(IEnumerable<MarkupString> items) => ConcatMany(items);
    public static MarkupString multiple(IEnumerable<MarkupString?> items) => ConcatMany(items.Where(x => x != null).Select(x => x!));

    public static MarkupString MarkupSingle2(IMarkup markup, MarkupString inner)
    {
        if (inner.Length == 0)
        {
            var run = new AttributeRun(0, 0, ImmutableArray.Create(markup));
            return new MarkupString("", ImmutableArray.Create(run));
        }
        var runsBldr = ImmutableArray.CreateBuilder<AttributeRun>(inner.Runs.Length);
        foreach (var run in inner.Runs)
        {
            var newMarkups = ImmutableArray.CreateBuilder<IMarkup>(run.Markups.Length + 1);
            newMarkups.AddRange(run.Markups);
            newMarkups.Add(markup);
            runsBldr.Add(new AttributeRun(run.Start, run.Length, newMarkups.ToImmutable()));
        }
        return new MarkupString(inner.Text, runsBldr.ToImmutable());
    }
    public static MarkupString markupSingle2((IMarkup, MarkupString) t) => MarkupSingle2(t.Item1, t.Item2);

    public static MarkupString MarkupMultiple(IMarkup markup, IEnumerable<MarkupString> items) =>
        MarkupSingle2(markup, ConcatMany(items));
    public static MarkupString markupMultiple((IMarkup, IEnumerable<MarkupString>) t) =>
        MarkupMultiple(t.Item1, t.Item2);

    public static MarkupString PlainText2(MarkupString ams) => Single(ams.ToPlainText());
    public static MarkupString plainText2(MarkupString ams) => PlainText2(ams);

    public static int IndexOf2(MarkupString ams, MarkupString search) =>
        ams.Text.IndexOf(search.Text, StringComparison.Ordinal);
    public static int indexOf2(MarkupString ams, MarkupString search) => IndexOf2(ams, search);

    public static IEnumerable<int> IndexesOf(MarkupString ams, MarkupString search)
    {
        string text = ams.Text, srch = search.Text;
        int pos = 0;
        while (pos < text.Length)
        {
            int found = text.IndexOf(srch, pos, StringComparison.Ordinal);
            if (found < 0) yield break;
            yield return found;
            pos = srch.Length > 0 ? found + srch.Length : found + 1;
        }
    }
    public static IEnumerable<int> indexesOf(MarkupString ams, MarkupString search) =>
        IndexesOf(ams, search);

    public static int IndexOfLast2(MarkupString ams, MarkupString search) =>
        ams.Text.LastIndexOf(search.Text, StringComparison.Ordinal);
    public static int indexOfLast2(MarkupString ams, MarkupString search) => IndexOfLast2(ams, search);

    public static MarkupString[] Split2(MarkupString delimiter, MarkupString ams) =>
        Split(delimiter.ToPlainText(), ams);
    public static MarkupString[] split2(MarkupString delimiter, MarkupString ams) => Split2(delimiter, ams);

    public static MarkupString[] SplitList(MarkupString delimiter, MarkupString ams)
    {
        var items = Split2(delimiter, ams);
        return delimiter.Length == 1 && delimiter.Text == " "
            ? Array.FindAll(items, x => x.Length > 0)
            : items;
    }
    public static MarkupString[] splitList(MarkupString? delimiter, MarkupString? ams) =>
        SplitList(delimiter ?? Empty(), ams ?? Empty());

    public static MarkupString Apply2(MarkupString ams, Func<MarkupString, MarkupString> transform)
    {
        var segments = new List<MarkupString>(ams.Runs.Length);
        foreach (var run in ams.Runs)
            segments.Add(transform(Substring(run.Start, run.Length, ams)));
        return ConcatMany(segments);
    }
    public static MarkupString apply2(MarkupString ams, Func<MarkupString, MarkupString> transform) =>
        Apply2(ams, transform);

    public static MarkupString Trim2(MarkupString ams, MarkupString trimStr, TrimType trimType) =>
        Trim(ams, trimStr.ToPlainText(), trimType);
    public static MarkupString trim2(MarkupString ams, MarkupString trimStr, TrimType trimType) =>
        Trim2(ams, trimStr, trimType);

    public static MarkupString ConcatAttach(MarkupString a, MarkupString b)
    {
        if (a.Runs.Length == 0) return Concat(a, b);
        var lastRun = a.Runs[a.Runs.Length - 1];
        if (lastRun.Markups.Length == 0) return Concat(a, b);
        var outerMarkup = lastRun.Markups[lastRun.Markups.Length - 1];
        return Concat(a, MarkupSingle2(outerMarkup, b));
    }
    public static MarkupString concatAttach(MarkupString a, MarkupString b) => ConcatAttach(a, b);

    public static IEnumerable<MarkupString> IntersperseFunc(Func<int, MarkupString> sepFunc, IEnumerable<MarkupString> items)
    {
        int i = 0;
        foreach (var item in items)
        {
            if (i > 0) yield return sepFunc(i);
            yield return item;
            i++;
        }
    }
    public static IEnumerable<MarkupString> intersperseFunc(Func<int, MarkupString> sepFunc, IEnumerable<MarkupString> items) =>
        IntersperseFunc(sepFunc, items);

    public static MarkupString MultipleWithDelimiterFunc(Func<int, MarkupString> delimiterFunc, IEnumerable<MarkupString> items) =>
        ConcatMany(IntersperseFunc(delimiterFunc, items));
    public static MarkupString multipleWithDelimiterFunc(Func<int, MarkupString> delimiterFunc, IEnumerable<MarkupString> items) =>
        MultipleWithDelimiterFunc(delimiterFunc, items);

    public static string Render(string format, MarkupString ams) => ams.Render(format);
    public static string render(string format, MarkupString ams) => ams.Render(format);

    public static string RenderFormat(RenderFormat format, MarkupString ams) => ams.Render(format);
    public static string renderFormat(RenderFormat format, MarkupString ams) => ams.Render(format);

    public static string EvaluateWith(Func<IMarkup?, string, string> evaluator, MarkupString ams) =>
        ams.EvaluateWith(evaluator);
    public static string evaluateWith(Func<IMarkup?, string, string> evaluator, MarkupString ams) =>
        ams.EvaluateWith(evaluator);

    public static readonly string FixedCss =
        ".ms-bold { font-weight: bold; }\n" +
        ".ms-faint { opacity: 0.5; }\n" +
        ".ms-italic { font-style: italic; }\n" +
        ".ms-underline { text-decoration: underline; }\n" +
        ".ms-strike { text-decoration: line-through; }\n" +
        ".ms-overline { text-decoration: overline; }\n" +
        ".ms-blink { animation: blink 1s step-start infinite; }\n";

    public static readonly string fixedCss = FixedCss;

    public static string CssSheet(MarkupString _ams) => FixedCss;
    public static string cssSheet(MarkupString _ams) => FixedCss;

    public static MarkupString Center2(MarkupString ams, MarkupString padStr, MarkupString padStrRight,
                                       int width, TruncationType truncType)
    {
        int lengthToPad = width - ams.Length;
        if (lengthToPad <= 0)
            return truncType == TruncationType.Overflow
                ? ams
                : lengthToPad == 0 ? ams : Substring(0, width, ams);
        int lp = lengthToPad / 2, rp = lengthToPad - lp;
        var result = ConcatMany(new[] { BuildPadding(padStr, lp), ams, BuildPadding(padStrRight, rp) });
        return truncType == TruncationType.Truncate ? Substring(0, width, result) : result;
    }
    public static MarkupString center2(MarkupString ams, MarkupString padStr, MarkupString padStrRight,
                                       int width, TruncationType truncType) =>
        Center2(ams, padStr, padStrRight, width, truncType);

    // ── Wildcard / regex helpers ───────────────────────────────────────────────

    [GeneratedRegex(@"(?<!\\)\\\*")]
    private static partial Regex GlobPatternRegex();
    [GeneratedRegex(@"(?<!\\)\\\?")]
    private static partial Regex QuestionPatternRegex();
    [GeneratedRegex(@"\\\\\\\*")]
    private static partial Regex KindPatternRegex();
    [GeneratedRegex(@"\\\\\\\?")]
    private static partial Regex KindPattern2Regex();

    private static string ApplyRegexPattern(string pat)
    {
        pat = GlobPatternRegex().Replace(pat, @"(.*?)");
        pat = QuestionPatternRegex().Replace(pat, @"(.)");
        pat = KindPatternRegex().Replace(pat, @"\*");
        pat = KindPattern2Regex().Replace(pat, @"\?");
        return pat;
    }

    public static string GetWildcardMatchAsRegex(MarkupString pattern) =>
        ApplyRegexPattern($"^{Regex.Escape(PlainText(pattern))}$");
    public static string getWildcardMatchAsRegex(MarkupString pattern) =>
        GetWildcardMatchAsRegex(pattern);

    public static string GetWildcardMatchAsRegex2(string pattern) =>
        ApplyRegexPattern($"^{Regex.Escape(pattern)}$");
    public static string getWildcardMatchAsRegex2(string pattern) =>
        GetWildcardMatchAsRegex2(pattern);

    public static bool IsWildcardMatch(MarkupString input, MarkupString pattern) =>
        Regex.IsMatch(PlainText(input), GetWildcardMatchAsRegex(pattern));
    public static bool isWildcardMatch(MarkupString? input, MarkupString? pattern) =>
        input is null || pattern is null ? false : IsWildcardMatch(input, pattern);

    public static bool IsWildcardMatch2(MarkupString input, string pattern) =>
        Regex.IsMatch(PlainText(input), GetWildcardMatchAsRegex2(pattern));
    public static bool isWildcardMatch2(MarkupString input, string pattern) =>
        IsWildcardMatch2(input, pattern);

    public static IEnumerable<(Match Match, IEnumerable<MarkupString> Groups)>
        GetMatches(MarkupString input, string pattern)
    {
        foreach (Match m in Regex.Matches(PlainText(input), pattern).Cast<Match>())
        {
            var groups = m.Groups.Cast<Group>()
                .Select(g => Substring(g.Index, g.Length, input));
            yield return (m, groups);
        }
    }
    public static IEnumerable<(Match, IEnumerable<MarkupString>)> getMatches(MarkupString input, string pattern) =>
        GetMatches(input, pattern);

    public static IEnumerable<(Match, IEnumerable<MarkupString>)>
        GetRegexpMatches(MarkupString input, MarkupString pattern) =>
        GetMatches(input, PlainText(pattern));
    public static IEnumerable<(Match, IEnumerable<MarkupString>)> getRegexpMatches(MarkupString input, MarkupString pattern) =>
        GetRegexpMatches(input, pattern);

    public static IEnumerable<(Match, IEnumerable<MarkupString>)>
        GetWildcardMatches(MarkupString input, MarkupString pattern) =>
        GetMatches(input, GetWildcardMatchAsRegex(pattern));
    public static IEnumerable<(Match, IEnumerable<MarkupString>)> getWildcardMatches(MarkupString input, MarkupString pattern) =>
        GetWildcardMatches(input, pattern);

    // ── Serialization ──────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _serializationOptions = BuildSerializationOptions();
    private static JsonSerializerOptions BuildSerializationOptions()
    {
        var opts = new JsonSerializerOptions
        {
            // Polymorphic serialization for IMarkup subtypes via [JsonDerivedType] on IMarkup
        };
        opts.Converters.Add(new ColorJsonConverter());
        return opts;
    }
    public static JsonSerializerOptions SerializationOptions => _serializationOptions;
    public static JsonSerializerOptions serializationOptions => _serializationOptions;

    public static string Serialize(MarkupString ams) =>
        JsonSerializer.Serialize(ams, _serializationOptions);
    public static string serialize(MarkupString ams) => Serialize(ams);

    public static MarkupString Deserialize(string jsonString)
    {
        if (jsonString.Length == 0) return Empty();
        return JsonSerializer.Deserialize<MarkupString>(jsonString, _serializationOptions)
               ?? Empty();
    }
    public static MarkupString deserialize(string jsonString) => Deserialize(jsonString);
}
