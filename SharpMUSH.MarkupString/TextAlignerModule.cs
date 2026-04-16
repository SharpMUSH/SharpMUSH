// Converted from TextAlignerModule.fs — module SharpMUSH.MarkupString.TextAlignerModule
using System;
using System.Collections.Generic;
using System.Linq;
using MarkupString;
using SharpMUSH.MarkupString.ColumnModule;

namespace SharpMUSH.MarkupString.TextAlignerModule;

using ColumnState = (ColumnSpec Spec, global::MarkupString.MarkupString Text);
using LineResult  = (ColumnSpec Spec, global::MarkupString.MarkupString Remainder, global::MarkupString.MarkupString Line);

public static class TextAlignerModule
{
    // ── Private helpers ────────────────────────────────────────────────────────

    private static (int SplitPoint, bool FoundSpace) FindWrapPoint(global::MarkupString.MarkupString text, int width)
    {
        int splitPoint = width;
        bool foundSpace = false;
        for (int i = width; i >= 0 && !foundSpace; i--)
        {
            if (i < text.Length && MarkupStringModule.plainText(MarkupStringModule.substring(i, 1, text)) == " ")
            {
                splitPoint = i;
                foundSpace = true;
            }
        }
        return (splitPoint, foundSpace);
    }

    private static global::MarkupString.MarkupString ApplyRepeatOption(
        ColumnSpec spec, global::MarkupString.MarkupString text, global::MarkupString.MarkupString remainder) =>
        spec.Options.HasFlag(ColumnOptions.Repeat) && remainder.Length == 0 && text.Length > 0
            ? text
            : remainder;

    private static (global::MarkupString.MarkupString Line, global::MarkupString.MarkupString Remainder)
        ExtractLineWithNewline(ColumnSpec spec, global::MarkupString.MarkupString text, int rowSepIndex)
    {
        var lineText = MarkupStringModule.substring(0, rowSepIndex, text);
        var remainder = rowSepIndex + 1 < text.Length
            ? MarkupStringModule.substring(rowSepIndex + 1, text.Length - (rowSepIndex + 1), text)
            : MarkupStringModule.empty();
        return (lineText, ApplyRepeatOption(spec, text, remainder));
    }

    private static (global::MarkupString.MarkupString Line, global::MarkupString.MarkupString Remainder)
        ExtractLineFitting(ColumnSpec spec, global::MarkupString.MarkupString text)
    {
        var remainder = spec.Options.HasFlag(ColumnOptions.Repeat) ? text : MarkupStringModule.empty();
        return (text, remainder);
    }

    private static (global::MarkupString.MarkupString Line, global::MarkupString.MarkupString Remainder)
        ExtractLineWithWrap(ColumnSpec spec, global::MarkupString.MarkupString text)
    {
        var (splitPoint, foundSpace) = FindWrapPoint(text, spec.Width);
        if (!foundSpace) splitPoint = spec.Width;

        var lineText = MarkupStringModule.substring(0, splitPoint, text);
        int remainderStart = foundSpace && splitPoint < text.Length ? splitPoint + 1 : splitPoint;
        var remainder = remainderStart < text.Length
            ? MarkupStringModule.substring(remainderStart, text.Length - remainderStart, text)
            : MarkupStringModule.empty();
        return (lineText, ApplyRepeatOption(spec, text, remainder));
    }

    private static (global::MarkupString.MarkupString Line, global::MarkupString.MarkupString Remainder)
        ExtractLineTruncated(ColumnSpec spec, global::MarkupString.MarkupString text, int rowSepIndex)
    {
        int splitPoint = rowSepIndex >= 0 && rowSepIndex < spec.Width
            ? rowSepIndex
            : text.Length > spec.Width ? spec.Width : text.Length;
        var lineText = MarkupStringModule.substring(0, splitPoint, text);
        return (lineText, MarkupStringModule.empty());
    }

