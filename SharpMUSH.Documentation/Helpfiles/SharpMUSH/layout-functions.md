# themes()

`themes()`

The names of the themes the game offers, for the `"theme"` layout option, [@THEME] and the `layout_theme` game option: the built-in ones staff have not disabled, then those staff added (see [@THEME/LIST]).

The built-in ones are `terminal`, which uses the sixteen standard colours each client draws in its own palette; one for each genre a game can name in its MSSP settings, each with its own border, title ornaments, bullet and gauge as well as colours (see [LAYOUT THEMES]): `fantasy historical horror modern mystery romance science-fiction spiritual`; and well-known colour schemes: `catppuccin-mocha catppuccin-latte dracula gruvbox-dark nord solarized-dark solarized-light tokyo-night`.

### Example
```sharp
> think first(themes())
terminal
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

# swatch()

`swatch(<theme>[, <width>])`

A table of *<theme>*'s colours: each colour's name, a sample drawn in it, the colour, the standard colour a sixteen-colour client is sent, and how far it stands out from the background. A colour that stands out less than its use needs is marked with what it needs. A standard colour on its own shows `client`, since it looks however the reader's client draws it. The table is drawn in the theme.

### Example
```sharp
> think swatch(nord,64)
Role        Sample  Colour   16-colour       Contrast
--------------------------------------------------------------
background  -       #2e3440  0 black         -
surface     Sample  #3b4252  8 bright black  -
foreground  Sample  #e5e9f0  7 white         10.3:1
primary     Sample  #81a1c1  4 blue          4.6:1
secondary   Sample  #b48ead  5 magenta       4.4:1 (needs 4.5)
tertiary    Sample  #88c0d0  6 cyan          6.2:1
muted       Sample  #4c566a  8 bright black  1.7:1 (needs 3)
success     Sample  #a3be8c  2 green         6.1:1
warning     Sample  #ebcb8b  3 yellow        8.0:1
error       Sample  #bf616a  1 red           3.1:1 (needs 4.5)
info        Sample  #88c0d0  6 cyan          6.2:1
```

::: seealso
- [THEMES()]
- [THEME()]
- [LAYOUT THEMES]
:::

# box()

`box(<body>[, <title>[, <width>[, <options>]]])`

Draws a frame round *<body>*, with *<title>* set into the top edge. A [RULE()] on a line of its own in the body becomes a divider across the box; a [FLEX()], [FIELDS()] or [TREE()] keeps its layout inside it.

*<width>* is the whole box, borders included; see [LAYOUT FUNCTIONS] for an empty width.

Options:
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS].
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"title":"left"`, `"title":"center"` or `"title":"right"` - where the title sits. Centred by default.
- `"titles":[...]` - more titles in the top edge, written as for [RULE()].
- `"bottomtitles":[...]` - titles in the bottom edge, written the same way.
- `"pad":<n>` - spaces between each side and the body, 0 to 10. One by default.

### Examples
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

::: seealso
- [RULE()]
- [LAYOUT BORDERS]
- [LAYOUT FUNCTIONS]
:::

# rule()

`rule([<title>[, <width>[, <options>]]])`

A line across the width, with *<title>* set into it. On a line of its own inside a [BOX()], it divides the box, its ends meeting the box's sides.

