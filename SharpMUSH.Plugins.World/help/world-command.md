<!-- help-article
{
  "corpus": "help",
  "id": "world-command",
  "lookup": "@world",
  "aliases": [],
  "listedIn": ["commands with @"],
  "sections": [
    {"id": "world-create", "heading": "Making a world map", "lookup": "@world create", "aliases": ["@world/create"]},
    {"id": "world-paint", "heading": "Painting places", "lookup": "@world paint", "aliases": ["@world/paint"]},
    {"id": "world-wall", "heading": "Walls and ways", "lookup": "@world wall", "aliases": ["@world/wall"]},
    {"id": "world-lock", "heading": "Locks", "lookup": "@world lock", "aliases": ["@world/lock"]},
    {"id": "world-link", "heading": "Ways on a graph map", "lookup": "@world link", "aliases": ["@world/link"]},
    {"id": "world-terrain-lock", "heading": "Terrain locks", "lookup": "@world terrain", "aliases": ["terrain locks"]},
    {"id": "world-place", "heading": "Naming places", "lookup": "@world place", "aliases": ["@world/place"]},
    {"id": "world-data", "heading": "A place's data", "lookup": "@world data", "aliases": ["@world/data", "@world/data/merge", "@world/data/clear"]},
    {"id": "world-room", "heading": "Rooms on a map", "lookup": "@world room", "aliases": ["@world/room"]},
    {"id": "world-sweep", "heading": "Lent rooms", "lookup": "@world sweep", "aliases": ["@world/sweep"]}
  ]
}
-->
# @world

`@world[/<switch>] <map>[/<x>,<y>[,<z>][/<side or way>]][=<value>]`

A world map keeps its places itself, so a large grid needs no room for each one. Any object can be one. Its places are painted with a terrain type, may have walls and one-way or locked sides, and may have a name and data of their own. The World map in the portal draws them, a window at a time.

`@world <map>` describes the map, and `@world <map>/<x>,<y>,<z>` one place on it: its terrain, name, sides or ways, and data. Reading needs examine; every switch needs control of the map. A place is `x,y` or `x,y,z`; z, the level, is 0 when left out. Each number is at most 1000000 either way.

- [@world create]: make an object a world map
- [@world paint]: paint places with a terrain type, or erase them
- [@world wall]: walls, one-way sides and locks
- [@world lock]: who passes a locked side
- [@world link]: the named ways between a graph map's places
- [@world terrain]: locks on moving into a terrain type
- [@world place]: give a place a name
- [@world data]: JSON a place keeps, and how it is drawn
- [@world room]: place a room on a place to stay
- [@world sweep]: the rooms lent to places, and taking them back

The [world functions] read the same places.

## Making a world map

`@world/create <map>[=square|hex|pointy-hex|graph]`

Makes `<map>` a world map, a level at a time in one of four spaces:

- `square` (the default): places at x and y, with sides `n`, `e`, `s` and `w`.
- `hex`: flat-topped hexagons at column x and row y, with sides `n`, `ne`, `se`, `s`, `sw` and `nw`. North is y + 1, northeast x + 1, and southeast x + 1 and y - 1.
- `pointy-hex`: pointy-topped hexagons, with sides `ne`, `e`, `se`, `sw`, `w` and `nw`. East is x + 1, northeast y + 1, and northwest x - 1 and y + 1.

- `graph`: places at x and y with no sides. They are joined only by the ways made between them (see [@world link]); x and y say where the World map draws them.

Places on the first three have `u` and `d` too. When the map has no `` MAP`KIND `` yet, it gets `coordinates`, so the World map lists it.

```sharp
> @create Wilds
> @world/create Wilds
> @create Steppe
> @world/create Steppe=hex
```

Output:
```
Wilds is now a world map.
Steppe is now a world map.
```

## Painting places

`@world/paint <map>/<x>,<y>[,<z>][:<x2>,<y2>]=[<type>]`

Paints a place, or every place in the box from it to `<x2>,<y2>` on the same level, with a terrain type: letters, digits, `_` and `-`, at most 32. Painting with no type erases the places; their sides stay. One paint covers at most 262144 places.

What a type looks like comes from the map's legend, as for any map: `` MAP`LEGEND`<type>`LABEL ``, `` `TONE `` and `` `ICON ``, and `` `CHAR `` the character a text drawing such as [worlddraw()] shows for it. A type whose `` `CLOSED `` holds a reason cannot be entered, and the World map shows it closed. A type can be locked instead (see [@world terrain]).

