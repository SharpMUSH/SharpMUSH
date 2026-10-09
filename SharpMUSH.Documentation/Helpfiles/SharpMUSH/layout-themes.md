<!-- help-article
{
  "corpus": "help",
  "id": "layout-themes",
  "lookup": "layout themes",
  "aliases": [],
  "sections": [
    {
      "id": "kinds",
      "heading": "Kinds of theme",
      "lookup": "layout theme kinds"
    },
    {
      "id": "colours",
      "heading": "Colours",
      "lookup": "layout theme colours",
      "aliases": [
        "layout theme colors"
      ]
    },
    {
      "id": "looks",
      "heading": "Looks",
      "lookup": "layout theme looks"
    },
    {
      "id": "sixteen",
      "heading": "Sixteen-colour clients",
      "lookup": "layout themes sixteen colours"
    },
    {
      "id": "making",
      "heading": "Making a theme",
      "lookup": "layout themes making"
    },
    {
      "id": "from-another",
      "heading": "Starting from another theme",
      "lookup": "layout themes from another theme"
    },
    {
      "id": "using",
      "heading": "Using themes",
      "lookup": "layout themes using"
    }
  ]
}
-->
# Layout Themes

A theme colours the parts of a layout: borders and gauge bars, titles, table headings, field labels, list bullets, tree guides, and the quiet lines between things. The text inside is left as it is written. A layout takes a theme from its `"theme"` option, and one that names none takes the game's `layout_theme` option. Unset, that is `sharpmush`, the portal's teal on black; set to `none`, layouts have no colour. A layout's theme reaches every layout inside it, the way its `"border"` does.

```sharp
> think box(Hi there,Sheet,30,{{"theme":"nord"}})
+==========< Sheet >=========+
| Hi there                   |
+============================+
```

That box is drawn in Nord's blue, its title in Nord's purple and bold. The examples in these topics show the text a client is sent, without its colour.

## Kinds of theme

A theme is one of these:

| Kind | Written |
|---|---|
| a name from [THEMES()] | `"theme":"nord"` |
| one of the web portal's themes | `"theme":"cyberpunk"` |
| a genre | `"theme":"fantasy"` |
| a theme made from one colour | `"theme":{"seed":"#7aa2f7","harmony":"triadic"}` |
| a base16 scheme | `"theme":{"base16":["#2e3440", ... sixteen colours]}` |
| any of these with colours changed | `"theme":{"preset":"nord","colors":{"primary":"#bf616a"}}` |

`terminal` uses only the sixteen standard colours, so each player sees it in the colours their own client is set to. Hundreds of base16 schemes exist for editors and terminals.

### The portal's themes

`sharpmush` is the game's own look: borders and headings in the portal's dim teal, titles, labels and bullets in its bright teal, on black. The web portal's themes have the same names here, with their colours: `phosphor`, `daylight`, `cyberpunk`, `idol`, `starship-console` and the rest [THEMES()] lists. So does each colour blindness theme, such as `deutan-dark`, whose warning, error and success colours stay apart for that reader. A theme the portal draws on a light page, such as `daylight` or `shonen`, is made for a client with a light background.

The portal's genre themes share their names with the genres below, which keep their own colours and shapes.

### Genres