Options:
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. Inside a box, a rule with none of these takes the box's border.
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"title":"left"`, `"title":"center"` or `"title":"right"` - where the title sits. Centred by default.
- `"titles":[...]` - more titles. Each is its text, set in the middle, or an object with `"text"`, `"side"` (`"left"`, `"center"` or `"right"`) and `"priority"` (1 to 1000). Titles on one side sit in the order given. When the line is too narrow for them all, the title with the highest priority is left out first. A title without one takes its side's: 1 on the left, 2 on the right, 3 in the middle.

### Examples
```sharp
> think rule(Factions,40)
==============< Factions >==============
> think rule(Left,30,{{"title":"left"}})
=< Left >=====================
```

A name on the left and a scene on the right. On a narrow screen the scene is left out first, since a right title's priority is 2 and a left one's 1:

```sharp
> think rule(Wren,40,{{"title":"left","titles":\[{"text":"Scene 5","side":"right"}]}})
=< Wren >===================< Scene 5 >=
```

::: seealso
- [BOX()]
- [LAYOUT BORDERS]
- [LAYOUT FUNCTIONS]
:::

# flex()

`flex(<options>, <item1>[, ... , <itemN>])`

Puts the items side by side, sharing the width. When they do not fit, because an item would fall under its least width, they stack one under the other instead; in the web portal they wrap onto rows of their own. An item is any text or layout, or an [ITEM()] that says how wide it wants to be. Items that do not say share what the others leave.

Options (the first argument, which may be empty):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"width":<n>` - the width of the whole row; see [LAYOUT FUNCTIONS] for leaving it out.
- `"gap":<n>` - spaces between items, 0 to 20. Two by default.
- `"sep":"<text>"` - drawn between items on every line instead of the gap, for example `"sep":" | "`.
- `"justify":"<where>"` - `start`, `end`, `center` or `between`: where spare width goes when no item takes it.
- `"align":"<where>"` - `top`, `center` or `bottom`: where a shorter item sits against the tallest.
- `"vertical":true` - always one under the other.

### Examples
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

::: seealso
- [ITEM()]
- [BOX()]
- [ALIGN()]
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

# fields()

`fields(<options>, <label1>, <value1>[, ... , <labelN>, <valueN>])`

Labels and their values, the `Sex: Male` part of a sheet. The labels make one column, as wide as the longest; every value starts in the same column, and a value too long for its line wraps under itself rather than under the label. A value can be any text or layout. A field with an empty label carries on the value above it.

When the value column would be narrower than ten cells, each label goes on a line of its own with its value indented under it. In the web portal the fields are a definition list.

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

### Examples
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

```sharp
> think fields({{"width":30}},Quote,Hooooo? said the machine child as he laughed.)
Quote: Hooooo? said the
       machine child as he
       laughed.
> think fields({{"width":12}},Species,Human)
Species:
  Human
```

::: seealso
- [BOX()]
- [FLEX()]
- [LAYOUT FUNCTIONS]
:::

# tree()

`tree(<options>, <item1>[, ... , <itemN>])`

Items with the items under them, joined by guide lines. The top-level items sit at the left edge; each level below is drawn after a guide. An item is any text or layout, or a [NODE()] with items of its own. An item too long for its line wraps under itself. In the web portal it is a nested list with the guides drawn as lines.

Options (the first argument, which may be empty):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"width":<n>` - see [LAYOUT FUNCTIONS].
- `"guide":"<style>"` - `line` (the default), `rounded`, `heavy`, `double`, `ascii` or `none`. A client without Unicode is sent every guide but `none` as `|-` and `` `- ``, as the examples show.
- `branch`, `last`, `pipe` and `blank` - replace one piece of the guide: before an item with more after it, before the last item, before the lines of a level that carries on, and before the lines of one that has ended. Each is padded to the width of `branch`.

### Examples
```sharp
> think tree({{"width":40}},node(Channels,node(Public,+chat,+ooc),node(Staff,+admin)))
Channels
|- Public
|  |- +chat
|  `- +ooc
`- Staff
   `- +admin
```

```sharp
> think tree({{"width":40,"guide":"rounded"}},node(BBS,node(1. Announcements,Welcome!,Server move),node(2. Scenes,Tonight at 8)))
BBS
|- 1. Announcements
|  |- Welcome!
|  `- Server move
`- 2. Scenes
   `- Tonight at 8
```

