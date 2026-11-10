<!-- help-article
{
  "corpus": "help",
  "id": "flex-function",
  "lookup": "flex()",
  "aliases": [],
  "sections": [
    {
      "id": "options",
      "heading": "Options",
      "lookup": "flex() options"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "flex() examples"
    },
    {
      "id": "panes",
      "heading": "Panes",
      "lookup": "flex() panes"
    }
  ]
}
-->
# flex()

`flex(<options>, <item1>[, ... , <itemN>])`

Puts the items side by side, sharing the width. When they do not fit, because an item would fall under its least width, they stack one under the other instead; in the web portal they wrap onto rows of their own. An item is any text or layout, or an [ITEM()] that says how wide it wants to be. Items that do not say share what the others leave.

- [flex() options]: borders, theme, width, gap, separator and alignment
- [flex() examples]: separated columns, spread items and centred items
- [flex() panes]: a row that splits a box into panes sharing its edges

::: seealso
- [ITEM()]
- [LFLEX()]
- [BOX()]
- [ALIGN()]
- [LAYOUT FUNCTIONS]
:::

## Options

Options (the first argument, which may be empty):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"width":<n>` - the width of the whole row; see [LAYOUT FUNCTIONS] for leaving it out.
- `"gap":<n>` - spaces between items, 0 to 20. Two by default.
- `"sep":"<text>"` - drawn between items on every line instead of the gap, for example `"sep":" | "`.
- `"justify":"<where>"` - `start`, `end`, `center` or `between`: where spare width goes when no item takes it.
- `"align":"<where>"` - `top`, `center` or `bottom`: where a shorter item sits against the tallest.
- `"vertical":true` - always one under the other.
- `"join":true` - inside a [BOX()], the items are panes that share the box's edges; see [flex() panes].

## Examples

```sharp
> think flex({{"sep":" | ","width":40}},item(Strength%rAgility,18),item(High%rLow,19))
Strength           | High
Agility            | Low
```

```sharp
> think flex({{"width":40,"justify":"between"}},item(Left,8),item(Right,8))
Left                            Right
```

```sharp
> think flex({{"width":30,"sep":" | ","align":"center"}},item(One%rTwo%rThree,10),item(Mid,10))
One        |
Two        | Mid
Three      |
```

## Panes

With `"join":true`, a flex() that is a part of a [BOX()] body splits the box into panes. A line of the box's side runs between each two panes, and a [RULE()] on a line of its own in a pane divides that pane from side to side. Where lines meet, the box uses its own tees and crosses (the `teet`, `teeb` and `cross` pieces in [LAYOUT BORDERS]). A rule() in the box outside the flex() is a divider across the whole box, as in any box.

```sharp
> think box(flex({{"join":true}},item(Stats%r[rule()]%rSkills,12),Bio%r[rule()]%rNotes,Portrait)%r[rule(Footer)]%rThe end.,Sheet,40)
+============+==< Sheet >=+============+
| Stats      | Bio        | Portrait   |
+============+============+            |
| Skills     | Notes      |            |
+============+=< Footer >=+============+
| The end.                             |
+======================================+
```

How panes are drawn:
- An [ITEM()] keeps its width. The last pane takes the width left over, so the row always reaches the right side. `"justify"` has no effect on panes.
- `"align"` places a shorter pane at the top, middle or bottom.
- A title in an edge is never cut by a line; the line meets the edge without a junction there.
- A rule() first in the box draws a divider right under the top edge, and last, right over the bottom edge.
- A picture in a pane shows as it does anywhere in a box: as its text in a terminal, and as an image no wider than the pane in the web portal.
- A screen reader hears the panes one after another, without lines.

The items are an ordinary row, and stack when they do not fit, when the flex() is not directly in a box, when the box has no border, when the flex() is `"vertical"`, or when the box's sides are wider than one character.

