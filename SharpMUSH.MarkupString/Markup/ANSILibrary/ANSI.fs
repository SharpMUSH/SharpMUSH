namespace ANSILibrary 

open System
open System.Drawing  

// Converted and heavily changed from: https://github.com/WilliamRagstad/ANSIConsole/blob/main/ANSIConsole/
module ANSI =
  type AnsiColor = 
    | RGB of Color 
    | ANSI of byte array
    | NoAnsi

  /// The ASCII escape character (decimal 27).
  let private ESC = "\u001b"

  /// Introduces a control sequence that uses 8-bit characters.
  let private CSI = ESC + "[" 

  let private SGR (codes: byte array) =
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
      | ANSI ansi -> SGR ansi
      | NoAnsi -> ""

  let Background (color: AnsiColor) = 
    match color with 
      | RGB rgb -> SGR [|48uy; 2uy; rgb.R; rgb.G; rgb.B|]
      | ANSI ansi -> SGR ansi
      | NoAnsi -> ""

  let Hyperlink (text: string, link: string) = $"\u001b]8;;%s{link}\a%s{text}\u001b]8;;\a"

open ANSI

[<Flags>]
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
    | TrueClear = 2048

type ANSIString(text: string, hyperlink: string option, colorForeground: AnsiColor option, colorBackground: AnsiColor option, opacity: float option, formatting: ANSIFormatting) =
  new(text: string) = ANSIString(text, None, None, None, None, ANSIFormatting.None)
  member this.Text = text
  member this.Hyperlink = hyperlink
  member this.ColorForeground = colorForeground
  member this.ColorBackground = colorBackground
  member this.Opacity = opacity
  member this.Formatting = formatting

  member this.AddFormatting(add: ANSIFormatting) =
      let newFormatting = formatting ||| add
      if newFormatting.HasFlag(ANSIFormatting.UpperCase ||| ANSIFormatting.LowerCase) then
          raise (ArgumentException("formatting cannot include both UpperCase and LowerCase!", "formatting"))
      ANSIString(text, hyperlink, colorForeground, colorBackground, opacity, newFormatting)

  member this.RemoveFormatting(rem: ANSIFormatting) =
      let newFormatting = formatting &&& ~~~rem
      ANSIString(text, hyperlink, colorForeground, colorBackground, opacity, newFormatting)

  member this.SetForegroundColor(color: AnsiColor) =
      ANSIString(text, hyperlink, Some color, colorBackground, opacity, formatting)
      
  member this.SetBackgroundColor(color: AnsiColor) =
      ANSIString(text, hyperlink, colorForeground, Some color, opacity, formatting)

  member this.SetOpacity(opacity: float) =
      ANSIString(text, hyperlink, colorForeground, colorBackground, Some opacity, formatting)

  member this.SetHyperlink(link: string) =
      ANSIString(text, Some link, colorForeground, colorBackground, opacity, formatting)
      
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
    let applyFormatting format transform result =
        if formatting.HasFlag(format) then transform(result) else result

    let applyColor colorFunc result =
        match opacity, colorForeground, colorBackground with
        | Some o, Some bg, Some fg -> 
          match bg, fg with
            | RGB b, RGB f -> colorFunc(ANSIString.Interpolate(b, f, o)) + result
            | _, _ -> result // TODO: Handle ANSI colors
        | _, Some cf, _ -> colorFunc(cf) + result
        | _ -> result

    let initialResult = text
    let resultWithCase =
        initialResult
        |> applyFormatting ANSIFormatting.UpperCase _.ToUpper()
        |> applyFormatting ANSIFormatting.LowerCase _.ToLower()

    let resultWithFormatting =
        resultWithCase
        |> applyFormatting ANSIFormatting.Bold (fun r -> Bold + r)
        |> applyFormatting ANSIFormatting.Faint (fun r -> Faint + r)
        |> applyFormatting ANSIFormatting.Italic (fun r -> Italic + r)
        |> applyFormatting ANSIFormatting.Underlined (fun r -> Underlined + r)
        |> applyFormatting ANSIFormatting.Overlined (fun r -> Overlined + r)
        |> applyFormatting ANSIFormatting.Blink (fun r -> Blink + r)
        |> applyFormatting ANSIFormatting.Inverted (fun r -> Inverted + r)
        |> applyFormatting ANSIFormatting.StrikeThrough (fun r -> StrikeThrough + r)

    let resultWithColors = applyColor Foreground resultWithFormatting
    let resultWithBackground =
        match colorBackground with
        | Some bg -> Background(bg) + resultWithColors
        | None -> resultWithColors

    let resultWithHyperlink =
        match hyperlink with
        | Some link -> Hyperlink(resultWithBackground, link)
        | None -> resultWithBackground

    let finalResult =
        if formatting.HasFlag(ANSIFormatting.TrueClear) then resultWithHyperlink + Clear 
        else resultWithHyperlink

    // TODO: This needs to be changed. The clear needs to affect the span, so should be ahead of the resultWithHyperlink, 
    // But it also needs to be restored to the previous state. Which means we have to specifically restore colors afterwards.
    // We can do a manual reset, and have a separate call for a True Clear.
    if formatting.HasFlag(ANSIFormatting.Clear) then Clear + finalResult
    else finalResult 

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

  let endWithTrueClear (text: string) = toANSI text |> fun t -> t.AddFormatting(ANSIFormatting.TrueClear)
  let endWithTrueClearANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.TrueClear)

  let color (text: string) (color: AnsiColor) = toANSI text |> fun t -> t.SetForegroundColor(color)
  let colorANSI (text: ANSIString) (color: AnsiColor) = text.SetForegroundColor(color)
  
  let rgb (color: Color) = RGB color
  let ansiByte (color: byte) = ANSI [|color|]
  let ansiBytes (color: byte array) = ANSI color

  let background (text: string) (color: AnsiColor) = toANSI text |> _.SetBackgroundColor(color)
  let backgroundANSI (text: ANSIString) (color: AnsiColor) = text.SetBackgroundColor(color)

  let opacity (text: string) (percent: int) = toANSI text |> _.SetOpacity(float percent / 100.0)
  let opacityANSI (text: ANSIString) (percent: int) = text.SetOpacity(float percent / 100.0)

  let blink (text: string) = toANSI text |> _.AddFormatting(ANSIFormatting.Blink)
  let blinkANSI (text: ANSIString) = text.AddFormatting(ANSIFormatting.Blink)

  let link (text: string) (url: string) = toANSI text |> _.SetHyperlink(url)
  let linkANSI (text: ANSIString) (url: string) = text.SetHyperlink(url)