```sharp
> think tree({{"width":30,"guide":"ascii"}},node(Mail,Inbox,Sent))
Mail
|- Inbox
`- Sent
> think tree({{"width":30,"branch":"+> ","last":"*> "}},node(Mail,Inbox,Sent))
Mail
+> Inbox
*> Sent
```

::: seealso
- [NODE()]
- [LAYOUT BORDERS]
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

# figure()

`figure(<address>[, <description>[, <art>[, <float>[, <beside>[, <width>]]]]])`

A picture, the text art a terminal shows instead of it, and the text that goes beside it.

- *<address>* - the picture, as for [IMAGE()].
- *<description>* - what it shows, for a reader who cannot see it.
- *<art>* - the text art a telnet client sees; its lines keep their own spacing.
- *<float>* - `none` (the default: the picture on its own lines, *<beside>* under it), `left` or `right`.
- *<beside>* - the text beside the picture. It can be a layout, such as [FIELDS()].

Floated, *<beside>* flows down the other side of the art in a telnet client and wraps back to the full width once past it; in the web portal, it flows round the picture.

## Who sees the picture

The picture is shown only when the caller may use image() (a Wizard, or the Send_Image or Send_OOB @power; the approved role holds Send_Image) and the `image_hosts` option allows its host. Otherwise the portal shows the art too, or the description when there is none.

## Example
```sharp
> think figure(https://example.com/cat.png,A cat,/|_/|%r=^.^=%r%b> <,left,The cat sits by the fire and watches the door all night long.,24)
/|_/|  The cat sits by
=^.^=  the fire and
 > <   watches the door
all night long.
```

::: seealso
- [IMAGE()]
- [MEDIA FUNCTIONS]
- [LAYOUT FUNCTIONS]
:::

# gauge()

`gauge(<value>, <maximum>[, <label>[, <options>]])`

A bar filled to *<value>*'s share of *<maximum>*, with *<label>* before it and the figures after it. The bar takes whatever width the label and figures leave. A value below zero draws an empty bar and one above the maximum a full one. In the web portal it is a meter.

Options (the last argument):
- `"width":<n>` - see [LAYOUT FUNCTIONS].
- `"bar":<n>` - the bar's own width, in cells.
- `"show":"percent"`, `"show":"value"` or `"show":"none"` - the figures after the bar: `50%` (the default), `6/12`, or nothing.
- `"filled":"<text>"` and `"empty":"<text>"` - the pieces the bar is drawn with. A solid block and a light shade by default, sent as `#` and `-` to a client without Unicode.
- `"open":"<text>"` and `"close":"<text>"` - the ends. `[` and `]` by default; leave one empty for none.
- `"gradient":"<colors>"` - shade the bar through these colours, [ANSI()] codes split by `|`: `"gradient":"r|y|g"` or `"gradient":"#ff4040|#40ff80"`. See [GRADIENT()] for how they blend.
- `"shade":"cells"` or `"shade":"value"` - with `cells` (the default) each cell takes the colour at its place along the whole bar, so a fuller bar reaches further along the gradient. With `value` the whole filled part takes one colour, the one at the value's place: red when nearly empty, green when full.
- `"space":"oklch"`, `"space":"oklab"` or `"space":"hsl"`, `"mirror":true` and `"repeat":<n>` - how the colours blend and run; see [GRADIENT()].
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].

### Examples
The examples show what a client without Unicode is sent. A Unicode client sees solid blocks and light shades where these show `#` and `-`.

```sharp
> think gauge(6,12,HP,{{"width":20}})
HP [######-----] 50%
> think gauge(3,4,,{{"bar":8,"show":"value","filled":"=","empty":"-"}})
[======--] 3/4
> think gauge(30,100,XP,{{"width":30,"show":"none","filled":"#","empty":"."}})
XP [########.................]
> think gauge(9,10,HP,{{"width":20,"gradient":"r|y|g"}})
HP [##########-] 90%
```

The last bar runs from red through yellow to green, and the portal draws it with the same blend.

::: seealso
- [GRADIENT()]
- [DATATABLE()]
- [LAYOUT FUNCTIONS]
:::

# bullets()

`bullets(<list>[, <delimiter>[, <options>]])`

Each item of *<list>* on its own line after a bullet or a number. An item too long for its line wraps under its own text, not under the bullet; numbers line up on their right. In the web portal it is a bulleted or numbered list.

