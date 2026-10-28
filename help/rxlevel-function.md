<!-- help-article
{
  "corpus": "help",
  "id": "rxlevel-function",
  "lookup": "rxlevel()",
  "aliases": [
    "rxlevel",
    "txlevel()",
    "txlevel",
    "hasrxlevel()",
    "hasrxlevel",
    "hastxlevel()",
    "hastxlevel",
    "listrlevels()",
    "listrlevels"
  ],
  "sections": [
    {
      "id": "rxlevel-examples",
      "heading": "Examples",
      "lookup": "rxlevel examples"
    }
  ]
}
-->
# rxlevel()

`rxlevel(<object>[, <levels>])`<br>
`txlevel(<object>[, <levels>])`<br>
`hasrxlevel(<object>, <level>)`<br>
`hastxlevel(<object>, <level>)`<br>
`listrlevels()`

RhostMUSH's and TinyMUX's reality-level functions, over [@reality] layers. A level is a layer, named itself or by one of its aliases; case does not matter.

- `rxlevel()` and `txlevel()` return the layers `<object>` receives or transmits. Given `<levels>`, they change them as [@rxlevel] does and return nothing.
- `hasrxlevel()` and `hastxlevel()` return 1 when `<object>` receives or transmits `<level>`, else 0. A name that is not a layer or alias returns `#-1 NO SUCH LEVEL`.
- `listrlevels()` returns every configured layer.

You must control `<object>`. A dbref is taken as it stands, so you can read an object you cannot perceive. Changing levels needs the `reality.admin` permission, as `@reality` does.

See [rxlevel examples].

## Examples

A world with the layers `normal` and `ghost`, and `spirit` an alias of `ghost`:

```sharp
> think listrlevels()
normal ghost
> think rxlevel(#10,spirit)[rxlevel(#10)]
normal ghost
> think hasrxlevel(#10,Spirit) hastxlevel(#10,ghost)
1 0
```

Output: `#10` receives both layers, and transmits only `normal`.

::: seealso
- [@reality]
- [@rxlevel]
- [cansee()]
:::
