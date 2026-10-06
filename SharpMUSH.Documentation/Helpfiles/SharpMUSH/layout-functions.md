# LAYOUT FUNCTIONS

Boxes, titled rules, columns and pictures, said once. A telnet client gets box art, the same kind [ALIGN()], [CENTER()] and [REPEAT()] draw. The web portal draws the same layout as a bordered card whose columns wrap on a narrow screen, with real pictures.

Available functions:
- box()
- rule()
- flex()
- item()
- figure()

The result is text, so `strlen()`, `mid()`, `edit()` and listen patterns all work on the box art. A layout cut or edited by another function is shown as the text it now is, in the portal too.

A layout put inside another nests: a `flex()` on a line of its own inside a `box()` body becomes columns, and a `rule()` becomes a divider meeting the box's sides.

**Width.** Every layout function takes a width. Leave it empty, or write `auto`, and the layout is drawn at the width of the connection that ran the command (78 if it never said). Each telnet reader is then sent it again at the width their own client reported. A number fixes the width for everyone.

**Readers.** A client that cannot show box-drawing characters is sent ASCII borders. A client that says it is a screen reader is sent the content alone, in reading order, without borders.

**Options.** box(), rule() and flex() take options as `key:value` pairs separated by spaces. Put a value in double quotes when it holds a space: `open:"<< "`. A value keeps its colour, so `top:[ansi(hb,=)]` draws a blue edge.

### Example
```sharp
> think box(flex(sep:" | ",item(Sex: Male%rSpecies: Human,35),item(Job: Dark Warrior%rOnline: 1h,36))%r[rule(Quote)]%rHooooo?,Mannaz Byron,78,open:"<< " close:" >>")
+=============================<< Mannaz Byron >>=============================+
| Sex: Male                           | Job: Dark Warrior                    |
| Species: Human                      | Online: 1h                           |
+==================================< Quote >=================================+
| Hooooo?                                                                    |
+============================================================================+
```

::: seealso
- [BOX()]
- [RULE()]
- [FLEX()]
- [FIGURE()]
- [ALIGN()]
:::

# BOX()

`box(<body>[, <title>[, <width>[, <options>]]])`

Draws a frame round *<body>*, with *<title>* set into the top edge. A [RULE()] on a line of its own in the body becomes a divider across the box, and a [FLEX()] becomes columns inside it.

*<width>* is the whole box, borders included; see [LAYOUT FUNCTIONS] for an empty width.

Options:
- `border:<style>` — `none`, `ascii` (`+-|`), `mush` (`+=|`), `single`, `double`, `heavy` or `rounded`. The game's default is the `layout_border` option.
- `title:left`, `title:center` or `title:right` — where the title sits. Centred by default.
- `pad:<n>` — spaces between each side and the body, 0 to 10. One by default.
- Border pieces, each replacing one part of the style: `corner` (all four), `tl`, `tr`, `bl`, `br`, `top`, `bottom`, `side` (both), `left`, `right`, `teel` and `teer` (where a divider meets the sides), and `open` and `close` (the brackets round a title). An edge repeats as a pattern, so `top:=-` alternates.

### Examples
```sharp
> think box(Hello there.,Greeting,30)
+========< Greeting >========+
| Hello there.               |
+============================+
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
- [FLEX()]
- [LAYOUT FUNCTIONS]
:::

# RULE()

`rule([<title>[, <width>[, <options>]]])`

A line across the width, with *<title>* set into it. On a line of its own inside a [BOX()], it divides the box, its ends meeting the box's sides.

It takes the same `border`, `title`, `top`, `open` and `close` options as [BOX()].

### Example
```sharp
> think rule(Factions,40)
==============< Factions >==============
```

::: seealso
- [BOX()]
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

### Example
```sharp
> think flex(sep:" | " width:40,item(Strength%rAgility,18),item(High%rLow,19))
Strength           | High
Agility            | Low
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

::: seealso
- [FLEX()]
- [LAYOUT FUNCTIONS]
:::

# FIGURE()

`figure(<address>[, <description>[, <art>[, <float>[, <beside>[, <width>]]]]])`

A picture, the text art a terminal shows instead of it, and the text that goes beside it.

*<address>* is the picture, as for [IMAGE()]. *<description>* says what it shows, for a reader who cannot see it. *<art>* is the text art a telnet client sees; its lines keep their own spacing. *<float>* is `none` (the default: the picture on its own lines, *<beside>* under it), `left` or `right`. Floated, *<beside>* flows down the other side of the art in a telnet client and wraps back to the full width once past it; in the web portal, it flows round the picture.

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
