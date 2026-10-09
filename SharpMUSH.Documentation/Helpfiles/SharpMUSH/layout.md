<!-- help-article
{
  "corpus": "help",
  "id": "layout-functions",
  "lookup": "layout functions",
  "aliases": [],
  "sections": [
    {
      "id": "nesting",
      "heading": "Text and nesting",
      "lookup": "layout nesting"
    },
    {
      "id": "width",
      "heading": "Width",
      "lookup": "layout width"
    },
    {
      "id": "readers",
      "heading": "Readers",
      "lookup": "layout readers"
    },
    {
      "id": "options",
      "heading": "Options",
      "lookup": "layout options"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "layout examples"
    }
  ]
}
-->
# Layout Functions

Boxes, titled rules, columns, labelled fields, trees and pictures, described once. A telnet client gets box art, the same kind [ALIGN()], [CENTER()] and [REPEAT()] draw. The web portal draws the same layout as page structure: a bordered card, columns that wrap on a narrow screen, a definition list, a nested list, a real picture.

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
| [TONE()] | text in a theme colour, which each reader sees in their own theme |
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
