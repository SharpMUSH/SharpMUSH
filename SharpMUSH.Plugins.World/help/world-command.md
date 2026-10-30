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
    {"id": "world-place", "heading": "Naming places", "lookup": "@world place", "aliases": ["@world/place"]},
    {"id": "world-data", "heading": "A place's data", "lookup": "@world data", "aliases": ["@world/data", "@world/data/merge", "@world/data/clear"]}
  ]
}
-->
# @world

`@world[/<switch>] <map>[/<x>,<y>[,<z>][/<side>]][=<value>]`

A world map keeps its places itself, so a large grid needs no room for each one. Any object can be one. Its places are painted with a terrain type, may have walls and one-way or locked sides, and may have a name and data of their own. The World map in the portal draws them, a window at a time.

`@world <map>` describes the map, and `@world <map>/<x>,<y>,<z>` one place on it: its terrain, name, sides and data. Reading needs examine; every switch needs control of the map. A place is `x,y` or `x,y,z`; z, the level, is 0 when left out. Each number is at most 1000000 either way.

- [@world create]: make an object a world map
- [@world paint]: paint places with a terrain type, or erase them
- [@world wall]: walls, one-way sides and locks
- [@world place]: give a place a name
- [@world data]: JSON a place keeps, and how it is drawn

The [world functions] read the same places.

## Making a world map

`@world/create <map>[=square]`

Makes `<map>` a world map. Square, places on x and y a level at a time, is the only space so far. When the map has no `` MAP`KIND `` yet, it gets `coordinates`, so the World map lists it.

```sharp
> @create Wilds
> @world/create Wilds
```

Output: Wilds is now a world map.

## Painting places

`@world/paint <map>/<x>,<y>[,<z>][:<x2>,<y2>]=[<type>]`

Paints a place, or every place in the box from it to `<x2>,<y2>` on the same level, with a terrain type: letters, digits, `_` and `-`, at most 32. Painting with no type erases the places; their sides stay. One paint covers at most 262144 places.

What a type looks like comes from the map's legend, as for any map: `` MAP`LEGEND`<type>`LABEL ``, `` `TONE `` and `` `ICON ``. A type whose `` `CLOSED `` holds a reason cannot be entered, and the World map shows it closed.

```sharp
> @world/paint Wilds/0,0:9,9=forest
> &MAP`LEGEND`FOREST`LABEL Wilds=Old forest
```

Output: Painted 100 places as forest.

## Walls and ways

`@world/wall <map>/<x>,<y>[,<z>]/<side>=<state>[,<reason>]`

Sets one side of a place: `n`, `e`, `s`, `w`, `u` or `d`, or the word. The place on the other side shares it. The state is `open`, `wall`, `oneway` (a mover may leave this way but not come back) or `lock`. The reason is what a refused mover is told, and what the World map shows.

```sharp
> @world/wall Wilds/4,4/n=wall,A fallen oak blocks the path.
```

Output: The north side of 4,4,0 is now a wall.

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

Two keys are read by the World map. `render` changes how the place is drawn: `icon`, `tone`, `label`, `badge`, `image`, and `hidden` set to `true`, which leaves it off the map for whoever may not examine the map. `info` is the panel it shows: an object of labels and values, or a list of `{"label": ..., "value": ...}`. Only these two keys show to whoever may not examine the map.

```sharp
> @world/data Wilds/4,4={"render":{"icon":"🌳"},"info":{"Danger":"Low"}}
```

Output: Set the data of 4,4,0.
