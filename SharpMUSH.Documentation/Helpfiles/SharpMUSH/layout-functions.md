# themes()

`themes()`

The names of the themes the game offers, for the `"theme"` layout option, [@THEME] and the `layout_theme` game option: the built-in ones staff have not disabled, then those staff added (see [@THEME/LIST]).

The built-in ones are `sharpmush`, the portal's teal on black, which a game is drawn in unless its `layout_theme` says otherwise; the web portal's themes, under the names the portal gives them: `phosphor daylight magical-girl shojo idol slice-of-life shonen sports mechmachine isekai yokai comic-book rubber-hose eighties-cartoon cyberpunk synthwave space-opera starship-console real-robot super-robot`, and one dark and one light for each kind of colour blindness: `protan-dark protan-light deutan-dark deutan-light tritan-dark tritan-light mono-dark mono-light`; `terminal`, which uses the sixteen standard colours each client draws in its own palette; one for each genre a game can name in its MSSP settings, each with its own border, title ornaments, bullet and gauge as well as colours (see [LAYOUT THEMES]): `fantasy historical horror modern mystery romance science-fiction spiritual`; and well-known colour schemes: `catppuccin-mocha catppuccin-latte dracula gruvbox-dark nord solarized-dark solarized-light tokyo-night`.

### Example
```sharp
> think first(themes())
sharpmush
```

::: seealso
- [THEME()]
- [SWATCH()]
- [LAYOUT THEMES]
- [@THEME/LIST]
:::

# theme()

`theme(<theme>)`

Writes *<theme>* out in full as JSON: its name, whether it is for a dark or light background, and each colour with the standard colour a sixteen-colour client is sent. *<theme>* is anything the `"theme"` option takes; see [LAYOUT THEMES]. A theme made from a seed colour can be made once and kept:

```
think set(me,MYTHEME:[theme({{"seed":"#d08770","harmony":"split"}})])
think box(Hello,,30,json(object,theme,v(MYTHEME)))
```

`&MYTHEME me=...` would keep the `[theme(...)]` call as written instead of the theme it makes. [SET()] tells you the attribute was set, as `@set` does.

The result is read back as it is, so it can be edited and passed on.

### Example
```sharp
> think json_query(theme(nord),get,colors,primary,rgb)
"#81a1c1"
```

::: seealso
- [THEMES()]
- [SWATCH()]
- [LAYOUT THEMES]
:::

# lflex()

`lflex(<options>, <items>[, <delimiter>])`

[FLEX()] with its items given as one list, split on *<delimiter>* (a space when none is given), so a row can be as long as a list another function made. Each item is text, a layout or an [ITEM()], as in flex(). It takes flex()'s options. An empty list draws nothing.

The text of a layout is its box art, so a list of layouts needs a delimiter its art never holds: `^` rather than `|` or a space.

### Example
```sharp
> think lflex({{"sep":" | ","width":40}},iter(Strength Agility Wits,item(##,10),,^),^)
Strength   | Agility    | Wits
```

::: seealso
- [FLEX()]
- [ITEM()]
- [LAYOUT FUNCTIONS]
:::

# item()

`item(<content>[, <width>[, <min>[, <grow>]]])`

One item of a [FLEX()]. *<width>* is `auto`, a number of cells (`35`) or a percentage (`40%`), optionally after [ALIGN()]'s `<`, `-` or `>` to place the text left, centred or right. *<min>* is the fewest cells the item can be drawn in before the items stack. *<grow>* is its share of any spare width, against the other items'; an item with a fixed width takes none unless it is given one.

Used on its own, item() draws its content as a single column.

### Example
```sharp
> think item(Right,>20)
               Right
```

::: seealso
- [FLEX()]
- [LAYOUT FUNCTIONS]
:::

# lfields()

`lfields(<options>, <labels>, <values>[, <delimiter>])`

[FIELDS()] with the labels in one list and the values in another, the way [LALIGN()] takes [ALIGN()]'s columns as a list. The first label goes with the first value, and so on. Use it when the fields are only known when the code runs, such as a sheet built from a list of attributes.

*<delimiter>* splits both lists; a space by default. Labels such as `Full Name` need another one. The two lists must have as many items as each other, or it returns `#-1 NUMBER OF WORDS MUST BE EQUAL`. Two empty lists draw nothing.

*<options>* are [FIELDS()]'s.

### Examples
```sharp
> think lfields({{"width":40}},Sex Species,Male Human)
Sex:     Male
Species: Human
> think lfields({{"width":40}},Sex|Species|Origin,Male|Human|Super Robot Wars AG,|)
Sex:     Male
Species: Human
Origin:  Super Robot Wars AG
> think lfields({{"width":40}},Sex Species,Male)
#-1 NUMBER OF WORDS MUST BE EQUAL
```

::: seealso
- [FIELDS()]
- [LALIGN()]
- [LAYOUT FUNCTIONS]
:::

# ltree()

`ltree(<options>, <items>[, <delimiter>])`

[TREE()] with its top-level items given as one list, split on *<delimiter>* (a space when none is given). Each item is text, a layout or a [NODE()] or [LNODE()], as in tree(). It takes tree()'s options. An empty list draws nothing.

