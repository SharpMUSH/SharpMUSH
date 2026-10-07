# LAYOUT FUNCTIONS

Boxes, titled rules, columns, labelled fields, trees and pictures, described once. A telnet client gets box art, the same kind [ALIGN()], [CENTER()] and [REPEAT()] draw. The web portal draws the same layout as page structure: a bordered card, columns that wrap on a narrow screen, a definition list, a nested list, a real picture.

Available functions:
- box() - a frame with a title in its top edge
- rule() - a line across, with a title in it
- flex() and item() - things side by side
- fields() - labels and values, the values lined up
- tree() and node() - items under their parents
- figure() - a picture, with text art for a terminal
- gauge() - a bar filled to a value
- bullets() - a bulleted or numbered list
- grid() - short items in as many columns as fit
- datatable() and datacolumns() - a table that gives way on a narrow screen, given by rows or by columns
- gradient() - text shaded through colours
- badge() - a coloured status tag

The result is text, so `strlen()`, `mid()`, `edit()` and listen patterns all work on the box art. A layout cut or edited by another function is shown as the text it now is, in the portal too.

Layouts nest. A `flex()`, `fields()` or `tree()` on a line of its own inside a `box()` body becomes columns, fields or a tree inside the box, and a `rule()` becomes a divider meeting the box's sides.

**Width.** Every layout function takes a width. Leave it empty, or write `auto`, and the layout is drawn at the width of the connection that ran the command (78 if it never said). Each telnet reader is then sent it again at the width their own client reported. A number fixes the width for everyone.

**Readers.** A client that cannot show Unicode is sent ASCII borders; see [LAYOUT BORDERS]. A client that says it is a screen reader is sent the content alone, in reading order: no borders, fields as `Label: value` lines, tree levels as indentation.

**Options.** The layout functions take their options as one JSON object: `{"border":"double","pad":2}`. Written straight into an argument it needs a second pair of braces, because the outer pair only keeps its commas together: `box(Hello,,30,{{"border":"double","pad":2}})`. It can also be built with [JSON()]: `box(Hello,,30,json(object,border,"double",pad,2))`. A number option takes a JSON number, an on-or-off option (`"vertical"`, `"across"`, `"mirror"`) takes `true` or `false`, and every other option takes a string. A string keeps its colour, so `"top":"[ansi(hb,=)]"` draws a blue edge; a double quote inside one is written `\\"`. Options apply in the order written, except a preset (`"border"`, `"guide"`), which applies first so the pieces written beside it change it whatever their order. Every function answers bad options the same way: `#-1 LAYOUT OPTIONS MUST BE A JSON OBJECT` for text that is not one object of strings, numbers and booleans, `#-1 UNKNOWN LAYOUT OPTION <KEY>` for a key it does not take, `#-1 DUPLICATE LAYOUT OPTION <KEY>` for a key given twice, `#-1 ARGUMENT OUT OF RANGE` for a number outside its range, and `#-1 INVALID ARGUMENT` for any other value it cannot read.

### Example: a finger sheet
```sharp
> think box(fields({{"cols":2}},Sex,Male,Species,Human,Job,Dark Warrior,Online,1h)%r[rule(Quote)]%rHooooo?,Mannaz Byron,60)
+=====================< Mannaz Byron >=====================+
| Sex:     Male                 Job:    Dark Warrior       |
| Species: Human                Online: 1h                 |
+=========================< Quote >========================+
| Hooooo?                                                  |
+==========================================================+
```

### Example: a stat sheet
```sharp
> think box(fields({{"leader":".","cols":2}},Strength,3,Dexterity,4,Stamina,3,Charisma,2,Manipulation,1,Appearance,3),Attributes,56)
+====================< Attributes >====================+
| Strength.: 3                Charisma....: 2          |
| Dexterity: 4                Manipulation: 1          |
| Stamina..: 3                Appearance..: 3          |
+======================================================+
```

### Example: a who list
```sharp
> think box(flex({{"gap":1}},item(Mannaz%rRaya%rTomas,12),item(0s%r5m%r2h,5),item(Hooooo?%rWriting a scene%rAFK)),Who's Online,50)
+================< Who's Online >================+
| Mannaz       0s    Hooooo?                     |
| Raya         5m    Writing a scene             |
| Tomas        2h    AFK                         |
+================================================+
```

