namespace MarkupString

open System
open System.Collections.Immutable
open System.Text
open MarkupString.MarkupImplementation
open ANSILibrary.ANSI

/// <summary>
/// NSAttributedString-inspired flat markup string model.
/// Uses a contiguous string with an immutable array of attribute runs describing formatting.
/// All types are fully immutable — text is a .NET string (inherently immutable),
/// runs use ImmutableArray (struct-based, no defensive copies needed), and
/// markup arrays within runs are also ImmutableArray.
///
/// Includes a Strategy Pattern render pipeline (section 5.2 Option A) that replaces
/// string-based format dispatch with typed render strategies, enabling extensibility
/// without modifying core types.
/// </summary>
module MarkupStringModule =

    /// <summary>
    /// Describes a contiguous range of characters that share the same markup attributes.
    /// Runs are non-overlapping and ordered by Start position.
    /// All fields are immutable — Markups uses ImmutableArray to prevent external mutation.
    /// </summary>
    [<Struct>]
    type AttributeRun =
        {
            /// Start index within the parent string (inclusive).
            Start: int
            /// Number of characters this run covers.
            Length: int
            /// The markup attributes applied to this range.
            /// Empty ImmutableArray means plain/unformatted text.
            Markups: ImmutableArray<Markup>
        }
        member this.End = this.Start + this.Length

    type TrimType =
        | TrimStart
        | TrimEnd
        | TrimBoth

    type PadType =
        | Left
        | Right
        | Center
        | Full

    type TruncationType =
        | Truncate
        | Overflow

    /// <summary>
    /// Comparer for binary search over the ImmutableArray&lt;AttributeRun&gt;.
    /// Compares runs by their Start position, enabling O(log n) lookup
    /// of the first run at or near a given character position.
    /// Used with ImmutableArray&lt;T&gt;.BinarySearch — .NET's built-in
    /// "immutable sorted array" search capability.
    /// </summary>
    let private runStartComparer =
        { new System.Collections.Generic.IComparer<AttributeRun> with
            member _.Compare(a, b) = compare a.Start b.Start }

    /// <summary>
    /// Finds the index of the first run that could overlap with the given position.
    /// Uses ImmutableArray.BinarySearch for O(log n) lookup instead of O(n) linear scan.
    /// Returns 0 if no runs exist or the position precedes all runs.
    /// </summary>
    let private findFirstOverlappingRunIndex (runs: ImmutableArray<AttributeRun>) (position: int) : int =
        if runs.Length = 0 then 0
        else
            let probe = { Start = position; Length = 0; Markups = ImmutableArray<Markup>.Empty }
            let idx = runs.BinarySearch(probe, runStartComparer)
            if idx >= 0 then
                // Exact match on Start — but a previous run might extend into this position
                max 0 (idx - 1)
            else
                // BinarySearch returns ~insertionPoint when not found (bitwise complement).
                // ~~~idx (F#'s bitwise NOT, equivalent to C#'s ~idx) recovers the insertion
                // point — the index of the first element greater than the probe.
                // The run before that point might overlap the target position.
                let insertionPoint = ~~~idx
                max 0 (insertionPoint - 1)

    // ── Render Strategy Pattern (5.2 Option A) ─────────────────────

    /// <summary>
    /// Defines how to render an MarkupString to a specific output format.
    /// Replaces string-based format dispatch ("ansi", "html") with a typed, extensible
    /// strategy pattern. New formats (BBCode, MXP, Pueblo, etc.) can be added by
    /// implementing this interface without modifying core types.
    /// </summary>
    type IRenderStrategy =
        /// <summary>
        /// Encodes leaf text content for the target format.
        /// For HTML, this would HTML-entity-encode the text.
        /// For ANSI/plain text, this is typically the identity function.
        /// </summary>
        abstract member EncodeText: string -> string

        /// <summary>
        /// Applies a single markup to already-encoded inner text.
        /// For ANSI, this wraps with escape codes. For HTML, this wraps with span tags.
        /// </summary>
        abstract member ApplyMarkup: Markup -> string -> string

        /// <summary>
        /// Returns a prefix to prepend to the overall rendered output.
        /// </summary>
        abstract member Prefix: string

        /// <summary>
        /// Returns a postfix to append to the overall rendered output.
        /// For ANSI, this is typically the SGR reset code.
        /// </summary>
        abstract member Postfix: string

        /// <summary>
        /// Post-processes the fully rendered output string.
        /// For ANSI, this eliminates redundant escape sequences.
        /// For other formats, this is typically the identity function.
        /// </summary>
        abstract member Optimize: string -> string

    /// <summary>
    /// Renders to ANSI terminal escape codes.
    /// Text is passed through unchanged; markups are applied via Markup.WrapAs("ansi", ...);
    /// output is post-processed with ANSI optimization to eliminate redundant sequences.
    /// HtmlMarkup tags (b, i, u, s) are converted to their ANSI equivalents;
    /// unknown HTML tags are stripped.
    /// </summary>
    type AnsiRenderStrategy() =
        interface IRenderStrategy with
            member _.EncodeText(text) = text
            member _.ApplyMarkup(markup)(text) = markup.WrapAs("ansi", text)
            member _.Prefix = String.Empty
            member _.Postfix = ANSILibrary.StringExtensions.endWithTrueClear(String.Empty).ToString()
            member _.Optimize(text) = ANSILibrary.Optimization.optimize text

    /// <summary>
    /// Renders to HTML with inline styles and CSS classes.
    /// Text is HTML-entity-encoded; markups are applied via Markup.WrapAs("html", ...);
    /// no post-processing optimization is needed.
    /// </summary>
    type HtmlRenderStrategy() =
        interface IRenderStrategy with
            member _.EncodeText(text) = System.Net.WebUtility.HtmlEncode(text)
            member _.ApplyMarkup(markup)(text) = markup.WrapAs("html", text)
            member _.Prefix = String.Empty
            member _.Postfix = String.Empty
            member _.Optimize(text) = text

    /// <summary>
    /// Renders to plain text with no formatting.
    /// Text is passed through unchanged; all markups are stripped.
    /// </summary>
    type PlainTextRenderStrategy() =
        interface IRenderStrategy with
            member _.EncodeText(text) = text
            member _.ApplyMarkup(_markup)(text) = text
            member _.Prefix = String.Empty
            member _.Postfix = String.Empty
            member _.Optimize(text) = text

    /// <summary>
    /// Native render strategy that delegates to each markup's own Wrap() method.
    /// This preserves the behavior of the original MarkupString.ToString() which
    /// dispatched rendering based on the concrete markup type (AnsiMarkup uses ANSI codes,
    /// HtmlMarkup uses HTML tags, NeutralMarkup passes through). Unlike AnsiRenderStrategy,
    /// this does not apply ANSI-specific post-processing or optimization.
    /// </summary>
    type NativeRenderStrategy() =
        interface IRenderStrategy with
            member _.EncodeText(text) = text
            member _.ApplyMarkup(markup)(text) = markup.Wrap(text)
            member _.Prefix = String.Empty
            member _.Postfix = String.Empty
            member _.Optimize(text) = text

    /// <summary>
    /// Renders to Pueblo-compatible HTML (HTML 3.2-era tags).
    /// Text is HTML-entity-encoded; markups are applied via Markup.WrapAs("pueblo", ...).
    /// Pueblo clients parse HTML tags like FONT COLOR, B, I, U.
    /// </summary>
    type PuebloRenderStrategy() =
        interface IRenderStrategy with
            member _.EncodeText(text) = System.Net.WebUtility.HtmlEncode(text)
            member _.ApplyMarkup(markup)(text) = markup.WrapAs("pueblo", text)
            member _.Prefix = String.Empty
            member _.Postfix = String.Empty
            member _.Optimize(text) = text

    /// <summary>
    /// Renders to BBCode format (forum-style markup).
    /// Text is passed through unchanged (BBCode is plain text);
    /// markups are applied via Markup.WrapAs("bbcode", ...) which produces [b], [i], [color=X], etc.
    /// </summary>
    type BBCodeRenderStrategy() =
        interface IRenderStrategy with
            member _.EncodeText(text) = text
            member _.ApplyMarkup(markup)(text) = markup.WrapAs("bbcode", text)
            member _.Prefix = String.Empty
            member _.Postfix = String.Empty
            member _.Optimize(text) = text

    /// <summary>
    /// Renders to MXP (MUD eXtension Protocol) tags.
    /// Text is HTML-entity-encoded (MXP uses XML-like encoding);
    /// markups are applied via Markup.WrapAs("mxp", ...) which produces MXP elements
    /// like &lt;B&gt;, &lt;I&gt;, &lt;COLOR FORE=X&gt;, &lt;SEND HREF=X&gt;.
    /// </summary>
    type MxpRenderStrategy() =
        interface IRenderStrategy with
            member _.EncodeText(text) = System.Net.WebUtility.HtmlEncode(text)
            member _.ApplyMarkup(markup)(text) = markup.WrapAs("mxp", text)
            member _.Prefix = String.Empty
            member _.Postfix = String.Empty
            member _.Optimize(text) = text

    /// <summary>
    /// Registry of built-in render strategies, providing typed access and
    /// format-string lookup for backward compatibility.
    /// </summary>
    module RenderStrategies =
        /// Singleton ANSI render strategy.
        let ansi : IRenderStrategy = AnsiRenderStrategy()

        /// Singleton HTML render strategy.
        let html : IRenderStrategy = HtmlRenderStrategy()

        /// Singleton plain text render strategy.
        let plainText : IRenderStrategy = PlainTextRenderStrategy()

        /// Singleton native render strategy (delegates to markup.Wrap()).
        let native : IRenderStrategy = NativeRenderStrategy()

        /// Singleton Pueblo render strategy.
        let pueblo : IRenderStrategy = PuebloRenderStrategy()

        /// Singleton BBCode render strategy.
        let bbcode : IRenderStrategy = BBCodeRenderStrategy()

        /// Singleton MXP render strategy.
        let mxp : IRenderStrategy = MxpRenderStrategy()

        /// <summary>
        /// Looks up a render strategy by format name (case-insensitive).
        /// Returns the ANSI strategy for unknown formats (backward-compatible default).
        /// </summary>
        let forFormat (format: string) : IRenderStrategy =
            match format.ToLowerInvariant() with
            | "html" -> html
            | "pueblo" -> pueblo
            | "bbcode" -> bbcode
            | "mxp" -> mxp
            | "plaintext" | "plain" -> plainText
            | "native" -> native
            | _ -> ansi  // "ansi" and any unknown format default to ANSI

    /// <summary>
    /// Type-safe discriminated union for selecting a render format.
    /// Provides compile-time safety over string-based format dispatch.
    /// Use with <c>Render(format: RenderFormat)</c> or <c>renderFormat</c>.
    /// The <c>Custom</c> case allows ad-hoc strategies without implementing IRenderStrategy:
    /// the first function encodes text, the second applies markup to encoded text.
    /// </summary>
    type RenderFormat =
        /// Render to ANSI terminal escape codes.
        | Ansi
        /// Render to HTML with inline styles and CSS classes.
        | Html
        /// Render to plain text with no formatting.
        | PlainText
        /// Render using Pueblo-compatible HTML (HTML 3.2-era tags).
        | Pueblo
        /// Render to BBCode format (forum-style markup).
        | BBCode
        /// Render using MXP (MUD eXtension Protocol) tags.
        | Mxp
        /// Render using each markup's native Wrap()/Prefix/Postfix/Optimize.
        | Native
        /// Custom render format: (encodeText, applyMarkup).
        | Custom of encodeText: (string -> string) * applyMarkup: (Markup -> string -> string)
    with
        /// Converts a RenderFormat to the corresponding IRenderStrategy.
        member this.ToStrategy() : IRenderStrategy =
            match this with
            | Ansi -> RenderStrategies.ansi
            | Html -> RenderStrategies.html
            | PlainText -> RenderStrategies.plainText
            | Pueblo -> RenderStrategies.pueblo
            | BBCode -> RenderStrategies.bbcode
            | Mxp -> RenderStrategies.mxp
            | Native -> RenderStrategies.native
            | Custom (encodeText, applyMarkup) ->
                { new IRenderStrategy with
                    member _.EncodeText(text) = encodeText text
                    member _.ApplyMarkup(markup)(text) = applyMarkup markup text
                    member _.Prefix = String.Empty
                    member _.Postfix = String.Empty
                    member _.Optimize(text) = text }

    /// <summary>
    /// A flat, attributed markup string inspired by NSAttributedString.
    /// Stores text contiguously with an immutable array of attribute runs.
    /// Fully immutable — text is a .NET string, runs are ImmutableArray&lt;AttributeRun&gt;,
    /// and markups within each run are ImmutableArray&lt;Markup&gt;.
    /// Safe for concurrent access without synchronization.
    /// </summary>
    type MarkupString(text: string, runs: ImmutableArray<AttributeRun>) =

        // ── Private rendering helpers ──────────────────────────────────

        /// <summary>
        /// Core rendering engine that performs single-pass rendering by iterating
        /// attribute runs and delegating text encoding, markup application, and
        /// post-processing to the provided <see cref="IRenderStrategy"/>.
        /// For each run: encodes the text segment, then applies all markup layers.
        /// After all runs are processed, applies prefix/postfix and optimization.
        /// </summary>
        let renderWith (strategy: IRenderStrategy) : string =
            if runs.Length = 0 then
                strategy.EncodeText(text)
            else
                let renderRun (run: AttributeRun) =
                    let segment = strategy.EncodeText(text.Substring(run.Start, run.Length))
                    if run.Markups.Length = 0 then
                        (false, segment)
                    else
                        let wrapped = run.Markups |> Seq.fold (fun acc markup -> strategy.ApplyMarkup markup acc) segment
                        (true, wrapped)

                let (hasAnyMarkup, rendered) =
                    let sb = StringBuilder(text.Length * 2)
                    let hasMarkup =
                        runs |> Seq.fold (fun foundMarkup run ->
                            let (hadMarkup, rendered) = renderRun run
                            sb.Append(rendered) |> ignore
                            foundMarkup || hadMarkup
                        ) false
                    (hasMarkup, sb.ToString())

                if hasAnyMarkup then
                    let prefix = strategy.Prefix
                    let postfix = strategy.Postfix
                    let withFixing =
                        if prefix.Length > 0 || postfix.Length > 0 then
                            prefix + rendered + postfix
                        else
                            rendered
                    strategy.Optimize(withFixing)
                else
                    rendered

        /// Finds the first markup in any run, used to select the appropriate rendering strategy.
        let findFirstMarkup () : Markup option =
            runs |> Seq.tryPick (fun run ->
                if run.Markups.Length > 0 then Some (run.Markups.[0])
                else None)

        /// Creates a render strategy based on the first markup type found.
        /// This matches the original MarkupString.toString() behavior:
        /// - Uses markup.Wrap() for applying markup (type-dispatched)
        /// - Uses markup.Prefix/Postfix for wrapping
        /// - Uses markup.Optimize for post-processing
        let nativeToString () : string =
            match findFirstMarkup () with
            | None -> renderWith RenderStrategies.native
            | Some markup ->
                let strategy = {
                    new IRenderStrategy with
                        member _.EncodeText(t) = t
                        member _.ApplyMarkup(m)(t) = m.Wrap(t)
                        member _.Prefix = markup.Prefix
                        member _.Postfix = markup.Postfix
                        member _.Optimize(t) = markup.Optimize(t)
                }
                renderWith strategy

        let cachedToString = Lazy<string>(fun () -> nativeToString ())
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
        /// Uses the RenderStrategies registry for format lookup.
        member _.Render(format: string) : string =
            renderWith (RenderStrategies.forFormat format)

        /// <summary>
        /// Renders to the specified format using the type-safe RenderFormat union.
        /// Provides compile-time safety over string-based format dispatch.
        /// The Custom case allows ad-hoc strategies inline.
        /// </summary>
        member _.Render(format: RenderFormat) : string =
            renderWith (format.ToStrategy())

        /// <summary>
        /// Renders using a specific render strategy.
        /// This is the primary rendering method — type-safe, extensible, no string dispatch.
        /// Use this to render with custom strategies (BBCode, MXP, Pueblo, etc.).
        /// </summary>
        member _.RenderWith(strategy: IRenderStrategy) : string =
            renderWith strategy

        /// <summary>
        /// Evaluates the attributed string using a custom evaluation function.
        /// Groups consecutive characters by their markup and calls the evaluator per group.
        /// For runs with multiple markups, evaluates from innermost to outermost,
        /// matching the tree-based MarkupString's recursive evaluation behavior.
        /// </summary>
        member _.EvaluateWith(evaluator: Func<Markup option, string, string>) : string =
            let sb = StringBuilder(text.Length)
            for run in runs do
                let segment = text.Substring(run.Start, run.Length)
                if run.Markups.Length = 0 then
                    sb.Append(evaluator.Invoke(None, segment)) |> ignore
                else
                    // Apply evaluator from innermost (index 0) to outermost (last index)
                    // to match tree-based recursive behavior
                    let result =
                        run.Markups
                        |> Seq.fold (fun acc markup -> evaluator.Invoke(Some markup, acc)) segment
                    sb.Append(result) |> ignore
            sb.ToString()

        override _.Equals(obj) =
            match obj with
            | :? MarkupString as other -> text.Equals(other.Text)
            | :? string as other -> text.Equals(other)
            | _ -> false

        override _.GetHashCode() = text.GetHashCode()

    // ── Construction functions ──────────────────────────────────────

    /// Creates an MarkupString from a plain string with no markup.
    let single (str: string) : MarkupString =
        if str.Length = 0 then
            MarkupString(String.Empty, ImmutableArray<AttributeRun>.Empty)
        else
            MarkupString(str, ImmutableArray.Create({ Start = 0; Length = str.Length; Markups = ImmutableArray<Markup>.Empty }))

    /// Returns an empty MarkupString.
    let empty () : MarkupString =
        MarkupString(String.Empty, ImmutableArray<AttributeRun>.Empty)

    /// Creates an MarkupString from a markup and a plain string.
    let markupSingle (markup: Markup, str: string) : MarkupString =
        let run = { Start = 0; Length = str.Length; Markups = ImmutableArray.Create(markup) }
        MarkupString(str, ImmutableArray.Create(run))

    /// Creates an MarkupString from multiple markups applied to a string.
    let markupSingleMulti (markups: ImmutableArray<Markup>, str: string) : MarkupString =
        let run = { Start = 0; Length = str.Length; Markups = markups }
        MarkupString(str, ImmutableArray.Create(run))

    // ── Core operations ────────────────────────────────────────────

    /// Returns the plain text of an MarkupString.
    let plainText (ams: MarkupString) : string = ams.ToPlainText()

    /// Returns the plain text length.
    let getLength (ams: MarkupString) : int = ams.Length

    /// <summary>
    /// Concatenates two MarkupStrings.
    /// Runs from the second string are shifted by the length of the first.
    /// </summary>
    let concat (a: MarkupString) (b: MarkupString) : MarkupString =
        if a.Length = 0 then b
        elif b.Length = 0 then a
        else
            let combinedText = a.Text + b.Text
            let offset = a.Text.Length
            let builder = ImmutableArray.CreateBuilder<AttributeRun>(a.Runs.Length + b.Runs.Length)
            for run in a.Runs do
                builder.Add(run)
            for run in b.Runs do
                builder.Add({ Start = run.Start + offset; Length = run.Length; Markups = run.Markups })
            MarkupString(combinedText, builder.ToImmutable())

    /// <summary>
    /// Concatenates any number of MarkupStrings in a single pass — O(n) total
    /// where n is the sum of all text lengths and run counts.
    /// Avoids the O(n²) intermediate allocations from chained binary concat calls
    /// like <c>concat(concat(a,b),c)</c>.
    /// </summary>
    let concatMany (items: MarkupString seq) : MarkupString =
        let textSb = StringBuilder()
        let runsBuilder = ImmutableArray.CreateBuilder<AttributeRun>()
        for item in items do
            if item.Length > 0 then
                let offset = textSb.Length
                textSb.Append(item.Text) |> ignore
                for run in item.Runs do
                    runsBuilder.Add({ Start = run.Start + offset; Length = run.Length; Markups = run.Markups })
        if textSb.Length = 0 then empty ()
        else MarkupString(textSb.ToString(), runsBuilder.ToImmutable())

    /// <summary>
    /// Returns a substring of an MarkupString, preserving markup runs.
    /// Runs that partially overlap the range are clipped to the range boundaries.
    /// Uses binary search to find the first overlapping run in O(log n),
    /// then scans forward only through overlapping runs — O(log n + k) total
    /// where k is the number of runs in the range (vs O(n) linear scan).
    /// </summary>
    let substring (start: int) (length: int) (ams: MarkupString) : MarkupString =
        if length <= 0 || start >= ams.Length then
            empty ()
        else
            let actualStart = max 0 start
            let actualEnd = min ams.Length (actualStart + length)
            let actualLength = actualEnd - actualStart
            let subText = ams.Text.Substring(actualStart, actualLength)
            let startIdx = findFirstOverlappingRunIndex ams.Runs actualStart

            let rec collectRuns i acc =
                if i >= ams.Runs.Length then List.rev acc
                else
                    let run = ams.Runs[i]
                    if run.Start >= actualEnd then List.rev acc
                    else
                        let runEnd = run.End
                        if runEnd > actualStart then
                            let clippedStart = max run.Start actualStart
                            let clippedEnd = min runEnd actualEnd
                            let clippedLength = clippedEnd - clippedStart
                            if clippedLength > 0 then
                                let clipped = {
                                    Start = clippedStart - actualStart
                                    Length = clippedLength
                                    Markups = run.Markups
                                }
                                collectRuns (i + 1) (clipped :: acc)
                            else
                                collectRuns (i + 1) acc
                        else
                            collectRuns (i + 1) acc

            let clippedRuns = collectRuns startIdx []
            MarkupString(subText, ImmutableArray.CreateRange(clippedRuns))

    /// <summary>
    /// Splits an MarkupString by a string delimiter.
    /// </summary>
    let split (delimiter: string) (ams: MarkupString) : MarkupString array =
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
    /// Trims characters from the start, end, or both of an MarkupString.
    /// </summary>
    let trim (ams: MarkupString) (trimChars: string) (trimType: TrimType) : MarkupString =
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
            | TrimType.TrimStart ->
                let leftTrim = countLeft 0
                if leftTrim = 0 then ams
                else substring leftTrim (len - leftTrim) ams
            | TrimType.TrimEnd ->
                let rightBoundary = countRight (len - 1)
                if rightBoundary = len then ams
                else substring 0 rightBoundary ams
            | TrimType.TrimBoth ->
                let leftTrim = countLeft 0
                let rightBoundary = countRight (len - 1)
                if leftTrim = 0 && rightBoundary = len then ams
                else substring leftTrim (rightBoundary - leftTrim) ams

    /// <summary>
    /// Optimizes an MarkupString by merging adjacent runs with identical markups.
    /// </summary>
    let optimize (ams: MarkupString) : MarkupString =
        if ams.Runs.Length <= 1 then ams
        else
            let markupsEqual (a: ImmutableArray<Markup>) (b: ImmutableArray<Markup>) =
                a.Length = b.Length && Seq.forall2 (=) a b

            let rec mergeRuns i (current: AttributeRun) acc =
                if i >= ams.Runs.Length then
                    List.rev (current :: acc)
                else
                    let next = ams.Runs[i]
                    if current.End = next.Start && markupsEqual current.Markups next.Markups then
                        mergeRuns (i + 1) { Start = current.Start; Length = current.Length + next.Length; Markups = current.Markups } acc
                    else
                        mergeRuns (i + 1) next (current :: acc)

            let merged = mergeRuns 1 ams.Runs[0] []
            MarkupString(ams.Text, ImmutableArray.CreateRange(merged))

    /// <summary>
    /// Returns the first index where a search string occurs.
    /// Returns -1 if not found.
    /// </summary>
    let indexOf (ams: MarkupString) (search: string) : int =
        ams.Text.IndexOf(search, StringComparison.Ordinal)

    /// <summary>
    /// Returns the last index where a search string occurs.
    /// Returns -1 if not found.
    /// </summary>
    let indexOfLast (ams: MarkupString) (search: string) : int =
        ams.Text.LastIndexOf(search, StringComparison.Ordinal)

    /// <summary>
    /// Applies a text transformation function to all text content, preserving runs.
    /// For transforms that preserve text length (e.g., ToUpper), attribute runs are
    /// preserved exactly. For length-changing transforms, the result is treated as
    /// plain text with no markup (attribute information is lost).
    /// </summary>
    let apply (ams: MarkupString) (transform: string -> string) : MarkupString =
        let newText = transform ams.Text
        if newText.Length = ams.Text.Length then
            MarkupString(newText, ams.Runs)
        else
            MarkupString(newText, ImmutableArray.Create({ Start = 0; Length = newText.Length; Markups = ImmutableArray<Markup>.Empty }))

    /// <summary>
    /// Removes a range of characters from the string and adjusts runs accordingly.
    /// </summary>
    let remove (ams: MarkupString) (index: int) (length: int) : MarkupString =
        if length <= 0 || index >= ams.Length then ams
        else
            let left = substring 0 index ams
            let rightStart = index + length
            let right = substring rightStart (ams.Length - rightStart) ams
            concat left right

    /// <summary>
    /// Replaces a range of characters with a new MarkupString.
    /// </summary>
    let replace (ams: MarkupString) (replacement: MarkupString) (index: int) (length: int) : MarkupString =
        if index >= ams.Length then
            concat ams replacement
        elif index < 0 then
            concat replacement ams
        else
            let left = substring 0 index ams
            let rightStart = min (index + length) ams.Length
            let right = substring rightStart (ams.Length - rightStart) ams
            concatMany [left; replacement; right]

    /// <summary>
    /// Repeats an MarkupString the given number of times.
    /// Uses exponential doubling for O(log n) concat operations.
    /// </summary>
    let repeat (ams: MarkupString) (count: int) : MarkupString =
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
    /// Creates padding of exact length by repeating padStr, avoiding intermediate allocations.
    /// Constructs the result directly in a single pass instead of repeat+substring.
    /// </summary>
    let private buildPadding (padStr: MarkupString) (exactLength: int) : MarkupString =
        if exactLength <= 0 then empty ()
        elif padStr.Length = 0 then empty ()
        else
            let textSb = StringBuilder(exactLength)
            let runsBuilder = ImmutableArray.CreateBuilder<AttributeRun>()
            let mutable remaining = exactLength
            while remaining > 0 do
                let offset = textSb.Length
                let charsToTake = min remaining padStr.Text.Length
                if charsToTake = padStr.Text.Length then
                    textSb.Append(padStr.Text) |> ignore
                    for run in padStr.Runs do
                        runsBuilder.Add({ Start = run.Start + offset; Length = run.Length; Markups = run.Markups })
                else
                    textSb.Append(padStr.Text.Substring(0, charsToTake)) |> ignore
                    for run in padStr.Runs do
                        if run.Start < charsToTake then
                            let clippedLength = min run.Length (charsToTake - run.Start)
                            if clippedLength > 0 then
                                runsBuilder.Add({ Start = run.Start + offset; Length = clippedLength; Markups = run.Markups })
                remaining <- remaining - charsToTake
            MarkupString(textSb.ToString(), runsBuilder.ToImmutable())

    /// <summary>
    /// Pads an MarkupString to a specified width.
    /// Constructs padding directly to exact length — no intermediate repeat+truncate.
    /// </summary>
    let pad (ams: MarkupString) (padStr: MarkupString) (width: int) (padType: PadType) (truncType: TruncationType) : MarkupString =
        let len = ams.Length
        let padLen = padStr.Length
        let lengthToPad = width - len
        if lengthToPad <= 0 then
            match truncType with
            | TruncationType.Overflow -> ams
            | TruncationType.Truncate ->
                if lengthToPad = 0 then ams
                else substring 0 width ams
        else
            match padType with
            | PadType.Right ->
                let padding = buildPadding padStr lengthToPad
                let result = concat ams padding
                match truncType with
                | TruncationType.Truncate -> substring 0 width result
                | _ -> result
            | PadType.Left ->
                let padding = buildPadding padStr lengthToPad
                let result = concat padding ams
                match truncType with
                | TruncationType.Truncate -> substring 0 width result
                | _ -> result
            | PadType.Center ->
                let leftPadLength = lengthToPad / 2
                let rightPadLength = lengthToPad - leftPadLength
                let leftPad = buildPadding padStr leftPadLength
                let rightPad = buildPadding padStr rightPadLength
                let result = concatMany [leftPad; ams; rightPad]
                match truncType with
                | TruncationType.Truncate -> substring 0 width result
                | _ -> result
            | PadType.Full ->
                match truncType with
                | TruncationType.Truncate when ams.Length > width -> substring 0 width ams
                | TruncationType.Overflow when ams.Length > width -> ams
                | _ ->
                    let wordArr = split " " ams
                    let fences = Math.Max(wordArr.Length - 1, 0)
                    if fences = 0 then ams
                    else
                        let totalSpaces = fences + lengthToPad
                        let space = single " "
                        let minimumFenceWidth = totalSpaces / fences
                        let thickerFences = totalSpaces % fences
                        let fenceStr = repeat space minimumFenceWidth
                        let thickFenceStr = repeat space (minimumFenceWidth + 1)
                        let delFunc = (fun i -> if i <= thickerFences then thickFenceStr else fenceStr)
                        // Inline intersperse + concatMany to avoid forward reference
                        let parts = seq {
                            for i, word in wordArr |> Seq.indexed do
                                if i > 0 then
                                    yield delFunc i
                                yield word
                        }
                        concatMany parts

    /// <summary>
    /// Creates an MarkupString by interspersing a delimiter between elements.
    /// Uses single-pass builder — O(n) instead of O(n²) from left-fold concat.
    /// </summary>
    let multipleWithDelimiter (delimiter: MarkupString) (items: MarkupString seq) : MarkupString =
        let textSb = StringBuilder()
        let runsBuilder = ImmutableArray.CreateBuilder<AttributeRun>()
        let mutable first = true
        for item in items do
            if not first then
                let offset = textSb.Length
                textSb.Append(delimiter.Text) |> ignore
                for run in delimiter.Runs do
                    runsBuilder.Add({ Start = run.Start + offset; Length = run.Length; Markups = run.Markups })
            first <- false
            let offset = textSb.Length
            textSb.Append(item.Text) |> ignore
            for run in item.Runs do
                runsBuilder.Add({ Start = run.Start + offset; Length = run.Length; Markups = run.Markups })
        if first then empty ()
        else MarkupString(textSb.ToString(), runsBuilder.ToImmutable())

    /// <summary>
    /// Inserts an MarkupString at a specified index.
    /// </summary>
    let insertAt (input: MarkupString) (insert: MarkupString) (index: int) : MarkupString =
        if index <= 0 then concat insert input
        elif index >= input.Length then concat input insert
        else
            let before = substring 0 index input
            let after = substring index (input.Length - index) input
            // Find the run at the insertion point to inherit its markups
            let runIndex = findFirstOverlappingRunIndex input.Runs index
            let wrappedInsert =
                if runIndex >= 0 && runIndex < input.Runs.Length then
                    let run = input.Runs.[runIndex]
                    if run.Markups.Length > 0 then
                        // Wrap insert with the surrounding markup context
                        let insertRuns = ImmutableArray.CreateBuilder<AttributeRun>(insert.Runs.Length)
                        for r in insert.Runs do
                            let newMarkups = ImmutableArray.CreateBuilder<Markup>(r.Markups.Length + run.Markups.Length)
                            newMarkups.AddRange(r.Markups)
                            newMarkups.AddRange(run.Markups)
                            insertRuns.Add({ Start = r.Start; Length = r.Length; Markups = newMarkups.ToImmutable() })
                        MarkupString(insert.Text, insertRuns.ToImmutable())
                    else insert
                else insert
            concatMany [before; wrappedInsert; after]

    // ── Additional functions for API compatibility ──────────────────

    /// Creates an MarkupString from a sequence of MarkupStrings.
    let multiple (items: seq<MarkupString>) : MarkupString =
        concatMany items

    /// Creates an MarkupString wrapping another with the given markup.
    let markupSingle2 (markup: Markup, inner: MarkupString) : MarkupString =
        if inner.Length = 0 then
            // Even for empty text, preserve the markup (e.g., <br></br>)
            let run = { Start = 0; Length = 0; Markups = ImmutableArray.Create(markup) }
            MarkupString("", ImmutableArray.Create(run))
        else
            let runsBuilder = ImmutableArray.CreateBuilder<AttributeRun>(inner.Runs.Length)
            for run in inner.Runs do
                let newMarkups = ImmutableArray.CreateBuilder<Markup>(run.Markups.Length + 1)
                newMarkups.AddRange(run.Markups)
                newMarkups.Add(markup)
                runsBuilder.Add({ Start = run.Start; Length = run.Length; Markups = newMarkups.ToImmutable() })
            MarkupString(inner.Text, runsBuilder.ToImmutable())

    /// Creates an MarkupString from a markup and a sequence of MarkupStrings.
    let markupMultiple (markup: Markup, items: seq<MarkupString>) : MarkupString =
        markupSingle2 (markup, concatMany items)

    /// Returns an MarkupString containing only the plain text (no markup).
    let plainText2 (ams: MarkupString) : MarkupString =
        single (ams.ToPlainText())

    /// Returns the first index where a search MarkupString occurs.
    /// Returns -1 if not found.
    let indexOf2 (ams: MarkupString) (search: MarkupString) : int =
        ams.Text.IndexOf(search.Text, StringComparison.Ordinal)

    /// Returns all indexes where a search string occurs.
    let indexesOf (ams: MarkupString) (search: MarkupString) : seq<int> =
        let text = ams.Text
        let srch = search.Text

        let rec findDelimiters pos acc =
            if pos < text.Length then
                match text.IndexOf(srch, pos, StringComparison.Ordinal) with
                | -1 -> Seq.rev acc
                | foundPos ->
                    let newAcc = foundPos :: acc
                    if srch <> String.Empty then
                        findDelimiters (foundPos + srch.Length) newAcc
                    else
                        findDelimiters (foundPos + 1) newAcc
            else
                Seq.rev acc

        findDelimiters 0 []

    /// Returns the last index where a search MarkupString occurs.
    /// Returns -1 if not found.
    let indexOfLast2 (ams: MarkupString) (search: MarkupString) : int =
        ams.Text.LastIndexOf(search.Text, StringComparison.Ordinal)

    /// Splits by an MarkupString delimiter (uses plain text of delimiter).
    let split2 (delimiter: MarkupString) (ams: MarkupString) : MarkupString array =
        split (delimiter.ToPlainText()) ams

    /// Applies a transformation function, operating on each segment individually.
    let apply2 (ams: MarkupString) (transform: MarkupString -> MarkupString) : MarkupString =
        let segments =
            ams.Runs |> Seq.map (fun run ->
                let segment = substring run.Start run.Length ams
                transform segment)
        concatMany segments

    /// Trims an MarkupString using another MarkupString as trim characters.
    let trim2 (ams: MarkupString) (trimStr: MarkupString) (trimType: TrimType) : MarkupString =
        trim ams (trimStr.ToPlainText()) trimType

    /// Concatenates and attaches: the last markup context of `a` extends to cover `b`.
    /// In the tree model, this recursively nests `b` inside `a`'s last MarkupText node.
    /// In the flat model, this finds the outermost markup from `a`'s last run and
    /// applies it as a wrapper to all runs of `b`, then concatenates.
    let concatAttach (a: MarkupString) (b: MarkupString) : MarkupString =
        if a.Runs.Length = 0 then concat a b
        else
            let lastRun = a.Runs.[a.Runs.Length - 1]
            if lastRun.Markups.Length = 0 then
                // Last run of 'a' has no markup — just concat
                concat a b
            else
                // Find the outermost markup (last in the markups array = outermost from markupSingle2)
                let outerMarkup = lastRun.Markups.[lastRun.Markups.Length - 1]
                // Wrap b with that outer markup, then concat
                let wrappedB = markupSingle2(outerMarkup, b)
                concat a wrappedB

    /// Intersperses a function-generated separator between elements of a sequence.
    let intersperseFunc (sepFunc: int -> MarkupString) (items: MarkupString seq) : MarkupString seq =
        seq {
            for i, element in items |> Seq.indexed do
                if i > 0 then
                    yield sepFunc i
                yield element
        }

    /// Creates by interspersing a function-generated delimiter between elements.
    let multipleWithDelimiterFunc (delimiterFunc: int -> MarkupString) (items: MarkupString seq) : MarkupString =
        items |> intersperseFunc delimiterFunc |> concatMany

    /// Renders an MarkupString to the specified output format.
    let render (format: string) (ams: MarkupString) : string =
        ams.Render(format)

    /// Renders an MarkupString using the type-safe RenderFormat union.
    let renderFormat (format: RenderFormat) (ams: MarkupString) : string =
        ams.Render(format)

    /// Evaluates an MarkupString using a custom evaluator function.
    let evaluateWith (evaluator: System.Func<Markup option, string, string>) (ams: MarkupString) : string =
        ams.EvaluateWith(evaluator)

    /// The fixed CSS rules for formatting classes emitted by Render("html").
    let fixedCss =
        ".ms-bold { font-weight: bold; }\n" +
        ".ms-faint { opacity: 0.5; }\n" +
        ".ms-italic { font-style: italic; }\n" +
        ".ms-underline { text-decoration: underline; }\n" +
        ".ms-strike { text-decoration: line-through; }\n" +
        ".ms-overline { text-decoration: overline; }\n" +
        ".ms-blink { animation: blink 1s step-start infinite; }\n"

    /// Generates a CSS stylesheet (returns fixed CSS rules).
    let cssSheet (_ams: MarkupString) : string =
        fixedCss

    /// Centers an MarkupString with different left/right padding.
    let center2
        (ams: MarkupString)
        (padStr: MarkupString)
        (padStrRight: MarkupString)
        (width: int)
        (truncType: TruncationType)
        : MarkupString =
        let len = ams.Length
        let lengthToPad = width - len

        if lengthToPad <= 0 then
            match truncType with
            | TruncationType.Overflow -> ams
            | TruncationType.Truncate ->
                if lengthToPad = 0 then ams
                else substring 0 width ams
        else
            let leftPadLength = lengthToPad / 2
            let rightPadLength = lengthToPad - leftPadLength
            let leftPad = buildPadding padStr leftPadLength
            let rightPad = buildPadding padStrRight rightPadLength
            let result = concatMany [leftPad; ams; rightPad]
            match truncType with
            | TruncationType.Truncate -> substring 0 width result
            | _ -> result

    // ── Wildcard/regex functions ────────────────────────────────────

    open System.Text.RegularExpressions

    /// Constant function: always returns the given value regardless of input.
    let private konst value _ = value

    type private GlobPatternRegex = FSharp.Text.RegexProvider.Regex< @"(?<!\\)\\\*" >
    type private QuestionPatternRegex = FSharp.Text.RegexProvider.Regex< @"(?<!\\)\\\?" >
    type private KindPatternRegex = FSharp.Text.RegexProvider.Regex< @"\\\\\\\*" >
    type private KindPattern2Regex = FSharp.Text.RegexProvider.Regex< @"\\\\\\\?" >

    let private globPatternRegexInstance = GlobPatternRegex()
    let private questionPatternRegexInstance = QuestionPatternRegex()
    let private kindPatternRegexInstance = KindPatternRegex()
    let private kindPattern2RegexInstance = KindPattern2Regex()

    /// Converts a wildcard pattern to a regex string.
    let getWildcardMatchAsRegex (pattern: MarkupString) : string =
        let applyRegexPattern (pat: string) =
            pat
            |> fun x -> globPatternRegexInstance.TypedReplace(x, konst @"(.*?)")
            |> fun x -> questionPatternRegexInstance.TypedReplace(x, konst @"(.)")
            |> fun x -> kindPatternRegexInstance.TypedReplace(x, konst @"\*")
            |> fun x -> kindPattern2RegexInstance.TypedReplace(x, konst @"\?")

        pattern |> plainText |> Regex.Escape |> (fun x -> $"^{x}$") |> applyRegexPattern

    /// Converts a wildcard pattern string to a regex string.
    let getWildcardMatchAsRegex2 (pattern: string) : string =
        let applyRegexPattern (pat: string) =
            pat
            |> fun x -> globPatternRegexInstance.TypedReplace(x, konst @"(.*?)")
            |> fun x -> questionPatternRegexInstance.TypedReplace(x, konst @"(.)")
            |> fun x -> kindPatternRegexInstance.TypedReplace(x, konst @"\*")
            |> fun x -> kindPattern2RegexInstance.TypedReplace(x, konst @"\?")

        pattern |> Regex.Escape |> (fun x -> $"^{x}$") |> applyRegexPattern

    /// Determines if the input matches the wildcard pattern.
    let isWildcardMatch (input: MarkupString) (pattern: MarkupString) : bool =
        let newPattern = getWildcardMatchAsRegex pattern
        (plainText input, newPattern) |> Regex.IsMatch

    /// Determines if the input matches the wildcard pattern string.
    let isWildcardMatch2 (input: MarkupString) (pattern: string) : bool =
        let newPattern = getWildcardMatchAsRegex2 pattern
        (plainText input, newPattern) |> Regex.IsMatch

    /// Gets regex matches from input and pattern.
    let getMatches (input: MarkupString) (pattern: string) : (Match * MarkupString seq) seq =
        let captureToString (captureGroup: Group) =
            substring captureGroup.Index captureGroup.Length input

        let allMatches (mtch: Match) =
            (mtch, mtch.Groups |> Seq.map captureToString)

        ((plainText input), pattern)
        |> Regex.Matches
        |> Seq.cast<Match>
        |> Seq.map allMatches

    /// Gets regex matches from input and pattern MarkupStrings.
    let getRegexpMatches (input: MarkupString) (pattern: MarkupString) : (Match * MarkupString seq) seq =
        getMatches input (plainText pattern)

    /// Gets wildcard matches from input and pattern.
    let getWildcardMatches (input: MarkupString) (pattern: MarkupString) : (Match * MarkupString seq) seq =
        getMatches input (getWildcardMatchAsRegex pattern)

    // ── Serialization ──────────────────────────────────────────────

    open System.Text.Json
    open System.Text.Json.Serialization
    open System.Drawing

    /// JSON converter for System.Drawing.Color, serializing to/from hex color strings.
    type ColorJsonConverter() =
        inherit JsonConverter<Color>()

        override _.Read(reader, _typeToConvert, _options) =
            ColorTranslator.FromHtml(reader.GetString())

        override _.Write(writer, value, _) =
            writer.WriteStringValue($"#{value.R:X2}{value.G:X2}{value.B:X2}".ToLower())

    /// Serialization options for MarkupString.
    let serializationOptions =
        let serializeOption = JsonFSharpOptions.Default().ToJsonSerializerOptions()
        serializeOption.Converters.Add(ColorJsonConverter())
        serializeOption

    /// Serializes to JSON string.
    let serialize (ams: MarkupString) : string =
        JsonSerializer.Serialize(ams, serializationOptions)

    /// Deserializes from JSON string.
    let deserialize (jsonString: string) : MarkupString =
        if jsonString.Length = 0 then
            empty ()
        else
            JsonSerializer.Deserialize<MarkupString>(jsonString, serializationOptions)