A tree's guide lines hold `|` and spaces, so a list of nodes needs another delimiter, such as `^`.

### Example
```sharp
> think ltree({{"width":40}},iter(Public Staff,lnode(##,switch(##,Public,+chat +ooc,+admin)),,^),^)
Public
|- +chat
`- +ooc
Staff
`- +admin
```

::: seealso
- [TREE()]
- [LNODE()]
- [LAYOUT FUNCTIONS]
:::

# node()

`node(<content>[, <child1>[, ... , <childN>]])`

One item of a [TREE()] and the items under it. Each child is text, a layout, or another node(). Used on its own, node() draws a tree with *<content>* at the top.

### Example
```sharp
> think node(Mail,Inbox,Sent)
Mail
|- Inbox
`- Sent
```

::: seealso
- [TREE()]
- [LAYOUT FUNCTIONS]
:::

# lnode()

`lnode(<content>, <children>[, <delimiter>])`

[NODE()] with its children given as one list, split on *<delimiter>* (a space when none is given). Each child is text, a layout or another node. Used on its own, lnode() draws a tree with *<content>* at the top; an empty list draws *<content>* alone.

### Example
```sharp
> think lnode(Mail,Inbox Sent Drafts)
Mail
|- Inbox
|- Sent
`- Drafts
```

::: seealso
- [NODE()]
- [LTREE()]
- [LAYOUT FUNCTIONS]
:::

# grid()

`grid(<list>[, <delimiter>[, <options>]])`

Short items, such as names, in as many columns as fit the width, each column as wide as the longest item. The items run down each column, the way `ls` lists files, or across each row with `across`. In the web portal the columns follow the width of the page.

Options (the last argument):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"width":<n>` - see [LAYOUT FUNCTIONS].
- `"gap":<n>` - spaces between the columns. Two by default.
- `"across":true` - fill each row before the next.

### Examples
```sharp
> think grid(Mannaz Raya Tomas Ilse Quill Ottoline Bram,,{{"width":32}})
Mannaz    Ilse      Bram
Raya      Quill
Tomas     Ottoline
> think grid(Mannaz Raya Tomas Ilse Quill Ottoline Bram,,{{"width":32,"across":true}})
Mannaz    Raya      Tomas
Ilse      Quill     Ottoline
Bram
```

::: seealso
- [BULLETS()]
- [DATATABLE()]
- [LAYOUT FUNCTIONS]
:::

# ldatatable()

`ldatatable(<options>, <headings>, <rows>[, <delimiter>])`

[DATATABLE()] with its rows given as one list, so one [ITER()] can make a row for each record. It has two delimiters:
- *<delimiter>* splits *<rows>* into rows. A space when none is given.
- The `delim` option splits the headings and each row's cells. `|` by default; see [datatable() delimiters].

It takes datatable()'s options and gives way on a narrow screen the same way. An empty list draws the headings alone.

### Examples
```sharp
> think ldatatable({{"width":30}},Name|>Score,iter(Ann:10 Bob:7,[before(##,:)]|[after(##,:)]))
Name  Score
-----------
Ann      10
Bob       7
```

Cells that hold spaces need another delimiter between the rows:

```sharp
> think ldatatable({{"width":30}},Name|Doing,Mannaz|Hooooo?%rRaya|Writing a scene,%r)
Name    Doing
-----------------------
Mannaz  Hooooo?
Raya    Writing a scene
```

::: seealso
- [DATATABLE()]
- [DATACOLUMNS()]
- [ITER()]
- [LAYOUT FUNCTIONS]
:::

# datacolumns()

`datacolumns(<options>, <column1>[, ... , <columnN>])`

The same table as [DATATABLE()], given a column at a time instead of a row at a time. Each column is its heading and then its cells, split by `|` or by the `delim` option (see [datatable() delimiters]), so a list another function made is a column as it stands. A column shorter than the others is filled out with empty cells. It takes the same options, and gives way on a narrow screen the same way.

### Example
```sharp
> think datacolumns({{"width":30,"nowrap":"2"}},Name|Mannaz|Raya,>Idle|0s|5m,Doing|Hooooo?)
Name    Idle  Doing
---------------------
Mannaz    0s  Hooooo?
Raya      5m
```

### Lists from other functions

With lists from other functions, set `delim` to the delimiter they use: `datacolumns({{"delim":" "}},Name [lwho()],Idle [iter(lwho(),idle(##))])`. A cell holding a space then needs another delimiter, such as a newline; see [datatable() delimiters].

::: seealso
- [DATATABLE()]
- [LAYOUT FUNCTIONS]
:::

# badge()

`badge(<text>[, <kind>])`

*<text>* in brackets, coloured for its kind: `ok` (green), `warn` (yellow), `error` (red), `info` (cyan, the default) or `muted` (grey). For a status beside a name or in a table cell. When the game sets `layout_theme`, the kinds take that theme's success, warning, error, info and muted colours instead.

### Example
```sharp
> think Mannaz [badge(Online,ok)] Raya [badge(Away,muted)]
Mannaz [Online] Raya [Away]
```

::: seealso
- [ANSI()]
- [NOTICE()]
- [LAYOUT FUNCTIONS]
:::
