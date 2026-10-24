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

printf() builds a line of text from a format and values, for reports that line up. Each directive in `<format>` is written `%[flags][width][.precision]type` and takes one value. The type is `s` for text, `d` for a whole number or `f` for a decimal number. `%%` writes one percent sign and takes no value.

Too few or too many values return `#-1 PRINTF ARGUMENT COUNT MISMATCH`, and a directive printf() cannot read, or one with a flag given twice, `#-1 INVALID PRINTF FORMAT`.

The parser reads percent signs before printf() does, so wrap the format in lit(), or double each percent sign: `printf(lit(%s),name)` and `printf(%%s,name)` both return `name`, and `printf(%%%%)` returns `%`.

- [printf width]: padding, sign and zero flags, and precision
- [printf values]: which numbers are accepted, and how markup carries over
- [printf limits]: the size limits and their errors
- [printf rhost]: the field grammar used with rhost_printf on
- [printf examples]: text, signed decimal and wide character fields

## Width and precision

The width is the fewest columns the field takes, padded with spaces on the left. The flags are:

- `-`: pad on the right instead.
- `+`: show a `+` before a positive number.
- `0`: pad a number with zeros after its sign. `-` wins over `0`.

The precision means a different thing for each type. For `s` it is the most columns of text kept; a character made of several parts, such as an accented letter or an emoji, is never split. For `d` it is the fewest digits, with zeros in front. For `f` it is the digits after the point, 6 unless given, and a value exactly halfway rounds to the even digit: `printf(lit(%.1f),0.25)` returns `0.2`.

Width counts columns as the screen shows them, so a wide character, such as a Chinese one or an emoji, takes two.

## Values and markup

A number is written with an optional sign, the digits 0 to 9 and, for `f`, a decimal point. Spaces, thousands separators and exponents such as `1e3` are refused with the usual number error, as is a number too large: a whole number must fit in 64 bits, and a decimal in 28 or 29 significant digits.

Colour and other markup in the format and in text values is kept. A number written by `d` or `f` takes the markup of the value's first character, and markup on a directive's `%` covers its whole field, padding included; other padding is plain. Control characters and line breaks in text are kept.

## Limits

The format can be at most 65,536 characters long, with at most 1,024 directives, `%%` included, and 128 values. A width, or a precision for `s` or `d`, is at most 65,536, and a precision for `f` at most 28. Past these, printf() returns `#-1 PRINTF FIELD LIMIT EXCEEDED`. A result longer than the game's limit on one value stops evaluation with `#-1 OUTPUT EXCEEDED MAXIMUM SIZE`.

## RhostMUSH fields

With the `rhost_printf` option on, printf() reads RhostMUSH's fields instead of these directives: a field is `$`, its codes, then `s`, as in `printf(|$-5s|$5s|,ab,cd)`, which returns `|ab   |   cd|`. That form wants at least one value, and the fields can wrap a value over several rows. This grammar comes from RhostMUSH; see [compatibility config] for its codes.

## Examples

```sharp
> think printf(lit(%-8s %4d),Ore,12)
Ore        12
> think printf(lit(%+08.2f),12.345)
+0012.34
> think printf(lit(%5.3d|%-5s|%.2s),7,ab,abcdef)
  007|ab   |ab
```

A wide character takes two columns: `printf(lit(%4s|%-4s),chr(30028),chr(128512))` pads a Chinese character and an emoji each with two spaces, to four columns.

::: seealso
- [DISPLAYWIDTH()]
- [align()]
- [TABLE()]
- [WRAP()]
:::
