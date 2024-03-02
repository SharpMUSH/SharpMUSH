﻿namespace MarkupString

module MarkupStringModule =
  open MarkupImplementation

  let initialize() =
    ANSIConsole.ANSIInitializer.Enabled <- true
    ANSIConsole.ANSIInitializer.Init false |> ignore
    
  type Content =
      | Text of string
      | MarkupText of MarkupString

  and MarkupTypes =
      | MarkedupText of Markup
      | Empty

  and MarkupString(markupDetails: MarkupTypes, content: List<Content>) =
      member val MarkupDetails = markupDetails with get, set
      member val Content = content with get, set

      with override this.ToString() = 
            let rec getText (markupStr: MarkupString) : string =
                let innerText = (markupStr.Content |> List.fold (fun acc item ->
                    match item with
                    | Text str -> acc + str
                    | MarkupText mStr -> acc + getText mStr
                ) "")
                match markupStr.MarkupDetails with
                  | Empty -> innerText
                  | MarkedupText str -> str.Wrap(innerText)
            getText(this)

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
      MarkupString(Empty, [Text ""])
      
  let rec plainText (markupStr: MarkupString) : string =
      let innerText = (markupStr.Content |> List.fold (fun acc item ->
              match item with
              | Text str -> acc + str
              | MarkupText mStr -> acc + plainText mStr
          ) "")
      innerText
      
  [<TailCall>]
  let rec getLength (markupStr: MarkupString) : int =
      markupStr.Content |> List.fold (fun acc item ->
          acc + (match item with
                | Text str -> str.Length
                | MarkupText mStr -> getLength mStr)) 0
                
  let concat (originalMarkupStr: MarkupString) (newMarkupStr: MarkupString) : MarkupString =
    match originalMarkupStr.MarkupDetails with
    | Empty ->
        let combinedContent = originalMarkupStr.Content @ [MarkupText newMarkupStr]
        MarkupString(Empty, combinedContent)
    | _ ->
        let combinedContent = [MarkupText originalMarkupStr; MarkupText newMarkupStr]
        MarkupString(Empty, combinedContent)
  
  // TODO: Merge with concat and take an optional separator.
  let concat2 (originalMarkupStr: MarkupString, separator: MarkupString) (newMarkupStr: MarkupString) : MarkupString =
    match originalMarkupStr.MarkupDetails with
    | Empty ->
        let combinedContent = originalMarkupStr.Content @ [MarkupText separator] @ [MarkupText newMarkupStr]
        MarkupString(Empty, combinedContent)
    | _ ->
        let combinedContent = [MarkupText originalMarkupStr; MarkupText separator; MarkupText newMarkupStr]
        MarkupString(Empty, combinedContent)

  [<TailCall>]
  let rec substring (start, length) (markupStr: MarkupString) : MarkupString =
      let inline extractText str start length =
          if length <= 0 || str = "" then None
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
                    if srch <> "" then
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
            | idx -> idx :: if(delimiter <> "") 
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