    public static (global::MarkupString.MarkupString Line, global::MarkupString.MarkupString Remainder)
        ExtractLine(ColumnSpec spec, global::MarkupString.MarkupString text)
    {
        if (text.Length == 0) return (MarkupStringModule.empty(), MarkupStringModule.empty());
        if (spec.Options.HasFlag(ColumnOptions.TruncateV2)) return (text, MarkupStringModule.empty());

        int rowSepIndex = MarkupStringModule.indexOf(text, "\n");
        if (rowSepIndex >= 0 && rowSepIndex < spec.Width)
            return ExtractLineWithNewline(spec, text, rowSepIndex);
        if (spec.Options.HasFlag(ColumnOptions.Truncate))
            return ExtractLineTruncated(spec, text, rowSepIndex);
        if (text.Length <= spec.Width)
            return ExtractLineFitting(spec, text);
        return ExtractLineWithWrap(spec, text);
    }

    public static global::MarkupString.MarkupString Justify(
        Justification justification,
        global::MarkupString.MarkupString text,
        int width,
        global::MarkupString.MarkupString fill)
    {
        PadType padType = justification switch
        {
            Justification.Left      => PadType.Right,
            Justification.Center    => PadType.Center,
            Justification.Full      => PadType.Full,
            Justification.Right
            or Justification.Paragraph => PadType.Left,
            _ => throw new NotSupportedException(),
        };
        return MarkupStringModule.pad(text, fill, width, padType, TruncationType.Truncate);
    }

    private static List<ColumnState> MergeColumnLeft(List<ColumnState> columns, int index, ColumnSpec spec)
    {
        var result = new List<ColumnState>(columns);
        var leftState = result[index - 1];
        var leftSpec  = leftState.Spec;

        ColumnOptions newOptions = leftSpec.Options;
        if (spec.Options.HasFlag(ColumnOptions.NoFill))   newOptions |= ColumnOptions.NoFill;
        if (spec.Options.HasFlag(ColumnOptions.NoColSep)) newOptions |= ColumnOptions.NoColSep;

        int newWidth = leftSpec.Width + spec.Width - 2;
        result[index - 1] = (leftSpec with { Width = newWidth, Options = newOptions }, leftState.Text);
        result[index]     = (spec, MarkupStringModule.empty());
        return result;
    }

    private static List<ColumnState> MergeColumnRight(List<ColumnState> columns, int index, ColumnSpec spec)
    {
        var result = new List<ColumnState>(columns);
        var rightState = result[index + 1];
        var rightSpec  = rightState.Spec;
        result[index + 1] = (rightSpec with { Width = rightSpec.Width + spec.Width + 1 }, rightState.Text);
        result[index]     = (spec, MarkupStringModule.empty());
        return result;
    }

    private static List<ColumnState> HandleMerging(List<ColumnState> columns, int index)
    {
        var (spec, text) = columns[index];
        if (text.Length == 0 && spec.Options.HasFlag(ColumnOptions.MergeToLeft) && index > 0)
            return MergeColumnLeft(columns, index, spec);
        if (text.Length == 0 && spec.Options.HasFlag(ColumnOptions.MergeToRight) && index < columns.Count - 1)
            return MergeColumnRight(columns, index, spec);
        return columns;
    }

    private static bool MoreToDo(List<ColumnState> columns) =>
        columns.Any(cs => cs.Text.Length > 0 && !cs.Spec.Options.HasFlag(ColumnOptions.Repeat));

    private static global::MarkupString.MarkupString JustifyColumnLine(
        ColumnSpec spec, global::MarkupString.MarkupString line, global::MarkupString.MarkupString filler) =>
        spec.Options.HasFlag(ColumnOptions.NoFill)
            ? line
            : Justify(spec.Justification, line, spec.Width, filler);

    private static IEnumerable<global::MarkupString.MarkupString> BuildOutputParts(
        List<LineResult> lineResults,
        global::MarkupString.MarkupString columnSeparator,
        global::MarkupString.MarkupString filler)
    {
        for (int i = 0; i < lineResults.Count; i++)
        {
            var (spec, _, line) = lineResults[i];
            yield return JustifyColumnLine(spec, line, filler);

            bool needsSeparator = i < lineResults.Count - 1
                && !spec.Options.HasFlag(ColumnOptions.NoColSep);
            if (needsSeparator)
            {
                if (i > 0)
                {
                    var prevSpec = lineResults[i - 1].Spec;
                    if (prevSpec.Options.HasFlag(ColumnOptions.MergeToRight))
                    {
                        var extraPadding = MarkupStringModule.single(new string(' ', prevSpec.Width));
                        yield return MarkupStringModule.concat(extraPadding, columnSeparator);
                        continue;
                    }
                }
                yield return columnSeparator;
            }
        }
    }

