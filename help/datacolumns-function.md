<!-- help-article
{
  "corpus": "help",
  "id": "datacolumns-function",
  "lookup": "datacolumns()",
  "aliases": [],
  "sections": [
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "datacolumns() examples"
    },
    {
      "id": "lists",
      "heading": "Lists from other functions",
      "lookup": "datacolumns() lists"
    }
  ]
}
-->
# datacolumns()

`datacolumns(<options>, <column1>[, ... , <columnN>])`

The same table as [DATATABLE()], given a column at a time instead of a row at a time. Each column is its heading and then its cells, split by `|` or by the `delim` option, so a list another function made is a column as it stands. A column shorter than the others is filled out with empty cells.

It takes [datatable() options], splits as [datatable() delimiters] describes, and gives way on a narrow screen as [datatable() narrow screens] describes.

- [datacolumns() examples]: a who list, and a room's contents and exits in a box
- [datacolumns() lists]: columns made by [ITER()], [LWHO()] and other functions

::: seealso
- [DATATABLE()]
- [LDATATABLE()]
- [LAYOUT FUNCTIONS]
:::

## Examples

```sharp
> think datacolumns({{"width":30,"nowrap":"2"}},Name|Mannaz|Raya,>Idle|0s|5m,Doing|Hooooo?)
Name    Idle  Doing
---------------------
Mannaz    0s  Hooooo?
Raya      5m
```

A room's contents and exits side by side in a box, each column growing to share the width. The cells are split by `%r`, so the lists inside the options are too:

```sharp
> think box(datacolumns({{"delim":"%r","grow":"1%r1","rule":""}},Contents%rMannaz%rRaya,Exits%rNorth <N>%rOut <O>),Garden,40)
+==============< Garden >==============+
| Contents           Exits             |
| Mannaz             North <N>         |
| Raya               Out <O>           |
+======================================+
```

## Lists from other functions

A list another function made is a column once its heading is put in front, joined with the delimiter. Functions such as [LWHO()] and [ITER()] use a space, so set `delim` to one:

```sharp
> think datacolumns({{"delim":" "}},Name Mannaz Raya,>Score 10 7)
Name    Score
-------------
Mannaz     10
Raya        7
```

A cell holding a space then needs another delimiter. Make [ITER()] join its results with `%r`, and set `delim` to `%r`:

```sharp
> think datacolumns({{"delim":"%r"}},Name%r[iter(Mannaz Raya,##,,%r)],Motto%r[iter(Mannaz Raya,Hooooo? said ##,,%r)])
Name    Motto
---------------------------
Mannaz  Hooooo? said Mannaz
Raya    Hooooo? said Raya
```
