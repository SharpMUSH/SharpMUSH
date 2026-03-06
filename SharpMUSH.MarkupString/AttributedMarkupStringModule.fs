namespace MarkupString

open System
open System.Text
open MarkupString.MarkupImplementation
open ANSILibrary.ANSI

/// <summary>
/// NSAttributedString-inspired flat markup string model.
/// Uses a contiguous string with an array of attribute runs describing formatting.
/// This eliminates tree traversal, provides O(1) plain text access, and enables
/// single-pass rendering with excellent cache locality.
/// </summary>
module AttributedMarkupStringModule =

    /// <summary>
    /// Describes a contiguous range of characters that share the same markup attributes.
    /// Runs are non-overlapping and ordered by Start position.
    /// </summary>
    [<Struct>]
    type AttributeRun =
        {
            /// Start index within the parent string (inclusive).
            Start: int
            /// Number of characters this run covers.
            Length: int
            /// The markup attributes applied to this range.
            /// Empty array means plain/unformatted text.
            Markups: Markup array
        }
        member this.End = this.Start + this.Length

    /// <summary>
    /// A flat, attributed markup string inspired by NSAttributedString.
    /// Stores text contiguously with a parallel array of attribute runs.
    /// </summary>
    type AttributedMarkupString(text: string, runs: AttributeRun array) =

        // ── Private rendering helpers ──────────────────────────────────

        let renderAnsi () : string =
            if runs.Length = 0 then
                text
            else
                let sb = StringBuilder(text.Length * 2)
                let mutable hasAnyMarkup = false

                for run in runs do
                    let segment = text.Substring(run.Start, run.Length)
                    if run.Markups.Length = 0 then
                        sb.Append(segment) |> ignore
                    else
                        hasAnyMarkup <- true
                        let mutable result = segment
                        for markup in run.Markups do
                            result <- markup.Wrap(result)
                        sb.Append(result) |> ignore

                if hasAnyMarkup then
                    let firstMarkup = runs |> Array.tryFind (fun r -> r.Markups.Length > 0)
                    match firstMarkup with
                    | Some r ->
                        let m = r.Markups[0]
                        m.Optimize(m.Prefix + sb.ToString() + m.Postfix)
                    | None -> sb.ToString()
                else
                    sb.ToString()

        let renderFormat (format: string) : string =
            let fmt = format.ToLowerInvariant()
            match fmt with
            | "ansi" -> renderAnsi()
            | _ ->
                let sb = StringBuilder(text.Length * 2)
                let encodeText =
                    if fmt = "html" then System.Net.WebUtility.HtmlEncode
                    else id
                for run in runs do
                    let segment = encodeText (text.Substring(run.Start, run.Length))
                    if run.Markups.Length = 0 then
                        sb.Append(segment) |> ignore
                    else
                        let mutable result = segment
                        for markup in run.Markups do
                            result <- markup.WrapAs(fmt, result)
                        sb.Append(result) |> ignore
                sb.ToString()

        let cachedToString = Lazy<string>(renderAnsi)
        let cachedPlainText = Lazy<string>(fun () -> text)

        // ── Public API ─────────────────────────────────────────────────

        /// The raw underlying text with no markup.
        member _.Text = text

        /// The attribute runs describing formatting ranges.
        member _.Runs = runs

        /// The plain text length.
        member _.Length = text.Length

        /// Returns the plain text with no formatting.
        member _.ToPlainText() : string = cachedPlainText.Value

        /// Renders to ANSI escape codes (default format).
        override _.ToString() : string = cachedToString.Value

        /// Renders to the specified format ("ansi", "html", etc.).
        member _.Render(format: string) : string = renderFormat format

        /// <summary>
        /// Evaluates the attributed string using a custom evaluation function.
        /// Groups consecutive characters by their markup and calls the evaluator per group.
        /// </summary>
        member _.EvaluateWith(evaluator: Func<MarkupStringModule.MarkupTypes, string, string>) : string =
            let sb = StringBuilder(text.Length)
            for run in runs do
                let segment = text.Substring(run.Start, run.Length)
                let markupType =
                    if run.Markups.Length > 0 then
                        MarkupStringModule.MarkupTypes.MarkedupText run.Markups[0]
                    else
                        MarkupStringModule.MarkupTypes.Empty
                sb.Append(evaluator.Invoke(markupType, segment)) |> ignore
            sb.ToString()

        override _.Equals(obj) =
            match obj with
            | :? AttributedMarkupString as other -> text.Equals(other.Text)
            | :? string as other -> text.Equals(other)
            | _ -> false

        override _.GetHashCode() = text.GetHashCode()

    // ── Construction functions ──────────────────────────────────────

    /// Creates an AttributedMarkupString from a plain string with no markup.
    let single (str: string) : AttributedMarkupString =
        if str.Length = 0 then
            AttributedMarkupString(String.Empty, Array.empty)
        else
            AttributedMarkupString(str, [| { Start = 0; Length = str.Length; Markups = Array.empty } |])

    /// Returns an empty AttributedMarkupString.
    let empty () : AttributedMarkupString =
        AttributedMarkupString(String.Empty, Array.empty)

    /// Creates an AttributedMarkupString from a markup and a plain string.
    let markupSingle (markup: Markup, str: string) : AttributedMarkupString =
        if str.Length = 0 then
            AttributedMarkupString(String.Empty, Array.empty)
        else
            AttributedMarkupString(str, [| { Start = 0; Length = str.Length; Markups = [| markup |] } |])

    /// Creates an AttributedMarkupString from a markup and multiple markups applied to a string.
    let markupSingleMulti (markups: Markup array, str: string) : AttributedMarkupString =
        if str.Length = 0 then
            AttributedMarkupString(String.Empty, Array.empty)
        else
            AttributedMarkupString(str, [| { Start = 0; Length = str.Length; Markups = markups } |])

    // ── Core operations ────────────────────────────────────────────

    /// Returns the plain text of an AttributedMarkupString.
    let plainText (ams: AttributedMarkupString) : string = ams.ToPlainText()

    /// Returns the plain text length.
    let getLength (ams: AttributedMarkupString) : int = ams.Length

    /// <summary>
    /// Concatenates two AttributedMarkupStrings.
    /// Runs from the second string are shifted by the length of the first.
    /// </summary>
    let concat (a: AttributedMarkupString) (b: AttributedMarkupString) : AttributedMarkupString =
        if a.Length = 0 then b
        elif b.Length = 0 then a
        else
            let combinedText = a.Text + b.Text
            let offset = a.Text.Length
            let shiftedBRuns =
                b.Runs |> Array.map (fun r ->
                    { Start = r.Start + offset; Length = r.Length; Markups = r.Markups })
            let combinedRuns = Array.append a.Runs shiftedBRuns
            AttributedMarkupString(combinedText, combinedRuns)

    /// <summary>
    /// Returns a substring of an AttributedMarkupString, preserving markup runs.
    /// Runs that partially overlap the range are clipped to the range boundaries.
    /// </summary>
    let substring (start: int) (length: int) (ams: AttributedMarkupString) : AttributedMarkupString =
        if length <= 0 || start >= ams.Length then
            empty ()
        else
            let actualStart = max 0 start
            let actualEnd = min ams.Length (actualStart + length)
            let actualLength = actualEnd - actualStart
            let subText = ams.Text.Substring(actualStart, actualLength)
            let newRuns = ResizeArray<AttributeRun>()
            for run in ams.Runs do
                let runEnd = run.End
                if runEnd > actualStart && run.Start < actualEnd then
                    let clippedStart = max run.Start actualStart
                    let clippedEnd = min runEnd actualEnd
                    let clippedLength = clippedEnd - clippedStart
                    if clippedLength > 0 then
                        newRuns.Add({
                            Start = clippedStart - actualStart
                            Length = clippedLength
                            Markups = run.Markups
                        })
            AttributedMarkupString(subText, newRuns.ToArray())

    /// <summary>
    /// Splits an AttributedMarkupString by a string delimiter.
    /// </summary>
    let split (delimiter: string) (ams: AttributedMarkupString) : AttributedMarkupString array =
        if ams.Length = 0 then
            [| ams |]
        else
            let text = ams.Text
            let rec findPositions (pos: int) acc =
                if pos >= text.Length then
                    List.rev acc
                else
                    match text.IndexOf(delimiter, pos, StringComparison.Ordinal) with
                    | -1 -> List.rev acc
                    | idx ->
                        let nextPos = if delimiter.Length > 0 then idx + delimiter.Length else idx + 1
                        findPositions nextPos (idx :: acc)
            let positions = findPositions 0 []
            match positions with
            | [] -> [| ams |]
            | _ ->
                let rec buildSegments (positions: int list) (lastPos: int) acc =
                    match positions with
                    | [] ->
                        let seg = substring lastPos (text.Length - lastPos) ams
                        List.rev (seg :: acc)
                    | pos :: tail ->
                        let len = pos - lastPos
                        let seg = substring lastPos len ams
                        buildSegments tail (pos + delimiter.Length) (seg :: acc)
                buildSegments positions 0 [] |> Array.ofList

    /// <summary>
    /// Trims characters from the start, end, or both of an AttributedMarkupString.
    /// </summary>
    let trim (ams: AttributedMarkupString) (trimChars: string) (trimType: MarkupStringModule.TrimType) : AttributedMarkupString =
        let text = ams.Text
        let len = text.Length
        if len = 0 then ams
        else
            let rec countLeft i =
                if i >= len || not (trimChars.Contains(text.[i])) then i
                else countLeft (i + 1)
            let rec countRight i =
                if i < 0 || not (trimChars.Contains(text.[i])) then i + 1
                else countRight (i - 1)
            match trimType with
            | MarkupStringModule.TrimType.TrimStart ->
                let leftTrim = countLeft 0
                if leftTrim = 0 then ams
                else substring leftTrim (len - leftTrim) ams
            | MarkupStringModule.TrimType.TrimEnd ->
                let rightBoundary = countRight (len - 1)
                if rightBoundary = len then ams
                else substring 0 rightBoundary ams
            | MarkupStringModule.TrimType.TrimBoth ->
                let leftTrim = countLeft 0
                let rightBoundary = countRight (len - 1)
                if leftTrim = 0 && rightBoundary = len then ams
                else substring leftTrim (rightBoundary - leftTrim) ams

    /// <summary>
    /// Optimizes an AttributedMarkupString by merging adjacent runs with identical markups.
    /// </summary>
    let optimize (ams: AttributedMarkupString) : AttributedMarkupString =
        if ams.Runs.Length <= 1 then ams
        else
            let markupsEqual (a: Markup array) (b: Markup array) =
                if a.Length <> b.Length then false
                else Array.forall2 (fun (x: Markup) (y: Markup) -> x = y) a b

            let merged = ResizeArray<AttributeRun>()
            let mutable current = ams.Runs[0]
            for i in 1 .. ams.Runs.Length - 1 do
                let next = ams.Runs[i]
                if current.End = next.Start && markupsEqual current.Markups next.Markups then
                    current <- { Start = current.Start; Length = current.Length + next.Length; Markups = current.Markups }
                else
                    merged.Add(current)
                    current <- next
            merged.Add(current)
            AttributedMarkupString(ams.Text, merged.ToArray())

    /// <summary>
    /// Returns the first index where a search string occurs.
    /// Returns -1 if not found.
    /// </summary>
    let indexOf (ams: AttributedMarkupString) (search: string) : int =
        ams.Text.IndexOf(search, StringComparison.Ordinal)

    /// <summary>
    /// Returns the last index where a search string occurs.
    /// Returns -1 if not found.
    /// </summary>
    let indexOfLast (ams: AttributedMarkupString) (search: string) : int =
        ams.Text.LastIndexOf(search, StringComparison.Ordinal)

    /// <summary>
    /// Applies a text transformation function to all text content, preserving runs.
    /// Note: This only works correctly for character-preserving transforms (same length).
    /// For length-changing transforms, runs would need to be recalculated.
    /// </summary>
    let apply (ams: AttributedMarkupString) (transform: string -> string) : AttributedMarkupString =
        let newText = transform ams.Text
        if newText.Length = ams.Text.Length then
            AttributedMarkupString(newText, ams.Runs)
        else
            AttributedMarkupString(newText, [| { Start = 0; Length = newText.Length; Markups = Array.empty } |])

    /// <summary>
    /// Removes a range of characters from the string and adjusts runs accordingly.
    /// </summary>
    let remove (ams: AttributedMarkupString) (index: int) (length: int) : AttributedMarkupString =
        if length <= 0 || index >= ams.Length then ams
        else
            let left = substring 0 index ams
            let rightStart = index + length
            let right = substring rightStart (ams.Length - rightStart) ams
            concat left right

    /// <summary>
    /// Replaces a range of characters with a new AttributedMarkupString.
    /// </summary>
    let replace (ams: AttributedMarkupString) (replacement: AttributedMarkupString) (index: int) (length: int) : AttributedMarkupString =
        if index >= ams.Length then
            concat ams replacement
        elif index < 0 then
            concat replacement ams
        else
            let left = substring 0 index ams
            let rightStart = min (index + length) ams.Length
            let right = substring rightStart (ams.Length - rightStart) ams
            concat (concat left replacement) right

    /// <summary>
    /// Repeats an AttributedMarkupString the given number of times.
    /// Uses exponential doubling for O(log n) concat operations.
    /// </summary>
    let repeat (ams: AttributedMarkupString) (count: int) : AttributedMarkupString =
        if count <= 0 then empty ()
        elif count = 1 then ams
        else
            let rec exponentialRepeat acc current remaining =
                if remaining <= 0 then acc
                elif remaining = 1 then concat acc current
                elif remaining % 2 = 0 then exponentialRepeat acc (concat current current) (remaining / 2)
                else exponentialRepeat (concat acc current) (concat current current) (remaining / 2)
            exponentialRepeat (empty ()) ams count

    /// <summary>
    /// Pads an AttributedMarkupString to a specified width.
    /// </summary>
    let pad (ams: AttributedMarkupString) (padStr: AttributedMarkupString) (width: int) (padType: MarkupStringModule.PadType) (truncType: MarkupStringModule.TruncationType) : AttributedMarkupString =
        let len = ams.Length
        let padLen = padStr.Length
        let lengthToPad = width - len
        if lengthToPad <= 0 then
            match truncType with
            | MarkupStringModule.TruncationType.Overflow -> ams
            | MarkupStringModule.TruncationType.Truncate ->
                if lengthToPad = 0 then ams
                else substring 0 width ams
        else
            let repeatCount = (lengthToPad / padLen) + 1
            let padding = repeat padStr repeatCount |> substring 0 lengthToPad
            match padType with
            | MarkupStringModule.PadType.Right ->
                let result = concat ams padding
                match truncType with
                | MarkupStringModule.TruncationType.Truncate -> substring 0 width result
                | _ -> result
            | MarkupStringModule.PadType.Left ->
                let result = concat padding ams
                match truncType with
                | MarkupStringModule.TruncationType.Truncate -> substring 0 width result
                | _ -> result
            | MarkupStringModule.PadType.Center ->
                let leftPadLength = lengthToPad / 2
                let rightPadLength = lengthToPad - leftPadLength
                let leftPad = substring 0 leftPadLength padding
                let rightPad = substring leftPadLength rightPadLength padding
                let result = concat (concat leftPad ams) rightPad
                match truncType with
                | MarkupStringModule.TruncationType.Truncate -> substring 0 width result
                | _ -> result
            | MarkupStringModule.PadType.Full ->
                ams

    // ── Conversion functions ───────────────────────────────────────

    /// <summary>
    /// Converts a tree-based MarkupString to the flat AttributedMarkupString.
    /// Walks the tree depth-first, collecting text and attribute runs.
    /// </summary>
    let fromMarkupString (ms: MarkupStringModule.MarkupString) : AttributedMarkupString =
        let textSb = StringBuilder()
        let runs = ResizeArray<AttributeRun>()

        let rec collect (ms: MarkupStringModule.MarkupString) (parentMarkups: Markup list) =
            let currentMarkups =
                match ms.MarkupDetails with
                | MarkupStringModule.MarkupTypes.MarkedupText m -> m :: parentMarkups
                | MarkupStringModule.MarkupTypes.Empty -> parentMarkups

            for content in ms.Content do
                match content with
                | MarkupStringModule.Content.Text str ->
                    if str.Length > 0 then
                        let startPos = textSb.Length
                        textSb.Append(str) |> ignore
                        runs.Add({
                            Start = startPos
                            Length = str.Length
                            Markups = currentMarkups |> List.rev |> Array.ofList
                        })
                | MarkupStringModule.Content.MarkupText childMs ->
                    collect childMs currentMarkups

        collect ms []
        let finalText = textSb.ToString()
        if finalText.Length = 0 then
            empty ()
        else
            AttributedMarkupString(finalText, runs.ToArray())

    /// <summary>
    /// Converts a flat AttributedMarkupString back to the tree-based MarkupString.
    /// Each run becomes a leaf in the tree. Runs with the same markup can be
    /// grouped under a single parent node.
    /// </summary>
    let toMarkupString (ams: AttributedMarkupString) : MarkupStringModule.MarkupString =
        if ams.Length = 0 then
            MarkupStringModule.empty ()
        elif ams.Runs.Length = 0 then
            MarkupStringModule.single ams.Text
        else
            let contentList = ResizeArray<MarkupStringModule.Content>()
            for run in ams.Runs do
                let segment = ams.Text.Substring(run.Start, run.Length)
                if run.Markups.Length = 0 then
                    contentList.Add(MarkupStringModule.Content.Text segment)
                else
                    let rec buildNested (markups: Markup list) (text: string) : MarkupStringModule.MarkupString =
                        match markups with
                        | [] -> MarkupStringModule.MarkupString(MarkupStringModule.MarkupTypes.Empty, [ MarkupStringModule.Content.Text text ])
                        | [m] -> MarkupStringModule.MarkupString(MarkupStringModule.MarkupTypes.MarkedupText m, [ MarkupStringModule.Content.Text text ])
                        | m :: rest ->
                            let inner = buildNested rest text
                            MarkupStringModule.MarkupString(MarkupStringModule.MarkupTypes.MarkedupText m, [ MarkupStringModule.Content.MarkupText inner ])
                    let nested = buildNested (Array.toList run.Markups) segment
                    contentList.Add(MarkupStringModule.Content.MarkupText nested)
            MarkupStringModule.MarkupString(MarkupStringModule.MarkupTypes.Empty, contentList |> Seq.toList)

    /// <summary>
    /// Creates an AttributedMarkupString by interspersing a delimiter between elements.
    /// </summary>
    let multipleWithDelimiter (delimiter: AttributedMarkupString) (items: AttributedMarkupString seq) : AttributedMarkupString =
        items
        |> Seq.fold (fun (acc: AttributedMarkupString option) item ->
            match acc with
            | None -> Some item
            | Some a -> Some (concat (concat a delimiter) item)
        ) None
        |> Option.defaultWith empty

    /// <summary>
    /// Inserts an AttributedMarkupString at a specified index.
    /// </summary>
    let insertAt (input: AttributedMarkupString) (insert: AttributedMarkupString) (index: int) : AttributedMarkupString =
        if index <= 0 then concat insert input
        elif index >= input.Length then concat input insert
        else
            let before = substring 0 index input
            let after = substring index (input.Length - index) input
            concat (concat before insert) after
