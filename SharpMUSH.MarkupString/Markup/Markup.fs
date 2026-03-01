namespace MarkupString

open System.Runtime.InteropServices
open System.Text.RegularExpressions
open ANSILibrary.ANSI
open System.Text.Json.Serialization

module MarkupImplementation =
  open ANSILibrary
  
  [<Struct>]
  type AnsiStructure =
    {
      Foreground: AnsiColor;
      Background: AnsiColor;
      LinkText: string option;
      LinkUrl: string option;
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
    
  [<Struct>]
  type HtmlStructure =
    {
      TagName: string;
      Attributes: string option;
    }

  [<JsonDerivedType(typeof<NeutralMarkup>, "Neutral")>]
  [<JsonDerivedType(typeof<AnsiMarkup>, "Ansi")>]
  [<JsonDerivedType(typeof<HtmlMarkup>, "Html")>]
  type Markup =
      abstract member Wrap: string -> string
      abstract member WrapAndRestore: string * Markup -> string
      abstract member WrapAs: string * string -> string
      abstract member WrapAndRestoreAs: string * string * Markup -> string
      abstract member Prefix: string
      abstract member Postfix: string
      abstract member Optimize: string -> string
    
  and NeutralMarkup() =
    interface Markup with
      member this.Postfix: string = System.String.Empty
      member this.Prefix: string = System.String.Empty
      member this.Wrap(text: string): string = text
      member this.WrapAndRestore(text: string, _: Markup): string = text
      member this.WrapAs(_format: string, text: string): string = text
      member this.WrapAndRestoreAs(_format: string, text: string, _: Markup): string = text
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
        LinkText = defaultArg linkText (Some System.String.Empty)
        LinkUrl = defaultArg linkUrl (Some System.String.Empty)
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

    /// Returns the CSS class names that represent this AnsiStructure in HTML rendering.
    /// Color classes are prefixed with "fg-" or "bg-" followed by a 6-digit hex color.
    /// Fixed formatting classes are prefixed with "ms-".
    static member HtmlClassNames (details: AnsiStructure) : string list =
      let fg, bg =
        if details.Inverted then details.Background, details.Foreground
        else details.Foreground, details.Background

      let colorClass (prefix: string) (color: AnsiColor) =
        match color with
        | NoAnsi -> None
        | RGB c -> Some (sprintf "%s-%02x%02x%02x" prefix c.R c.G c.B)
        | ANSI bytes ->
          let rgb = AnsiToRgb bytes
          Some (sprintf "%s-%02x%02x%02x" prefix rgb.R rgb.G rgb.B)

      [
        match colorClass "fg" fg with Some cls -> yield cls | None -> ()
        match colorClass "bg" bg with Some cls -> yield cls | None -> ()
        if details.Bold then yield "ms-bold"
        if details.Faint then yield "ms-faint"
        if details.Italic then yield "ms-italic"
        if details.Underlined then yield "ms-underline"
        if details.StrikeThrough then yield "ms-strike"
        if details.Overlined then yield "ms-overline"
        if details.Blink then yield "ms-blink"
      ]

    /// Renders an AnsiStructure as an HTML span using CSS classes.
    /// The <paramref name="text"/> parameter is expected to already be HTML-entity-encoded
    /// by the caller (getTextAs applies HtmlEncode to all Text leaf nodes before wrapping).
    static member private wrapAsHtmlClass (details: AnsiStructure) (text: string) : string =
      let classes = AnsiMarkup.HtmlClassNames details

      let inner =
        match details.LinkUrl with
        | Some url when url.Length > 0 ->
          sprintf "<a href=\"%s\">%s</a>" (System.Net.WebUtility.HtmlEncode url) text
        | _ -> text

      if classes.IsEmpty then inner
      else sprintf "<span class=\"%s\">%s</span>" (String.concat " " classes) inner

    static member applyDetails (details: AnsiStructure) (text: string) =
        StringExtensions.toANSI text
        |> (fun t -> match details.LinkUrl with | None -> t | Some url -> if url.Length <> 0 then StringExtensions.linkANSI t url else t)
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

      // ANSI optimization moved to ANSILibrary.Optimization module
      [<TailCall>]
      override this.Optimize (text: string) : string =
        ANSILibrary.Optimization.optimize text

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
            | :? HtmlMarkup -> StringExtensions.toANSI System.String.Empty // HTML tags don't need restoration codes
            | _ -> raise (System.Exception "Unknown markup type")
        (AnsiMarkup.applyDetails details text).ToString() + restoreDetailsF(outerDetails).ToString()

      override this.Wrap (text: string) : string =
        StringExtensions.endWithTrueClear((AnsiMarkup.applyDetails details text).ToString()).ToString()

      override this.WrapAs(format: string, text: string) : string =
        match format.ToLower() with
        | "html" -> AnsiMarkup.wrapAsHtmlClass details text
        | _ -> (this :> Markup).Wrap(text)

      override this.WrapAndRestoreAs(format: string, text: string, outerDetails: Markup) : string =
        match format.ToLower() with
        | "html" -> AnsiMarkup.wrapAsHtmlClass details text
        | _ -> (this :> Markup).WrapAndRestore(text, outerDetails)

  and HtmlMarkup(details: HtmlStructure) =
    member val Details = details with get
    
    static member Create(tagName: string, [<Optional;DefaultParameterValue(null)>]?attributes) =
      {
        TagName = tagName
        Attributes = defaultArg attributes None
      }
      |> HtmlMarkup

    interface Markup with
      // For HTML, we use empty prefix/postfix since Wrap handles everything
      override this.Postfix: string = System.String.Empty

      override this.Prefix: string = System.String.Empty

      override this.Wrap (text: string) : string =
        // Wrap includes the full HTML tags
        match details.Attributes with
        | None -> sprintf "<%s>%s</%s>" details.TagName text details.TagName
        | Some attrs -> sprintf "<%s %s>%s</%s>" details.TagName attrs text details.TagName

      override this.WrapAndRestore (text: string, outerDetails: Markup) : string =
        // For HTML, we just wrap without restoring outer markup since HTML tags are independent
        (this :> Markup).Wrap(text)

      override this.WrapAs(_format: string, text: string) : string =
        (this :> Markup).Wrap(text)

      override this.WrapAndRestoreAs(_format: string, text: string, _: Markup) : string =
        (this :> Markup).Wrap(text)

      override this.Optimize (text: string) : string = text