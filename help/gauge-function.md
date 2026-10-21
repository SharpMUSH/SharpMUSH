<!-- help-article
{
  "corpus": "help",
  "id": "gauge-function",
  "lookup": "gauge()",
  "aliases": [],
  "sections": [
    {
      "id": "options",
      "heading": "Options",
      "lookup": "gauge() options"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "gauge() examples"
    }
  ]
}
-->
# gauge()

`gauge(<value>, <maximum>[, <label>[, <options>]])`

A bar filled to *<value>*'s share of *<maximum>*, with *<label>* before it and the figures after it. The bar takes whatever width the label and figures leave. A value below zero draws an empty bar and one above the maximum a full one. In the web portal it is a meter.

- [gauge() options]: width, figures, pieces, ends, gradient and theme
- [gauge() examples]: bars with percent, value, no figures and a gradient

::: seealso
- [GRADIENT()]
- [DATATABLE()]
- [LAYOUT FUNCTIONS]
:::

## Options

Options (the last argument, which may be empty):
- `"width":<n>` - see [LAYOUT FUNCTIONS].
- `"bar":<n>` - the bar's own width, in cells.
- `"show":"percent"`, `"show":"value"` or `"show":"none"` - the figures after the bar: `50%` (the default), `6/12`, or nothing.
- `"filled":"<text>"` and `"empty":"<text>"` - the pieces the bar is drawn with. A solid block and a light shade by default, sent as `#` and `-` to a client without Unicode.
- `"open":"<text>"` and `"close":"<text>"` - the ends. `[` and `]` by default; leave one empty for none.
- `"gradient":"<colors>"` - shade the bar through these colours, [ANSI()] codes split by `|`: `"gradient":"r|y|g"` or `"gradient":"#ff4040|#40ff80"`. See [GRADIENT()] for how they blend.
- `"shade":"cells"` or `"shade":"value"` - with `cells` (the default) each cell takes the colour at its place along the whole bar, so a fuller bar reaches further along the gradient. With `value` the whole filled part takes one colour, the one at the value's place: red when nearly empty, green when full.
- `"space":"oklch"`, `"space":"oklab"` or `"space":"hsl"`, `"mirror":true` and `"repeat":<n>` - how the colours blend and run; see [GRADIENT()].
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].

## Examples

The examples show what a client without Unicode is sent. A Unicode client sees solid blocks and light shades where these show `#` and `-`.

```sharp
> think gauge(6,12,HP,{{"width":20}})
HP [######-----] 50%
> think gauge(3,4,,{{"bar":8,"show":"value","filled":"=","empty":"-"}})
[======--] 3/4
> think gauge(30,100,XP,{{"width":30,"show":"none","filled":"#","empty":"."}})
XP [########.................]
> think gauge(9,10,HP,{{"width":20,"gradient":"r|y|g"}})
HP [##########-] 90%
```

The last bar runs from red through yellow to green, and the portal draws it with the same blend.
