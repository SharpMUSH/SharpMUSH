/// <summary>
/// Provides functions for aligning and justifying text using MarkupString.
/// </summary>
module SharpMUSH.MarkupString.TextAlignerModule

open MarkupString.MarkupStringModule

open SharpMUSH.MarkupString.ColumnModule
    (*
ALIGN()
  align(<widths>, <col>[, ... , <colN>[, <filler>[, <colsep>[, <rowsep>]]]])
  lalign(<widths>, <colList>[, <delim>[, <filler>[, <colsep>[, <rowsep>]]]])

  Creates columns of text, each column designated by <col> arguments. Each <col> is individually wrapped inside its own column, allowing for easy creation of book pages, newsletters, or the like. In lalign(), <colList> is a <delim>-separated list of the columns.

  <widths> is a space-separated list of column widths. '10 10 10' for the widths argument specifies that there are 3 columns, each 10 spaces wide. You can alter the behavior of a column in multiple ways. (Check 'help align2' for more details)

  <filler> is a single character that, if given, is the character used to fill empty columns and remaining spaces. <colsep>, if given, is inserted between every column, on every row. <rowsep>, if given, is inserted between every line. By default, <filler> and <colsep> are a space, and <rowsep> is a newline.

  You can modify column behavior within align(). The basic format is:

  [justification]Width[options][(ansi)]

  Justification: Placing one of these characters before the width alters the spacing for this column (e.g: <30). Defaults to < (left-justify).
    < Left-justify       - Center-justify        > Right-justify
    _ Full-justify       = Paragraph-justify

  Other options: Adding these after the width will alter the column's behaviour in some situtations
    . Repeat for as long as there is non-repeating text in another column.
    ` When this column runs out of text, merge with the column to the left
    ' When this column runs out of text, merge with the column to the right
    $ nofill: Don't use filler after the text. If this is combined with merge-left, the column to its left inherits the 'nofill' when merged.
    x Truncate each (%r-separated) row instead of wrapping at the colwidth
    X Truncate the entire column at the end of the first row instead of wrapping
    # Don't add a <colsep> after this column. If combined with merge-left, the column to its left inherits this when merged.

  Ansi: Place ansi characters (as defined in 'help ansi()') within ()s to define a column's ansi markup.

  Examples:

    > &line me=align(<3 10 20$,([ljust(get(%0/sex),1,,1)]), name(%0),name(loc(%0)))
    > th iter(lwho(),u(line,##),%b,%r)
      (M) Walker     Tree
      (M) Ashen-Shug Apartment 306
          ar
      (F) Jane Doe   Nowhere

    > &line me=align(<3 10X 20X$,([ljust(get(%0/sex),1,,1)]), name(%0),name(loc(%0)))
    > th iter(lwho(),u(line,##),%b,%r)
      (M) Walker     Tree
      (M) Ashen-Shug Apartment 306
      (F) Jane Doe   Nowhere

    > &haiku me = Alignment function,%rIt justifies your writing,%rBut the words still suck.%rLuke

    > th [align(5 -40 5,,[repeat(-,40)]%r[u(haiku)]%r[repeat(-,40)],,%b,+)]

         +----------------------------------------+
         +          Alignment function,           +
         +       It justifies your writing,       +
         +       But the words still suck.        +
         +                  Luke                  +
         +----------------------------------------+

  > &dropcap me=%b_______%r|__%b%b%b__|%r%b%b%b|%b|%r%b%b%b|_|
  > &story me=%r'was the night before Christmas, when all through the house%rNot a creature was stirring, not even a mouse.%rThe stockings were hung by the chimney with care,%rIn hopes that St Nicholas soon would be there.
  > th align(9'(ch) 68, u(dropcap), u(story))

   _______
  |__   __| 'was the night before Christmas, when all through the house
     | |    Not a creature was stirring, not even a mouse.
     |_|    The stockings were hung by the chimney with care,
  In hopes that St Nicholas soon would be there.

  The dropcap 'T' will be in ANSI cyan-highlight, and merges with the 'story'
  column.

  > th align(>15 60,Walker,Staff & Developer,x,x)
  xxxxxxxxxWalkerxStaff & Developerxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
  > th align(>15 60$,Walker,Staff & Developer,x,x)
  xxxxxxxxxWalkerxStaff & Developer
    *)

    type private ColumnState = ColumnSpec * MarkupString
    type private LineResult = ColumnSpec * MarkupString * MarkupString

    /// <summary>
    /// Finds the best word-wrap point in text within the specified width.
    /// Returns the split position and whether a space was found.
    /// </summary>
    let private findWrapPoint (text: MarkupString) (width: int) : int * bool =
        let mutable splitPoint = width
        let mutable foundSpace = false

        for i in width .. -1 .. 0 do
            if not foundSpace && i < text.Length && (plainText (substring i 1 text)) = " " then
                splitPoint <- i
                foundSpace <- true

        (splitPoint, foundSpace)

    /// <summary>
    /// Handles extraction with Repeat option logic.
    /// </summary>
    let private applyRepeatOption (spec: ColumnSpec) (text: MarkupString) (remainder: MarkupString) : MarkupString =
        if
            spec.Options.HasFlag(ColumnOptions.Repeat)
            && remainder.Length = 0
            && text.Length > 0
        then
            text
        else
            remainder

    /// <summary>
    /// Extracts a line when text has an explicit newline.
    /// </summary>
    let private extractLineWithNewline
        (spec: ColumnSpec)
        (text: MarkupString)
        (rowSepIndex: int)
        : MarkupString * MarkupString =
        let lineText = substring 0 rowSepIndex text

        let remainder =
            if rowSepIndex + 1 < text.Length then
                substring (rowSepIndex + 1) (text.Length - (rowSepIndex + 1)) text
            else
                empty ()

        (lineText, applyRepeatOption spec text remainder)

    /// <summary>
    /// Extracts a line when text fits within column width.
    /// </summary>
    let private extractLineFitting (spec: ColumnSpec) (text: MarkupString) : MarkupString * MarkupString =
        let remainder =
            if spec.Options.HasFlag(ColumnOptions.Repeat) then
                text
            else
                empty ()

        (text, remainder)

    /// <summary>
    /// Extracts a line when text needs word wrapping.
    /// </summary>
    let private extractLineWithWrap (spec: ColumnSpec) (text: MarkupString) : MarkupString * MarkupString =
        let splitPoint, foundSpace = findWrapPoint text spec.Width
        let splitPoint = if not foundSpace then spec.Width else splitPoint

        let lineText = substring 0 splitPoint text

        let remainderStart =
            if foundSpace && splitPoint < text.Length then
                splitPoint + 1
            else
                splitPoint

        let remainder =
            if remainderStart < text.Length then
                substring remainderStart (text.Length - remainderStart) text
            else
                empty ()

        (lineText, applyRepeatOption spec text remainder)

    /// <summary>
    /// Extracts a line for truncation mode.
    /// </summary>
    let private extractLineTruncated
        (spec: ColumnSpec)
        (text: MarkupString)
        (rowSepIndex: int)
        : MarkupString * MarkupString =
        let splitPoint =
            if rowSepIndex >= 0 && rowSepIndex < spec.Width then
                rowSepIndex
            elif text.Length > spec.Width then
                spec.Width
            else
                text.Length

        let lineText = substring 0 splitPoint text
        // In truncate (x) mode we discard the remainder so that only one row is output.
        (lineText, empty ())

    /// <summary>
    /// Extracts one line from a MarkupString based on column width and truncation settings.
    /// Returns (extracted line, remainder).
    /// </summary>
    let extractLine (spec: ColumnSpec) (text: MarkupString) : MarkupString * MarkupString =
        match text.Length, spec.Options.HasFlag(ColumnOptions.TruncateV2) with
        | 0, _ -> (empty (), empty ())
        | _, true -> (text, empty ())
        | _ ->
            let rowSepIndex = indexOf text (single "\n")
            // Always prefer explicit newline if present, regardless of width
            if rowSepIndex >= 0 && rowSepIndex < spec.Width then
                extractLineWithNewline spec text rowSepIndex
            elif spec.Options.HasFlag(ColumnOptions.Truncate) then
                extractLineTruncated spec text rowSepIndex
            elif text.Length <= spec.Width then
                extractLineFitting spec text
            else
                extractLineWithWrap spec text

    /// <summary>
    /// Justifies a MarkupString according to the specified justification and width.
    /// </summary>
    let justify (justification: Justification) (text: MarkupString) (width: int) (fill: MarkupString) : MarkupString =
        let padType =
            match justification with
            | Justification.Left -> PadType.Right
            | Justification.Center -> PadType.Center
            | Justification.Full -> PadType.Full
            | Justification.Right
            | Justification.Paragraph -> PadType.Left

        pad text fill width padType TruncationType.Truncate

    /// <summary>
    /// Merges a column to the left, inheriting options.
    /// </summary>
    let private mergeColumnLeft (columns: ColumnState seq) (index: int) (spec: ColumnSpec) : ColumnState seq =
        columns
        |> Seq.mapi (fun i (s, t) ->
            if i = index - 1 then
                let newOptions =
                    let leftSpec, _ = Seq.item (index - 1) columns

                    leftSpec.Options
                    |> (fun opts ->
                        if spec.Options.HasFlag(ColumnOptions.NoFill) then
                            opts ||| ColumnOptions.NoFill
                        else
                            opts)
                    |> (fun opts ->
                        if spec.Options.HasFlag(ColumnOptions.NoColSep) then
                            opts ||| ColumnOptions.NoColSep
                        else
                            opts)

                let leftSpec, leftText = Seq.item (index - 1) columns
                let newWidth = leftSpec.Width + spec.Width - 2

                ({ leftSpec with
                    Width = newWidth
                    Options = newOptions },
                 leftText)
            elif i = index then
                (spec, empty ())
            else
                (s, t))

    /// <summary>
    /// Merges a column to the right.
    /// </summary>
    let private mergeColumnRight (columns: ColumnState seq) (index: int) (spec: ColumnSpec) : ColumnState seq =
        columns
        |> Seq.mapi (fun i (s, t) ->
            if i = index + 1 then
                let rightSpec, rightText = Seq.item (index + 1) columns

                let newRightSpec =
                    { rightSpec with
                        Width = rightSpec.Width + spec.Width + 1 }

                (newRightSpec, rightText)
            elif i = index then
                (spec, empty ())
            else
                (s, t))

    /// <summary>
    /// Processes column merging logic when a column is empty.
    /// </summary>
    let private handleMerging (columns: ColumnState seq) (index: int) : ColumnState seq =
        let spec, text = Seq.item index columns

        match text.Length, spec.Options with
        | 0, opts when opts.HasFlag(ColumnOptions.MergeToLeft) && index > 0 -> mergeColumnLeft columns index spec
        | 0, opts when opts.HasFlag(ColumnOptions.MergeToRight) && index < Seq.length columns - 1 ->
            mergeColumnRight columns index spec
        | _ -> columns

    /// <summary>
    /// Determines if there is more text to process in any column.
    /// </summary>
    let private moreToDo (columns: ColumnState seq) : bool =
        columns
        |> Seq.exists (fun (spec, text) -> text.Length > 0 && not (spec.Options.HasFlag(ColumnOptions.Repeat)))

    /// <summary>
    /// Justifies a single column line with proper handling of NoFill option.
    /// If NoFill is set, the text is not padded beyond its natural length.
    /// </summary>
    let private justifyColumnLine (spec: ColumnSpec) (line: MarkupString) (filler: MarkupString) : MarkupString =
        if spec.Options.HasFlag(ColumnOptions.NoFill) then
            line
        else
            justify spec.Justification line spec.Width filler

    /// <summary>
    /// Builds output parts sequence with column separators.
    /// </summary>
    let private buildOutputParts
        (lineResults: LineResult seq)
        (columnSeparator: MarkupString)
        (filler: MarkupString)
        : MarkupString seq =
        lineResults
        |> Seq.mapi (fun i (spec, _, line) ->
            let justifiedLine = justifyColumnLine spec line filler

            let needsSeparator =
                i < Seq.length lineResults - 1
                && not (spec.Options.HasFlag(ColumnOptions.NoColSep))

            let separatorWithExtra =
                if i > 0 then
                    let prevSpec = lineResults |> Seq.item (i - 1) |> (fun (s, _, _) -> s)

                    if prevSpec.Options.HasFlag(ColumnOptions.MergeToRight) then
                        let extraPadding = single (String.replicate prevSpec.Width " ")
                        concat extraPadding columnSeparator None
                    else
                        columnSeparator
                else
                    columnSeparator

            seq {
                yield justifiedLine

                if needsSeparator then
                    yield separatorWithExtra
            })
        |> Seq.concat

    /// <summary>
    /// Processes one line across all columns, returning remainders and the formatted line.
    /// </summary>
    let private doLine
        (columns: ColumnState seq)
        (filler: MarkupString)
        (columnSeparator: MarkupString)
        : ColumnState seq * MarkupString =

        let mergedColumns =
            Seq.fold handleMerging columns [ 0 .. Seq.length columns - 1 ]

        let lineResults =
            mergedColumns
            |> Seq.map (fun (spec, text) ->
                let line, remainder = extractLine spec text
                (spec, remainder, line))

        let filteredLineResults =
            lineResults
            |> Seq.filter (fun (spec, _, line) ->
                not (
                    line.Length = 0
                    && (spec.Options.HasFlag(ColumnOptions.MergeToLeft)
                        || spec.Options.HasFlag(ColumnOptions.MergeToRight))
                ))

        let outputParts = buildOutputParts filteredLineResults columnSeparator filler
        let outputLine = multiple outputParts
        let remainders = filteredLineResults |> Seq.map (fun (spec, rem, _) -> (spec, rem))

        (remainders, outputLine)

    /// <summary>
    /// Recursively processes columns until no more text remains.
    /// </summary>
    let rec private alignLoop
        (columns: ColumnState seq)
        (filler: MarkupString)
        (columnSeparator: MarkupString)
        (rowSeparator: MarkupString)
        (accumulator: MarkupString seq)
        : MarkupString =

        if not (moreToDo columns) then
            accumulator |> Seq.rev |> multipleWithDelimiter rowSeparator
        else
            let remainder, newLine = doLine columns filler columnSeparator
            alignLoop remainder filler columnSeparator rowSeparator (Seq.append (seq { yield newLine }) accumulator)

    /// <summary>
    /// Validates alignment parameters.
    /// </summary>
    let private validateParameters
        (columnSpecs: ColumnSpec seq)
        (columns: MarkupString seq)
        (filler: MarkupString)
        : Result<unit, string> =

        if Seq.length columnSpecs <> Seq.length columns then
            Error "#-1 COLUMN COUNT MISMATCH"
        elif filler.Length > 1 then
            Error "#-1 FILLER MUST BE ONE CHARACTER"
        elif columnSpecs |> Seq.exists (fun spec -> spec.Width <= 0) then
            Error "#-1 CANNOT HAVE COLUMNS OF NEGATIVE SIZE"
        elif columnSpecs |> Seq.exists (fun spec -> spec.Width > 5_000_000) then
            Error "#-1 CANNOT HAVE COLUMNS THAT LARGE"
        elif
            columnSpecs |> Seq.exists (fun spec ->
                (spec.Options.HasFlag ColumnOptions.Repeat)
                && ((spec.Options.HasFlag ColumnOptions.Truncate)
                    || (spec.Options.HasFlag ColumnOptions.TruncateV2)))
        then
            Error "#-1 CANNOT REPEAT AND TRUNCATE"
        else
            Ok()

    /// <summary>
    /// Aligns a list of MarkupStrings into columns according to a width specification.
    /// </summary>
    let align
        (widths: string)
        (columns: MarkupString seq)
        (filler: MarkupString)
        (columnSeparator: MarkupString)
        (rowSeparator: MarkupString)
        : MarkupString =

        let columnSpecs = ColumnSpec.parseList widths

        match validateParameters columnSpecs columns filler with
        | Error msg -> single msg
        | Ok() ->
            Seq.zip columnSpecs columns
            |> fun cols -> alignLoop cols filler columnSeparator rowSeparator Seq.empty


