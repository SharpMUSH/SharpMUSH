# LAYOUT FUNCTIONS

Boxes, titled rules, columns, labelled fields, trees and pictures, described once. A telnet client gets box art, the same kind [ALIGN()], [CENTER()] and [REPEAT()] draw. The web portal draws the same layout as page structure: a bordered card, columns that wrap on a narrow screen, a definition list, a nested list, a real picture.

Available functions:
- box() — a frame with a title in its top edge
- rule() — a line across, with a title in it
- flex() and item() — things side by side
- fields() — labels and values, the values lined up
- tree() and node() — items under their parents
- figure() — a picture, with text art for a terminal

The result is text, so `strlen()`, `mid()`, `edit()` and listen patterns all work on the box art. A layout cut or edited by another function is shown as the text it now is, in the portal too.

Layouts nest. A `flex()`, `fields()` or `tree()` on a line of its own inside a `box()` body becomes columns, fields or a tree inside the box, and a `rule()` becomes a divider meeting the box's sides.

**Width.** Every layout function takes a width. Leave it empty, or write `auto`, and the layout is drawn at the width of the connection that ran the command (78 if it never said). Each telnet reader is then sent it again at the width their own client reported. A number fixes the width for everyone.

**Readers.** A client that cannot show Unicode is sent ASCII borders; see [LAYOUT BORDERS]. A client that says it is a screen reader is sent the content alone, in reading order: no borders, fields as `Label: value` lines, tree levels as indentation.

**Options.** box(), rule(), flex(), fields() and tree() take options as `key:value` pairs separated by spaces. Put a value in double quotes when it holds a space: `open:"<< "`. A value keeps its colour, so `top:[ansi(hb,=)]` draws a blue edge. An unknown key is an error naming it.

### Example: a finger sheet
```sharp
> think box(fields(cols:2,Sex,Male,Species,Human,Job,Dark Warrior,Online,1h)%r[rule(Quote)]%rHooooo?,Mannaz Byron,60)
+=====================< Mannaz Byron >=====================+
| Sex:     Male                 Job:    Dark Warrior       |
| Species: Human                Online: 1h                 |
+=========================< Quote >========================+
| Hooooo?                                                  |
+==========================================================+
```

### Example: a stat sheet
```sharp
> think box(fields(leader:. cols:2,Strength,3,Dexterity,4,Stamina,3,Charisma,2,Manipulation,1,Appearance,3),Attributes,56)
+====================< Attributes >====================+
| Strength.: 3                Charisma....: 2          |
| Dexterity: 4                Manipulation: 1          |
| Stamina..: 3                Appearance..: 3          |
+======================================================+
```

### Example: a who list
```sharp
> think box(flex(gap:1,item(Mannaz%rRaya%rTomas,12),item(0s%r5m%r2h,5),item(Hooooo?%rWriting a scene%rAFK)),Who's Online,50)
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
- [LAYOUT BORDERS]
- [ALIGN()]
:::

# LAYOUT BORDERS

box() and rule() draw with a border style. The game's default is the `layout_border` option; `border:<style>` picks another.

```
+=====< Title >====+  +-----< Title >----+  ┌─────┤ Title ├────┐
| mush             |  | ascii            |  │ single           │
+==================+  +------------------+  └──────────────────┘
╔═════╡ Title ╞════╗  ┏━━━━━┫ Title ┣━━━━┓  ╭─────┤ Title ├────╮
║ double           ║  ┃ heavy            ┃  │ rounded          │
╚══════════════════╝  ┗━━━━━━━━━━━━━━━━━━┛  ╰──────────────────╯
```

`border:none` draws no frame, only the title and the padded body.

Any part of the style can be replaced, after `border:` picks the starting point:
- `corner` (all four), or `tl`, `tr`, `bl`, `br` one at a time
- `top`, `bottom`; `side` (both), or `left`, `right`
- `teel`, `teer` — where a divider meets the left and right sides
- `open`, `close` — the brackets round a title

An edge is a pattern repeated along it, so `top:=-` alternates and `top:[ansi(hb,=)]` is blue.

```sharp
> think box(Two fields,,30,side:" " corner:" " top:~ bottom:~)
 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~
  Two fields
 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~