/// <summary>
/// ANSI optimization functions for reducing redundant escape sequences.
/// Moved from Markup.fs to improve separation of concerns.
/// </summary>
module Optimization =
  open System.Text.RegularExpressions
  
  /// <summary>
  /// Optimizes repeated patterns like: [31mtext[0m[31mmore[0m → [31mtextmore[0m
  /// </summary>
  let rec optimizeRepeatedPattern (text: string) : string =
    let pattern = @"(?<Pattern>(?:\u001b[^m]*m)+)(?<Body1>[^\u001b]+)\u001b\[0m\1(?<Body2>[^\u001b]+)\u001b\[0m"
    if not(Regex.Match(text, pattern).Success)
    then text
    else optimizeRepeatedPattern (Regex.Replace(text, pattern, "${Pattern}${Body1}${Body2}\u001b[0m"))
  
  /// <summary>
  /// Optimizes repeated clear codes: ]0m]0m → ]0m
  /// </summary>
  let optimizeRepeatedClear (text: string) : string =
    text.Replace("]0m]0m", "]0m")
  
  /// <summary>
  /// Removes duplicate consecutive escape codes.
  /// Example: [31m[31m → [31m
  /// </summary>
  let rec optimizeImpl (text: string) (currentIndex: int) (currentEscapeCode: string) : string =
    if currentIndex >= text.Length - 1 then
      text
    else
      match text.IndexOf("\u001b[", currentIndex, System.StringComparison.Ordinal) with
      | -1 -> text
      | escapeCodeStartIndex ->
        // TODO: Implement a case that turns:
        // this: `[38;2;255;0;0mre[0m[38;2;255;0;0ma[0m[38;2;255;0;0md[0m`
        // into: [38;2;255;0;0mread[0m
        // By recognizing that a pattern is the same as a previous pattern, and removing the duplicate in-between 'poles'.
        let escapeCodeEndIndex = text.IndexOf("m", escapeCodeStartIndex, System.StringComparison.Ordinal)
        if escapeCodeEndIndex = -1 then
          text
        else
          let escapeCode = text.Substring(escapeCodeStartIndex, escapeCodeEndIndex - escapeCodeStartIndex + 1)
          if escapeCode = currentEscapeCode then
            let updatedText = text.Remove(escapeCodeStartIndex, escapeCodeEndIndex - escapeCodeStartIndex + 1)
            optimizeImpl updatedText escapeCodeStartIndex currentEscapeCode
          else
            optimizeImpl text (escapeCodeEndIndex + 1) escapeCode
  
  /// <summary>
  /// Main optimization function that applies all optimizations to ANSI text.
  /// </summary>
  let optimize (text: string) : string =
    optimizeImpl text 0 System.String.Empty
    |> optimizeRepeatedPattern
    |> optimizeRepeatedClear
