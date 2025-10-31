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

    and MarkupTypes = // TODO: Consider using built-in option type.
        | MarkedupText of Markup
        | Empty

    and ColorJsonConverter() =
        inherit JsonConverter<Color>()

        override _.Read(reader, _typeToConvert, _options) =
            ColorTranslator.FromHtml(reader.GetString())

        override _.Write(writer, value, _) =
            writer.WriteStringValue($"#{value.R:X2}{value.G:X2}{value.B:X2}".ToLower())

    and MarkupString(markupDetails: MarkupTypes, content: Content list) as ms =
        // TODO: Optimize the ansi strings, so we don't re-initialize at least the exact same tag sequentially.
        [<TailCall>]
        let rec getText (markupStr: MarkupString, outerMarkupType: MarkupTypes) : string =
            let accumulate (acc: string, items: Content list) =
                let rec loop (acc: string, items: Content list) =
                    match items with
                    | [] -> acc
                    | Text str :: tail -> loop (acc + str, tail)
                    | MarkupText mStr :: tail ->
                        let inner =
                            match markupStr.MarkupDetails with
                            | Empty -> getText (mStr, outerMarkupType)
                            | MarkedupText _ -> getText (mStr, markupStr.MarkupDetails)

                        loop (acc + inner, tail)

                loop (acc, items)

            let innerText = accumulate (String.Empty, markupStr.Content)

            match markupStr.MarkupDetails with
            | Empty -> innerText
            | MarkedupText str ->
                match outerMarkupType with
                | Empty -> str.Wrap(innerText)
                | MarkedupText outerMarkup -> str.WrapAndRestore(innerText, outerMarkup)

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

        let isMarkedUp (m: MarkupTypes) =
            match m with
            | MarkedupText _ -> true
            | Empty -> false

        // BUG: This is not correctly matching the first MarkedUp Text
        [<TailCall>]
        let findFirstMarkedUpText (markupStr: MarkupString) : MarkupTypes =
            let rec find (content: Content list) : MarkupTypes =
                match content with
                | [] -> Empty
                | MarkupText mStr :: _ when isMarkedUp mStr.MarkupDetails -> mStr.MarkupDetails
                | MarkupText a :: tail ->
                    match (find a.Content, find tail) with
                    | MarkedupText res, _ -> MarkedupText res
                    | _, MarkedupText res -> MarkedupText res
                    | _ -> Empty
                | _ -> Empty

            match markupStr.MarkupDetails with
            | MarkedupText _ -> markupStr.MarkupDetails
            | _ -> find markupStr.Content

        let len: Lazy<int> = Lazy<int>(length)

        /// <summary>
        /// Evaluates the MarkupString using a custom evaluation function that receives markup information.
        /// This allows reconstructing original function calls like ansi() from the markup data.
        /// </summary>
        /// <param name="evaluator">Function that takes (markupType, innerText) and returns reconstructed string</param>
        [<TailCall>]
        let rec evaluateWith (evaluator: MarkupTypes -> string -> string) : string =
            let rec evalContent (content: Content list) : string =
                content
                |> List.map (function
                    | Text str -> str
                    | MarkupText mStr -> evaluateWithMarkup mStr evaluator)
                |> String.concat ""

            and evaluateWithMarkup (markupStr: MarkupString) (evaluator: MarkupTypes -> string -> string) : string =
                let innerText = evalContent markupStr.Content
                evaluator markupStr.MarkupDetails innerText

            // Start evaluation with this MarkupString's structure
            evaluateWithMarkup ms evaluator

        let toString () : string =
            let postfix (markupType: MarkupTypes) : string =
                match markupType with
                | MarkedupText markup -> markup.Postfix
                | Empty -> String.Empty

            let prefix (markupType: MarkupTypes) : string =
                match markupType with
                | MarkedupText markup -> markup.Prefix
                | Empty -> String.Empty

            let optimize (markupType: MarkupTypes) (text: string) : string =
                match markupType with
                | MarkedupText markup -> markup.Optimize text
                | Empty -> String.Empty

            let firstMarkedupTextType = findFirstMarkedUpText ms

            match firstMarkedupTextType with
            | Empty -> getText (ms, Empty)
            | _ ->
                optimize
                    firstMarkedupTextType
                    (prefix firstMarkedupTextType
                     + getText (ms, Empty)
                     + postfix firstMarkedupTextType)

        let strVal: Lazy<string> = Lazy<string>(toString)

        [<TailCall>]
        let rec toPlainText () : string =
            let rec loop (content: Content list) (acc: string list) =
                match content with
                | [] -> List.rev acc
                | Text str :: tail -> loop tail (str :: acc)
                | MarkupText mStr :: tail -> loop (mStr.Content @ tail) acc

            String.Concat(loop ms.Content [])

        let plainStrVal: Lazy<string> = Lazy<string>(toPlainText)

        member val MarkupDetails = markupDetails with get, set

        member val Content = content with get, set

        member val Length = len.Value

        override this.ToString() : string = strVal.Value

        member this.ToPlainText() : string = plainStrVal.Value

        member this.EvaluateWith(evaluator: System.Func<MarkupTypes, string, string>) : string =
            evaluateWith (fun markup text -> evaluator.Invoke(markup, text))

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
        MarkupString(MarkedupText markupDetails, [ Text str ])

    /// <summary>
    /// Creates a MarkupString from a markup and another MarkupString.
    /// </summary>
    /// <param name="markupDetails">The markup to apply.</param>
    /// <param name="mu">The MarkupString content.</param>
    let markupSingle2 (markupDetails: Markup, mu: MarkupString) : MarkupString =
        MarkupString(MarkedupText markupDetails, [ MarkupText mu ])

    /// <summary>
    /// Creates a MarkupString from a markup and a sequence of MarkupStrings.
    /// </summary>
    /// <param name="markupDetails">The markup to apply.</param>
    /// <param name="mu">The sequence of MarkupStrings.</param>
    let markupMultiple (markupDetails: Markup, mu: seq<MarkupString>) : MarkupString =
        MarkupString(MarkedupText markupDetails, mu |> Seq.map MarkupText |> Seq.toList)

    /// <summary>
    /// Creates a MarkupString from a plain string.
    /// </summary>
    /// <param name="str">The plain string content.</param>
    let single (str: string) : MarkupString = MarkupString(Empty, [ Text str ])

    /// <summary>
    /// Creates a MarkupString from a sequence of MarkupStrings.
    /// </summary>
    /// <param name="mu">The sequence of MarkupStrings.</param>
    let multiple (mu: seq<MarkupString>) : MarkupString =
        MarkupString(Empty, mu |> Seq.map MarkupText |> Seq.toList)

    /// <summary>
    /// Returns an empty MarkupString.
    /// </summary>
    let empty () : MarkupString =
        MarkupString(Empty, [ Text String.Empty ])

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
        MarkupString(Empty, [ Text(markupStr.ToPlainText()) ])

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
    let evaluateWith (evaluator: System.Func<MarkupTypes, string, string>) (markupStr: MarkupString) : string =
        markupStr.EvaluateWith(evaluator)

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
        | Empty ->
            let combinedContent =
                originalMarkupStr.Content @ separatorContent @ [ MarkupText newMarkupStr ]

            MarkupString(Empty, combinedContent)
        | _ ->
            let combinedContent =
                [ MarkupText originalMarkupStr ]
                @ separatorContent
                @ [ MarkupText newMarkupStr ]

            MarkupString(Empty, combinedContent)

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
            match contents, length with
            | _, 0 -> List.rev acc
            | [], _ -> List.rev acc
            | head :: tail, _ ->
                match head with
                | Text str when start < str.Length ->
                    let skip = max start 0
                    let take = min (str.Length - skip) length

                    match extractText str skip take with
                    | Some result -> substringAux tail (start - str.Length) (length - take) (Text result :: acc)
                    | None -> substringAux tail (start - str.Length) length acc
                | Text str when start >= str.Length -> substringAux tail (start - str.Length) length acc
                | MarkupText markupStr ->
                    let strLen = getLength markupStr

                    if start < strLen then
                        let skip = max start 0
                        let take = min strLen length
                        let subMarkup = substring skip take markupStr
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
                match text.IndexOf(srch, pos) with
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
        let matches = indexesOf markupStr search

        match matches with
        | _ when Seq.isEmpty matches -> -1
        | _ -> matches |> Seq.head
        
    [<TailCall>]
    let rec indexOf2 (markupStr: MarkupString) (search: string) : int =
        indexOf markupStr (single search)
        
    /// <summary>
    /// Returns the last index where a search MarkupString occurs.
    /// </summary>
    /// <param name="markupStr">Input MarkupString.</param>
    /// <param name="search">Search MarkupString.</param>
    [<TailCall>]
    let rec indexOfLast (markupStr: MarkupString) (search: MarkupString) : int =
        let matches = indexesOf markupStr search

        match matches with
        | _ when Seq.isEmpty matches -> -1
        | _ -> matches |> Seq.last

    /// <summary>
    /// Splits a MarkupString by a string delimiter.
    /// </summary>
    /// <param name="delimiter">The delimiter string.</param>
    /// <param name="markupStr">The MarkupString to split.</param>
    [<TailCall>]
    let rec split (delimiter: string) (markupStr: MarkupString) : MarkupString[] =
        let rec findDelimiters (text: string) (pos: int) =
            if pos >= text.Length then
                []
            else
                match text.IndexOf(delimiter, pos) with
                | -1 -> []
                | idx ->
                    idx
                    :: if (delimiter <> String.Empty) then
                           findDelimiters text (idx + delimiter.Length)
                       else
                           findDelimiters text (idx + 1)

        let fullText = plainText markupStr
        let delimiterPositions = findDelimiters fullText 0

        let rec buildSplits positions lastPos segments =
            match positions with
            | [] ->
                let lastSegment = substring lastPos (fullText.Length - lastPos) markupStr
                List.rev (lastSegment :: segments)
            | pos :: tail ->
                let length = pos - lastPos
                let segment = substring lastPos length markupStr
                buildSplits tail (pos + delimiter.Length) (segment :: segments)

        buildSplits delimiterPositions 0 [] |> Array.ofList

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
    /// </summary>
    /// <param name="markupStr">Input MarkupString.</param>
    /// <param name="trimStr">String to trim.</param>
    /// <param name="trimType">Trim type (start, end, both).</param>
    let trim (markupStr: MarkupString) (trimStr: MarkupString) (trimType: TrimType) : MarkupString =
        let trimStrLen = getLength trimStr

        match trimType with
        | TrimStart ->
            let start = indexOf markupStr trimStr

            if start = -1 || start > trimStrLen then
                markupStr
            else
                substring start (trimStrLen - start) markupStr
        | TrimEnd ->
            let markupStrLen = getLength markupStr
            let start = indexOfLast markupStr trimStr

            if start = -1 || start + trimStrLen < markupStrLen then
                markupStr
            else
                substring 0 start markupStr
        | TrimBoth ->
            let indexes = indexesOf markupStr trimStr

            markupStr
            |> (fun x ->
                match Seq.isEmpty indexes with
                | true -> x
                | false -> // TODO: This needs changing. I should also be able to composite these functions.
                    let start = indexes |> Seq.head
                    let ed = indexes |> Seq.last
                    substring start (ed - start) x)

    /// <summary>
    /// Repeat a MarkupString a specified number of times, concatenating them to the aggregator.
    /// </summary>
    /// <param name="markupStr">The MarkupString to repeat.</param>
    /// <param name="count">The number of times to repeat.</param>
    /// <param name="aggregator">The initial MarkupString to aggregate into.</param>
    [<TailCall>]
    let rec repeat (markupStr: MarkupString) (count: int) (aggregator: MarkupString) =
        if count <= 0 then
            aggregator
        else
            repeat markupStr (count - 1) (concat aggregator markupStr None)

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

    /// <summary>
    /// Converts a wildcard pattern MarkupString to a regex string.
    /// </summary>
    /// <param name="pattern">The wildcard pattern as a MarkupString.</param>
    let getWildcardMatchAsRegex (pattern: MarkupString) : string =
        let applyRegexPattern (pat: string) =
            pat
            |> fun x -> GlobPatternRegex().TypedReplace(x, konst @"(.*?)")
            |> fun x -> QuestionPatternRegex().TypedReplace(x, konst @"(.)")
            |> fun x -> KindPatternRegex().TypedReplace(x, konst @"\*")
            |> fun x -> KindPattern2Regex().TypedReplace(x, konst @"\?")

        pattern |> plainText |> Regex.Escape |> (fun x -> $"^{x}$") |> applyRegexPattern

    /// <summary>
    /// Determines if the input MarkupString matches the wildcard pattern.
    /// </summary>
    /// <param name="input">The input MarkupString.</param>
    /// <param name="pattern">The wildcard pattern MarkupString.</param>
    let isWildcardMatch (input: MarkupString) (pattern: MarkupString) : bool =
        let newPattern = getWildcardMatchAsRegex pattern
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
            let removed = remove markupStr index trueLength
            insertAt removed replacementStr index