Options (the last argument):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"width":<n>` - see [LAYOUT FUNCTIONS].
- `"style":"<style>"` - `bullet` (a round bullet, sent as `*` to a client without Unicode; the default), `dash`, `star`, `number`, `alpha`, `roman` or `none`.
- `"start":<n>` - the first number, letter or numeral. 1 by default.
- `"marker":"<text>"` - your own marker in front of every item.

### Examples
A Unicode client sees round bullets where these examples show `*`.

```sharp
> think bullets(Be kind to other players|No spam|Have fun,|,{{"width":20}})
* Be kind to other
  players
* No spam
* Have fun
> think bullets(Mannaz Raya Tomas,,{{"style":"number","start":9}})
 9. Mannaz
10. Raya
11. Tomas
> think bullets(North South,,{{"style":"roman"}})
 i. North
ii. South
> think bullets(a b,,{{"marker":"->"}})
-> a
-> b
```

::: seealso
- [GRID()]
- [TREE()]
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

# datatable()

`datatable(<options>, <headings>[, <row1>, ... , <rowN>])`

Rows under headings, the headings and each row's cells split by `|`. Each column is as wide as its widest cell. Begin a heading with `<`, `-` or `>`, as in [ALIGN()], to place its column's text left, centred or right.

## Narrow screens

When the table is too wide, the columns that wrap give way first, widest first, down to their least widths. If it is still too wide, the least important column is left out, then the next. A column that does not wrap is shown whole or not at all. When not even one column fits, each row is shown as labelled values instead. In the web portal it is a table; on a narrow screen it hides the columns of priority 2 or more, the least important first.

## Options

Options (the first argument, which may be empty). The lists are split like the cells, and a list may stop short or leave a column's place empty:
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"width":<n>` - see [LAYOUT FUNCTIONS].
- `"priority":"<list>"` - how important each column is: 1 is the most important. Every column is 1 by default, and among equals the rightmost is left out first.
- `"min":"<list>"` and `"max":"<list>"` - each column's least and greatest width. A cell wider than its column's greatest width wraps.
- `"nowrap":"<list>"` - the numbers of the columns that never wrap.
- `"grow":"<list>"` - each column's share of the width left over once every column fits. A table with a column that grows fills its width; one without is as wide as its cells. No column grows past its greatest width.
- `"gap":<n>` - spaces between the columns. Two by default.
- `"sep":"<text>"` - drawn between the columns instead of spaces.
- `"rule":"<text>"` - the line under the headings. `-` by default; `"rule":""` for none.
- `"stripe":true` - lays every second row on a background of its own, to help the eye along a row: the theme's `surface` colour, or dark grey when the theme has none. `"stripe":"<codes>"` uses that colour instead, in [ANSI()] codes: `"stripe":"/#203040"`. Rows shown as labelled values are not striped.
- `"delim":"<text>"` - what splits the headings, cells and lists. `|` by default.

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

::: seealso
- [DATACOLUMNS()]
- [GRID()]
- [FIELDS()]
- [LAYOUT FUNCTIONS]
:::

# datacolumns()

`datacolumns(<options>, <column1>[, ... , <columnN>])`

The same table as [DATATABLE()], given a column at a time instead of a row at a time. Each column is its heading and then its cells, split by `|`, so a list another function made is a column as it stands. A column shorter than the others is filled out with empty cells. It takes the same options, and gives way on a narrow screen the same way.

### Example
```sharp
> think datacolumns({{"width":30,"nowrap":"2"}},Name|Mannaz|Raya,>Idle|0s|5m,Doing|Hooooo?)
Name    Idle  Doing
---------------------
Mannaz    0s  Hooooo?
Raya      5m
```

### Lists from other functions

With lists from other functions, change the delimiter to the space they use: `datacolumns({{"delim":" "}},Name [lwho()],Idle [iter(lwho(),idle(##))])`. The space is quoted: `"delim":"%b"` would leave the option empty. A cell holding a space then needs another delimiter.

::: seealso
- [DATATABLE()]
- [LAYOUT FUNCTIONS]
:::

