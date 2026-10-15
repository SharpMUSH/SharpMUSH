<!-- help-article
{
  "corpus": "help",
  "id": "columns-function",
  "lookup": "columns()",
  "aliases": [
    "columns"
  ],
  "sections": []
}
-->
# columns()

`columns(<text>, <width>, <columns>[, <justification>[, <crop>[, <cut>[, <left>[, <between>[, <right>[, <order>[, <delimiter>[, <filler>[, <fill rows>]]]]]]]]]])`

columns() sets the words of `<text>` out in `<columns>` columns, each `<width>` characters wide. By default the words run down the first column, then down the next, and the first columns take one more word each when the words do not fill the last row. A word wider than `<width>` is cut into pieces that each take a cell of their own. Color in `<text>` is dropped.

The optional arguments, in order:

- `<justification>`: where a word sits in its cell. `L` is left (the default), `R` right and `C` centered. `J` widens the spaces inside an item until it fills the cell, which matters only when `<delimiter>` lets an item hold spaces.
- `<crop>`: 1 narrows every column to the widest word, or piece of a word, there is.
- `<cut>`: 1 keeps only the first piece of a word wider than `<width>` and drops the rest.
- `<left>`, `<between>` and `<right>`: text put before each row, between the columns, and after each row.
- `<order>`: 1 runs the words along the first row, then along the next, instead of down the columns.
- `<delimiter>`: the character that separates the words. Without one, any white space does.
- `<filler>`: what fills a cell around its word, instead of spaces. A filler of several characters repeats from its start in every gap.
- `<fill rows>`: a number above 0 makes up a short last row with blank cells, so every row has all its columns.

An empty item between two delimiters is a blank cell, but RhostMUSH, and so columns(), runs it on into the cell after it, making that cell twice as wide.

`<width>` must be at least 1, or columns() returns `#-1 WIDTH MUST BE >= 1`. A width or column count too large for the output limit returns `#-1 WIDTH ARGUMENT OUT OF BOUNDS` or `#-1 COLUMNS ARGUMENT OUT OF BOUNDS`, and an unknown justification returns `#-1 INVALID JUSTIFICATION SPECIFIED`.

Examples:
```sharp
> think columns(this is a test of columns, 10, 3)
this      a         of
is        test      columns
> think columns(this is a test of columns, 10, 3, L, , , , , , 1)
this      is        a
test      of        columns
> think columns(this is a test of columns, 10, 3, R, 0, 0, <*, |, *>)
<*      this|         a|        of*>
<*        is|      test|   columns*>
> think columns(a b c d e, 4, 3, L, , , <, |, >, 0, , , 1)
<a   |c   |e   >
<b   |d   |    >
> think columns(ab cd@e, 6, 2, J, , , , |, , 1, @)
ab  cd|e
```

columns() comes from RhostMUSH and takes the same arguments.

::: seealso
- [ALIGN()]
- [TABLE()]
- [WRAP()]
:::
