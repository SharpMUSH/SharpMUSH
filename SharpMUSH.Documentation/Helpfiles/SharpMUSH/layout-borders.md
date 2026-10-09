<!-- help-article
{
  "corpus": "help",
  "id": "layout-borders",
  "lookup": "layout borders",
  "aliases": [],
  "sections": [
    {
      "id": "styles",
      "heading": "Styles",
      "lookup": "layout border styles"
    },
    {
      "id": "nested",
      "heading": "Borders inside other layouts",
      "lookup": "layout borders nested"
    },
    {
      "id": "pieces",
      "heading": "Border pieces",
      "lookup": "layout border pieces"
    },
    {
      "id": "ascii",
      "heading": "ASCII clients",
      "lookup": "layout borders ascii"
    }
  ]
}
-->
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