There is one genre for each MSSP genre: `fantasy`, `historical`, `horror`, `modern`, `mystery`, `romance`, `science-fiction` and `spiritual` (`romance` serves MSSP's Adult as well). A genre theme changes the shapes as well as the colours, down to a box's corners:

- `fantasy` - double lines with diamond corners and fleurons either side of a title, and a gauge of solid and shaded blocks
- `horror` - a cracked heavy line with daggers at the corners
- `mystery` - dashed lines with hollow diamonds
- `romance` - rounded lines with hearts
- `science-fiction` - heavy lines with bracket corners, triangle bullets and a segmented gauge

A client without Unicode gets each in ASCII.

### Themes from one colour

The other colours take their hues from the seed, by `"harmony"`:

- `monochrome` - one hue
- `analogous` - its neighbours (the default)
- `complementary` - the opposite hue
- `split` - either side of the opposite
- `triadic` - three evenly round
- `tetradic` - four

Each is made lighter or darker until it stands out from the background: 3 to 1 for lines, 4.5 to 1 for text, by the measure the web accessibility guidelines use. `"contrast"` from 0 to 1 raises both toward 7 to 1. `"mode":"light"` makes it for a light background; dark is the default.

## Colours

The colours, which `"colors"` sets by name:
- `primary` - borders, rules, gauge bars and table headings
- `secondary` - titles and field labels
- `tertiary` - list bullets
- `muted` - tree guides, separators, the line under table headings, a gauge's empty part
- `success`, `warning`, `error`, `info` - [BADGE()] and [NOTICE()] use these
- `surface` - the background behind every second row of a striped table or field list (`"stripe":true`)
- `background`, `foreground` - what the others are measured against; neither is painted

A colour is `"#rrggbb"`, a standard colour from 0 to 15, or both as `{"rgb":"#88c0d0","slot":6}`. `null` leaves one out.

## Looks

`"look"` sets a theme's shapes:
- `"border"` - a border style from [LAYOUT BORDERS]
- `"title"` - the pieces either side of a title, joining it to the line: `["< ", " >"]`
- `"guide"` - a tree guide style, see [TREE()]
- `"bullet"` - the mark before each item of [BULLETS()]
- `"gauge"` - [GAUGE()]'s pieces: `["[", "#", "-", "]"]`, the opening, the filled part, the empty part and the close
- `"separator"` - after each label of [FIELDS()]
- `"rule"` - the line under [DATATABLE()]'s headings

`"theme":{"preset":"fantasy","look":{"bullet":"+"}}` keeps the rest of fantasy's look; `"look":null` drops it. A theme's border wins over the game's `layout_border`, and a layout's own `"border"` wins over both. A client without Unicode gets the plain ASCII form of each piece.

```sharp
> think box(bullets(Sword|Shield,|),Kit,20,{{"theme":"fantasy"}})
+======< Kit >=====+
| * Sword          |
| * Shield         |
+==================+
```

That is how a client without Unicode sees it. With Unicode the edges are double lines with a diamond at each corner, the title sits between two fleurons, and each bullet is a fleuron.

## Sixteen-colour clients

Each colour carries the standard colour a client with only the sixteen is sent instead, picked by its hue, so a pale blue is sent as blue rather than the grey nearest it. A client without colour is sent the layout as it is; nothing in a layout depends on colour alone. A client without Unicode gets ASCII borders in the same colours.

## Making a theme

[THEME()] shows what a theme comes to, and [SWATCH()] shows whether each colour stands out enough. Each of these starts from something small and lets the generator do the rest.

From one colour, here a blue with triadic harmony. The seed becomes the primary colour and the others are spaced round it:

```sharp
> think json_query(theme({{"seed":"#7aa2f7","harmony":"triadic"}}),get,colors,secondary,rgb)
"#ffa099"
```

The same theme for a light background. Each colour is darkened until it stands out from white:

```sharp
> think json_query(theme({{"seed":"#7aa2f7","harmony":"triadic","mode":"light"}}),get,colors,primary,rgb)
"#4568b8"
```

With `"contrast":1`, every colour stands out at least 7 to 1. The quiet `muted` colour moves furthest: without it, it is `#6b615d` at 3.1 to 1.

```sharp
> think swatch({{"seed":"#d08770","harmony":"split","contrast":1}},64)
Role        Sample  Colour   16-colour         Contrast
-------------------------------------------------------
background  -       #190f0b  0 black           -
surface     Sample  #2b1c18  8 bright black    -
foreground  Sample  #e8dbd7  7 white           13.9:1
primary     Sample  #d98f78  9 bright red      7.3:1
secondary   Sample  #6cd2c9  14 bright cyan    10.5:1
tertiary    Sample  #7fb7ec  12 bright blue    8.9:1
muted       Sample  #a69c97  7 white           7.0:1
success     Sample  #9abd53  10 bright green   8.8:1
warning     Sample  #ee9748  11 bright yellow  8.2:1
error       Sample  #f98b6e  9 bright red      8.0:1
info        Sample  #86acff  12 bright blue    8.4:1
```

## Starting from another theme

From a base16 scheme, copied from an editor or terminal theme. Its sixteen colours go in order, base00 to base0F; base0D, the scheme's blue, becomes the primary colour:

```sharp
> think json_query(theme({{"base16":\["#1d1f21","#282a2e","#373b41","#969896","#b4b7b4","#c5c8c6","#e0e0e0","#ffffff","#cc6666","#de935f","#f0c674","#b5bd68","#8abeb7","#81a2be","#b294bb","#a3685a"]}}),get,colors,primary,rgb)
"#81a2be"
```

From a built-in theme with one colour changed:

```sharp
> think json_query(theme({{"preset":"nord","colors":{"primary":"#bf616a"}}}),get,colors,primary,rgb)
"#bf616a"
```

From a genre with its look changed. This keeps fantasy's colours and gauge but draws ASCII lines, a dash for each bullet and round brackets about the title:

```sharp
> think box(bullets(Sword|Shield,|),Kit,20,{{"theme":{"preset":"fantasy","look":{"bullet":"-","border":"ascii","title":\["( "," )"]}}}})
+------( Kit )-----+
| - Sword          |
| - Shield         |
+------------------+
```

A gauge drawn in pieces of your own, in the reader's own sixteen colours:

```sharp
> think gauge(7,10,HP,{{"width":30,"theme":{"preset":"terminal","look":{"gauge":\["<","=",".",">"]}}}})
HP <===============......> 70%
```

A theme made once can be used again without making it each time. [THEME()] writes it out, and [JSON()] puts it into the options. To keep it, store it in an attribute as [THEME()] shows:

```sharp
> think [setq(0,theme({{"seed":"#d08770","harmony":"split"}}))][box(Hello,,20,json(object,theme,%q0))]
+==================+
| Hello            |
+==================+
```

## Using themes

Written straight into options, a theme object goes inside them as it is: `box(Hi,,30,{{"theme":{"seed":"#d08770"}}})`. Each player can also pick a theme of their own for every layout they read with [@THEME]. [THEME()] writes any theme out in full, to keep in an attribute, and [SWATCH()] shows one's colours and how well each stands out.

::: seealso
- [THEMES()]
- [THEME()]
- [SWATCH()]
- [LAYOUT BORDERS]
- [LAYOUT FUNCTIONS]
:::