# gradient()

`gradient(<text>, <colors>[, <options>])`

*<text>* in colours blended one into the next through *<colors>*, which are [ANSI()] codes split by `|`: `r|y|g`, `#ff6000|#8040ff`, or colour names. Spaces take no colour and no place. The text keeps its own markup.

Given a layout, such as a [BOX()] or a [DATATABLE()], the whole block is shaded, borders and all, and stays a layout: each reader still gets it at their own width, and the portal draws it as a card with its text and borders in the gradient.

Options (the last argument):
- `"flow":"characters"`, `"flow":"words"`, `"flow":"across"`, `"flow":"down"` or `"flow":"diagonal"` - which way the colours run. `characters` (the default) runs along the characters that show, on through every line; `words` gives each word one colour; `across` runs left to right by column, every line alike; `down` gives each line one colour; `diagonal` runs from the top-left corner to the bottom-right. The portal runs `characters` and `words` across.
- `"space":"oklch"`, `"space":"oklab"` or `"space":"hsl"` - the space the colours blend in.
- `"mirror":true` - run there and back: red to blue to red.
- `"repeat":<n>` - run through the colours *<n>* times, 1 to 1000.

## Colour spaces

The colours blend in a space built to look even to the eye, not in plain RGB, whose midpoints go grey and dark (red to green through a muddy olive):
- `oklch` (the default) keeps the middle as bright and vivid as the ends; red to green passes through yellow.
- `oklab` goes straight across, with no swing through other hues; colours far apart meet in a softer middle.
- `hsl` is a brighter, less even rainbow sweep.

## Clients with fewer colours

A blend needs a client with 256 colours or more, which gets each shade's nearest. A client with only the sixteen standard colours gets bands of the colours you named instead, each character in the one it lies nearest: `r|b` shows as a red half and a blue half. The portal shows the full blend.

## Examples
```sharp
> think gradient(Mannaz Byron,#ff4040|#ffd040|#40c0ff)
Mannaz Byron
> think gradient(box(Sheet,Mannaz,20),hc|hm,{{"flow":"diagonal"}})
+====< Mannaz >====+
| Sheet            |
+==================+
```

::: seealso
- [ANSI()]
- [GAUGE()]
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

# notice()

`notice(<source>, <text>[, <kind>])`

A message from a system, such as `+job` or `+scene`: *<source>* as a badge, then *<text>*. *<kind>* is one of [BADGE()]'s kinds, and colours the badge. Every kind but `info` (the default) and `muted` also puts a word before the text, so a reader without colour, or a screen reader, still knows what kind of message it is:

- `info` - nothing; the message is news
- `ok` - `Done:`; the thing asked for happened
- `warn` - `Warning:`; something typed was wrong (no such job, a missing title) or needs a second look
- `error` - `Error:`; a real error: the player may not do this, or something broke
- `muted` - nothing, in grey

A client that says it is a screen reader is sent the badge without its brackets and the word in lower case, `JOBS error: Only a Job Admin can do that.`, and an info notice as `JOBS: Job 12 is due Friday.` The portal keeps the brackets from its screen reader the same way.

The word takes the badge's colour. When the game sets `layout_theme`, the kinds take that theme's colours, as [BADGE()]'s do. An empty *<source>* leaves the badge out.

A package says its own name once and uses it for every message, so its players learn which system is talking. Keep the name short and in capitals.

### Examples
```sharp
> think notice(JOBS,Job 12 is due Friday.)
[JOBS] Job 12 is due Friday.
> think notice(JOBS,Job 12 is closed.,ok)
[JOBS] Done: Job 12 is closed.
> think notice(WIKI,There is no page called Lore/Dragons.,warn)
[WIKI] Warning: There is no page called Lore/Dragons.
> think notice(JOBS,Only a Job Admin can do that.,error)
[JOBS] Error: Only a Job Admin can do that.
```

::: seealso
- [BADGE()]
- [LAYOUT THEMES]
- [LAYOUT FUNCTIONS]
:::