### Example: a picture beside a sheet
```sharp
> think box(figure(https://example.com/mannaz.png,Mannaz,/|_/|%r=^.^=%r%b> <,left,fields(,Sex,Male,Species,Human,Job,Dark Warrior)),Mannaz Byron,44)
+=============< Mannaz Byron >=============+
| /|_/|  Sex:     Male                     |
| =^.^=  Species: Human                    |
|  > <   Job:     Dark Warrior             |
+==========================================+
```

In the portal the picture shows in place of the cat, with the fields beside it.

::: seealso
- [BOX()]
- [RULE()]
- [FLEX()]
- [FIELDS()]
- [TREE()]
- [FIGURE()]
- [GAUGE()]
- [BULLETS()]
- [GRID()]
- [DATATABLE()]
- [DATACOLUMNS()]
- [GRADIENT()]
- [BADGE()]
- [LAYOUT BORDERS]
- [ALIGN()]
:::

# LAYOUT BORDERS

box() and rule() draw with a border style. The game's default is the `layout_border` option; `"border":"<style>"` picks another.

The border options are not only for box() and rule(). Given to any layout that holds others, such as box(), flex(), fields(), tree(), bullets(), grid() or datatable(), they set the border of every box and rule inside it that names none, the way `layout_border` does for the whole game. A box or rule that names its own keeps it.

```sharp
> think flex({{"border":"ascii"}},box(Left,,12),box(Right,,12,{{"border":"double"}}))
+------------------------------------+  +====================================+
| Left                               |  | Right                              |
+------------------------------------+  +====================================+
```

```
+=====< Title >====+  +-----< Title >----+
| mush             |  | ascii            |
+==================+  +------------------+
```

The other styles are drawn with Unicode box-drawing lines: `single` with light lines, `double` with double lines, `heavy` with thick lines, and `rounded` with light lines and rounded corners. Each sets its title between small tee brackets in the top edge. A client without Unicode is sent them in ASCII, as **ASCII clients** below says. The examples in these topics show the ASCII form.

`"border":"none"` draws no frame, only the title and the padded body.

Any part of the style can be replaced, after `"border"` picks the starting point:
- `corner` (all four), or `tl`, `tr`, `bl`, `br` one at a time
- `top`, `bottom`; `side` (both), or `left`, `right`
- `teel`, `teer` - where a divider meets the left and right sides
- `open`, `close` - the brackets round a title

An edge is a pattern repeated along it, so `"top":"=-"` alternates and `"top":"[ansi(hb,=)]"` is blue.

```sharp
> think box(Two fields,,30,{{"side":" ","corner":" ","top":"~","bottom":"~"}})
 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~
  Two fields
 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~
```

**ASCII clients.** A telnet client that did not agree to UTF-8 is sent each box-drawing character as the nearest ASCII one: a light line as `-`, a double or heavy line as `=`, an upright as `|`, and a corner or tee as `+`. A double box stays recognisably double, and colour is kept. A piece that is not box drawing at all, such as an emoji or the tee bracket round a title in the Unicode styles, becomes the `ascii` style's piece instead. Tree guides become `|-` and `` `- ``. A `"sep"` between flex items is translated the same way. The text inside a layout is never changed. The portal ignores border characters and draws borders itself.

::: seealso
- [BOX()]
- [RULE()]
- [TREE()]
- [LAYOUT FUNCTIONS]
:::

# BOX()

`box(<body>[, <title>[, <width>[, <options>]]])`

Draws a frame round *<body>*, with *<title>* set into the top edge. A [RULE()] on a line of its own in the body becomes a divider across the box; a [FLEX()], [FIELDS()] or [TREE()] keeps its layout inside it.

*<width>* is the whole box, borders included; see [LAYOUT FUNCTIONS] for an empty width.

Options:
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS].
- `"title":"left"`, `"title":"center"` or `"title":"right"` - where the title sits. Centred by default.
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

