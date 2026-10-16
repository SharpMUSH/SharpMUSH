<!-- help-article
{
  "corpus": "help",
  "id": "printf-function",
  "lookup": "printf()",
  "aliases": [],
  "sections": [
    {"id": "width", "heading": "Width and precision", "lookup": "printf width"},
    {"id": "values", "heading": "Values and markup", "lookup": "printf values"},
    {"id": "limits", "heading": "Limits", "lookup": "printf limits"},
    {"id": "rhost", "heading": "RhostMUSH fields", "lookup": "printf rhost"},
    {"id": "examples", "heading": "Examples", "lookup": "printf examples"}
  ]
}
-->
# printf()
`printf(<format>[, <value>...])`

  Builds compact reports from a format and its values. Directives have the form `%[flags][width][.precision]type`, where type is `s` (text), `d` (signed 64-bit integer), or `f` (decimal). Each directive consumes one value. `%%` emits a literal percent and consumes none. Missing or extra values return `#-1 PRINTF ARGUMENT COUNT MISMATCH`; unsupported, incomplete, or repeated flags return `#-1 INVALID PRINTF FORMAT`.

  Normal MUSH percent substitutions happen first. Use `lit()` around the format to pass its percent signs unchanged, or double each percent sign at the MUSH layer. Thus `printf(lit(%s),name)` and `printf(%%s,name)` both return `name`; `printf(%%%%)` returns one percent sign.

- [printf width]: padding, sign and zero flags, and precision
- [printf values]: which numbers are accepted, and how markup carries over
- [printf limits]: the size limits and their errors
- [printf rhost]: the field grammar used with rhost_printf on
- [printf examples]: text, signed decimal and wide character fields

## Width and precision

  Width is a minimum number of display columns, with spaces on the left by default. `-` moves padding to the right. Numeric fields also accept `+` for a positive sign and `0` for zeros after the sign; `-` takes precedence over `0`. String precision is a maximum number of display columns and keeps whole grapheme clusters. Integer precision is a minimum digit count. Decimal precision is fractional digits, defaults to six, and rounds ties to even.

## Values and markup

  Numeric values use a strict invariant grammar: an optional sign, ASCII digits, and (for decimals) a decimal point. Spaces, separators, exponent notation, and Tiny math coercions are not accepted. Values outside signed 64-bit integer or .NET decimal range return the usual number error. Literal and string-value markup survives. Generated numeric text inherits the first input character's markup; markup on a directive's percent sign wraps its field. Padding is plain unless covered by that directive markup. Controls and newlines in strings are retained and use the library's display-width policy.

## Limits

  The format may contain at most 65,536 UTF-16 code units, 1,024 directives including percent escapes, and 128 value fields. Width and string/integer precision are at most 65,536; decimal precision is at most 28. Exceeding these bounds returns `#-1 PRINTF FIELD LIMIT EXCEEDED` before large padding is allocated. Directives cannot cut through a grapheme cluster. The shared 5,242,880 UTF-16-unit result ceiling also applies; exceeding it stops evaluation with `#-1 OUTPUT EXCEEDED MAXIMUM SIZE`.

## RhostMUSH fields

  With the `rhost_printf` option on, printf() reads RhostMUSH's fields instead of these directives: a field is `$`, its codes, then `s`, as in `printf(|$-5s|$5s|,ab,cd)`, which returns `|ab   |   cd|`. That form wants at least one value, and the fields can wrap a value over several rows. This grammar comes from RhostMUSH; see [compatibility config] for its codes.

## Examples

  Examples:
```sharp
printf(lit(%-8s %4d),Ore,12)
printf(lit(%+08.2f),12.345)
printf(lit(%4s|%-4s),ansi(r,chr(30028)),ansi(b,chr(128512)))
```

::: seealso
- [DISPLAYWIDTH()]
- [align()]
- [TABLE()]
- [WRAP()]
:::