    private static (List<ColumnState> Remainders, global::MarkupString.MarkupString OutputLine) DoLine(
        List<ColumnState> columns,
        global::MarkupString.MarkupString filler,
        global::MarkupString.MarkupString columnSeparator)
    {
        // Handle merging
        var mergedColumns = columns;
        for (int i = 0; i < mergedColumns.Count; i++)
            mergedColumns = HandleMerging(mergedColumns, i);

        // Extract one line per column
        var lineResults = new List<LineResult>(mergedColumns.Count);
        foreach (var (spec, text) in mergedColumns)
        {
            var (line, remainder) = ExtractLine(spec, text);
            lineResults.Add((spec, remainder, line));
        }

        // Filter out empty merged columns
        var filteredLineResults = lineResults
            .Where(lr => !(lr.Line.Length == 0 &&
                (lr.Spec.Options.HasFlag(ColumnOptions.MergeToLeft) ||
                 lr.Spec.Options.HasFlag(ColumnOptions.MergeToRight))))
            .ToList();

        var outputParts = BuildOutputParts(filteredLineResults, columnSeparator, filler);
        var outputLine = MarkupStringModule.multiple(outputParts);
        var remainders = filteredLineResults.Select(lr => (lr.Spec, lr.Remainder)).ToList();

        return (remainders, outputLine);
    }

    private static global::MarkupString.MarkupString AlignLoop(
        List<ColumnState> columns,
        global::MarkupString.MarkupString filler,
        global::MarkupString.MarkupString columnSeparator,
        global::MarkupString.MarkupString rowSeparator)
    {
        var accumulator = new List<global::MarkupString.MarkupString>();
        while (MoreToDo(columns))
        {
            var (remainder, newLine) = DoLine(columns, filler, columnSeparator);
            accumulator.Add(newLine);
            columns = remainder;
        }
        accumulator.Reverse();
        return MarkupStringModule.multipleWithDelimiter(rowSeparator, accumulator);
    }

    private static global::MarkupString.MarkupString? ValidateParameters(
        List<ColumnSpec> columnSpecs,
        IEnumerable<global::MarkupString.MarkupString> columns,
        global::MarkupString.MarkupString filler)
    {
        var colList = columns.ToList();
        if (columnSpecs.Count != colList.Count)
            return MarkupStringModule.single("#-1 COLUMN COUNT MISMATCH");
        if (filler.Length > 1)
            return MarkupStringModule.single("#-1 FILLER MUST BE ONE CHARACTER");
        if (columnSpecs.Any(s => s.Width <= 0))
            return MarkupStringModule.single("#-1 CANNOT HAVE COLUMNS OF NEGATIVE SIZE");
        if (columnSpecs.Any(s => s.Width > 5_000_000))
            return MarkupStringModule.single("#-1 CANNOT HAVE COLUMNS THAT LARGE");
        if (columnSpecs.Any(s =>
                s.Options.HasFlag(ColumnOptions.Repeat) &&
                (s.Options.HasFlag(ColumnOptions.Truncate) || s.Options.HasFlag(ColumnOptions.TruncateV2))))
            return MarkupStringModule.single("#-1 CANNOT REPEAT AND TRUNCATE");
        return null;
    }

    public static global::MarkupString.MarkupString Align(
        string widths,
        IEnumerable<global::MarkupString.MarkupString> columns,
        global::MarkupString.MarkupString filler,
        global::MarkupString.MarkupString columnSeparator,
        global::MarkupString.MarkupString rowSeparator)
    {
        var columnSpecs = ColumnSpecParser.ParseList(widths);
        var colList = columns.ToList();

        var error = ValidateParameters(columnSpecs, colList, filler);
        if (error is not null) return error;

        var cols = columnSpecs.Zip(colList, (spec, text) => (spec, text)).ToList();
        return AlignLoop(cols, filler, columnSeparator, rowSeparator);
    }

    // F#-style lowercase alias
    public static global::MarkupString.MarkupString align(
        string widths,
        IEnumerable<global::MarkupString.MarkupString> columns,
        global::MarkupString.MarkupString filler,
        global::MarkupString.MarkupString columnSeparator,
        global::MarkupString.MarkupString rowSeparator) =>
        Align(widths, columns, filler, columnSeparator, rowSeparator);
}