```sharp
> @world/paint Wilds/0,0:9,9=forest
> &MAP`LEGEND`FOREST`LABEL Wilds=Old forest
```

Output: Painted 100 places as forest.

## Walls and ways

`@world/wall <map>/<x>,<y>[,<z>]/<side>=<state>[,<reason>]`

Sets one side of a place, by its letters or the word, from the sides its space has (see [@world create]). The place on the other side shares it. The state is `open`, `wall`, `oneway` (a mover may leave this way but not come back) or `lock` (see [@world lock]). The reason is what a refused mover is told, and what the World map shows. A locked side keeps its key; any other state drops it.

```sharp
> @world/wall Wilds/4,4/n=wall,A fallen oak blocks the path.
> @world/wall Steppe/0,0/sw=wall,A thorn hedge.
```

Output:
```
The north side of 4,4,0 is now a wall.
The southwest side of 0,0,0 is now a wall.
```

## Locks

`@world/lock <map>/<x>,<y>[,<z>]/<side>=[<key>]`

Locks one side of a place with a lock key, as `@lock` locks an exit: a mover who passes the key goes through, with the map as the locked object. The reason set with `@world/wall ...=lock,<reason>` stays. With no key, the side is open again and its reason goes. A side set to `lock` with `@world/wall` and given no key lets nobody through.

[worldway()] gives `lock` for a locked way, and evaluates its keys when given a mover.

```sharp
> @world/wall Wilds/2,1/n=lock,The gate is barred.
> @world/lock Wilds/2,1/n=RANGER:yes
```

Output: The north side of 2,1,0 is now locked.

## Ways on a graph map

- `@world/link <map>/<x>,<y>[,<z>]=<x2>,<y2>[,<z2>],<names>[,<names back>]`
- `@world/link <map>/<x>,<y>[,<z>]/<way>`

Makes a way out of a place of a graph map, to any place on any level of it. Its names are given as an exit's are, `;` between them: the first is shown, and any of them walks it. Given names back, the way back is made too. A way of the same name out of the same place is replaced; a way that would share any other name with another way out of that place is refused. With a way and no `=`, removes that way; the way back stays. A place has at most 64 ways out.

On a graph map, `@world/wall` and `@world/lock` take a way's name where a side goes. A way is `open`, `wall` (it stays, and refuses with its reason) or `lock`. A way leads one way already, so it is never `oneway`.

```sharp
> @create Downs
> @world/create Downs=graph
> @world/paint Downs/0,0=grass
> @world/paint Downs/4,2=grass
> @world/link Downs/0,0=4,2,Old Road;road,Back Road;back
> @world/wall Downs/4,2/back=wall,The bridge is out.
```

Output:
```
Old Road leads from 0,0,0 to 4,2,0, and Back Road from 4,2,0 back.
The way Back Road out of 4,2,0 is now a wall.
```

## Terrain locks

The map's legend can lock moving into a terrain type:

- `` MAP`LEGEND`<type>`LOCK ``: the key for entering a place of that type from a place of another type. Moving between two places of the type needs no key.
- `` MAP`LEGEND`<type>`LOCK`<from> ``: the key for entering it from a place of type `<from>`, in place of `` `LOCK ``. Set for the type itself, it locks moving within it.
- `` MAP`LEGEND`<type>`FAILURE `` and `` `FAILURE`<from> ``: what a refused mover is told.

A legend key takes what `@lock` takes; names in it are found as the map finds them. `#FALSE` lets nobody in, and `#TRUE` everyone.

```sharp
> &MAP`LEGEND`MOUNTAIN`LOCK Wilds=#FALSE
> &MAP`LEGEND`MOUNTAIN`LOCK`HILLS Wilds=CLIMBER:yes
> &MAP`LEGEND`MOUNTAIN`FAILURE Wilds=The slope is too steep.
> think worldway(Wilds, 1, 1, 0, n, reason, me)
```

Output: The slope is too steep.

## Naming places

`@world/place <map>/<x>,<y>[,<z>]=[<name>]`

Gives a place its own name, which the World map and [worldcell()] show. An empty name takes it off.

```sharp
> @world/place Wilds/4,4=Old Oak
```

Output: Named 4,4,0 Old Oak.

## A place's data

- `@world/data <map>/<x>,<y>[,<z>]=<json object>`
- `@world/data/merge <map>/<x>,<y>[,<z>]=<json object>`
- `@world/data/clear <map>/<x>,<y>[,<z>]`

Keeps a JSON object on a place, taken as typed, at most as long as an attribute value may be. `/merge` merges it into what is there: a key set to `null` is removed, an object is merged into the object under its key. `/clear` removes it all.

Two keys are read by the World map. `render` changes how the place is drawn: `icon`, `tone`, `label`, `badge`, `image`, and `hidden` set to `true`, which draws it as bare terrain for whoever may not examine the map: no name, data, room or walls of its own. `info` is the panel it shows: an object of labels and values, or a list of `{"label": ..., "value": ...}`. Only these two keys show to whoever may not examine the map.

```sharp
> @world/data Wilds/4,4={"render":{"icon":"T"},"info":{"Danger":"Low"}}
```

Output: Set the data of 4,4,0.

## Rooms on a map

`@world/room <map>/<x>,<y>[,<z>]=[<room>]`

Places a room on a place to stay. It joins the map's zone and holds the place's id in `` MAP`PLACE ``, and [worldroom()] gives it for that place from then on. A room placed elsewhere moves. Placing the room lent to the place keeps it, as it is. An empty room takes the room placed there off; it keeps its zone. Needs control of the room too.

`@world <map>/<x>,<y>,<z>` names the room standing on a place, and whether it is lent or there to stay.

```sharp
> @dig Wayside Inn
> @world/room Wilds/4,5=Wayside Inn
```

Output: Wayside Inn now stands on 4,5,0 to stay.

## Lent rooms

`@world/sweep`

A place with no room of its own is lent one by [worldroom()]: a spare room when there is one, else a new one. A lent room is named after its place, or its terrain's label and where it is ("Old forest (3,1,0)"), joins the map's zone, takes the parent the map's `` MAP`CELLPARENT `` names, is set FLOATING, and holds the place's id in `` MAP`PLACE ``.

Once a lent room has held nothing for 5 minutes it goes back: renamed Spare room, out of the zone and the parent, and a scene held there let go. It waits as a spare for another place on its owner's maps, and keeps anything else put on it. A room holding anyone or anything stays lent; one whose map is destroyed waits until it is empty. Spares past the newest 50, or waiting a week, are destroyed.

The game checks once a minute. `@world/sweep` takes back every empty lent room now. Wizards only.

```sharp
> @world/sweep
```

Output: Took back 3 empty rooms, and destroyed 0 spares.