::: seealso
- [RULE()]
- [LAYOUT BORDERS]
- [LAYOUT FUNCTIONS]
:::

# RULE()

`rule([<title>[, <width>[, <options>]]])`

A line across the width, with *<title>* set into it. On a line of its own inside a [BOX()], it divides the box, its ends meeting the box's sides.

Options:
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. Inside a box, a rule with none of these takes the box's border.
- `"title":"left"`, `"title":"center"` or `"title":"right"` - where the title sits. Centred by default.

### Examples
```sharp
> think rule(Factions,40)
==============< Factions >==============
> think rule(Left,30,{{"title":"left"}})
=< Left >=====================
```

::: seealso
- [BOX()]
- [LAYOUT BORDERS]
- [LAYOUT FUNCTIONS]
:::

# FLEX()

`flex(<options>, <item1>[, ... , <itemN>])`

Puts the items side by side, sharing the width. When they do not fit, because an item would fall under its least width, they stack one under the other instead; in the web portal they wrap onto rows of their own. An item is any text or layout, or an [ITEM()] that says how wide it wants to be. Items that do not say share what the others leave.

Options (the first argument, which may be empty):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
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

# ITEM()

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

# FIELDS()

`fields(<options>, <label1>, <value1>[, ... , <labelN>, <valueN>])`

Labels and their values, the `Sex: Male` part of a sheet. The labels make one column, as wide as the longest; every value starts in the same column, and a value too long for its line wraps under itself rather than under the label. A value can be any text or layout. A field with an empty label carries on the value above it.

When the value column would be narrower than ten cells, each label goes on a line of its own with its value indented under it. In the web portal the fields are a definition list.

Options (the first argument, which may be empty):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
- `"width":<n>` - see [LAYOUT FUNCTIONS].
- `"align":"left"` or `"align":"right"` - where each label sits in its column. Left by default.
- `"sep":"<text>"` - after each label. `": "` by default.
- `"leader":"<text>"` - fills from a short label to the separator: `"leader":"."` draws `Name.....: `.
- `"cols":<n>` - deal the fields into that many columns side by side, down each column first. The columns stack when they do not fit.
- `"gap":<n>` - spaces between those columns. Three by default.

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

# TREE()

`tree(<options>, <item1>[, ... , <itemN>])`

Items with the items under them, joined by guide lines. The top-level items sit at the left edge; each level below is drawn after a guide. An item is any text or layout, or a [NODE()] with items of its own. An item too long for its line wraps under itself. In the web portal it is a nested list with the guides drawn as lines.

Options (the first argument, which may be empty):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
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

# NODE()

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

# FIGURE()

`figure(<address>[, <description>[, <art>[, <float>[, <beside>[, <width>]]]]])`

A picture, the text art a terminal shows instead of it, and the text that goes beside it.

*<address>* is the picture, as for [IMAGE()]. *<description>* says what it shows, for a reader who cannot see it. *<art>* is the text art a telnet client sees; its lines keep their own spacing. *<float>* is `none` (the default: the picture on its own lines, *<beside>* under it), `left` or `right`. Floated, *<beside>* flows down the other side of the art in a telnet client and wraps back to the full width once past it; in the web portal, it flows round the picture. *<beside>* can be a layout, such as [FIELDS()].

The picture is shown only when the caller is a Wizard or has the Send_OOB @power, as for image(), and the `image_hosts` option allows its host. Otherwise the portal shows the art too, or the description when there is none.

### Example
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

# GAUGE()

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

# BULLETS()

`bullets(<list>[, <delimiter>[, <options>]])`

Each item of *<list>* on its own line after a bullet or a number. An item too long for its line wraps under its own text, not under the bullet; numbers line up on their right. In the web portal it is a bulleted or numbered list.

Options (the last argument):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
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

# GRID()

`grid(<list>[, <delimiter>[, <options>]])`

Short items, such as names, in as many columns as fit the width, each column as wide as the longest item. The items run down each column, the way `ls` lists files, or across each row with `across`. In the web portal the columns follow the width of the page.

Options (the last argument):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
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

# DATATABLE()

