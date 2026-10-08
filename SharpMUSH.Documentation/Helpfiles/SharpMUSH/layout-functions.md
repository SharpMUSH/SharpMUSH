# Layout Functions

Boxes, titled rules, columns, labelled fields, trees and pictures, described once. A telnet client gets box art, the same kind [ALIGN()], [CENTER()] and [REPEAT()] draw. The web portal draws the same layout as page structure: a bordered card, columns that wrap on a narrow screen, a definition list, a nested list, a real picture.

This topic covers the functions, how layouts nest, their width, what each kind of reader is sent, and the options every layout function takes, then ends with examples.

## Functions

| Function | Draws |
|---|---|
| [BOX()] | a frame with a title in its top edge |
| [RULE()] | a line across, with a title in it |
| [FLEX()] and [ITEM()] | things side by side |
| [FIELDS()] | labels and values, the values lined up |
| [TREE()] and [NODE()] | items under their parents |
| [FIGURE()] | a picture, with text art for a terminal |
| [GAUGE()] | a bar filled to a value |
| [BULLETS()] | a bulleted or numbered list |
| [GRID()] | short items in as many columns as fit |
| [DATATABLE()] and [DATACOLUMNS()] | a table that gives way on a narrow screen, given by rows or by columns |
| [GRADIENT()] | text shaded through colours |
| [BADGE()] | a coloured status tag |
| [NOTICE()] | a message led by the badge of the system it comes from |
| [THEMES()], [THEME()] and [SWATCH()] | colour themes for layouts; see [LAYOUT THEMES] |

## Text and nesting

The result is text, so `strlen()`, `mid()`, `edit()` and listen patterns all work on the box art. A layout cut or edited by another function is shown as the text it now is, in the portal too.

Layouts nest. A `flex()`, `fields()` or `tree()` on a line of its own inside a `box()` body becomes columns, fields or a tree inside the box, and a `rule()` becomes a divider meeting the box's sides.

## Width

Every layout function takes a width. Leave it empty, or write `auto`, and the layout is drawn at the width of the connection that ran the command (78 if it never said). Each telnet reader is then sent it again at the width their own client reported. A number fixes the width for everyone.

## Readers

A client that cannot show Unicode is sent ASCII borders; see [LAYOUT BORDERS]. A client that says it is a screen reader is sent the content alone, in reading order: no borders, fields as `Label: value` lines, tree levels as indentation.

## Options

The layout functions take their options as one JSON object: `{"border":"double","pad":2}`. Write it in one of two ways:

- straight into the argument, inside a second pair of braces, because the outer pair only keeps its commas together: `box(Hello,,30,{{"border":"double","pad":2}})`
- built with [JSON()]: `box(Hello,,30,json(object,border,"double",pad,2))`

Written straight into an argument, a JSON list needs `\[` in place of its opening bracket, since `[` starts a function call: `{{"title":\["( "," )"]}}`. The same goes for a `[` inside a string. `]` can stay as it is.

### Values

| Option | Takes |
|---|---|
| a number option | a JSON number |
| an on-or-off option (`"vertical"`, `"across"`, `"mirror"`) | `true` or `false` |
| `"stripe"` | `true`, `false` or a string |
| `"theme"` | a string or an object; see [LAYOUT THEMES] |
| every other option | a string |

A string keeps its colour, so `"top":"[ansi(hb,=)]"` draws a blue edge; a double quote inside one is written `\\"`.

Options apply in the order written, except a preset (`"border"`, `"guide"`), which applies first so the pieces written beside it change it whatever their order.

### Errors

Every function answers bad options the same way:

| Error | When |
|---|---|
| `#-1 LAYOUT OPTIONS MUST BE A JSON OBJECT` | the text is not one JSON object |
| `#-1 UNKNOWN LAYOUT OPTION <KEY>` | a key the function does not take |
| `#-1 DUPLICATE LAYOUT OPTION <KEY>` | a key given twice |
| `#-1 ARGUMENT OUT OF RANGE` | a number outside its range |
| `#-1 INVALID ARGUMENT` | any other value it cannot read |