```

**ASCII clients.** A telnet client that did not agree to UTF-8 is sent each box-drawing character as the nearest ASCII one: a light line as `-`, a double or heavy line as `=`, an upright as `|`, and a corner or tee as `+`. A double box stays recognisably double, and colour is kept. A piece that is not box drawing at all, such as an emoji or the `┤ ` bracket round a title, becomes the `ascii` style's piece instead. Tree guides become `|-` and `` `- ``. A `sep:` between flex items is translated the same way. The text inside a layout is never changed. The portal ignores border characters and draws borders itself.

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
- `border:<style>` and the border pieces — see [LAYOUT BORDERS].
- `title:left`, `title:center` or `title:right` — where the title sits. Centred by default.
- `pad:<n>` — spaces between each side and the body, 0 to 10. One by default.

### Examples
```sharp
> think box(Hello there.,Greeting,30)
+========< Greeting >========+
| Hello there.               |
+============================+
```

```sharp
> think box(Hello,Title,20,title:left)
+=< Title >========+
| Hello            |
+==================+
```

```sharp
> think box(Hello there.,,30,border:rounded)
╭────────────────────────────╮
│ Hello there.               │
╰────────────────────────────╯
```

```sharp
> think box(x,,20,top:-= corner:*)
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

It takes the same `border`, `title`, `top`, `open` and `close` options as [BOX()].

### Examples
```sharp
> think rule(Factions,40)
==============< Factions >==============
> think rule(Left,30,title:left)
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
- `width:<n>` — the width of the whole row; see [LAYOUT FUNCTIONS] for leaving it out.
- `gap:<n>` — spaces between items, 0 to 20. Two by default.
- `sep:<text>` — drawn between items on every line instead of the gap, for example `sep:" | "`.
- `justify:start`, `end`, `center` or `between` — where spare width goes when no item takes it.
- `align:top`, `center` or `bottom` — where a shorter item sits against the tallest.
- `vertical` — always one under the other.

### Examples
```sharp
> think flex(sep:" | " width:40,item(Strength%rAgility,18),item(High%rLow,19))
Strength           | High
Agility            | Low
```

```sharp
> think flex(width:40 justify:between,item(Left,8),item(Right,8))
Left                            Right
```

```sharp
> think flex(width:30 sep:" | " align:center,item(One%rTwo%rThree,10),item(Mid,10))
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
- `width:<n>` — see [LAYOUT FUNCTIONS].
- `align:left` or `align:right` — where each label sits in its column. Left by default.
- `sep:<text>` — after each label. `": "` by default.
- `leader:<text>` — fills from a short label to the separator: `leader:.` draws `Name.....: `.
- `cols:<n>` — deal the fields into that many columns side by side, down each column first. The columns stack when they do not fit.
- `gap:<n>` — spaces between those columns. Three by default.

### Examples
```sharp
> think fields(width:40,Sex,Male,Species,Human,Origin,Super Robot Wars AG)
Sex:     Male
Species: Human
Origin:  Super Robot Wars AG
```

```sharp
> think fields(width:40 align:right,Sex,Male,Species,Human)
    Sex: Male
Species: Human
> think fields(width:40 leader:.,Sex,Male,Species,Human)
Sex....: Male
Species: Human
> think fields(width:40 sep:" = ",HP,10/12,MP,4/8)
HP = 10/12
MP = 4/8
```

```sharp
> think fields(width:30,Quote,Hooooo? said the machine child as he laughed.)
Quote: Hooooo? said the
       machine child as he
       laughed.
> think fields(width:12,Species,Human)
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
- `width:<n>` — see [LAYOUT FUNCTIONS].
- `guide:<style>` — `line` (the default), `rounded`, `heavy`, `double`, `ascii` or `none`.
- `branch`, `last`, `pipe` and `blank` — replace one piece of the guide: before an item with more after it, before the last item, before the lines of a level that carries on, and before the lines of one that has ended. Each is padded to the width of `branch`.

### Examples
```sharp
> think tree(width:40,node(Channels,node(Public,+chat,+ooc),node(Staff,+admin)))
Channels
├─ Public
│  ├─ +chat
│  └─ +ooc
└─ Staff
   └─ +admin
```

```sharp
> think tree(width:40 guide:rounded,node(BBS,node(1. Announcements,Welcome!,Server move),node(2. Scenes,Tonight at 8)))
BBS
├─ 1. Announcements
│  ├─ Welcome!
│  ╰─ Server move
╰─ 2. Scenes
   ╰─ Tonight at 8
```

```sharp
> think tree(width:30 guide:ascii,node(Mail,Inbox,Sent))
Mail
|- Inbox
`- Sent
> think tree(width:30 branch:"+> " last:"*> ",node(Mail,Inbox,Sent))
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
├─ Inbox
└─ Sent
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
