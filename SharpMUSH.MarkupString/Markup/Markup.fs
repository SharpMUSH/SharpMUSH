namespace MarkupString

open System.Runtime.InteropServices

module MarkupImplementation =
  open ANSILibrary

  type Markup =
      abstract member Wrap: string -> string
    
  type NeutralMarkup() =
    interface Markup with
      override this.Wrap text = text

  [<Struct>]
  type AnsiStructure =
    {
      Foreground: string;
      Background: string;
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

  type AnsiMarkup(details: AnsiStructure) =
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
        Foreground = defaultArg foreground null
        Background = defaultArg background null
        LinkText = defaultArg linkText null
        LinkUrl = defaultArg linkUrl null
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

    interface Markup with
      override this.Wrap (text: string) : string =
        let applyDetails (details: AnsiStructure) (text: string) =
          StringExtensions.toANSI text
          |> (fun t -> match details.LinkUrl with | null -> t | url -> StringExtensions.linkANSI t url)
          |> (fun t -> match details.Foreground with | null -> t | fg -> StringExtensions.colorString t fg)
          |> (fun t -> match details.Background with | null -> t | bg -> StringExtensions.backgroundString t bg)
          |> (fun t -> if details.Blink then StringExtensions.blinkANSI(t) else t)
          |> (fun t -> if details.Bold then StringExtensions.boldANSI(t) else t)
          |> (fun t -> if details.Faint then StringExtensions.faintANSI(t) else t)
          |> (fun t -> if details.Italic then StringExtensions.italicANSI(t) else t)
          |> (fun t -> if details.Overlined then StringExtensions.overlinedANSI(t) else t)
          |> (fun t -> if details.Underlined then StringExtensions.underlinedANSI(t) else t)
          |> (fun t -> if details.StrikeThrough then StringExtensions.strikeThroughANSI(t) else t)
          |> (fun t -> if details.Inverted then StringExtensions.invertedANSI(t) else t)
          |> (fun t -> if details.Clear then StringExtensions.clearANSI(t) else t)

        (applyDetails details text).ToString()
