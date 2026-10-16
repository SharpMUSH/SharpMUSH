<!-- help-article
{
  "corpus": "help",
  "id": "fields-function",
  "lookup": "fields()",
  "aliases": [],
  "sections": [
    {
      "id": "options",
      "heading": "Options",
      "lookup": "fields() options"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "fields() examples"
    },
    {
      "id": "wrapping-examples",
      "heading": "Wrapping examples",
      "lookup": "fields() wrapping examples"
    }
  ]
}
-->
# fields()

`fields(<options>, <label1>, <value1>[, ... , <labelN>, <valueN>])`

Labels and their values, the `Sex: Male` part of a sheet. The labels make one column, as wide as the longest; every value starts in the same column, and a value too long for its line wraps under itself rather than under the label. A value can be any text or layout. A field with an empty label carries on the value above it.

When the value column would be narrower than ten cells, each label goes on a line of its own with its value indented under it. In the web portal the fields are a definition list.

- [fields() options]: label alignment, separator, leader, columns and stripes
- [fields() examples]: plain fields, right-aligned labels, leaders and separators
- [fields() wrapping examples]: a long value, and a narrow width

::: seealso
- [LFIELDS()]
- [BOX()]
- [FLEX()]
- [LAYOUT FUNCTIONS]
:::

## Options

Options (the first argument, which may be empty):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"width":<n>` - see [LAYOUT FUNCTIONS].
- `"align":"left"` or `"align":"right"` - where each label sits in its column. Left by default.
- `"sep":"<text>"` - after each label. `": "` by default.
- `"leader":"<text>"` - fills from a short label to the separator: `"leader":"."` draws `Name.....: `.
- `"cols":<n>` - deal the fields into that many columns side by side, down each column first. The columns stack when they do not fit.
- `"gap":<n>` - spaces between those columns. Three by default.
- `"stripe":true` - lays every second field on a background of its own, to help the eye along a row: the theme's `surface` colour, or dark grey when the theme has none. `"stripe":"<codes>"` uses that colour instead, in [ANSI()] codes: `"stripe":"/#203040"`.

## Examples

```sharp
> think fields({{"width":40}},Sex,Male,Species,Human,Origin,Super Robot Wars AG)
Sex:     Male
Species: Human
Origin:  Super Robot Wars AG
```

```sharp
> think fields({{"width":40,"align":"right"}},Sex,Male,Species,Human)
    Sex: Male
Species: Human
> think fields({{"width":40,"leader":"."}},Sex,Male,Species,Human)
Sex....: Male
Species: Human
> think fields({{"width":40,"sep":" = "}},HP,10/12,MP,4/8)
HP = 10/12
MP = 4/8
```

## Wrapping examples

```sharp
> think fields({{"width":30}},Quote,Hooooo? said the machine child as he laughed.)
Quote: Hooooo? said the
       machine child as he
       laughed.
> think fields({{"width":12}},Species,Human)
Species:
  Human
```
