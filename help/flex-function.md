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
    }
  ]
}
-->
# flex()

`flex(<options>, <item1>[, ... , <itemN>])`

Puts the items side by side, sharing the width. When they do not fit, because an item would fall under its least width, they stack one under the other instead; in the web portal they wrap onto rows of their own. An item is any text or layout, or an [ITEM()] that says how wide it wants to be. Items that do not say share what the others leave.

- [flex() options]: borders, theme, width, gap, separator and alignment
- [flex() examples]: separated columns, spread items and centred items

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
