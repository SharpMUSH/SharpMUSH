namespace MarkupString

open System.Text.Json
open System.Text.RegularExpressions
open System.Runtime.InteropServices
open System
open MarkupString.MarkupImplementation
open System.Text.Json.Serialization
open FSharpPlus
open System.Drawing

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

        let isMarkedup (m: MarkupTypes) =
            match m with
            | MarkedupText _ -> true
            | Empty -> false

        // BUG: This is not correctly matching the first MarkedUp Text
        [<TailCall>]
        let findFirstMarkedupText (markupStr: MarkupString) : MarkupTypes =
            let rec find (content: Content list) : MarkupTypes =
                match content with
                | [] -> Empty
                | MarkupText mStr :: _ when isMarkedup mStr.MarkupDetails -> mStr.MarkupDetails
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

            let firstMarkedupTextType = findFirstMarkedupText ms

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

    let (|MarkupStringPattern|) (markupStr: MarkupString) =
        (markupStr.MarkupDetails, markupStr.Content)

    let markupSingle (markupDetails: Markup, str: string) : MarkupString =
        MarkupString(MarkedupText markupDetails, [ Text str ])

    let markupSingle2 (markupDetails: Markup, mu: MarkupString) : MarkupString =
        MarkupString(MarkedupText markupDetails, [ MarkupText mu ])

    let markupMultiple (markupDetails: Markup, mu: seq<MarkupString>) : MarkupString =
        MarkupString(MarkedupText markupDetails, mu |> Seq.map MarkupText |> Seq.toList)

    let single (str: string) : MarkupString = MarkupString(Empty, [ Text str ])

    let multiple (mu: seq<MarkupString>) : MarkupString =
        MarkupString(Empty, mu |> Seq.map MarkupText |> Seq.toList)

    let empty () : MarkupString =
        MarkupString(Empty, [ Text String.Empty ])

    let multipleWithDelimiter (delimiter: MarkupString) (mu: MarkupString seq) : MarkupString =
        mu |> Seq.intersperse delimiter |> multiple

    let intersperseFunc sepFunc list =
        seq {
            for i, element in list |> Seq.indexed do
                if i > 0 then
                    yield sepFunc (i)

                yield element
        }

    let multipleWithDelimiterFunc (delimiterFunc: int -> MarkupString) (mu: MarkupString seq) : MarkupString =
        mu |> intersperseFunc delimiterFunc |> multiple

    let serializationOptions =
        let serializeOption = JsonFSharpOptions.Default().ToJsonSerializerOptions()
        serializeOption.Converters.Add(ColorJsonConverter())
        serializeOption

    let serialize (markupStr: MarkupString) : string =
        JsonSerializer.Serialize(markupStr, serializationOptions)

    let deserialize (markupString: string) : MarkupString =
        if markupString.Length = 0 then
            empty ()
        else
            JsonSerializer.Deserialize(markupString, serializationOptions)

    [<TailCall>]
    let rec plainText (markupStr: MarkupString) : string = markupStr.ToPlainText()

    let plainText2 (markupStr: MarkupString) : MarkupString =
        MarkupString(Empty, [ Text(markupStr.ToPlainText()) ])

    [<TailCall>]
    let rec getLength (markupStr: MarkupString) : int = markupStr.Length

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

    [<TailCall>]
    let rec indexOf (markupStr: MarkupString) (search: MarkupString) : int =
        let matches = indexesOf markupStr search

        match matches with
        | _ when Seq.isEmpty matches -> -1
        | _ -> matches |> Seq.head

    [<TailCall>]
    let rec indexOfLast (markupStr: MarkupString) (search: MarkupString) : int =
        let matches = indexesOf markupStr search

        match matches with
        | _ when Seq.isEmpty matches -> -1
        | _ -> matches |> Seq.last

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

    let split2 (delimiter: MarkupString) (markupStr: MarkupString) = split (plainText delimiter) markupStr

    [<TailCall>]
    let rec apply (str: MarkupString) (transform: string -> string) : MarkupString =
        let rec mapContent content =
            content
            |> List.map (function
                | Text s -> Text (transform s)
                | MarkupText m -> MarkupText (apply m transform))
        MarkupString(str.MarkupDetails, mapContent str.Content)
    
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

    [<TailCall>]
    let rec repeat (markupStr: MarkupString) (count: int) (aggregator: MarkupString) =
        if count <= 0 then
            aggregator
        else
            repeat markupStr (count - 1) (concat aggregator markupStr None)

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

        if lengthToPad <= 0 then
            match truncType with
            | Overflow -> markupStr
            | Truncate -> substring 0 lengthToPad markupStr
        else
            match padType, truncType with
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

    let getWildcardMatchAsRegex (pattern: MarkupString) : string =
        let applyRegexPattern (pat: string) =
            pat
            |> fun x -> GlobPatternRegex().TypedReplace(x, konst @"(.*?)")
            |> fun x -> QuestionPatternRegex().TypedReplace(x, konst @"(.)")
            |> fun x -> KindPatternRegex().TypedReplace(x, konst @"\*")
            |> fun x -> KindPattern2Regex().TypedReplace(x, konst @"\?")

        pattern |> plainText |> Regex.Escape |> (fun x -> $"^{x}$") |> applyRegexPattern

    let isWildcardMatch (input: MarkupString) (pattern: MarkupString) : bool =
        let newPattern = getWildcardMatchAsRegex pattern
        (plainText input, newPattern) |> Regex.IsMatch

    let getMatches (input: MarkupString) (pattern: string) : (Match * MarkupString seq) seq =
        let captureToString (captureGroup: Group) =
            substring captureGroup.Index captureGroup.Length input

        let allMatches (mtch: Match) =
            (mtch, mtch.Groups |> Seq.map captureToString)

        ((plainText input), pattern)
        |> Regex.Matches
        |> Seq.cast<Match>
        |> Seq.map allMatches

    let getRegexpMatches (input: MarkupString) (pattern: MarkupString) : (Match * MarkupString seq) seq =
        getMatches input (plainText pattern)

    let getWildcardMatches (input: MarkupString) (pattern: MarkupString) : (Match * MarkupString seq) seq =
        getMatches input (getWildcardMatchAsRegex pattern)

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

