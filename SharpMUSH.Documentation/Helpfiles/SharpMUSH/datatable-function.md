<!-- help-article
{
  "corpus": "help",
  "id": "datatable-function",
  "lookup": "datatable()",
  "aliases": [],
  "sections": [
    {
      "id": "narrow-screens",
      "heading": "Narrow screens",
      "lookup": "datatable() narrow screens"
    },
    {
      "id": "options",
      "heading": "Options",
      "lookup": "datatable() options"
    },
    {
      "id": "delimiters",
      "heading": "Delimiters",
      "lookup": "datatable() delimiters"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "datatable() examples"
    }
  ]
}
-->
# datatable()

`datatable(<options>, <headings>[, <row1>, ... , <rowN>])`

Rows under headings. The headings and each row's cells are split by `|`, or by the text the `delim` option names. Each column is as wide as its widest cell. Begin a heading with `<`, `-` or `>`, as in [ALIGN()], to place its column's text left, centred or right.

- [datatable() narrow screens]: how a table too wide gives way
- [datatable() options]: priority, widths, wrapping, growth, separators and stripes
- [datatable() delimiters]: splitting cells on something other than `|`
- [datatable() examples]: one table at three widths

::: seealso
- [DATACOLUMNS()]
- [LDATATABLE()]
- [GRID()]
- [FIELDS()]
- [LAYOUT FUNCTIONS]
:::

## Narrow screens

When the table is too wide, the columns that wrap give way first, widest first, down to their least widths. If it is still too wide, the least important column is left out, then the next. A column that does not wrap is shown whole or not at all. When not even one column fits, each row is shown as labelled values instead. In the web portal it is a table; on a narrow screen it hides the columns of priority 2 or more, the least important first.

## Options

Options (the first argument, which may be empty). The lists are split by `delim`, like the cells, and a list may stop short or leave a column's place empty:
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"width":<n>` - see [LAYOUT FUNCTIONS].
- `"priority":"<list>"` - how important each column is: 1 is the most important. Every column is 1 by default, and among equals the rightmost is left out first.
- `"min":"<list>"` and `"max":"<list>"` - each column's least and greatest width. A cell wider than its column's greatest width wraps.
- `"nowrap":"<list>"` - the numbers of the columns that never wrap.
- `"grow":"<list>"` - each column's share of the width left over once every column fits. A table with a column that grows fills its width; one without is as wide as its cells. No column grows past its greatest width.
- `"gap":<n>` - spaces between the columns. Two by default.
- `"sep":"<text>"` - drawn between the columns instead of spaces. It does not split anything; `delim` does.
- `"rule":"<text>"` - the line under the headings. `-` by default; `"rule":""` for none.
- `"stripe":true` - lays every second row on a background of its own, to help the eye along a row: the theme's `surface` colour, or dark grey when the theme has none. `"stripe":"<codes>"` uses that colour instead, in [ANSI()] codes: `"stripe":"/#203040"`. Rows shown as labelled values are not striped.
- `"delim":"<text>"` - what splits the headings, cells and lists. `|` by default; see [datatable() delimiters].

## Delimiters

The `delim` option names what splits the headings, the cells and the lists in the other options. Change it when a cell can hold a `|`:

```sharp
> think datatable({{"delim":";"}},Name;Motto,Raya;a|b)
Name  Motto
-----------
Raya  a|b
```

- A space, for lists other functions made: `{{"delim":" "}}`. A run of spaces splits once, so no cell can be empty.
- A newline: build the options with `json(object,delim,json(string,%r))`. Inline, `%r` is not valid JSON and `\n` is the letter `n`.
- Text a player typed: a marked delimiter, written inline as `{{"delim":"%*<^>"}}`, splits only at a marked `^`. `json()` drops the mark. See [LIST SEPARATORS].

## Examples

```sharp
> think datatable({{"width":30,"nowrap":"2","min":"6||8"}},Name|>Idle|Doing,Mannaz|0s|Hooooo?,Raya|5m|Writing a scene in the garden)
Name    Idle  Doing
------------------------------
Mannaz    0s  Hooooo?
Raya      5m  Writing a scene
              in the garden
> think datatable({{"width":18,"nowrap":"2","min":"6||8"}},Name|>Idle|Doing,Mannaz|0s|Hooooo?,Raya|5m|Writing a scene in the garden)
Name    Idle
------------
Mannaz    0s
Raya      5m
> think datatable({{"width":18,"priority":"1|3|2","nowrap":"2","min":"6||8"}},Name|>Idle|Doing,Mannaz|0s|Hooooo?,Raya|5m|Writing a scene in the garden)
Name    Doing
------------------
Mannaz  Hooooo?
Raya    Writing a
        scene in
        the garden
```
