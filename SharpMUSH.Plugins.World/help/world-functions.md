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
    {"id": "worlddraw", "heading": "worlddraw()", "lookup": "worlddraw()", "aliases": []},
    {"id": "worldroom", "heading": "worldroom()", "lookup": "worldroom()", "aliases": []},
    {"id": "worldplace", "heading": "worldplace()", "lookup": "worldplace()", "aliases": []}
  ]
}
-->
# World Functions

These read the places of a world map that [@world] keeps. A place is given as x, y and z; an empty z is 0. A map that is not a world map gives `#-1 NOT A WORLD MAP`, and a place out of reach `#-1 BAD PLACE`.

A place whose data hides it reads as nothing to whoever may not examine the map, and only the `render` and `info` keys of its data show to them.

- [worldcell()]: a place's terrain, name, data or id
- [worldway()]: whether a mover can go one way out of a place
- [worlddraw()]: the places around one, drawn as text
- [worldroom()]: the room standing on a place, lending it one
- [worldplace()]: the place a room stands on

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
- `room`: the room standing there, lent or placed; empty when none is. It lends none
- `permanent`: 1 when that room was placed there to stay, else 0

Any other field is `#-1 BAD FIELD`.

```sharp
> think worldcell(Wilds, 4, 4)/[worldcell(Wilds, 4, 4, , name)]
```

Output: forest/Old Oak

## worldway()

`worldway(<map>, <x>, <y>, <z>, <direction>[, <field>[, <mover>]])`

The way out of a place toward a direction: `n`, `e`, `s`, `w`, `u`, `d`, `ne`, `nw`, `se` or `sw`, or the word. The fields:

- `state`: how a mover fares (the default)
- `reason`: why, when the way is not open
- `to`: the place it leads to, as `x,y,z`

The state is `open`; `oneway`, open but not back; `lock`, a locked side or a terrain lock (see [@world lock]); `closed`, when the place it leads to has a closed type; `wall`, also for a one-way side coming the wrong way; or `none`, when nothing is painted there. A diagonal goes round a corner, and is as good as the better of its two routes.

Given a `<mover>`, each lock on the way is evaluated for them, with the map as the locked object: a lock they pass is `open`, and one they do not is `lock` with its reason. Naming anyone but yourself needs examine of the map.

```sharp
> think worldway(Wilds, 4, 4, 0, n)/[worldway(Wilds, 4, 4, 0, n, reason)]
> &RANGER me=yes
> think worldway(Wilds, 2, 1, 0, n)/[worldway(Wilds, 2, 1, 0, n, state, me)]
```

Output:
```
wall/A fallen oak blocks the path.
lock/open
```

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

## worldroom()

- `worldroom(<map>, <x>, <y>[, <z>])`
- `worldroom(<map>, <x>,<y>,<z>)`

The room standing on a place: the one placed there with [@world room], else the one lent to it, else one lent to it now (see [@world sweep]). The second form takes the place in one argument, as [worldway()] gives it with `to`. A place with nothing painted gives `#-1 NOTHING THERE`. Needs control of the map.

It changes the game, so where side-effect functions are turned off it gives `#-1 FUNCTION DISABLED`, and where they need the SIDEFX flag the caller needs it.

```sharp
> think name(worldroom(Wilds, 4, 4))
```

Output: Old Oak

## worldplace()

`worldplace(<room>[, <field>])`

Where a room stands on a world map, empty when it stands on none:

- `coords`: the place, as `x,y,z` (the default)
- `x`, `y` or `z`: one of them
- `map`: the map
- `id`: the place's id, as `MAP`PLACE` holds it
- `permanent`: 1 when the room was placed there to stay, else 0

A room needs examining, unless the caller is in it. Anything but a room gives `#-1 NOT A ROOM`.

```sharp
> think worldplace(here)
```

Output: 4,4,0