## Examples

### A finger sheet

```sharp
> think box(fields({{"cols":2}},Sex,Male,Species,Human,Job,Dark Warrior,Online,1h)%r[rule(Quote)]%rHooooo?,Mannaz Byron,60)
+=====================< Mannaz Byron >=====================+
| Sex:     Male                 Job:    Dark Warrior       |
| Species: Human                Online: 1h                 |
+=========================< Quote >========================+
| Hooooo?                                                  |
+==========================================================+
```

### A stat sheet

```sharp
> think box(fields({{"leader":".","cols":2}},Strength,3,Dexterity,4,Stamina,3,Charisma,2,Manipulation,1,Appearance,3),Attributes,56)
+====================< Attributes >====================+
| Strength.: 3                Charisma....: 2          |
| Dexterity: 4                Manipulation: 1          |
| Stamina..: 3                Appearance..: 3          |
+======================================================+
```

### A who list

```sharp
> think box(flex({{"gap":1}},item(Mannaz%rRaya%rTomas,12),item(0s%r5m%r2h,5),item(Hooooo?%rWriting a scene%rAFK)),Who's Online,50)
+================< Who's Online >================+
| Mannaz       0s    Hooooo?                     |
| Raya         5m    Writing a scene             |
| Tomas        2h    AFK                         |
+================================================+
```

### A picture beside a sheet

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
- [NOTICE()]
- [LAYOUT BORDERS]
- [LAYOUT THEMES]
- [ALIGN()]
:::

# Layout Borders

box() and rule() draw with a border style. The game's default is the `layout_border` option, `double` unless the game changes it; `"border":"<style>"` picks another. A client without Unicode is sent `double` as the `mush` style shown below.

## Styles

```
+=====< Title >====+  +-----< Title >----+
| mush             |  | ascii            |
+==================+  +------------------+
```

The other styles are drawn with Unicode box-drawing lines:

- `single` - light lines
- `double` - double lines
- `heavy` - thick lines
- `rounded` - light lines with rounded corners

Each sets its title between small tee brackets in the top edge. A client without Unicode is sent them in ASCII, as described under ASCII clients below. The examples in these topics show the ASCII form.

`"border":"none"` draws no frame, only the title and the padded body.

## Borders inside other layouts

The border options are not only for box() and rule(). Given to any layout that holds others, such as box(), flex(), fields(), tree(), bullets(), grid() or datatable(), they set the border of every box and rule inside it that names none, the way `layout_border` does for the whole game. A box or rule that names its own keeps it.

```sharp
> think flex({{"border":"ascii"}},box(Left,,12),box(Right,,12,{{"border":"double"}}))
+------------------------------------+  +====================================+
| Left                               |  | Right                              |
+------------------------------------+  +====================================+
```

## Border pieces

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

## ASCII clients

A telnet client that did not agree to UTF-8 is sent each box-drawing character as the nearest ASCII one:

- a light line as `-`
- a double or heavy line as `=`
- an upright as `|`
- a corner or tee as `+`

A double box stays recognisably double, and colour is kept. A piece that is not box drawing at all, such as an emoji or the tee bracket round a title in the Unicode styles, becomes the `ascii` style's piece instead. Tree guides become `|-` and `` `- ``. A `"sep"` between flex items is translated the same way. The text inside a layout is never changed. The portal ignores border characters and draws borders itself.

::: seealso
- [BOX()]
- [RULE()]
- [TREE()]
- [LAYOUT FUNCTIONS]
:::

# Layout Themes

A theme colours the parts of a layout: borders and gauge bars, titles, table headings, field labels, list bullets, tree guides, and the quiet lines between things. The text inside is left as it is written. A layout takes a theme from its `"theme"` option, and one that names none takes the game's `layout_theme` option. Unset, layouts have no colour. A layout's theme reaches every layout inside it, the way its `"border"` does.

