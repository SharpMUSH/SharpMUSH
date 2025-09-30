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

open MarkupStringModule

type Justification =
    | Left
    | Center
    | Right
    | Full
    | Paragraph

[<Flags>]
type ColumnOptions =
    | Default = 0
    | Repeat = 1
    | MergeToLeft = 2
    | MergeToRight = 4
    | NoFill = 8
    | Truncate = 16
    | TruncateV2 = 32
    | NoColSep = 64

type ColumnSpec =
    { Width: int
      Justification: Justification
      Options: ColumnOptions
      // TODO: Turn string into Markup
      Ansi: string }



/// <summary>
/// Functions for parsing and handling column specifications for text alignment.
/// </summary>
module ColumnSpec =
    type private WidthPatternRegex = FSharp.Text.RegexProvider.Regex< @"^([<>=_\-])?(\d+)([\.`'$xX#]*)(?:\((.+)\))?$" >

    /// <summary>
    /// Parses a column specification string into a ColumnSpec record.
    /// </summary>
    /// <param name="spec">The column specification string.</param>
    let parse (spec: string) : ColumnSpec =
        let matchResult = WidthPatternRegex().Match(spec)

        if not matchResult.Success then
            raise (ArgumentException $"Invalid column specification: %s{spec}")

        let justification =
            if not matchResult.Groups[1].Success then
                Justification.Left
            else
                match matchResult.Groups[1].Value with
                | "<" -> Justification.Left
                | "=" -> Justification.Paragraph
                | ">" -> Justification.Right
                | "_" -> Justification.Full
                | "-" -> Justification.Center
                | _ -> Justification.Left

        let options =
            if not matchResult.Groups[3].Success then
                ColumnOptions.Default
            else
                let optionStr = matchResult.Groups[3].Value

                optionStr
                |> Seq.fold
                    (fun result c ->
                        let flag =
                            match c with
                            | '.' -> ColumnOptions.Repeat
                            | '`' -> ColumnOptions.MergeToLeft
                            | '\'' -> ColumnOptions.MergeToRight
                            | '$' -> ColumnOptions.NoFill
                            | 'x' -> ColumnOptions.Truncate
                            | 'X' -> ColumnOptions.TruncateV2
                            | '#' -> ColumnOptions.NoColSep
                            | _ -> ColumnOptions.Default

                        if flag <> ColumnOptions.Default then
                            result ||| flag
                        else
                            result)
                    ColumnOptions.Default

        let width = int matchResult.Groups[2].Value

        let ansi =
            if not matchResult.Groups[5].Success then
                String.Empty
            else
                matchResult.Groups[5].Value

        { Width = width
          Justification = justification
          Options = options
          Ansi = ansi }

    /// <summary>
    /// Parses a space-separated list of column specifications.
    /// </summary>
    /// <param name="spec">The space-separated column specification string.</param>
    let parseList (spec: string) : ColumnSpec list = spec.Split(' ') |> map parse |> toList

/// <summary>
/// Provides functions for aligning and justifying text using MarkupString.
/// </summary>
module TextAligner =
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
    let private mergeColumnLeft (columns: seq<ColumnState>) (index: int) (spec: ColumnSpec) : seq<ColumnState> =
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
    let private mergeColumnRight (columns: seq<ColumnState>) (index: int) (spec: ColumnSpec) : seq<ColumnState> =
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
    let private handleMerging (columns: seq<ColumnState>) (index: int) : seq<ColumnState> =
        let spec, text = Seq.item index columns

        match text.Length, spec.Options with
        | 0, opts when opts.HasFlag(ColumnOptions.MergeToLeft) && index > 0 -> mergeColumnLeft columns index spec
        | 0, opts when opts.HasFlag(ColumnOptions.MergeToRight) && index < Seq.length columns - 1 ->
            mergeColumnRight columns index spec
        | _ -> columns

    /// <summary>
    /// Determines if there is more text to process in any column.
    /// </summary>
    let private moreToDo (columns: seq<ColumnState>) : bool =
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
        (lineResults: seq<LineResult>)
        (columnSeparator: MarkupString)
        (filler: MarkupString)
        : seq<MarkupString> =
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
        (columns: seq<ColumnState>)
        (filler: MarkupString)
        (columnSeparator: MarkupString)
        : seq<ColumnState> * MarkupString =

        let mergedColumns =
            Seq.fold (fun cols i -> handleMerging cols i) columns [ 0 .. Seq.length columns - 1 ]

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
        (columns: seq<ColumnState>)
        (filler: MarkupString)
        (columnSeparator: MarkupString)
        (rowSeparator: MarkupString)
        (accumulator: seq<MarkupString>)
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
        (columnSpecs: ColumnSpec list)
        (columns: MarkupString list)
        (filler: MarkupString)
        : Result<unit, string> =

        if columnSpecs.Length <> columns.Length then
            Error "#-1 COLUMN COUNT MISMATCH"
        elif filler.Length > 1 then
            Error "#-1 FILLER MUST BE ONE CHARACTER"
        elif columnSpecs |> List.exists (fun spec -> spec.Width <= 0) then
            Error "#-1 CANNOT HAVE COLUMNS OF NEGATIVE SIZE"
        elif columnSpecs |> List.exists (fun spec -> spec.Width > 5_000_000) then
            Error "#-1 CANNOT HAVE COLUMNS THAT LARGE"
        elif
            columnSpecs |> List.exists (fun spec ->
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
        (columns: MarkupString list)
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
