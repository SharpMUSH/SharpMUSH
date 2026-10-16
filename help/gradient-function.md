<!-- help-article
{
  "corpus": "help",
  "id": "gradient-function",
  "lookup": "gradient()",
  "aliases": [],
  "sections": [
    {
      "id": "options",
      "heading": "Options",
      "lookup": "gradient() options"
    },
    {
      "id": "colour-spaces",
      "heading": "Colour spaces",
      "lookup": "gradient() colour spaces"
    },
    {
      "id": "fewer-colours",
      "heading": "Clients with fewer colours",
      "lookup": "gradient() fewer colours"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "gradient() examples"
    }
  ]
}
-->
# gradient()

`gradient(<text>, <colors>[, <options>])`

*<text>* in colours blended one into the next through *<colors>*, which are [ANSI()] codes split by `|`: `r|y|g`, `#ff6000|#8040ff`, or colour names. Spaces take no colour and no place. The text keeps its own markup.

Given a layout, such as a [BOX()] or a [DATATABLE()], the whole block is shaded, borders and all, and stays a layout: each reader still gets it at their own width, and the portal draws it as a card with its text and borders in the gradient.

- [gradient() options]: flow, colour space, mirror and repeat
- [gradient() colour spaces]: how oklch, oklab and hsl blend
- [gradient() fewer colours]: what a client with fewer colours is sent
- [gradient() examples]: a shaded name, and a shaded box

::: seealso
- [ANSI()]
- [GAUGE()]
- [LAYOUT FUNCTIONS]
:::

## Options

Options (the last argument):
- `"flow":"characters"`, `"flow":"words"`, `"flow":"across"`, `"flow":"down"` or `"flow":"diagonal"` - which way the colours run. `characters` (the default) runs along the characters that show, on through every line; `words` gives each word one colour; `across` runs left to right by column, every line alike; `down` gives each line one colour; `diagonal` runs from the top-left corner to the bottom-right. The portal runs `characters` and `words` across.
- `"space":"oklch"`, `"space":"oklab"` or `"space":"hsl"` - the space the colours blend in.
- `"mirror":true` - run there and back: red to blue to red.
- `"repeat":<n>` - run through the colours *<n>* times, 1 to 1000.

## Colour spaces

The colours blend in a space built to look even to the eye, not in plain RGB, whose midpoints go grey and dark (red to green through a muddy olive):
- `oklch` (the default) keeps the middle as bright and vivid as the ends; red to green passes through yellow.
- `oklab` goes straight across, with no swing through other hues; colours far apart meet in a softer middle.
- `hsl` is a brighter, less even rainbow sweep.

## Clients with fewer colours

A blend needs a client with 256 colours or more, which gets each shade's nearest. A client with only the sixteen standard colours gets bands of the colours you named instead, each character in the one it lies nearest: `r|b` shows as a red half and a blue half. The portal shows the full blend.

## Examples

```sharp
> think gradient(Mannaz Byron,#ff4040|#ffd040|#40c0ff)
Mannaz Byron
> think gradient(box(Sheet,Mannaz,20),hc|hm,{{"flow":"diagonal"}})
+====< Mannaz >====+
| Sheet            |
+==================+
```
