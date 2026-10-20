<!-- help-article
{
  "corpus": "help",
  "id": "scene-functions",
  "lookup": "scene functions",
  "aliases": [],
  "listedIn": ["function list objects"],
  "sections": [
    {"id": "scene", "heading": "scene()", "lookup": "scene()", "aliases": []},
    {"id": "scenelist", "heading": "scenelist()", "lookup": "scenelist()", "aliases": []},
    {"id": "sceneshared", "heading": "sceneshared()", "lookup": "sceneshared()", "aliases": []},
    {"id": "scenewhere", "heading": "scenewhere()", "lookup": "scenewhere()", "aliases": []},
    {"id": "sceneposes", "heading": "sceneposes()", "lookup": "sceneposes()", "aliases": []},
    {"id": "scenepose", "heading": "scenepose()", "lookup": "scenepose()", "aliases": []},
    {"id": "sceneedits", "heading": "sceneedits()", "lookup": "sceneedits()", "aliases": []},
    {"id": "scenemembers", "heading": "scenemembers()", "lookup": "scenemembers()", "aliases": []},
    {"id": "scenemember", "heading": "scenemember()", "lookup": "scenemember()", "aliases": []},
    {"id": "scenefocus", "heading": "scenefocus()", "lookup": "scenefocus()", "aliases": []},
    {"id": "scenetags", "heading": "scenetags()", "lookup": "scenetags()", "aliases": []},
    {"id": "scenecast", "heading": "scenecast()", "lookup": "scenecast()", "aliases": []}
  ]
}
-->
# Scene Functions

These functions read the Scene System's records and change nothing. A public scene can be read by anyone. A scene that is not public can be read only by its members and wizards; for anyone else a function about it returns `#-1 PERMISSION`. The reader is the enactor (`%#`), not the object running the code, so a scene package command shows each player what they may see. A missing scene, pose or member is `#-1 NOT FOUND`.

- [scene()]: one field of a scene
- [scenelist()]: scene ids by status or schedule
- [sceneshared()]: the scenes two players were both in
- [scenewhere()]: the active scene in a room
- [sceneposes()]: a scene's pose ids
- [scenepose()]: one field of a pose
- [sceneedits()]: the versions of a pose
- [scenemembers()]: a scene's members
- [scenemember()]: one field of a member
- [scenefocus()]: a player's current scene
- [scenetags()]: the tags a scene's poses carry
- [scenecast()]: the names a scene's poses are shown under

The [scene type functions] read the pose types and scene details. The [scene write functions] change scenes.

## scene()

`scene(<id>[, <field>])`

One field of a scene; `status` when no field is given.

- `status`, `id`, `posecount`
- `public`, `istemp`: 1 or 0
- `scheduledfor`, `startedat`, `lastactivityat`: UTC milliseconds; `scheduledfor` is empty when the scene has no time
- `owner`, `starter`: dbrefs; `ownername`, `startername`: names
- `room`: the room's objid, to compare with [loc()]; `roomname`: its name
- `joinpolicy`: `open` unless set; `playercap`: 0 (no cap) unless set
- `details`: the keys of the details the scene has a value for, the game's in their order and then any others, leaving out hidden ones

Any other key the scene holds, such as `title` or `summary` (the pitch), reads as its text. A detail the game defines reads empty while it is unset. Anything else, an unset `title` included, is `#-1 UNKNOWN SCENE FIELD`.

```
> think scene(12, title)
```

Output: Harbour Night

## scenelist()

`scenelist([<filter>][, <from>][, <to>][, <player>])`

The ids of the scenes you may see among the first 50 the filter finds, separated by spaces. `<filter>` is one of:

- `recent` (the default, and any word not listed here): every scene but the cancelled ones, latest activity first
- `active`, `finished`: the scenes with that status, latest activity first
- `scheduled`: the scenes waiting to run, soonest first: those with a time that are not active or finished, and every paused one, the paused ones with no time last
- `upcoming`: `scheduled` without the scenes more than an hour past their time that never started
- `mine`: the scenes you are a member of, cancelled ones left out

`<from>` and `<to>` are UTC milliseconds. They limit `active`, `finished`, `scheduled` and `upcoming` to the scenes whose time falls between them, leaving out scenes with no time. With `mine`, `<player>` lists all of that player's scenes instead of yours, still only those you may see.

A bound that is not a number is `#-1 BAD ARGUMENT FORMAT TO SCENELIST`, and a player that is not found is `#-1 NO SUCH PLAYER`.

## sceneshared()

`sceneshared(<player>, <other>[, <count>])`

The ids of the scenes both players are members of and you may see, latest activity first: `<count>` of them, 1 when left out, found among the first player's latest 200 scenes. Cancelled scenes are left out. A count that is not a whole number above 0 is `#-1 BAD ARGUMENT FORMAT TO SCENESHARED`, and a player that is not found is `#-1 NO SUCH PLAYER`.

```
> think sceneshared(Alice, Bob, 3)
```

Output: 40 31 12

## scenewhere()

`scenewhere(<room>)`

The id of the active scene in `<room>`, or `#-1 NOT FOUND` when there is none. `here` names the room you are in.

## sceneposes()

`sceneposes(<scene>[, <author>][, <count>])`

The ids of a scene's poses in their order, separated by spaces, deleted poses included. `<author>` keeps only that player's poses, and `<count>` only the last that many. A count that is not a whole number above 0 is `#-1 BAD ARGUMENT FORMAT TO SCENEPOSES`.

## scenepose()

`scenepose(<scene>, <pose id>[, <field>])`

One field of a pose; `content` when no field is given. Leave `<scene>` empty to find the pose by its id alone. A pose that is in another scene is `#-1 NOT FOUND`.

- `content`: the pose as it was written, colours and layout kept; `markup`: the same pose, serialised
- `id`, `scene`, `type`, `source`, `tags` (separated by spaces)
- `author`, `origin`, `lasteditor`: dbrefs
- `authorname`, `originname`, `lasteditorname`, `showas`: names
- `createdat`, `lasteditedat`: UTC milliseconds
- `deleted`: 1 or 0; `editcount`: how many versions it has, 1 when never edited
- any other key set with [scenesetpose()]

Anything else is `#-1 UNKNOWN POSE FIELD`.

## sceneedits()

`sceneedits(<scene>, <pose id>)`

The ids of a pose's versions, oldest first, separated by spaces: every version the game's retention has kept. Leave `<scene>` empty to find the pose by its id alone.

## scenemembers()

`scenemembers(<scene>[, <role>])`

The dbrefs of a scene's members, separated by spaces, or only those with `<role>`. A member whose object is gone is listed by name.

## scenemember()

`scenemember(<scene>, <player>[, <field>])`

One field of a player's membership; `role` when no field is given. The others are `showas` (their name in the scene, empty for their own), `current` (1 when the scene is their focus), `grantedat` (when they joined, in UTC milliseconds) and `name`. Anything else is `#-1 UNKNOWN MEMBER FIELD`, and a player who is not a member is `#-1 NOT FOUND`.

## scenefocus()

`scenefocus(<player>)`

The id of the player's current scene, or `#-1 NOT FOUND` when they have none.

## scenetags()

`scenetags(<scene>)`

Every tag the scene's poses carry, once each, separated by spaces. Deleted poses are left out.

## scenecast()

`scenecast(<scene>)`

Every name the scene's poses are shown under, once each, separated by spaces: a pose's persona, or its author's name when it has none. Deleted poses are left out.

::: seealso
- [scene system]
- [@scene]
:::
