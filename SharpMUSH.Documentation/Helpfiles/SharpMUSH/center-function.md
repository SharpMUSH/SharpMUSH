<!-- help-article
{
  "corpus": "help",
  "id": "center-function",
  "lookup": "center()",
  "aliases": [],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "center examples"}
  ]
}
-->
# center()
`center(<string>, <width>[, <fill>[, <rightfill>]])`

This function will center `<string>` within a field `<width>` characters wide, using the `<fill>` string for padding on the left side of the string, and `<rightfill>` for padding on the right side. Each fill starts from its first character. Without `<rightfill>`, the right side is `<fill>` laid out the same way and then reversed. `<fill>` defaults to a space if neither `<fill>` nor `<rightfill>` are specified.

If `<string>` divides `<width>` into uneven portions, the left side will be one character shorter than the right side.

A `<string>` longer than `<width>` is returned unaltered. With the `mux_justify` @config option on, it is cut to `<width>` characters, as TinyMUX does. See [compatibility config].

- [center examples]: fill strings and long fills

## Examples

```sharp
> say center(X,5,-)
You say, "--X--"
> say center(X,5,-=)
You say, "-=X=-"
> say center(.NEAT.,15,-,=)
You say, "----.NEAT.====="
> say center(hello,16,12345)
You say, "12345hello154321"
```

::: seealso
- [align()]
- [LJUST()]
- [RJUST()]
:::
