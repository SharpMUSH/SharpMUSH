<!-- help-article
{
  "corpus": "help",
  "id": "align",
  "lookup": "align()",
  "aliases": [
    "LALIGN()",
    "align"
  ],
  "sections": [
    {
      "id": "columns",
      "heading": "Column specifications",
      "lookup": "align columns"
    },
    {
      "id": "examples",
      "heading": "Basic examples",
      "lookup": "align examples"
    },
    {
      "id": "decorated-layouts",
      "heading": "Decorated layouts",
      "lookup": "align decorated layouts"
    },
    {
      "id": "merging",
      "heading": "Column merging",
      "lookup": "align column merging"
    }
  ],
  "redirects": {
    "ALIGN2": "align columns",
    "ALIGN3": "align examples",
    "ALIGN4": "align decorated layouts",
    "ALIGN5": "align column merging"
  }
}
-->
# align()

`align(<widths>, <col>[, ... , <colN>[, <filler>[, <colsep>[, <rowsep>]]]])`

`lalign(<widths>, <colList>[, <delim>[, <filler>[, <colsep>[, <rowsep>]]]])`

Creates columns of text, each column designated by `<col>` arguments. Each `<col>` is individually wrapped inside its own column, allowing for easy creation of book pages, newsletters, or the like. In lalign(), `<colList>` is a `<delim>`-separated list of the columns.

`<widths>` is a space-separated list of column widths. '10 10 10' for the widths argument specifies that there are 3 columns, each 10 spaces wide. You can alter the behavior of a column in multiple ways. (See [align columns] for details.)

`<filler>` is a single character that, if given, is the character used to fill empty columns and remaining spaces. `<colsep>`, if given, is inserted between every column, on every row. `<rowsep>`, if given, is inserted between every line. By default, `<filler>` and `<colsep>` are a space, and `<rowsep>` is a newline.

## Column specifications

You can modify column behavior within align(). The basic format is:

`[justification]Width[options][(ansi)]`

### Justification

Placing one of these characters before the width alters the spacing for this column (e.g: <30). Defaults to < (left-justify).

- `<`: left justify (the default).
- `-`: center justify.
- `>`: right justify.
- `_`: full justify.
- `=`: paragraph justify.

### Options

- `.`: repeat while another non-repeating column still has text.
- `` ` ``: merge with the column to the left when this column runs out of text.
- `'`: merge with the column to the right when this column runs out of text.
- `$`: nofill; omit filler after text. Merge-left passes nofill to the left column.
- `x`: truncate each `%r`-separated row instead of wrapping.
- `X`: truncate the whole column at the end of its first row.
- `#`: omit the column separator after this column. Merge-left passes this option to the left column.

Ansi: Place ansi characters (as defined in [ansi()]) within ()s to define a column's ansi markup.

::: seealso
- [CENTER()]
- [LJUST()]
- [RJUST()]
- [TABLE()]
:::

## Basic examples

Examples:
```sharp

    > &line me=align(<3 10 20$,([ljust(get(%0/sex),1,,1)]), name(%0),name(loc(%0)))
    > th iter(lwho(),u(line,##),%b,%r)
      (M) Walker     Tree
      (M) Ashen-Shug Apartment 306
          ar
      (F) Jane Doe   Nowhere
```

```sharp
    > &line me=align(<3 10X 20X$,([ljust(get(%0/sex),1,,1)]), name(%0),name(loc(%0)))
    > th iter(lwho(),u(line,##),%b,%r)
      (M) Walker     Tree
      (M) Ashen-Shug Apartment 306
      (F) Jane Doe   Nowhere
```

### Filler and nofill

```sharp
  > th align(>15 60,Walker,Staff & Developer,x,x)
  xxxxxxxxxWalkerxStaff & Developerxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

```sharp
  > th align(>15 60$,Walker,Staff & Developer,x,x)
  xxxxxxxxxWalkerxStaff & Developer
```


## Decorated layouts

```sharp
    > &haiku me = Alignment function,%rIt justifies your writing,%rBut the words still suck.%rLuke

    > th [align(5 -40 5,,[repeat(-,40)]%r[u(haiku)]%r[repeat(-,40)],,%b,+)]

         +----------------------------------------+
         +          Alignment function,           +
         +       It justifies your writing,       +
         +       But the words still suck.        +
         +                  Luke                  +
         +----------------------------------------+
```

## Column merging

```sharp
  > &dropcap me=%b_______%r|__%b%b%b__|%r%b%b%b|%b|%r%b%b%b|_|
  > &story me=%r'was the night before Christmas, when all through the house%rNot a creature was stirring, not even a mouse.%rThe stockings were hung by the chimney with care,%rIn hopes that St Nicholas soon would be there.
  > th align(9'(ch) 68, u(dropcap), u(story))

   _______
  |__   __| 'was the night before Christmas, when all through the house
     | |    Not a creature was stirring, not even a mouse.
     |_|    The stockings were hung by the chimney with care,
  In hopes that St Nicholas soon would be there.

```

The drop-cap `T` appears in cyan highlight and merges with the story column.
