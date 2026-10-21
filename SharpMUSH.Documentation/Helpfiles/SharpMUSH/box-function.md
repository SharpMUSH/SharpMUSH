<!-- help-article
{
  "corpus": "help",
  "id": "box-function",
  "lookup": "box()",
  "aliases": [],
  "sections": [
    {
      "id": "options",
      "heading": "Options",
      "lookup": "box() options"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "box() examples"
    },
    {
      "id": "nesting-examples",
      "heading": "Layouts inside a box",
      "lookup": "box() nesting examples"
    },
    {
      "id": "border-examples",
      "heading": "Border examples",
      "lookup": "box() border examples"
    }
  ]
}
-->
# box()

`box(<body>[, <title>[, <width>[, <options>]]])`

Draws a frame round *<body>*, with *<title>* set into the top edge. A [RULE()] on a line of its own in the body becomes a divider across the box; a [FLEX()], [FIELDS()] or [TREE()] keeps its layout inside it.

*<width>* is the whole box, borders included; see [LAYOUT FUNCTIONS] for an empty width.

- [box() options]: borders, theme, title placement, more titles and padding
- [box() examples]: a centred title, one on the left, and two columns
- [box() nesting examples]: a centred body, and fields with a divider
- [box() border examples]: a rounded border, pieces of your own, and a bottom title

::: seealso
- [RULE()]
- [LAYOUT BORDERS]
- [LAYOUT FUNCTIONS]
:::

## Options

Options (the last argument, which may be empty):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS].
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"title":"left"`, `"title":"center"` or `"title":"right"` - where the title sits. Centred by default.
- `"titles":[...]` - more titles in the top edge, written as for [RULE()].
- `"bottomtitles":[...]` - titles in the bottom edge, written the same way.
- `"pad":<n>` - spaces between each side and the body, 0 to 10. One by default.

## Examples

```sharp
> think box(Hello there.,Greeting,30)
+========< Greeting >========+
| Hello there.               |
+============================+
```

```sharp
> think box(Hello,Title,20,{{"title":"left"}})
+=< Title >========+
| Hello            |
+==================+
```

Two columns in a box, with [FLEX()]:

```sharp
> think box(flex({{"sep":" | "}},item(Contents%rMannaz%rRaya,12),Exits%rNorth%rOut),Garden,34)
+===========< Garden >===========+
| Contents     | Exits           |
| Mannaz       | North           |
| Raya         | Out             |
+================================+
```

## Layouts inside a box

The body can be a layout, or several on lines of their own. To centre the body, give it as an [ITEM()] with `-` as its width; each line is centred, at any width:

```sharp
> think box(item(Hello there.%rWelcome to the garden.,-),,30)
+============================+
|        Hello there.        |
|   Welcome to the garden.   |
+============================+
```

[FIELDS()] above a [RULE()] that divides the box:

```sharp
> think box(fields(,Sex,Male,Species,Human)%r[rule(Quote)]%rHooooo?,Mannaz,30)
+=========< Mannaz >=========+
| Sex:     Male              |
| Species: Human             |
+==========< Quote >=========+
| Hooooo?                    |
+============================+
```

## Border examples

A Unicode client sees this box in light lines with rounded corners. A client without Unicode is sent the ASCII form shown here:

```sharp
> think box(Hello there.,,30,{{"border":"rounded"}})
+----------------------------+
| Hello there.               |
+----------------------------+
```

```sharp
> think box(x,,20,{{"top":"-=","corner":"*"}})
*-=-=-=-=-=-=-=-=-=*
| x                |
*==================*
```

```sharp
> think box(Page one,Notes,20,{{"bottomtitles":\[{"text":"1/3","side":"right"}]}})
+=====< Notes >====+
| Page one         |
+==========< 1/3 >=+
```
