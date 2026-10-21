<!-- help-article
{
  "corpus": "help",
  "id": "swatch-function",
  "lookup": "swatch()",
  "aliases": [],
  "sections": [
    {
      "id": "example",
      "heading": "Example",
      "lookup": "swatch() example"
    }
  ]
}
-->
# swatch()

`swatch(<theme>[, <width>])`

A table of *<theme>*'s colours: each colour's name, a sample drawn in it, the colour, the standard colour a sixteen-colour client is sent, and how far it stands out from the background. A colour that stands out less than its use needs is marked with what it needs. A standard colour on its own shows `client`, since it looks however the reader's client draws it. The table is drawn in the theme. *<width>* is the table's width; see [LAYOUT FUNCTIONS] for leaving it out.

- [swatch() example]: the colours of nord, with what each needs

::: seealso
- [THEMES()]
- [THEME()]
- [LAYOUT THEMES]
:::

## Example

```sharp
> think swatch(nord,64)
Role        Sample  Colour   16-colour       Contrast
--------------------------------------------------------------
background  -       #2e3440  0 black         -
surface     Sample  #3b4252  8 bright black  -
foreground  Sample  #e5e9f0  7 white         10.3:1
primary     Sample  #81a1c1  4 blue          4.6:1
secondary   Sample  #b48ead  5 magenta       4.4:1 (needs 4.5)
tertiary    Sample  #d08770  3 yellow        4.4:1 (needs 4.5)
muted       Sample  #4c566a  8 bright black  1.7:1 (needs 3)
success     Sample  #a3be8c  2 green         6.1:1
warning     Sample  #ebcb8b  3 yellow        8.0:1
error       Sample  #bf616a  1 red           3.1:1 (needs 4.5)
info        Sample  #88c0d0  6 cyan          6.2:1
subtle      Sample  #4d5668  8 bright black  1.7:1 (needs 3)
link        Sample  #81a1c1  4 blue          4.6:1
highlight   Sample  #434c5e  8 bright black  -
red         Sample  #bf616a  1 red           3.1:1 (needs 4.5)
orange      Sample  #d08770  3 yellow        4.4:1 (needs 4.5)
yellow      Sample  #ebcb8b  3 yellow        8.0:1
green       Sample  #a3be8c  2 green         6.1:1
cyan        Sample  #88c0d0  6 cyan          6.2:1
blue        Sample  #81a1c1  4 blue          4.6:1
purple      Sample  #b48ead  5 magenta       4.4:1 (needs 4.5)
pink        Sample  #bb7793  5 magenta       3.7:1 (needs 4.5)
```
