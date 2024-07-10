namespace MarkupString

open System.Runtime.InteropServices

module MarkupStringModule =
  open MarkupImplementation

  type Content =
      | Text of string
      | MarkupText of MarkupString

  and MarkupTypes = // TODO: Consider using built-in option type.
      | MarkedupText of Markup
      | Empty

  and MarkupString(markupDetails: MarkupTypes, content: List<Content>) =
      member val MarkupDetails = markupDetails with get, set
      member val Content = content with get, set

      with override this.ToString() = 
            let isMarkedup (m : MarkupTypes) =
              match m with
              | MarkedupText _ -> true
              | Empty -> false

            let findFirstMarkedupText (markupStr: MarkupString) : MarkupTypes =
                let rec find (content: List<Content>) : MarkupTypes =
                    match content with
                    | [] -> Empty
                    | MarkupText mStr :: _ when isMarkedup(mStr.MarkupDetails) -> mStr.MarkupDetails
                    | _ :: tail -> find tail
                match markupStr.MarkupDetails with
                | MarkedupText m -> markupStr.MarkupDetails
                | _ -> find markupStr.Content

            let postfix(markupType: MarkupTypes) : string = 
              match markupType with
                  | MarkedupText markup -> markup.Postfix
                  | Empty -> System.String.Empty

            let prefix(markupType: MarkupTypes) : string = 
              match markupType with
                  | MarkedupText markup -> markup.Prefix
                  | Empty -> System.String.Empty

            let rec getText (markupStr: MarkupString, outerMarkupType: MarkupTypes) : string =
                let innerText = (markupStr.Content |> List.fold (fun acc item ->
                    match item with
                    | Text str -> acc + str
                    | MarkupText mStr -> 
                      match markupStr.MarkupDetails with
                        | Empty -> acc + getText(mStr, outerMarkupType)
                        | MarkedupText _ -> acc + getText(mStr, markupStr.MarkupDetails)
                ) System.String.Empty)
                match markupStr.MarkupDetails with
                  | Empty -> innerText
                  | MarkedupText str -> 
                    match outerMarkupType with
                      | Empty -> str.Wrap(innerText)
                      | MarkedupText outerMarkup -> str.WrapAndRestore(innerText, outerMarkup)
            
            let firstMarkedupTextType = findFirstMarkedupText this
            prefix(firstMarkedupTextType) + getText(this, Empty) + postfix(firstMarkedupTextType)

  let (|MarkupStringPattern|) (markupStr: MarkupString) =
        (markupStr.MarkupDetails, markupStr.Content)

  let markupSingle (markupDetails: Markup, str: string) : MarkupString = 
      MarkupString(MarkedupText markupDetails, [Text str])
      
  let markupSingle2 (markupDetails: Markup, mu: MarkupString) : MarkupString = 
      MarkupString(MarkedupText markupDetails, [MarkupText mu])
      
  let markupMultiple (markupDetails: Markup, mu: seq<MarkupString>) : MarkupString = 
      MarkupString(MarkedupText markupDetails, mu |> Seq.map (fun x -> MarkupText x) |> Seq.toList )
    
  let single (str: string) : MarkupString = 
      MarkupString(Empty, [Text str])

  let multiple (mu: seq<MarkupString>) : MarkupString = 
      MarkupString(Empty, mu |> Seq.map (fun x -> MarkupText x) |> Seq.toList )

  let empty () : MarkupString = 
      MarkupString(Empty, [Text System.String.Empty])
      
  let rec plainText (markupStr: MarkupString) : string =
      let innerText = (markupStr.Content |> List.fold (fun acc item ->
              match item with
              | Text str -> acc + str
              | MarkupText mStr -> acc + plainText mStr
          ) System.String.Empty)
      innerText
      
  [<TailCall>]
  let rec getLength (markupStr: MarkupString) : int =
      markupStr.Content |> List.fold (fun acc item ->
          acc + (match item with
                | Text str -> str.Length
                | MarkupText mStr -> getLength mStr)) 0
                
  let concat (originalMarkupStr: MarkupString) 
             (newMarkupStr: MarkupString) 
             ([<Optional;DefaultParameterValue(null)>]optionalSeparator: MarkupString option) : MarkupString =
    let separatorContent = 
        match optionalSeparator with
        | Some separator -> [MarkupText separator]
        | None -> [Text System.String.Empty]

    match originalMarkupStr.MarkupDetails with
    | Empty ->
        let combinedContent = originalMarkupStr.Content @ separatorContent @ [MarkupText newMarkupStr]
        MarkupString(Empty, combinedContent)
    | _ ->
        let combinedContent = [MarkupText originalMarkupStr] @ separatorContent @ [MarkupText newMarkupStr]
        MarkupString(Empty, combinedContent)

  [<TailCall>]
  let rec substring (start, length) (markupStr: MarkupString) : MarkupString =
      let inline extractText str start length =
          if length <= 0 || str = System.String.Empty then None
          else Some(str.Substring(start, min (str.Length - start) length))

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
            | Text str when start >= str.Length -> 
                substringAux tail (start - str.Length) length acc
            | MarkupText markupStr ->
                let strLen = getLength markupStr
                if start < strLen then
                    let skip = max start 0
                    let take = min strLen length
                    let subMarkup = substring (skip, take) markupStr
                    let subLength = getLength subMarkup
                    if subLength > 0 then
                        substringAux tail (0) (length - subLength) (MarkupText subMarkup :: acc)
                    else
                        substringAux tail (start - strLen) length acc
                else
                    substringAux tail (start - strLen) length acc
            | _ -> raise (System.InvalidOperationException "Encountered unexpected content type in substring operation.")
      MarkupString(markupStr.MarkupDetails, substringAux markupStr.Content start length [])

  // TODO: IndexesOf, which will no doubt also affect split(), I believe fails if a searched item crosses two MarkupStrings.
  [<TailCall>]
  let indexesOf (markupStr: MarkupString, search: MarkupString) : seq<int> =
    let text = plainText markupStr
    let srch = plainText search

    let rec findDelimiters pos =
        seq {
            if pos < text.Length then
                let foundPos = text.IndexOf(srch, pos)
                if foundPos <> -1 then
                    yield foundPos
                    if srch <> System.String.Empty then
                        yield! findDelimiters (foundPos + srch.Length)
                    else
                        yield! findDelimiters (foundPos + 1)
        }

    findDelimiters 0
    
  [<TailCall>]
  let rec indexOf (markupStr: MarkupString, search: MarkupString) : int =
    let matches = indexesOf (markupStr, search) 
    match matches with 
      | _ when Seq.isEmpty matches -> -1
      | _ -> matches |> Seq.head

  [<TailCall>]
  let rec split (delimiter: string) (markupStr: MarkupString) : MarkupString[] =
    let rec findDelimiters (text: string) (pos: int) =
        if pos >= text.Length then []
        else
            match text.IndexOf(delimiter, pos) with
            | -1 -> []
            | idx -> idx :: if(delimiter <> System.String.Empty) 
                              then findDelimiters text (idx + delimiter.Length) 
                              else findDelimiters text (idx + 1) 

    let fullText = plainText markupStr

    let delimiterPositions = findDelimiters fullText 0

    let rec buildSplits positions lastPos segments =
        match positions with
        | [] ->
            let lastSegment = substring (lastPos, fullText.Length - lastPos) markupStr
            List.rev (lastSegment :: segments)
        | pos :: tail ->
            let length = pos - lastPos
            let segment = substring (lastPos, length) markupStr
            buildSplits tail (pos + delimiter.Length) (segment :: segments)

    buildSplits delimiterPositions 0 [] |> Array.ofList

  // Align code starts here. 
  // There's some errors in here assuredly. For instance, alignOptions should be per- width.
  type Justification = Left | Center | Right | Full | Paragraph

  type ColumnOptions = {
      Width: int
      Justification: Justification
      NoFill: bool
      TruncateRow: bool
      TruncateColumn: bool
      NoColSepAfter: bool
  }

  type AlignOptions = {
      Filler: char
      ColSep: string
      RowSep: string
  }

  let defaultAlignOptions = { Filler = ' '; ColSep = " "; RowSep = "\n" }

  let align (columns: MarkupString list) (widths: int list) (alignOptions: AlignOptions) : MarkupString =
    let justifyText (text: string) (width: int) (justification: Justification) : string =
        match justification with
        | Left -> text.PadRight(width)
        | Center -> text.PadLeft((width + text.Length) / 2).PadRight(width)
        | Right -> text.PadLeft(width)
        | _ -> text // Full and Paragraph justifications can be implemented as needed

    let formatColumn (content: MarkupString) (options: ColumnOptions) : MarkupString =
        // This function should format the content of a single column based on the provided options.
        // For simplicity, only basic text content is considered here.
        let formattedText = justifyText (plainText content) options.Width options.Justification
        single (formattedText) // Assuming markupSingle creates a MarkupString with the specified text

    let optionsList = 
        List.map2 (fun width _ -> 
            { Width = width; Justification = Left; NoFill = false; TruncateRow = false; TruncateColumn = false; NoColSepAfter = false }
        ) widths columns

    let formattedColumns = List.map2 formatColumn columns optionsList

    // Combine the formatted column contents, inserting column separators as needed.
    let fullContent = 
        formattedColumns
        |> List.collect (fun col -> col.Content @ [Text alignOptions.ColSep]) // Collect is used to flatten and concatenate the lists
        |> List.rev 
        |> List.tail 
        |> List.rev  // Removing the last separator added by the above process

    MarkupString(Empty, fullContent)