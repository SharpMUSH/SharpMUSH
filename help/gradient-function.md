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
    },
    {
      "id": "theme-examples",
      "heading": "Theme colour examples",
      "lookup": "gradient() theme examples"
    },
    {
      "id": "layout-examples",
      "heading": "Layout examples",
      "lookup": "gradient() layout examples"
    },
    {
      "id": "target-examples",
      "heading": "Border and text examples",
      "lookup": "gradient() target examples"
    }
  ]
}
-->
# gradient()

`gradient(<text>, <colors>[, <options>])`

*<text>* in colours blended one into the next through *<colors>*, which are [ANSI()] codes split by `|`: `r|y|g`, `#ff6000|#8040ff`, or colour names. A stop may also be a theme colour, named as [TONE()] names it: `primary|info`. Spaces take no colour and no place. The text keeps its own markup.

Given a layout, such as a [BOX()] or a [DATATABLE()], the whole block is shaded, borders and all, over the colours the game's or the reader's theme gives its parts, and stays a layout: each reader still gets it at their own width, and the portal draws it as a card with its text and borders in the gradient. The `target` option shades only the borders, or only the text.

- [gradient() options]: flow, colour space, mirror, repeat and target
- [gradient() colour spaces]: how oklch, oklab and hsl blend
- [gradient() fewer colours]: what a client with fewer colours is sent
- [gradient() examples]: a name, words, mirror, repeat and colour space
- [gradient() theme examples]: blending the game's theme colours
- [gradient() layout examples]: a box and a table
- [gradient() target examples]: shading only the borders, or only the text

::: seealso
- [ANSI()]
- [TONE()]
- [GAUGE()]
- [LAYOUT FUNCTIONS]
:::

## Options

Options (the last argument, which may be empty):
- `"flow":"characters"`, `"flow":"words"`, `"flow":"across"`, `"flow":"down"` or `"flow":"diagonal"` - which way the colours run. `characters` (the default) runs along the characters that show, on through every line; `words` gives each word one colour; `across` runs left to right by column, every line alike; `down` gives each line one colour; `diagonal` runs from the top-left corner to the bottom-right. The portal runs `characters` and `words` across.
- `"space":"oklch"`, `"space":"oklab"` or `"space":"hsl"` - the space the colours blend in.
- `"mirror":true` - run there and back: red to blue to red.
- `"repeat":<n>` - run through the colours *<n>* times, 1 to 1000.
- `"target":"all"`, `"target":"borders"` or `"target":"text"` - what a layout's gradient falls on. `all` (the default) is everything; `borders` is the box drawing alone: borders, rules, table separators, tree guides and gauge brackets; `text` is the rest, titles and labels with it. The part left out keeps its own colour, or its theme's. Text that is not a layout has no borders, so `borders` leaves it as it is.

## Colour spaces

The colours blend in a space built to look even to the eye, not in plain RGB, whose midpoints go grey and dark (red to green through a muddy olive):
- `oklch` (the default) keeps the middle as bright and vivid as the ends; red to green passes through yellow.
- `oklab` goes straight across, with no swing through other hues; colours far apart meet in a softer middle.
- `hsl` is a brighter, less even rainbow sweep.

## Clients with fewer colours

A blend needs a client with 256 colours or more, which gets each shade's nearest. A client with only the sixteen standard colours gets bands of the colours you named instead, each character in the one it lies nearest: `r|b` shows as a red half and a blue half, and `hc|hm` as a cyan half and a magenta half: a box shaded `diagonal` from `hc` to `hm` is cyan at its upper left and magenta at its lower right. A client without colour sees the text alone. The portal shows the full blend.

The game guesses a client's colours from its telnet negotiation; [terminfo()] shows what it chose. A client that shows more than it reports can say so with `SOCKSET colorstyle=xterm256` or `truecolor` (see [COLORSTYLE]).

## Examples

Colour cannot be shown here; the sentence under each example says what a 256-colour client sees.

```sharp
> think gradient(Mannaz Byron,#ff4040|#ffd040|#40c0ff)
Mannaz Byron
```

Output: red at the M, through gold, to sky blue at the n.

```sharp
> think gradient(The storm breaks over the harbour,hr|hy,{{"flow":"words"}})
The storm breaks over the harbour
```

Output: each word in one colour, from red to yellow.

```sharp
> think gradient(==============================,#ff0000|#0000ff,{{"mirror":true}})
==============================
> think gradient(------------------------------,hr|hb,{{"repeat":3}})
------------------------------
```

Output: the first rule is red at both ends and blue in the middle; the second runs red to blue three times over.

```sharp
> think gradient(Sunset over the bay,#ff6000|#8040ff,{{"space":"oklab"}})
Sunset over the bay
```

Output: orange to violet through a soft dusty rose. Without the `space` option, the middle is a vivid pink.

## Theme colour examples

A stop that is a [TONE()] name, such as `primary`, `info` or `muted`, is the game's [LAYOUT THEMES] colour for it, or a standard colour when the game has no theme, so the blend runs between colours the theme was made to read well with. Changing the theme changes every gradient written this way. Theme and fixed colours mix: `primary|#ffffff`. A hue name such as `red` is the theme's red.

```sharp
> think gradient(Welcome to the Gate,primary|info)
Welcome to the Gate
```

Output: the theme's primary colour at the W to its info colour at the e.

```sharp
> think gradient(box(All quiet.,Status,20),success|info,{{"flow":"diagonal"}})
+====< Status >====+
| All quiet.       |
+==================+
```

Output: the theme's success colour at the top-left corner to its info colour at the bottom-right.

Anything else is read as [ANSI()] codes.

## Layout examples

```sharp
> think gradient(box(Sheet,Mannaz,20),hc|hm,{{"flow":"diagonal"}})
+====< Mannaz >====+
| Sheet            |
+==================+
```

Output: cyan at the top-left corner to magenta at the bottom-right, the border and title with the text.

```sharp
> think gradient(box(Strength: 3%rDexterity: 2,Stats,20),#40c0ff|#ff40c0,{{"flow":"down"}})
+=====< Stats >====+
| Strength: 3      |
| Dexterity: 2     |
+==================+
```

Output: each line in one colour, blue at the top to pink at the bottom.

```sharp
> think gradient(datatable(,Name|Race,Mannaz|Human,Raya|Elf),#ffd040|#40c0ff,{{"flow":"across"}})
Name    Race
-------------
Mannaz  Human
Raya    Elf
```

Output: gold at the left edge to blue at the right, the same on every line, so each column keeps its colour.

## Border and text examples

```sharp
> think gradient(box(Sheet,Mannaz,20),#ff4040|#40c0ff,{{"target":"borders","flow":"diagonal"}})
+====< Mannaz >====+
| Sheet            |
+==================+
```

Output: the border runs red to blue; the title and the text keep their own colours.

```sharp
> think gradient(box(Strength: 3%rDexterity: 2,Stats,20),#ffd040|#ff40c0,{{"target":"text"}})
+=====< Stats >====+
| Strength: 3      |
| Dexterity: 2     |
+==================+
```

Output: the title and the text run gold to pink, a character at a time; the border keeps the theme's colour.