`datatable(<options>, <headings>[, <row1>, ... , <rowN>])`

Rows under headings, the headings and each row's cells split by `|`. Each column is as wide as its widest cell. Begin a heading with `<`, `-` or `>`, as in [ALIGN()], to place its column's text left, centred or right.

When the table is too wide, the columns that wrap give way first, widest first, down to their least widths. If it is still too wide, the least important column is left out, then the next. A column that does not wrap is shown whole or not at all. When not even one column fits, each row is shown as labelled values instead. In the web portal it is a table; on a narrow screen it hides the columns of priority 2 or more, the least important first.

Options (the first argument, which may be empty). The lists are split like the cells, and a list may stop short or leave a column's place empty:
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
- `"width":<n>` - see [LAYOUT FUNCTIONS].
- `"priority":"<list>"` - how important each column is: 1 is the most important. Every column is 1 by default, and among equals the rightmost is left out first.
- `"min":"<list>"` and `"max":"<list>"` - each column's least and greatest width. A cell wider than its column's greatest width wraps.
- `"nowrap":"<list>"` - the numbers of the columns that never wrap.
- `"gap":<n>` - spaces between the columns. Two by default.
- `"sep":"<text>"` - drawn between the columns instead of spaces.
- `"rule":"<text>"` - the line under the headings. `-` by default; `"rule":""` for none.
- `"delim":"<text>"` - what splits the headings, cells and lists. `|` by default.

### Examples
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

# DATACOLUMNS()

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

With lists from other functions, change the delimiter to the space they use: `datacolumns({{"delim":" "}},Name [lwho()],Idle [iter(lwho(),idle(##))])`. The space is quoted: `"delim":"%b"` would leave the option empty. A cell holding a space then needs another delimiter.

::: seealso
- [DATATABLE()]
- [LAYOUT FUNCTIONS]
:::

# GRADIENT()

`gradient(<text>, <colors>[, <options>])`

*<text>* in colours blended one into the next through *<colors>*, which are [ANSI()] codes split by `|`: `r|y|g`, `#ff6000|#8040ff`, or colour names. Spaces take no colour and no place. The text keeps its own markup.

Given a layout, such as a [BOX()] or a [DATATABLE()], the whole block is shaded, borders and all, and stays a layout: each reader still gets it at their own width, and the portal draws it as a card with its text and borders in the gradient.

Options (the last argument):
- `"flow":"characters"`, `"flow":"words"`, `"flow":"across"`, `"flow":"down"` or `"flow":"diagonal"` - which way the colours run. `characters` (the default) runs along the characters that show, on through every line; `words` gives each word one colour; `across` runs left to right by column, every line alike; `down` gives each line one colour; `diagonal` runs from the top-left corner to the bottom-right. The portal runs `characters` and `words` across.
- `"space":"oklch"`, `"space":"oklab"` or `"space":"hsl"` - the space the colours blend in.
- `"mirror":true` - run there and back: red to blue to red.
- `"repeat":<n>` - run through the colours *<n>* times, 1 to 1000.

The colours blend in a space built to look even to the eye, not in plain RGB, whose midpoints go grey and dark (red to green through a muddy olive):
- `oklch` (the default) keeps the middle as bright and vivid as the ends; red to green passes through yellow.
- `oklab` goes straight across, with no swing through other hues; colours far apart meet in a softer middle.
- `hsl` is a brighter, less even rainbow sweep.

A blend needs a client with 256 colours or more, which gets each shade's nearest. A client with only the sixteen standard colours gets bands of the colours you named instead, each character in the one it lies nearest: `r|b` shows as a red half and a blue half. The portal shows the full blend.

### Examples
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

# BADGE()

`badge(<text>[, <kind>])`

*<text>* in brackets, coloured for its kind: `ok` (green), `warn` (yellow), `error` (red), `info` (cyan, the default) or `muted` (grey). For a status beside a name or in a table cell.

### Example
```sharp
> think Mannaz [badge(Online,ok)] Raya [badge(Away,muted)]
Mannaz [Online] Raya [Away]
```

::: seealso
- [ANSI()]
- [LAYOUT FUNCTIONS]
:::
