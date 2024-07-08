namespace ANSILibrary 

open System
open System.Drawing

// Converted from: https://github.com/WilliamRagstad/ANSIConsole/blob/main/ANSIConsole/
module ANSI =
  type AnsiColor = 
    | RGB of rgb : Color 
    | ANSI of ansiCode : byte
    | NoAnsi

  /// The ASCII escape character (decimal 27).
  let ESC = "\u001b"

  /// Introduces a control sequence that uses 8-bit characters.
  let CSI = ESC + "["

  let SGR (codes: byte []) =
    CSI + (codes |> Array.map string |> String.concat ";") + "m"

  let Clear = SGR [|0uy|]
  let Bold = SGR [|1uy|]
  let Faint = SGR [|2uy|]
  let Italic = SGR [|3uy|]
  let Underlined = SGR [|4uy|]
  let Blink = SGR [|5uy|]
  let Inverted = SGR [|7uy|]
  let StrikeThrough = SGR [|9uy|]
  let Overlined = SGR [|53uy|]
  
  let Foreground (color: AnsiColor) = 
    match color with 
      | RGB rgb -> SGR [|38uy; 2uy; rgb.R; rgb.G; rgb.B|]
      | ANSI ansi -> SGR [|38uy; 5uy; ansi|]
      | NoAnsi -> ""
  let Background (color: AnsiColor) = 
    match color with 
      | RGB rgb -> SGR [|48uy; 2uy; rgb.R; rgb.G; rgb.B|]
      | ANSI ansi -> SGR [|48uy; 5uy; ansi|]
      | NoAnsi -> ""

  let Hyperlink (text: string, link: string) = sprintf "\u001b]8;;%s\a%s\u001b]8;;\a" link text

open ANSI

[<System.Flags>]
type ANSIFormatting =
    | None = 0
    | Bold = 1          
    | Faint = 2         
    | Italic = 4        
    | Underlined = 8    
    | Overlined = 16    
    | Blink = 32        
    | Inverted = 64     
    | StrikeThrough = 128  
    | LowerCase = 256    
    | UpperCase = 512      
    | Clear = 1024      

type ANSIString(text: string) =
  let mutable _hyperlink: string option = None
  let mutable _colorForeground: AnsiColor option = None
  let mutable _colorBackground: AnsiColor option = None
  let mutable _opacity: float option = None
  let mutable _formatting: ANSIFormatting = ANSIFormatting.Clear
  let _text: string = ""
  
  member internal this.AddFormatting(add: ANSIFormatting) =
      _formatting <- _formatting ||| add
      if _formatting.HasFlag(ANSIFormatting.UpperCase ||| ANSIFormatting.LowerCase) then
          raise (ArgumentException("formatting cannot include both UpperCase and LowerCase!", "_formatting"))
      this

  member internal this.RemoveFormatting(rem: ANSIFormatting) =
      _formatting <- _formatting &&& ~~~rem
      this

  member internal this.SetForegroundColor(color: AnsiColor) =
      _colorForeground <- Some color 
      this
      
  member internal this.SetBackgroundColor(color: AnsiColor) =
      _colorBackground <- Some color
      this

  member internal this.SetOpacity(opacity: float) =
      _opacity <- Some opacity
      this

  member internal this.SetHyperlink(link: string) =
      _hyperlink <- Some link
      this
      
  static member internal Interpolate(fromC: Color, toC: Color, percentage: float) =
    let percentageOfTo = percentage
    let percentageOfFrom = 1.0 - percentage

    let blend (fromComponent: byte) (toComponent: byte) =
        let fromAmount = float fromComponent * percentageOfFrom
        let toAmount = float toComponent * percentageOfTo
        byte (fromAmount + toAmount)

    let r = int (blend fromC.R toC.R)
    let g = int (blend fromC.G toC.G)
    let b = int (blend fromC.B toC.B)
    RGB(Color.FromArgb(r, g, b))

  override this.ToString() =
    let applyFormatting formatting transform result =
        if _formatting.HasFlag(formatting) then transform(result) else result

    let applyColor colorFunc result =
        match _opacity, _colorForeground, _colorBackground with
        | Some o, Some bg, Some fg -> 
          match bg,fg with
            | RGB b, RGB f -> colorFunc(ANSIString.Interpolate(b, f, o)) + result
            | _,_ -> result
        | _, Some cf, _ -> colorFunc(cf) + result
        | _ -> result

    let initialResult = _text
    let resultWithCase =
        initialResult
        |> applyFormatting ANSIFormatting.UpperCase (fun r -> r.ToUpper())
        |> applyFormatting ANSIFormatting.LowerCase (fun r -> r.ToLower())

    let resultWithFormatting =
        resultWithCase
        |> applyFormatting ANSIFormatting.Bold (fun r -> ANSI.Bold + r)
        |> applyFormatting ANSIFormatting.Faint (fun r -> ANSI.Faint + r)
        |> applyFormatting ANSIFormatting.Italic (fun r -> ANSI.Italic + r)
        |> applyFormatting ANSIFormatting.Underlined (fun r -> ANSI.Underlined + r)
        |> applyFormatting ANSIFormatting.Overlined (fun r -> ANSI.Overlined + r)
        |> applyFormatting ANSIFormatting.Blink (fun r -> ANSI.Blink + r)
        |> applyFormatting ANSIFormatting.Inverted (fun r -> ANSI.Inverted + r)
        |> applyFormatting ANSIFormatting.StrikeThrough (fun r -> ANSI.StrikeThrough + r)

    let resultWithColors = applyColor ANSI.Foreground resultWithFormatting
    let resultWithBackground =
        if Option.isSome _colorBackground then ANSI.Background(_colorBackground.Value) + resultWithColors
        else resultWithColors

    let resultWithHyperlink =
        match _hyperlink with
        | Some link -> ANSI.Hyperlink(resultWithBackground, link)
        | None -> resultWithBackground

    let finalResult =
        if _formatting.HasFlag(ANSIFormatting.Clear) then resultWithHyperlink + ANSI.Clear
        else resultWithHyperlink

    finalResult