```sharp
> think box(Hi there,Sheet,30,{{"theme":"nord"}})
+==========< Sheet >=========+
| Hi there                   |
+============================+
```

That box is drawn in Nord's blue, its title in Nord's purple and bold. The examples in these topics show the text a client is sent, without its colour.

## Kinds of theme

A theme is one of these:

| Kind | Written |
|---|---|
| a name from [THEMES()] | `"theme":"nord"` |
| a genre | `"theme":"fantasy"` |
| a theme made from one colour | `"theme":{"seed":"#7aa2f7","harmony":"triadic"}` |
| a base16 scheme | `"theme":{"base16":["#2e3440", ... sixteen colours]}` |
| any of these with colours changed | `"theme":{"preset":"nord","colors":{"primary":"#bf616a"}}` |

`terminal` uses only the sixteen standard colours, so each player sees it in the colours their own client is set to. Hundreds of base16 schemes exist for editors and terminals.

### Genres

There is one genre for each MSSP genre: `fantasy`, `historical`, `horror`, `modern`, `mystery`, `romance`, `science-fiction` and `spiritual` (`romance` serves MSSP's Adult as well). A genre theme changes the shapes as well as the colours, down to a box's corners:

- `fantasy` - double lines with diamond corners and fleurons either side of a title, and a gauge of solid and shaded blocks
- `horror` - a cracked heavy line with daggers at the corners
- `mystery` - dashed lines with hollow diamonds
- `romance` - rounded lines with hearts
- `science-fiction` - heavy lines with bracket corners, triangle bullets and a segmented gauge

A client without Unicode gets each in ASCII.

### Themes from one colour

The other colours take their hues from the seed, by `"harmony"`:

- `monochrome` - one hue
- `analogous` - its neighbours (the default)
- `complementary` - the opposite hue
- `split` - either side of the opposite
- `triadic` - three evenly round
- `tetradic` - four

Each is made lighter or darker until it stands out from the background: 3 to 1 for lines, 4.5 to 1 for text, by the measure the web accessibility guidelines use. `"contrast"` from 0 to 1 raises both toward 7 to 1. `"mode":"light"` makes it for a light background; dark is the default.

## Colours

The colours, which `"colors"` sets by name:
- `primary` - borders, rules, gauge bars and table headings
- `secondary` - titles and field labels
- `tertiary` - list bullets
- `muted` - tree guides, separators, the line under table headings, a gauge's empty part
- `success`, `warning`, `error`, `info` - [BADGE()] and [NOTICE()] use these
- `surface` - the background behind every second row of a striped table or field list (`"stripe":true`)
- `background`, `foreground` - what the others are measured against; neither is painted

A colour is `"#rrggbb"`, a standard colour from 0 to 15, or both as `{"rgb":"#88c0d0","slot":6}`. `null` leaves one out.

## Looks

`"look"` sets a theme's shapes:
- `"border"` - a border style from [LAYOUT BORDERS]
- `"title"` - the pieces either side of a title, joining it to the line: `["< ", " >"]`
- `"guide"` - a tree guide style, see [TREE()]
- `"bullet"` - the mark before each item of [BULLETS()]
- `"gauge"` - [GAUGE()]'s pieces: `["[", "#", "-", "]"]`, the opening, the filled part, the empty part and the close
- `"separator"` - after each label of [FIELDS()]
- `"rule"` - the line under [DATATABLE()]'s headings

`"theme":{"preset":"fantasy","look":{"bullet":"+"}}` keeps the rest of fantasy's look; `"look":null` drops it. A theme's border wins over the game's `layout_border`, and a layout's own `"border"` wins over both. A client without Unicode gets the plain ASCII form of each piece.

```sharp
> think box(bullets(Sword|Shield,|),Kit,20,{{"theme":"fantasy"}})
+======< Kit >=====+
| * Sword          |
| * Shield         |
+==================+
```

That is how a client without Unicode sees it. With Unicode the edges are double lines with a diamond at each corner, the title sits between two fleurons, and each bullet is a fleuron.

## Sixteen-colour clients

Each colour carries the standard colour a client with only the sixteen is sent instead, picked by its hue, so a pale blue is sent as blue rather than the grey nearest it. A client without colour is sent the layout as it is; nothing in a layout depends on colour alone. A client without Unicode gets ASCII borders in the same colours.

## Making a theme

[THEME()] shows what a theme comes to, and [SWATCH()] shows whether each colour stands out enough. Each of these starts from something small and lets the generator do the rest.

From one colour, here a blue with triadic harmony. The seed becomes the primary colour and the others are spaced round it:

```sharp
> think json_query(theme({{"seed":"#7aa2f7","harmony":"triadic"}}),get,colors,secondary,rgb)
"#ffa099"
```

The same theme for a light background. Each colour is darkened until it stands out from white:

```sharp
> think json_query(theme({{"seed":"#7aa2f7","harmony":"triadic","mode":"light"}}),get,colors,primary,rgb)
"#4568b8"
```

With `"contrast":1`, every colour stands out at least 7 to 1. The quiet `muted` colour moves furthest: without it, it is `#6b615d` at 3.1 to 1.

```sharp
> think swatch({{"seed":"#d08770","harmony":"split","contrast":1}},64)
Role        Sample  Colour   16-colour         Contrast
-------------------------------------------------------
background  -       #190f0b  0 black           -
surface     Sample  #2b1c18  8 bright black    -
foreground  Sample  #e8dbd7  7 white           13.9:1
primary     Sample  #d98f78  9 bright red      7.3:1
secondary   Sample  #6cd2c9  14 bright cyan    10.5:1
tertiary    Sample  #7fb7ec  12 bright blue    8.9:1
muted       Sample  #a69c97  7 white           7.0:1
success     Sample  #9abd53  10 bright green   8.8:1
warning     Sample  #ee9748  11 bright yellow  8.2:1
error       Sample  #f98b6e  9 bright red      8.0:1
info        Sample  #86acff  12 bright blue    8.4:1
```

From a base16 scheme, copied from an editor or terminal theme. Its sixteen colours go in order, base00 to base0F; base0D, the scheme's blue, becomes the primary colour:

```sharp
> think json_query(theme({{"base16":\["#1d1f21","#282a2e","#373b41","#969896","#b4b7b4","#c5c8c6","#e0e0e0","#ffffff","#cc6666","#de935f","#f0c674","#b5bd68","#8abeb7","#81a2be","#b294bb","#a3685a"]}}),get,colors,primary,rgb)
"#81a2be"
```

From a built-in theme with one colour changed:

```sharp
> think json_query(theme({{"preset":"nord","colors":{"primary":"#bf616a"}}}),get,colors,primary,rgb)
"#bf616a"
```

From a genre with its look changed. This keeps fantasy's colours and gauge but draws ASCII lines, a dash for each bullet and round brackets about the title:

```sharp
> think box(bullets(Sword|Shield,|),Kit,20,{{"theme":{"preset":"fantasy","look":{"bullet":"-","border":"ascii","title":\["( "," )"]}}}})
+------( Kit )-----+
| - Sword          |
| - Shield         |
+------------------+
```

A gauge drawn in pieces of your own, in the reader's own sixteen colours:

```sharp
> think gauge(7,10,HP,{{"width":30,"theme":{"preset":"terminal","look":{"gauge":\["<","=",".",">"]}}}})
HP <===============......> 70%
```

A theme made once can be used again without making it each time. [THEME()] writes it out, and [JSON()] puts it into the options. To keep it, store it in an attribute as [THEME()] shows:

```sharp
> think [setq(0,theme({{"seed":"#d08770","harmony":"split"}}))][box(Hello,,20,json(object,theme,%q0))]
+==================+
| Hello            |
+==================+
```

## Using themes

Written straight into options, a theme object goes inside them as it is: `box(Hi,,30,{{"theme":{"seed":"#d08770"}}})`. Each player can also pick a theme of their own for every layout they read with [@THEME]. [THEME()] writes any theme out in full, to keep in an attribute, and [SWATCH()] shows one's colours and how well each stands out.

::: seealso
- [THEMES()]
- [THEME()]
- [SWATCH()]
- [LAYOUT BORDERS]
- [LAYOUT FUNCTIONS]
:::

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

# @theme

- `@theme[/light|/dark] <object>=<theme>`
- `@theme <object>=`
- `@theme/refresh [<player>]`

Sets the theme every layout is drawn in for a player: boxes, tables, gauges and the rest, from any function in [LAYOUT FUNCTIONS]. *<theme>* is anything the `"theme"` option takes (see [LAYOUT THEMES]): a name from [THEMES()], or a theme written out as JSON inside a second pair of braces, as for a layout function's options: `@theme me={{"seed":"#7aa2f7","harmony":"triadic"}}`. With nothing after the `=`, the theme is cleared.

Output: none.

## Light and dark

`/light` makes the theme for a client with a light background, and `/dark` for a dark one. A theme made from one colour, which includes the genre themes, is made again for that background; a well-known scheme such as `nord` stays as it is. Code given with `/light` or `/dark` is worked out first, and the theme it gives is kept, not the code.

## Parents and the player ancestor

The theme is kept in the `THEME` attribute, and a player without one of their own takes the one their parent has, or their parent's parent, then the player ancestor's (see [ANCESTORS]). Set it on the object a group of players is parented to, a faction for example, and each of them gets it. *<object>* can be any object you control. `@theme <player>=` removes the player's own, so the inherited one shows again.

## Code in a theme

`@theme` keeps what follows the `=` exactly as it is typed, braces and all. Whenever the theme is needed it is evaluated as the player, with `%#` and `%!` the player, and what it works out to is the theme. Evaluating takes off the outer pair of braces round JSON, which is why JSON is typed inside two, and runs anything in `[ ]`, so a list in JSON is typed `\[ \]`. That lets one theme on an ancestor or parent choose by a value:

```
@theme #4=[if(strmatch(get(%#/FACTION),Rebel),horror,nord)]
```

The theme is worked out when the player connects, when `@theme` sets it, and when someone runs `@theme/refresh <player>`. Softcode that changes the value can run `@theme/refresh %#` after it. `/refresh` with no player refreshes your own, and says the theme in use. `@theme` works a theme out for *<object>* itself before it is kept, and refuses one that does not read. Code that works out to nothing is kept: for whoever it works out to nothing for, layouts use the game's theme.

## Which theme wins

A player's theme sits over the game's `layout_theme` and under a theme a layout names itself, so softcode that asks for its own colours still gets them. It applies to telnet and other MU* clients, which are sent each layout drawn again under it; the web portal has themes of its own. You must control *<object>*.

## Themes that do not read

`@theme` refuses a theme it cannot read and says why, leaving the old one in place. A `THEME` attribute set some other way that does not read, or that names a theme since removed or disabled, is ignored: layouts use the game's theme, and the player is told at login why theirs was not used.

## Examples

A built-in theme, then the same one for a light background:

```sharp
> @theme me=fantasy
Theme set.
> @theme/light me=fantasy
Theme set.
```

A theme made from one colour, with every colour made to stand out a little more than it must:

```sharp
> @theme me={{"seed":"#d08770","harmony":"split","contrast":0.5}}
Theme set.
```

A scheme copied from an editor theme, as sixteen base16 colours. A theme added with `@theme/add` (see [@THEME/LIST]) saves typing them, and the `\[ \]`, in each `THEME`:

```sharp
> @theme me={{"base16":\["#1d1f21","#282a2e","#373b41","#969896","#b4b7b4","#c5c8c6","#e0e0e0","#ffffff","#cc6666","#de935f","#f0c674","#b5bd68","#8abeb7","#81a2be","#b294bb","#a3685a"\]}}
Theme set.
```

Nord with red borders, and fantasy with plain bullets and round brackets about each title, its list typed `\[ \]`:

```sharp
> @theme me={{"preset":"nord","colors":{"primary":"#bf616a"}}}
Theme set.
> @theme me={{"preset":"fantasy","look":{"bullet":"-","title":\["( "," )"\]}}}
Theme set.
```

Which theme is in use, worked out again. With none set on the player, a parent or the ancestor:

```sharp
> @theme/refresh
No theme is set, so layouts use the game's theme.
```

With one set, it says `Theme in use:` and the theme.

A theme it cannot read is refused, and the old one stays. Clearing it goes back to the game's theme:

```sharp
> @theme me=nosuch
#-1 UNKNOWN THEME
> @theme me=
Theme cleared.
```

Try a theme on one layout with its `"theme"` option, and check its colours with [SWATCH()], before making it yours. A wizard sets the game's own with `@config/set layout_theme=<name>`, and staff choose which themes there are with [@THEME/LIST].

::: seealso
- [@THEME/LIST]
- [LAYOUT THEMES]
- [THEMES()]
- [SWATCH()]
- [ANCESTORS]
:::

# @theme/list
# @theme/add
# @theme/remove
# @theme/disable
# @theme/enable

- `@theme/list`
- `@theme/add <name>=<theme>`
- `@theme/remove <name>`
- `@theme/disable <name>`
- `@theme/enable <name>`

The themes a game offers: the built-in ones, less those staff disabled, and those staff added. Every place a theme is named reads this list: [@THEME], the `"theme"` layout option, [THEME()], [SWATCH()], [THEMES()] and the `layout_theme` game option.

`/list` shows every theme, built-in and added, and whether it is offered. Anyone may use it.

`/add` adds a theme under *<name>*, or replaces the one added under that name. *<theme>* is evaluated once, as you, and what it works out to is kept: JSON inside a second pair of braces, or the name of another theme. It can start from any theme with `"preset"`, a disabled one included. An added theme it starts from is copied into it, so it does not change when that one does. *<name>* is lower-case letters, digits and hyphens, up to 32 of them, and cannot be a built-in theme's name.

`/remove` removes an added theme. `/disable` takes a built-in theme off the list, and `/enable` puts it back.

A player whose theme stops reading after a change, or is removed or disabled, sees the game's theme and is told why. Players see added themes and changes at once.

Changing the themes needs the `layout.admin` permission. The themes are kept with the game's data, not in `mush.cnf`.

Output: none.

## Examples

Fantasy replaced with a version of the game's own, with red titles:

```
> @theme/disable fantasy
Theme fantasy disabled.
> @theme/add myfantasy={{"preset":"fantasy","colors":{"secondary":"#c0392b"}}}
Theme myfantasy saved.
> @theme me=myfantasy
Theme set.
> @theme me=fantasy
#-1 UNKNOWN THEME
```

::: seealso
- [@THEME]
- [LAYOUT THEMES]
- [THEMES()]
:::

# box()

`box(<body>[, <title>[, <width>[, <options>]]])`

Draws a frame round *<body>*, with *<title>* set into the top edge. A [RULE()] on a line of its own in the body becomes a divider across the box; a [FLEX()], [FIELDS()] or [TREE()] keeps its layout inside it.

*<width>* is the whole box, borders included; see [LAYOUT FUNCTIONS] for an empty width.

Options:
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS].
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
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

# rule()

`rule([<title>[, <width>[, <options>]]])`

A line across the width, with *<title>* set into it. On a line of its own inside a [BOX()], it divides the box, its ends meeting the box's sides.

Options:
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. Inside a box, a rule with none of these takes the box's border.
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
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