type ColumnSpec =
    { Width: int
      Justification: Justification
      Options: string
      // TODO: Turn string into Markup
      Ansi: string }

module ColumnSpec =
    let regex = Regex(@"^([<>=_])?(\d+)([\.`'$xX#]*)(?:\((.+)\))?$")

    let parse (spec: string) : ColumnSpec =
        let matchResult = regex.Match(spec)

        if not matchResult.Success then
            raise (ArgumentException $"Invalid column specification: %s{spec}")

        let justification =
            if matchResult.Groups[1].Success then
                match matchResult.Groups[1].Value with
                | "<" -> Justification.Left
                | "=" -> Justification.Paragraph
                | ">" -> Justification.Right
                | "_" -> Justification.Full
                | "-" -> Justification.Center
                | _ -> Justification.Left
            else
                Justification.Left

        let width = int matchResult.Groups[2].Value

        let options =
            if matchResult.Groups[3].Success then
                matchResult.Groups[3].Value
            else
                String.Empty

        let ansi =
            if matchResult.Groups[5].Success then
                matchResult.Groups[5].Value
            else
                String.Empty

        { Width = width
          Justification = justification
          Options = options
          Ansi = ansi }

    let parseList (spec: string) : ColumnSpec list = spec.Split(' ') |> map parse |> toList

module TextAligner =
    let justify (justification: Justification) (text: MarkupString) (width: int) (fill: MarkupString) : MarkupString =
        match justification with
        | Justification.Left -> pad text fill width PadType.Right TruncationType.Truncate
        | Justification.Center -> pad text fill width PadType.Center TruncationType.Truncate
        | Justification.Full -> pad text fill width PadType.Full TruncationType.Truncate
        | Justification.Right
        | Justification.Paragraph -> pad text fill width PadType.Left TruncationType.Truncate

    let moreToDo (column: (ColumnSpec * MarkupString) seq) : bool =
        column |> Seq.exists (fun (x, y) -> y.Length > 0)

    let rec alignFun
        (columns: (ColumnSpec * MarkupString) seq)
        (filler: MarkupString)
        (columnSeparator: MarkupString option)
        (rowSeparator: MarkupString)
        (agg: MarkupString seq)
        : MarkupString =

        if not (moreToDo columns) then
            multipleWithDelimiter rowSeparator agg
        else
            let newLine =
                ((single " "), columns)
                ||> Seq.fold (fun acc (_, y) -> concat acc y columnSeparator)
            // TODO: Need to consume from the Columns
            // TODO: Implement actual logic.
            // Right now this is an infinite recursion loop.
            alignFun columns filler columnSeparator rowSeparator (Seq.append agg [| newLine |])

    let align
        (widths: string)
        (columns: MarkupString list)
        (filler: MarkupString)
        (columnSeparator: MarkupString)
        (rowSeparator: MarkupString)
        : MarkupString =

        let columnSpecs = ColumnSpec.parseList widths

        if columnSpecs.Length <> columns.Length then
            empty () // TODO: Return a better error.
        elif filler.Length > 0 then
            single "Filler is too long."
        elif columnSeparator.Length > 0 then
            single "columnSeparator is too long."
        elif rowSeparator.Length > 0 then
            single "rowSeparator is too long."
        else
            alignFun (Seq.zip columnSpecs columns) filler (Some columnSeparator) rowSeparator [||]