module StringExtensions =
  let toANSI (text: string) = ANSIString(text)

  let addFormatting (text: string) (formatting: ANSIFormatting) = toANSI text |> fun t -> t.AddFormatting(formatting)
  let addFormattingANSI (text: ANSIString) (formatting: ANSIFormatting) = text.AddFormatting(formatting)

  let bold (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.Bold)
  let boldANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.Bold)

  let faint (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.Faint)
  let faintANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.Faint)

  let italic (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.Italic)
  let italicANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.Italic)

  let underlined (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.Underlined)
  let underlinedANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.Underlined)

  let overlined (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.Overlined)
  let overlinedANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.Overlined)

  let inverted (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.Inverted)
  let invertedANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.Inverted)

  let strikeThrough (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.StrikeThrough)
  let strikeThroughANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.StrikeThrough)

  let upperCase (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.UpperCase)
  let upperCaseANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.UpperCase)

  let lowerCase (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.LowerCase)
  let lowerCaseANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.LowerCase)

  let noClear (text: ANSIString) = text.RemoveFormatting(ANSIFormatting.Clear)

  let clear (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.Clear)
  let clearANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.Clear)

  let color (text: string) (color: AnsiColor) = toANSI text |> fun t -> t.SetForegroundColor(color)
  let colorANSI (text: ANSIString) (color: AnsiColor) = text.SetForegroundColor(color)
  
  let rgb (color: Color) = RGB color
  let ansiByte (color: byte) = ANSI color

  let background (text: string) (color: AnsiColor) = toANSI text |> fun t -> t.SetBackgroundColor(color)
  let backgroundANSI (text: ANSIString) (color: AnsiColor) = text.SetBackgroundColor(color)

  let opacity (text: string) (percent: int) = toANSI text |> fun t -> t.SetOpacity((float)percent / 100.0)
  let opacityANSI (text: ANSIString) (percent: int) = text.SetOpacity((float)percent / 100.0)

  let blink (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.Blink)
  let blinkANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.Blink)

  let link (text: string) (url: string) = toANSI text |> fun t -> t.SetHyperlink(url)
  let linkANSI (text: ANSIString) (url: string) = text.SetHyperlink(url)
  let linkSimple (text: string) = toANSI text |> fun t -> t.SetHyperlink(text)