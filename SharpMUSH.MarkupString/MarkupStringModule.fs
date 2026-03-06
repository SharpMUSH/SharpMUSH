namespace MarkupString

open System.Text.Json
open System.Text.RegularExpressions
open System.Runtime.InteropServices
open System
open MarkupString.MarkupImplementation
open System.Text.Json.Serialization
open FSharpPlus
open System.Drawing

/// <summary>
/// Provides core types and functions for working with markup-aware strings.
/// </summary>
module MarkupStringModule =
    /// <summary>
    /// Thread-safe StringBuilder pool to reduce allocations in hot paths.
    /// Uses ThreadLocal to maintain per-thread pools and avoid cross-thread contention.
    /// </summary>
    module StringBuilderPool =
        open System.Collections.Generic
        open System.Threading

        let private maxPoolSize = 256

        // Thread-local storage — each thread owns its stack, no lock needed
        let private threadLocalPool = new ThreadLocal<Stack<System.Text.StringBuilder>>(fun () -> 
            new Stack<System.Text.StringBuilder>())

        /// Get a StringBuilder from the pool, or create a new one if pool is empty
        let getStringBuilder() : System.Text.StringBuilder =
            let stack = threadLocalPool.Value
            if stack.Count > 0 then stack.Pop()
            else new System.Text.StringBuilder()

        /// Return a StringBuilder to the pool after clearing it
        let returnStringBuilder(sb: System.Text.StringBuilder) : unit =
            sb.Clear() |> ignore
            let stack = threadLocalPool.Value
            if stack.Count < maxPoolSize then
                stack.Push(sb)

    type Content =
        | Text of string
        | MarkupText of MarkupString

    and TrimType =
        | TrimStart
        | TrimEnd
        | TrimBoth

    and PadType =
        | Left
        | Right
        | Center
        | Full

    and TruncationType =
        | Truncate
        | Overflow

    and ColorJsonConverter() =
        inherit JsonConverter<Color>()

        override _.Read(reader, _typeToConvert, _options) =
            ColorTranslator.FromHtml(reader.GetString())

        override _.Write(writer, value, _) =
            writer.WriteStringValue($"#{value.R:X2}{value.G:X2}{value.B:X2}".ToLower())

    and MarkupString(markupDetails: Markup option, content: Content list) as ms =
        // TODO: Optimize the ansi strings, so we don't re-initialize at least the exact same tag sequentially.
        let rec getText (markupStr: MarkupString, outerMarkupType: Markup option) : string =
            let rec accumulate (sb: System.Text.StringBuilder) (items: Content list) =
                match items with
                | [] -> ()
                | Text str :: tail -> 
                    sb.Append(str) |> ignore
                    accumulate sb tail
                | MarkupText mStr :: tail ->
                    let inner =
                        match markupStr.MarkupDetails with
                        | None -> getText (mStr, outerMarkupType)
                        | Some _ -> getText (mStr, markupStr.MarkupDetails)
                    sb.Append(inner) |> ignore
                    accumulate sb tail

            let sb = StringBuilderPool.getStringBuilder()
            try
                accumulate sb markupStr.Content
                let innerText = sb.ToString()

                match markupStr.MarkupDetails with
                | None -> innerText
                | Some str ->
                    match outerMarkupType with
                    | None -> str.Wrap(innerText)
                    | Some outerMarkup -> str.WrapAndRestore(innerText, outerMarkup)
            finally
                StringBuilderPool.returnStringBuilder sb

        let rec getTextAs (format: string) (markupStr: MarkupString, outerMarkupType: Markup option) : string =
            let encodeText =
                if format = "html" then System.Net.WebUtility.HtmlEncode
                else id
            let rec accumulate (sb: System.Text.StringBuilder) (items: Content list) =
                match items with
                | [] -> ()
                | Text str :: tail ->
                    sb.Append(encodeText str) |> ignore
                    accumulate sb tail
                | MarkupText mStr :: tail ->
                    let inner =
                        match markupStr.MarkupDetails with
                        | None -> getTextAs format (mStr, outerMarkupType)
                        | Some _ -> getTextAs format (mStr, markupStr.MarkupDetails)
                    sb.Append(inner) |> ignore
                    accumulate sb tail

            let sb = StringBuilderPool.getStringBuilder()
            try
                accumulate sb markupStr.Content
                let innerText = sb.ToString()

                match markupStr.MarkupDetails with
                | None -> innerText
                | Some str ->
                    match outerMarkupType with
                    | None -> str.WrapAs(format, innerText)
                    | Some outerMarkup -> str.WrapAndRestoreAs(format, innerText, outerMarkup)
            finally
                StringBuilderPool.returnStringBuilder sb

        [<TailCall>]
        let rec length () : int =
            let rec getLengthInternal (internalContent: Content list) : int =
                internalContent
                |> List.fold
                    (fun acc item ->
                        acc
                        + (match item with
                           | Text str -> str.Length
                           | MarkupText mStr -> getLengthInternal mStr.Content))
                    0

            getLengthInternal content

        // BUG: This is not correctly matching the first MarkedUp Text
        [<TailCall>]
        let findFirstMarkedUpText (markupStr: MarkupString) : Markup option =
            let rec find (content: Content list) : Markup option =
                match content with
                | [] -> None
                | MarkupText mStr :: _ when Option.isSome mStr.MarkupDetails -> mStr.MarkupDetails
                | MarkupText a :: tail ->
                    match (find a.Content, find tail) with
                    | Some res, _ -> Some res
                    | _, Some res -> Some res
                    | _ -> None
                | _ -> None

            match markupStr.MarkupDetails with
            | Some _ -> markupStr.MarkupDetails
            | _ -> find markupStr.Content

        let len: Lazy<int> = Lazy<int>(length)

        /// <summary>
        /// Evaluates the MarkupString using a custom evaluation function that receives markup information.
        /// This allows reconstructing original function calls like ansi() from the markup data.
        /// </summary>
        /// <param name="evaluator">Function that takes (markupType, innerText) and returns reconstructed string</param>
        [<TailCall>]
        let rec evaluateWith (evaluator: Markup option -> string -> string) : string =
            let rec evalContent (sb: System.Text.StringBuilder) (content: Content list) : unit =
                match content with
                | [] -> ()
                | Text str :: tail ->
                    sb.Append(str) |> ignore
                    evalContent sb tail
                | MarkupText mStr :: tail ->
                    let innerText = evalContent2 mStr
                    sb.Append(innerText) |> ignore
                    evalContent sb tail

            and evalContent2 (markupStr: MarkupString) : string =
                let innerSb = StringBuilderPool.getStringBuilder()
                try
                    let rec innerLoop (sb: System.Text.StringBuilder) (content: Content list) : unit =
                        match content with
                        | [] -> ()
                        | Text str :: tail ->
                            sb.Append(str) |> ignore
                            innerLoop sb tail
                        | MarkupText mStr :: tail ->
                            let text = evalContent2 mStr
                            sb.Append(text) |> ignore
                            innerLoop sb tail
                    innerLoop innerSb markupStr.Content
                    let innerText = innerSb.ToString()
                    evaluator markupStr.MarkupDetails innerText
                finally
                    StringBuilderPool.returnStringBuilder innerSb

            // Evaluate content first, then apply evaluator to top-level markup
            let sb = StringBuilderPool.getStringBuilder()
            try
                evalContent sb ms.Content
                let contentText = sb.ToString()
                evaluator ms.MarkupDetails contentText
            finally
                StringBuilderPool.returnStringBuilder sb

        let toString () : string =
            let postfix (markupType: Markup option) : string =
                match markupType with
                | Some markup -> markup.Postfix
                | None -> String.Empty

            let prefix (markupType: Markup option) : string =
                match markupType with
                | Some markup -> markup.Prefix
                | None -> String.Empty

            let optimize (markupType: Markup option) (text: string) : string =
                match markupType with
                | Some markup -> markup.Optimize text
                | None -> String.Empty

            let firstMarkedupTextType = findFirstMarkedUpText ms

            match firstMarkedupTextType with
            | None -> getText (ms, None)
            | _ ->
                optimize
                    firstMarkedupTextType
                    (prefix firstMarkedupTextType
                     + getText (ms, None)
                     + postfix firstMarkedupTextType)

        let renderAs (format: string) : string =
            let fmt = format.ToLower()
            match fmt with
            | "ansi" -> toString()
            | _ -> getTextAs fmt (ms, None)

        let strVal: Lazy<string> = Lazy<string>(toString)

        [<TailCall>]
        let rec toPlainText () : string =
            let rec loop (sb: System.Text.StringBuilder) (content: Content list) =
                match content with
                | [] -> ()
                | Text str :: tail -> 
                    sb.Append(str) |> ignore
                    loop sb tail
                | MarkupText mStr :: tail -> 
                    loop sb mStr.Content
                    loop sb tail

            let sb = StringBuilderPool.getStringBuilder()
            try
                loop sb ms.Content
                sb.ToString()
            finally
                StringBuilderPool.returnStringBuilder sb

        let plainStrVal: Lazy<string> = Lazy<string>(toPlainText)

        member val MarkupDetails = markupDetails

        member val Content = content

        member val Length = len.Value

        member this.ToPlainText() : string = plainStrVal.Value

        member this.EvaluateWith(evaluator: System.Func<Markup option, string, string>) : string =
            evaluateWith (fun markup text -> evaluator.Invoke(markup, text))

        member this.Render(format: string) : string = renderAs format
            
        override this.ToString() : string = strVal.Value
        
        override this.Equals(obj) =
            let myPlainText = toPlainText()
            match obj with
            | :? MarkupString as other ->
                myPlainText.Equals(other.ToPlainText())
            | :? string as other ->
                myPlainText.Equals(other)
            | _ -> false
        
        override this.GetHashCode() = toPlainText().GetHashCode()

    /// <summary>
    /// Active pattern for extracting markup details and content from a MarkupString.
    /// </summary>
    /// <param name="markupStr">The MarkupString to extract from.</param>
    let (|MarkupStringPattern|) (markupStr: MarkupString) =
        (markupStr.MarkupDetails, markupStr.Content)

    /// <summary>
    /// Creates a MarkupString from a markup and a plain string.
    /// </summary>
    /// <param name="markupDetails">The markup to apply.</param>
    /// <param name="str">The plain string content.</param>
    let markupSingle (markupDetails: Markup, str: string) : MarkupString =
        MarkupString(Some markupDetails, [ Text str ])

    /// <summary>
    /// Creates a MarkupString from a markup and another MarkupString.
    /// </summary>
    /// <param name="markupDetails">The markup to apply.</param>
    /// <param name="mu">The MarkupString content.</param>
    let markupSingle2 (markupDetails: Markup, mu: MarkupString) : MarkupString =
        MarkupString(Some markupDetails, [ MarkupText mu ])

    /// <summary>
    /// Creates a MarkupString from a markup and a sequence of MarkupStrings.
    /// </summary>
    /// <param name="markupDetails">The markup to apply.</param>
    /// <param name="mu">The sequence of MarkupStrings.</param>
    let markupMultiple (markupDetails: Markup, mu: seq<MarkupString>) : MarkupString =
        MarkupString(Some markupDetails, mu |> Seq.map MarkupText |> Seq.toList)

    /// <summary>
    /// Creates a MarkupString from a plain string.
    /// </summary>
    /// <param name="str">The plain string content.</param>
    let single (str: string) : MarkupString = MarkupString(None, [ Text str ])

    /// <summary>
    /// Creates a MarkupString from a sequence of MarkupStrings.
    /// </summary>
    /// <param name="mu">The sequence of MarkupStrings.</param>
    let multiple (mu: seq<MarkupString>) : MarkupString =
        MarkupString(None, mu |> Seq.map MarkupText |> Seq.toList)

    /// <summary>
    /// Returns an empty MarkupString (cached singleton).
    /// </summary>
    let private emptyInstance = MarkupString(None, [ Text String.Empty ])
    let empty () : MarkupString = emptyInstance

    /// <summary>
    /// Creates a MarkupString by interspersing a delimiter between elements.
    /// </summary>
    /// <param name="delimiter">The delimiter MarkupString.</param>
    /// <param name="mu">The sequence of MarkupStrings.</param>
    let multipleWithDelimiter (delimiter: MarkupString) (mu: MarkupString seq) : MarkupString =
        mu |> Seq.intersperse delimiter |> multiple

    /// <summary>
    /// Intersperses a function-generated separator between elements of a list.
    /// </summary>
    /// <param name="sepFunc">Function to generate separator MarkupString given index.</param>
    /// <param name="list">The list to intersperse.</param>
    let intersperseFunc sepFunc list =
        seq {
            for i, element in list |> Seq.indexed do
                if i > 0 then
                    yield sepFunc i

                yield element
        }

    /// <summary>
    /// Creates a MarkupString by interspersing a function-generated delimiter between elements.
    /// </summary>
    /// <param name="delimiterFunc">Function to generate delimiter MarkupString given index.</param>
    /// <param name="mu">The sequence of MarkupStrings.</param>
    let multipleWithDelimiterFunc (delimiterFunc: int -> MarkupString) (mu: MarkupString seq) : MarkupString =
        mu |> intersperseFunc delimiterFunc |> multiple

    /// <summary>
    /// Serialization options for MarkupString, including color support.
    /// </summary>
    let serializationOptions =
        let serializeOption = JsonFSharpOptions.Default().ToJsonSerializerOptions()
        serializeOption.Converters.Add(ColorJsonConverter())
        serializeOption

    /// <summary>
    /// Serializes a MarkupString to a JSON string.
    /// </summary>
    /// <param name="markupStr">The MarkupString to serialize.</param>
    let serialize (markupStr: MarkupString) : string =
        JsonSerializer.Serialize(markupStr, serializationOptions)

    /// <summary>
    /// Deserializes a JSON string into a MarkupString.
    /// </summary>
    /// <param name="markupString">The JSON string to deserialize.</param>
    let deserialize (markupString: string) : MarkupString =
        if markupString.Length = 0 then
            empty ()
        else
            JsonSerializer.Deserialize(markupString, serializationOptions)

    /// <summary>
    /// Returns the plain text representation of a MarkupString.
    /// </summary>
    /// <param name="markupStr">The MarkupString to extract plain text from.</param>
    [<TailCall>]
    let rec plainText (markupStr: MarkupString) : string = markupStr.ToPlainText()

    /// <summary>
    /// Returns a MarkupString containing only the plain text of the input.
    /// </summary>
    /// <param name="markupStr">The MarkupString to convert.</param>
    let plainText2 (markupStr: MarkupString) : MarkupString =
        MarkupString(None, [ Text(markupStr.ToPlainText()) ])

    /// <summary>
    /// Gets the length of the plain text in a MarkupString.
    /// </summary>
    /// <param name="markupStr">The MarkupString to measure.</param>
    [<TailCall>]
    let rec getLength (markupStr: MarkupString) : int = markupStr.Length

    /// <summary>
    /// Evaluates a MarkupString using a custom evaluator function.
    /// Useful for reconstructing original function calls like ansi() from markup information.
    /// </summary>
    /// <param name="evaluator">Function that takes markup type and inner text, returns reconstructed string</param>
    /// <param name="markupStr">The MarkupString to evaluate</param>
    let evaluateWith (evaluator: System.Func<Markup option, string, string>) (markupStr: MarkupString) : string =
        markupStr.EvaluateWith(evaluator)

    /// <summary>
    /// Renders a MarkupString to the specified output format.
    /// </summary>
    /// <param name="format">The output format: "ansi" for ANSI escape codes (default), "html" for HTML spans.</param>
    /// <param name="markupStr">The MarkupString to render.</param>
    let render (format: string) (markupStr: MarkupString) : string =
        markupStr.Render(format)

    /// <summary>
    /// The fixed CSS rules for the formatting classes emitted by Render("html").
    /// Include this once in any page that displays HTML-rendered MarkupString output.
    /// Color classes (e.g. .fg-ff0000, .bg-008000) are dynamic and generated by cssSheet.
    /// </summary>
    let fixedCss =
        ".ms-bold { font-weight: bold; }\n" +
        ".ms-faint { opacity: 0.5; }\n" +
        ".ms-italic { font-style: italic; }\n" +
        ".ms-underline { text-decoration: underline; }\n" +
        ".ms-strike { text-decoration: line-through; }\n" +
        ".ms-overline { text-decoration: overline; }\n" +
        ".ms-blink { animation: blink 1s step-start infinite; }\n"

    /// <summary>
    /// Generates a CSS stylesheet for the given MarkupString.
    /// Colors are emitted as inline style attributes in the HTML output, so only the fixed
    /// formatting rules (ms-bold, ms-italic, etc.) are included in the stylesheet.
    /// </summary>
    /// <param name="markupStr">The MarkupString (unused; accepted for API compatibility).</param>
    let cssSheet (_markupStr: MarkupString) : string =
        fixedCss

    /// <summary>
    /// Optimizes a MarkupString by merging adjacent content with the same markup details
    /// and lifting child content when the parent and child have the same markup details.
    /// </summary>
    /// <param name="markupStr">The MarkupString to optimize.</param>
    let optimize (markupStr: MarkupString) : MarkupString =
        let rec msOptimize (ms: MarkupString) =
            // Depth-First optimization of nested MarkupStrings
            // This function should go to the bottom of each tree branch first
            // From there, it looks for any Content that, left to right, have the same MarkupDetails
            // If so, it should merge their Content lists together.
            // As it travels up, it should also check that the current MarkupDetails is the same as the child MarkupDetails
            // If so, and the child is the only Content, it should lift the child's Content up to the parent.
            
            // Helper function to check if two Markup options are equivalent
            let markupTypesEqual (a: Markup option) (b: Markup option) =
                a = b
            
            // First, recursively optimize all nested MarkupStrings (depth-first)
            let optimizedContent = 
                ms.Content 
                |> List.map (function
                    | Text t -> Text t
                    | MarkupText nestedMs -> MarkupText (msOptimize nestedMs))
            
            // Merge adjacent MarkupText items with the same MarkupDetails
            let rec mergeAdjacent (contentList: Content list) (acc: Content list) =
                match contentList with
                | [] -> List.rev acc
                | [single] -> List.rev (single :: acc)
                | MarkupText a :: MarkupText b :: tail when markupTypesEqual a.MarkupDetails b.MarkupDetails ->
                    // Merge the content lists of a and b
                    let mergedContent = a.Content @ b.Content
                    let merged = MarkupString(a.MarkupDetails, mergedContent)
                    mergeAdjacent (MarkupText merged :: tail) acc
                | head :: tail ->
                    mergeAdjacent tail (head :: acc)
            
            let mergedContent = mergeAdjacent optimizedContent []
            
            // Check if we can lift child content up to parent
            match mergedContent with
            | [MarkupText child] when markupTypesEqual ms.MarkupDetails child.MarkupDetails ->
                // Lift the child's content up to the parent level
                MarkupString(ms.MarkupDetails, child.Content)
            | _ ->
                MarkupString(ms.MarkupDetails, mergedContent)
        
        msOptimize markupStr

    /// <summary>
    /// Concatenates two MarkupStrings, optionally inserting a separator.
    /// </summary>
    /// <param name="originalMarkupStr">The first MarkupString.</param>
    /// <param name="newMarkupStr">The second MarkupString.</param>
    /// <param name="optionalSeparator">An optional separator MarkupString.</param>
    let concat
        (originalMarkupStr: MarkupString)
        (newMarkupStr: MarkupString)
        ([<Optional; DefaultParameterValue(null)>] optionalSeparator: MarkupString option)
        : MarkupString =
        let separatorContent =
            match optionalSeparator with
            | Some separator -> [ MarkupText separator ]
            | None -> []

        match originalMarkupStr.MarkupDetails with
        | None ->
            let combinedContent =
                originalMarkupStr.Content @ separatorContent @ [ MarkupText newMarkupStr ]
            MarkupString(None, combinedContent)
        | _ ->
            let combinedContent =
                [ MarkupText originalMarkupStr ]
                @ separatorContent
                @ [ MarkupText newMarkupStr ]
            MarkupString(None, combinedContent)

    /// <summary>
    /// Concatenates and attaches a MarkupString to another, handling nested structures.
    /// </summary>
    /// <remarks>
    /// This type of concatenation specifically extends the Markup that applies to the
    /// last element of the original MarkupString to the concatenated value.
    /// </remarks>
    /// <param name="originalMarkupStr">The original MarkupString.</param>
    /// <param name="newMarkupStr">The MarkupString to attach.</param>
    /// <param name="optionalSeparator">An optional separator MarkupString.</param>
    let rec concatAttach
        (originalMarkupStr: MarkupString)
        (newMarkupStr: MarkupString)
        ([<Optional; DefaultParameterValue(null)>] optionalSeparator: MarkupString option)
        : MarkupString =
        if originalMarkupStr.Content |> List.last |> _.IsText then
            MarkupString(originalMarkupStr.MarkupDetails, originalMarkupStr.Content @ [ MarkupText newMarkupStr ])
        else
            let split =
                originalMarkupStr.Content |> List.splitAt (originalMarkupStr.Content.Length - 1)

            match split with
            | [ MarkupText oneElement ], [] ->
                MarkupString(
                    originalMarkupStr.MarkupDetails,
                    [] @ [ MarkupText(concatAttach oneElement newMarkupStr optionalSeparator) ]
                )
            | list, [ MarkupText lastElement ] ->
                MarkupString(
                    originalMarkupStr.MarkupDetails,
                    list @ [ MarkupText(concatAttach lastElement newMarkupStr optionalSeparator) ]
                )
            | _, [ Text _ ] -> concat originalMarkupStr newMarkupStr optionalSeparator
            | _ -> failwith "concatAttach should never see an empty list."

    /// <summary>
    /// Returns a substring of a MarkupString, preserving markup.
    /// </summary>
    /// <param name="start">Start index.</param>
    /// <param name="length">Length of substring.</param>
    /// <param name="markupStr">Input MarkupString.</param>
    [<TailCall>]
    let rec substring (start: int) (length: int) (markupStr: MarkupString) : MarkupString =
        let inline extractText str start length =
            if length <= 0 || str = String.Empty then
                None
            else
                Some(str.Substring(start, min (str.Length - start) length))

        let rec substringAux contents start length acc =
            if length <= 0 then
                List.rev acc
            else
                match contents with
                | [] -> List.rev acc
                | head :: tail ->
                    match head with
                    | Text str when start < str.Length ->
                        let skip = max start 0
                        let take = min (str.Length - skip) length

                        match extractText str skip take with
                        | Some result -> substringAux tail (start - str.Length) (length - take) (Text result :: acc)
                        | None -> substringAux tail (start - str.Length) length acc
                    | Text str when start >= str.Length -> substringAux tail (start - str.Length) length acc
                    | MarkupText innerMarkup ->
                        let strLen = getLength innerMarkup

                        if start < strLen then
                            let skip = max start 0
                            let take = min strLen length
                            let subMarkup = substring skip take innerMarkup
                            let subLength = getLength subMarkup

                            if subLength > 0 then
                                substringAux tail 0 (length - subLength) (MarkupText subMarkup :: acc)
                            else
                                substringAux tail (start - strLen) length acc
                        else
                            substringAux tail (start - strLen) length acc
                    | _ -> raise (InvalidOperationException "Encountered unexpected content type in substring operation.")

        MarkupString(markupStr.MarkupDetails, substringAux markupStr.Content start length [])

    /// <summary>
    /// Returns all indexes where a search MarkupString occurs in another MarkupString.
    /// </summary>
    /// <param name="markupStr">Input MarkupString.</param>
    /// <param name="search">Search MarkupString.</param>
    [<TailCall>]
    let rec indexesOf (markupStr: MarkupString) (search: MarkupString) : seq<int> =
        let text = plainText markupStr
        let srch = plainText search

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

    /// <summary>
    /// Returns the first index where a search MarkupString occurs.
    /// Returns -1 if the item is not found.
    /// </summary>
    /// <param name="markupStr">Input MarkupString.</param>
    /// <param name="search">Search MarkupString.</param>
    [<TailCall>]
    let rec indexOf (markupStr: MarkupString) (search: MarkupString) : int =
        let text = plainText markupStr
        let srch = plainText search
        text.IndexOf(srch, StringComparison.Ordinal)
        
    [<TailCall>]
    let rec indexOf2 (markupStr: MarkupString) (search: string) : int =
        (plainText markupStr).IndexOf(search, StringComparison.Ordinal)
        
    /// <summary>
    /// Returns the last index where a search MarkupString occurs.
    /// </summary>
    /// <param name="markupStr">Input MarkupString.</param>
    /// <param name="search">Search MarkupString.</param>
    [<TailCall>]
    let rec indexOfLast (markupStr: MarkupString) (search: MarkupString) : int =
        let text = plainText markupStr
        let srch = plainText search
        text.LastIndexOf(srch, StringComparison.Ordinal)

    /// <summary>
    /// Splits a MarkupString by a string delimiter.
    /// </summary>
    /// <param name="delimiter">The delimiter string.</param>
    /// <param name="markupStr">The MarkupString to split.</param>
    [<TailCall>]
    let rec split (delimiter: string) (markupStr: MarkupString) : MarkupString[] =
        if markupStr.Length = 0 then
            [| markupStr |]
        else
            let fullText = plainText markupStr
            
            let rec findDelimiters (text: string) (pos: int) acc =
                if pos >= text.Length then
                    List.rev acc
                else
                    match text.IndexOf(delimiter, pos, StringComparison.Ordinal) with
                    | -1 -> List.rev acc
                    | idx ->
                        let nextPos = if delimiter.Length > 0 then idx + delimiter.Length else idx + 1
                        findDelimiters text nextPos (idx :: acc)

            let delimiterPositions = findDelimiters fullText 0 []

            match delimiterPositions with
            | [] -> [| markupStr |]  // No delimiters found, return original
            | positions ->
                let rec buildSplits (positions: int list) (lastPos: int) (segments: MarkupString list) =
                    match positions with
                    | [] ->
                        let lastSegment = substring lastPos (fullText.Length - lastPos) markupStr
                        List.rev (lastSegment :: segments)
                    | pos :: tail ->
                        let length = pos - lastPos
                        let segment = substring lastPos length markupStr
                        buildSplits tail (pos + delimiter.Length) (segment :: segments)

                buildSplits positions 0 [] |> Array.ofList

    /// <summary>
    /// Splits a MarkupString by another MarkupString as delimiter.
    /// </summary>
    /// <param name="delimiter">The delimiter MarkupString.</param>
    /// <param name="markupStr">The MarkupString to split.</param>
    let split2 (delimiter: MarkupString) (markupStr: MarkupString) = split (plainText delimiter) markupStr

    /// <summary>
    /// Applies a transformation function to the text content of a MarkupString.
    /// </summary>
    /// <param name="str">The MarkupString to transform.</param>
    /// <param name="transform">The transformation function.</param>
    [<TailCall>]
    let rec apply (str: MarkupString) (transform: string -> string) : MarkupString =
        let rec mapContent content =
            content
            |> List.map (function
                | Text s -> Text(transform s)
                | MarkupText m -> MarkupText(apply m transform))

        MarkupString(str.MarkupDetails, mapContent str.Content)
        
    /// <summary>
    /// Applies a transformation function to the text content of a MarkupString.
    /// </summary>
    /// <param name="str">The MarkupString to transform.</param>
    /// <param name="transform">The transformation function.</param>
    [<TailCall>]
    let rec apply2 (str: MarkupString) (transform: MarkupString -> MarkupString) : MarkupString =
        let rec mapContent content =
            content
            |> List.map (function
                | Text s -> MarkupText (transform (single s))
                | MarkupText m -> MarkupText (apply2 m transform))

        MarkupString(str.MarkupDetails, mapContent str.Content)

    /// <summary>
    /// Inserts a MarkupString at a specified index in another MarkupString.
    /// </summary>
    /// <param name="input">The original MarkupString.</param>
    /// <param name="insert">The MarkupString to insert.</param>
    /// <param name="index">The index at which to insert.</param>
    let insertAt (input: MarkupString) (insert: MarkupString) (index: int) : MarkupString =
        let len = getLength input

        if index <= 0 then
            concat insert input None
        elif index >= len then
            concat input insert None
        else
            let before = substring 0 index input
            let after = substring index (len - index) input
            let wrappedInsert = MarkupString(before.MarkupDetails, [ MarkupText insert ])
            concat (concat before wrappedInsert None) after None

    /// <summary>
    /// Trim a string from the start, end, or both ends based on the specified TrimType.
    /// Optimized to avoid building sequences of all match positions.
    /// </summary>
    /// <param name="markupStr">Input MarkupString.</param>
    /// <param name="trimStr">String to trim.</param>
    /// <param name="trimType">Trim type (start, end, both).</param>
    let trim (markupStr: MarkupString) (trimStr: MarkupString) (trimType: TrimType) : MarkupString =
        let text = plainText markupStr
        let trimChars = plainText trimStr
        let len = text.Length

        let rec countLeft i =
            if i >= len || not (trimChars.Contains(text.[i])) then i
            else countLeft (i + 1)

        let rec countRight i =
            if i < 0 || not (trimChars.Contains(text.[i])) then i + 1
            else countRight (i - 1)

        match trimType with
        | TrimStart ->
            let leftTrim = countLeft 0
            if leftTrim = 0 then markupStr
            else substring leftTrim (len - leftTrim) markupStr
        | TrimEnd ->
            let rightBoundary = countRight (len - 1)
            if rightBoundary = len then markupStr
            else substring 0 rightBoundary markupStr
        | TrimBoth ->
            let leftTrim = countLeft 0
            let rightBoundary = countRight (len - 1)
            if leftTrim = 0 && rightBoundary = len then markupStr
            else substring leftTrim (rightBoundary - leftTrim) markupStr

    /// <summary>
    /// Repeat a MarkupString a specified number of times, concatenating them to the aggregator.
    /// Uses exponential growth strategy for O(log n) concat operations instead of O(n).
    /// </summary>
    /// <param name="markupStr">The MarkupString to repeat.</param>
    /// <param name="count">The number of times to repeat.</param>
    /// <param name="aggregator">The initial MarkupString to aggregate into.</param>
    [<TailCall>]
    let rec repeat (markupStr: MarkupString) (count: int) (aggregator: MarkupString) =
        if count <= 0 then
            aggregator
        else
            let rec exponentialRepeat acc current remaining =
                if remaining <= 0 then
                    acc
                else if remaining = 1 then
                    concat acc current None
                else if remaining % 2 = 0 then
                    exponentialRepeat acc (concat current current None) (remaining / 2)
                else
                    exponentialRepeat (concat acc current None) (concat current current None) (remaining / 2)
            exponentialRepeat aggregator markupStr count

    /// <summary>
    /// Centers a MarkupString within a specified width using a padding string on the right.
    /// </summary>
    /// <param name="markupStr">Input MarkupString.</param>
    /// <param name="padStr">Padding MarkupString for left side.</param>
    /// <param name="padStrRight">Padding MarkupString for right side.</param>
    /// <param name="width">Total width.</param>
    /// <param name="truncType">Truncation type.</param>
    let center2
        (markupStr: MarkupString)
        (padStr: MarkupString)
        (padStrRight: MarkupString)
        (width: int)
        (truncType: TruncationType)
        : MarkupString =
        let len = getLength markupStr
        let padLen = getLength padStr
        let padLenRight = getLength padStrRight
        let lengthToPad = width - len
        let lengthTooLongPredicate = lengthToPad <= 0

        match truncType with
        | Overflow when lengthTooLongPredicate -> markupStr
        | Truncate when lengthTooLongPredicate -> substring 0 lengthToPad markupStr
        | Overflow ->
            let leftPadLength = lengthToPad / 2
            let rightPadLength = lengthToPad - leftPadLength

            let padding =
                repeat padStr ((width / padLen) + 1) (empty ()) |> substring 0 lengthToPad

            let paddingRight =
                repeat padStrRight ((width / padLenRight) + 1) (empty ())
                |> substring 0 lengthToPad

            let leftPad = substring 0 leftPadLength padding
            let rightPad = substring leftPadLength rightPadLength paddingRight

            concat leftPad markupStr None |> fun x -> concat x rightPad None
        | Truncate ->
            let leftPadLength = lengthToPad / 2
            let rightPadLength = lengthToPad - leftPadLength

            let padding =
                repeat padStr ((width / padLen) + 1) (empty ()) |> substring 0 lengthToPad

            let paddingRight =
                repeat padStrRight ((width / padLenRight) + 1) (empty ())
                |> substring 0 lengthToPad

            let leftPad = substring 0 leftPadLength padding
            let rightPad = substring leftPadLength rightPadLength paddingRight

            concat leftPad markupStr None
            |> fun x -> concat x rightPad None
            |> substring 0 width


    /// <summary>
    /// Pads a MarkupString to a specified width using a padding string and pad type.
    /// </summary>
    /// <param name="markupStr">Input MarkupString.</param>
    /// <param name="padStr">Padding MarkupString.</param>
    /// <param name="width">Total width.</param>
    /// <param name="padType">Pad type (left, right, center, full).</param>
    /// <param name="truncType">Truncation type.</param>
    let pad
        (markupStr: MarkupString)
        (padStr: MarkupString)
        (width: int)
        (padType: PadType)
        (truncType: TruncationType)
        : MarkupString =
        let len = getLength markupStr
        let padLen = getLength padStr
        let lengthToPad = width - len
        let repeatCount = (lengthToPad / padLen) + 1
        let lengthTooLongPredicate = lengthToPad <= 0

        match padType, truncType with
        | _, Overflow when lengthTooLongPredicate -> markupStr
        | _, Truncate when lengthToPad = 0 -> markupStr
        | _, Truncate when lengthTooLongPredicate -> substring 0 width markupStr
        | Right, Overflow ->
            repeat padStr repeatCount (empty ())
            |> substring 0 lengthToPad
            |> fun x -> concat markupStr x None
        | Right, Truncate ->
            repeat padStr repeatCount (empty ())
            |> substring 0 lengthToPad
            |> fun x -> concat markupStr x None
            |> substring 0 width
        | Left, Overflow ->
            repeat padStr repeatCount (empty ())
            |> substring 0 lengthToPad
            |> fun x -> concat x markupStr None
        | Left, Truncate ->
            repeat padStr repeatCount (empty ())
            |> substring 0 lengthToPad
            |> fun x -> concat x markupStr None
            |> substring 0 width
        | Center, Overflow ->
            let leftPadLength = lengthToPad / 2
            let rightPadLength = lengthToPad - leftPadLength

            let padding =
                repeat padStr ((width / padLen) + 1) (empty ()) |> substring 0 lengthToPad

            let leftPad = substring 0 leftPadLength padding
            let rightPad = substring leftPadLength rightPadLength padding

            concat leftPad markupStr None |> fun x -> concat x rightPad None
        | Center, Truncate ->
            let leftPadLength = lengthToPad / 2
            let rightPadLength = lengthToPad - leftPadLength

            let padding =
                repeat padStr ((width / padLen) + 1) (empty ()) |> substring 0 lengthToPad

            let leftPad = substring 0 leftPadLength padding
            let rightPad = substring leftPadLength rightPadLength padding

            concat leftPad markupStr None
            |> fun x -> concat x rightPad None
            |> substring 0 width
        // Full Justification requires the Padding String to be a space.
        | Full, Truncate when markupStr.Length > width -> substring 0 width markupStr
        | Full, Overflow when markupStr.Length > width -> markupStr
        | Full, _ ->
            let wordArr = split " " markupStr
            let fences = Math.Max(wordArr.Length - 1, 0)
            let totalSpaces = fences + lengthToPad
            let space = single " "
            let minimumFenceWidth = totalSpaces / fences
            let thickerFences = totalSpaces % fences
            let fenceStr = (repeat space minimumFenceWidth (empty ()))
            let thickFenceStr = (repeat space (minimumFenceWidth + 1) (empty ()))
            let delFunc = (fun i -> if i <= thickerFences then thickFenceStr else fenceStr)

            multipleWithDelimiterFunc delFunc wordArr

    type private GlobPatternRegex = FSharp.Text.RegexProvider.Regex< @"(?<!\\)\\\*" >
    type private QuestionPatternRegex = FSharp.Text.RegexProvider.Regex< @"(?<!\\)\\\?" >
    type private KindPatternRegex = FSharp.Text.RegexProvider.Regex< @"\\\\\\\*" >
    type private KindPattern2Regex = FSharp.Text.RegexProvider.Regex< @"\\\\\\\?" >

    // Cache compiled regex instances to avoid repeated allocations
    let private globPatternRegexInstance = GlobPatternRegex()
    let private questionPatternRegexInstance = QuestionPatternRegex()
    let private kindPatternRegexInstance = KindPatternRegex()
    let private kindPattern2RegexInstance = KindPattern2Regex()

    /// <summary>
    /// Converts a wildcard pattern MarkupString to a regex string.
    /// </summary>
    /// <param name="pattern">The wildcard pattern as a MarkupString.</param>
    let getWildcardMatchAsRegex (pattern: MarkupString) : string =
        let applyRegexPattern (pat: string) =
            pat
            |> fun x -> globPatternRegexInstance.TypedReplace(x, konst @"(.*?)")
            |> fun x -> questionPatternRegexInstance.TypedReplace(x, konst @"(.)")
            |> fun x -> kindPatternRegexInstance.TypedReplace(x, konst @"\*")
            |> fun x -> kindPattern2RegexInstance.TypedReplace(x, konst @"\?")

        pattern |> plainText |> Regex.Escape |> (fun x -> $"^{x}$") |> applyRegexPattern
        
    /// <summary>
    /// Converts a wildcard pattern string to a regex string.
    /// </summary>
    /// <param name="pattern">The wildcard pattern as a string.</param>
    let getWildcardMatchAsRegex2 (pattern: string) : string =
        let applyRegexPattern (pat: string) =
            pat
            |> fun x -> globPatternRegexInstance.TypedReplace(x, konst @"(.*?)")
            |> fun x -> questionPatternRegexInstance.TypedReplace(x, konst @"(.)")
            |> fun x -> kindPatternRegexInstance.TypedReplace(x, konst @"\*")
            |> fun x -> kindPattern2RegexInstance.TypedReplace(x, konst @"\?")

        pattern |> Regex.Escape |> (fun x -> $"^{x}$") |> applyRegexPattern

    /// <summary>
    /// Determines if the input MarkupString matches the wildcard pattern.
    /// </summary>
    /// <param name="input">The input MarkupString.</param>
    /// <param name="pattern">The wildcard pattern MarkupString.</param>
    let isWildcardMatch (input: MarkupString) (pattern: MarkupString) : bool =
        let newPattern = getWildcardMatchAsRegex pattern
        (plainText input, newPattern) |> Regex.IsMatch
        
    /// <summary>
    /// Determines if the input MarkupString matches the wildcard pattern.
    /// </summary>
    /// <param name="input">The input MarkupString.</param>
    /// <param name="pattern">The wildcard pattern MarkupString.</param>
    let isWildcardMatch2 (input: MarkupString) (pattern: string) : bool =
        let newPattern = getWildcardMatchAsRegex2 pattern
        (plainText input, newPattern) |> Regex.IsMatch

    /// <summary>
    /// Gets regex matches from a MarkupString input and pattern.
    /// </summary>
    /// <param name="input">The input MarkupString.</param>
    /// <param name="pattern">The regex pattern string.</param>
    let getMatches (input: MarkupString) (pattern: string) : (Match * MarkupString seq) seq =
        let captureToString (captureGroup: Group) =
            substring captureGroup.Index captureGroup.Length input

        let allMatches (mtch: Match) =
            (mtch, mtch.Groups |> Seq.map captureToString)

        ((plainText input), pattern)
        |> Regex.Matches
        |> Seq.cast<Match>
        |> Seq.map allMatches

    /// <summary>
    /// Gets regex matches from a MarkupString input and MarkupString pattern.
    /// </summary>
    /// <param name="input">The input MarkupString.</param>
    /// <param name="pattern">The regex pattern as a MarkupString.</param>
    let getRegexpMatches (input: MarkupString) (pattern: MarkupString) : (Match * MarkupString seq) seq =
        getMatches input (plainText pattern)

    /// <summary>
    /// Gets wildcard matches from a MarkupString input and pattern.
    /// </summary>
    /// <param name="input">The input MarkupString.</param>
    /// <param name="pattern">The wildcard pattern MarkupString.</param>
    let getWildcardMatches (input: MarkupString) (pattern: MarkupString) : (Match * MarkupString seq) seq =
        getMatches input (getWildcardMatchAsRegex pattern)

    /// <summary>
    /// Removes a substring from a MarkupString at a given index and length.
    /// </summary>
    /// <param name="markupStr">The MarkupString to remove from.</param>
    /// <param name="index">The starting index to remove.</param>
    /// <param name="length">The number of characters to remove.</param>
    [<TailCall>]
    let rec remove (markupStr: MarkupString) (index: int) (length: int) : MarkupString =
        let rightStart = index + length
        let rightEnd = markupStr.Length - rightStart
        let left = markupStr |> substring 0 index
        let right = markupStr |> substring rightStart rightEnd
        (concat left right None)

    /// <summary>
    /// Replaces a value in markupStr with the one in replacementStr,
    /// writing over position 'index' to 'index + length'.
    /// Optimized to perform single-pass replacement instead of remove+insertAt.
    /// </summary>
    /// <param name="markupStr">Original String</param>
    /// <param name="replacementStr">Replacement String</param>
    /// <param name="index">Index where to replace</param>
    /// <param name="length">Length of the area to replace over.</param>
    [<TailCall>]
    let rec replace (markupStr: MarkupString) (replacementStr: MarkupString) (index: int) (length: int) : MarkupString =
        if index >= markupStr.Length then
            concat markupStr replacementStr None
        elif index < 0 then
            concat replacementStr markupStr None
        else
            let trueLength = Math.Min(index + length, markupStr.Length) - index
            let rightStart = index + trueLength
            let rightEnd = markupStr.Length - rightStart
            let left = substring 0 index markupStr
            let right = substring rightStart rightEnd markupStr
            concat left replacementStr None |> (fun x -> concat x right None)
