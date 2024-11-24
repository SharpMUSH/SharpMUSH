namespace MarkupString

open System.Runtime.InteropServices
open ANSILibrary.ANSI
open System.Text.Json.Serialization

module MarkupImplementation =
  open ANSILibrary
  
  [<Struct>]
  type AnsiStructure =
    {
      Foreground: AnsiColor;
      Background: AnsiColor;
      LinkText: string;
      LinkUrl: string;
      Blink: bool;
      Bold: bool;
      Clear: bool;
      Faint: bool;
      Inverted: bool;
      Italic: bool;
      Overlined: bool;
      Underlined: bool;
      StrikeThrough: bool;
    }
    
  [<JsonDerivedType(typeof<NeutralMarkup>, "Neutral")>]
  [<JsonDerivedType(typeof<AnsiMarkup>, "Ansi")>]
  type Markup =
      abstract member Wrap: string -> string
      abstract member WrapAndRestore: string * Markup -> string
      abstract member Prefix: string
      abstract member Postfix: string
      abstract member Optimize: string -> string
    
  and NeutralMarkup() =
    interface Markup with
      member this.Postfix: string = System.String.Empty
      member this.Prefix: string = System.String.Empty
      member this.Wrap(text: string): string = text
      member this.WrapAndRestore(text: string, _: Markup): string = text
      member this.Optimize(text: string): string = text

  and AnsiMarkup(details: AnsiStructure) =
    member val Details = details with get
    
    static member Create(
        [<Optional;DefaultParameterValue(null)>]?foreground, 
        [<Optional;DefaultParameterValue(null)>]?background, 
        [<Optional;DefaultParameterValue(null)>]?linkText, 
        [<Optional;DefaultParameterValue(null)>]?linkUrl, 
        [<Optional;DefaultParameterValue(false)>]?blink, 
        [<Optional;DefaultParameterValue(false)>]?bold, 
        [<Optional;DefaultParameterValue(false)>]?clear, 
        [<Optional;DefaultParameterValue(false)>]?faint, 
        [<Optional;DefaultParameterValue(false)>]?inverted, 
        [<Optional;DefaultParameterValue(false)>]?italic, 
        [<Optional;DefaultParameterValue(false)>]?overlined, 
        [<Optional;DefaultParameterValue(false)>]?underlined, 
        [<Optional;DefaultParameterValue(false)>]?strikeThrough) =
      {
        Foreground = defaultArg foreground AnsiColor.NoAnsi
        Background = defaultArg background AnsiColor.NoAnsi
        LinkText = defaultArg linkText System.String.Empty
        LinkUrl = defaultArg linkUrl System.String.Empty
        Blink = defaultArg blink false
        Bold = defaultArg bold false
        Clear = defaultArg clear false
        Faint = defaultArg faint false
        Inverted = defaultArg inverted false
        Italic = defaultArg italic false
        Overlined = defaultArg overlined false
        Underlined = defaultArg underlined false
        StrikeThrough = defaultArg strikeThrough false
      }
      |> AnsiMarkup

    static member applyDetails (details: AnsiStructure) (text: string) =
        StringExtensions.toANSI text
        |> (fun t -> match details.LinkUrl with | null -> t | url -> if url.Length <> 0 then StringExtensions.linkANSI t url else t)
        |> (fun t -> match details.Foreground with | AnsiColor.NoAnsi -> t | fg -> StringExtensions.colorANSI t fg)
        |> (fun t -> match details.Background with | AnsiColor.NoAnsi -> t | bg -> StringExtensions.backgroundANSI t bg)
        |> (fun t -> if details.Blink then StringExtensions.blinkANSI(t) else t)
        |> (fun t -> if details.Bold then StringExtensions.boldANSI(t) else t)
        |> (fun t -> if details.Faint then StringExtensions.faintANSI(t) else t)
        |> (fun t -> if details.Italic then StringExtensions.italicANSI(t) else t)
        |> (fun t -> if details.Overlined then StringExtensions.overlinedANSI(t) else t)
        |> (fun t -> if details.Underlined then StringExtensions.underlinedANSI(t) else t)
        |> (fun t -> if details.StrikeThrough then StringExtensions.strikeThroughANSI(t) else t)
        |> (fun t -> if details.Inverted then StringExtensions.invertedANSI(t) else t)
        |> (fun t -> (if details.Clear then StringExtensions.clearANSI(t) else t))

    interface Markup with
      override this.Postfix: string = StringExtensions.endWithTrueClear(System.String.Empty).ToString()

      override this.Prefix: string = System.String.Empty

      [<TailCall>]
      override this.Optimize (text: string) : string =
        let rec optimizeImpl (acc: string) (currentIndex: int) (currentEscapeCode: string) : string =
            if currentIndex >= acc.Length - 1 then
                acc
            else
                match acc.IndexOf("\u001b[", currentIndex, System.StringComparison.Ordinal) with
                | -1 -> acc
                | escapeCodeStartIndex ->
                    let escapeCodeEndIndex = acc.IndexOf("m", escapeCodeStartIndex, System.StringComparison.Ordinal)
                    if escapeCodeEndIndex = -1 then
                        acc
                    else
                        let escapeCode = acc.Substring(escapeCodeStartIndex, escapeCodeEndIndex - escapeCodeStartIndex + 1)
                        if escapeCode = currentEscapeCode then
                            let updatedText = acc.Remove(escapeCodeStartIndex, escapeCodeEndIndex - escapeCodeStartIndex + 1)
                            optimizeImpl updatedText (escapeCodeStartIndex) currentEscapeCode
                        else
                            optimizeImpl acc (escapeCodeEndIndex + 1) escapeCode
        optimizeImpl text 0 System.String.Empty 

      override this.WrapAndRestore (text: string, outerDetails: Markup) : string =
        let restoreDetailsF (restoreDetails: Markup) =
          match restoreDetails with 
            | :? AnsiMarkup as markup -> 
              if details.Equals markup.Details then
                StringExtensions.toANSI System.String.Empty
              else
                StringExtensions.toANSI System.String.Empty
                |> (fun t -> match markup.Details.Foreground with | AnsiColor.NoAnsi -> t | fg -> StringExtensions.colorANSI t fg)
                |> (fun t -> match markup.Details.Background with | AnsiColor.NoAnsi -> t | bg -> StringExtensions.backgroundANSI t bg)
                |> (fun t -> if markup.Details.Blink then StringExtensions.blinkANSI(t) else t)
                |> (fun t -> if markup.Details.Bold then StringExtensions.boldANSI(t) else t)
                |> (fun t -> if markup.Details.Faint then StringExtensions.faintANSI(t) else t)
                |> (fun t -> if markup.Details.Italic then StringExtensions.italicANSI(t) else t)
                |> (fun t -> if markup.Details.Overlined then StringExtensions.overlinedANSI(t) else t)
                |> (fun t -> if markup.Details.Underlined then StringExtensions.underlinedANSI(t) else t)
                |> (fun t -> if markup.Details.StrikeThrough then StringExtensions.strikeThroughANSI(t) else t)
                |> (fun t -> if markup.Details.Inverted then StringExtensions.invertedANSI(t) else t)
            | _ -> raise (new System.Exception("Unknown markup type"))
        (AnsiMarkup.applyDetails details text).ToString() + restoreDetailsF(outerDetails).ToString()

      override this.Wrap (text: string) : string =
        (AnsiMarkup.applyDetails details text).ToString()