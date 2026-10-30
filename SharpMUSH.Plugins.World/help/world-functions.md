<!-- help-article
{
  "corpus": "help",
  "id": "world-functions",
  "lookup": "world functions",
  "aliases": [],
  "listedIn": ["function list objects"],
  "sections": [
    {"id": "worldcell", "heading": "worldcell()", "lookup": "worldcell()", "aliases": []},
    {"id": "worldway", "heading": "worldway()", "lookup": "worldway()", "aliases": []},
    {"id": "worlddraw", "heading": "worlddraw()", "lookup": "worlddraw()", "aliases": []}
  ]
}
-->
# World Functions

These read the places of a world map that [@world] keeps. A place is given as x, y and z; an empty z is 0. A map that is not a world map gives `#-1 NOT A WORLD MAP`, and a place out of reach `#-1 BAD PLACE`.

A place whose data hides it reads as nothing to whoever may not examine the map, and only the `render` and `info` keys of its data show to them.

- [worldcell()]: a place's terrain, name, data or id
- [worldway()]: whether a mover can go one way out of a place
- [worlddraw()]: the places around one, drawn as text

## worldcell()

`worldcell(<map>, <x>, <y>[, <z>[, <field>]])`

One field of a place:

- `type`: its terrain type, empty when nothing is painted there (the default)
- `label`: its type's label, from the map's legend
- `name`: the name [@world place] gave it
- `data`: the JSON [@world data] keeps there
- `closed`: 1 when its type is closed, else 0
- `reason`: why its type is closed
- `id`: an id that stays the same for that place, for a client's mapper

Any other field is `#-1 BAD FIELD`.

```sharp
> think worldcell(Wilds, 4, 4)/[worldcell(Wilds, 4, 4, , name)]
```

Output: forest/Old Oak

## worldway()

`worldway(<map>, <x>, <y>, <z>, <direction>[, <field>])`

The way out of a place toward a direction: `n`, `e`, `s`, `w`, `u`, `d`, `ne`, `nw`, `se` or `sw`, or the word. The fields:

- `state`: how a mover fares (the default)
- `reason`: why, when the way is not open
- `to`: the place it leads to, as `x,y,z`

The state is `open`; `oneway`, open but not back; `lock`; `closed`, when the place it leads to has a closed type; `wall`, also for a one-way side coming the wrong way; or `none`, when nothing is painted there. A diagonal goes round a corner, and is as good as the better of its two routes.

```sharp
> think worldway(Wilds, 4, 4, 0, n)/[worldway(Wilds, 4, 4, 0, n, reason)]
```

Output: wall/A fallen oak blocks the path.

## worlddraw()

`worlddraw(<map>, <x>, <y>[, <z>[, <radius>[, <mark>]]])`

The places within `<radius>` of a place, 3 by default and at most 20, one character each, north up and a row a line. A place shows its type's icon when that is a character or two, else its label's first letter; a place with nothing painted is a space. `<mark>`, when given, stands in the centre.

```sharp
> think worlddraw(Wilds, 1, 1, 0, 1, @)
```

Output:
```
OOO
O@O
OOO
```
