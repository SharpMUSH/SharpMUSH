module SharpMUSH.MarkupString.ColumnModule

open System

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
    let parseList (spec: string) : ColumnSpec list = spec.Split(' ') |> Seq.map parse |> Seq.toList
