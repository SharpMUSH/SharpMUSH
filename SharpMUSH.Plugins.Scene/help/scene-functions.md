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
    {"id": "scenelist-members", "heading": "Scenes by Member", "lookup": "scenelist members", "aliases": ["scenelist mine", "scenelist shared"]},
    {"id": "scenewhere", "heading": "scenewhere()", "lookup": "scenewhere()", "aliases": []},
    {"id": "sceneposes", "heading": "sceneposes()", "lookup": "sceneposes()", "aliases": []},
    {"id": "scenepose", "heading": "scenepose()", "lookup": "scenepose()", "aliases": []},
    {"id": "scenemembers", "heading": "scenemembers()", "lookup": "scenemembers()", "aliases": []},
    {"id": "scenemember", "heading": "scenemember()", "lookup": "scenemember()", "aliases": []},
    {"id": "scenefocus", "heading": "scenefocus()", "lookup": "scenefocus()", "aliases": []}
  ]
}
-->
# Scene Functions

These functions read the Scene System's records and change nothing. A public scene can be read by anyone holding the `scene.view` permission, which the scene package gives to everyone. Every scene can be read by its members and wizards; for anyone else a function about it returns `#-1 PERMISSION`. The reader is the enactor (`%#`), not the object running the code, so a scene package command shows each player what they may see. A missing scene, pose or member is `#-1 NOT FOUND`.

- [scene()]: one field of a scene
- [scenelist()]: scene ids by status, schedule or member
- [scenewhere()]: the active scene in a room
- [sceneposes()]: a scene's pose ids
- [scenepose()]: one field of a pose
- [scenemembers()]: a scene's members
- [scenemember()]: one field of a member
- [scenefocus()]: a player's current scene

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
- `tags`: every tag the scene's poses carry, once each, deleted poses left out
- `cast`: every name the scene's poses are shown under, once each: a pose's persona, or its author's name when it has none; deleted poses left out

Any other key the scene holds, such as `title` or `summary` (the pitch), reads as its text. A detail the game defines reads empty while it is unset. Anything else, an unset `title` included, is `#-1 UNKNOWN SCENE FIELD`.

```sharp
> think scene(12, title)
```

Output: Harbour Night

## scenelist()

`scenelist([<filter>][, <from>, <to>])`
`scenelist(mine[, <player>])`
`scenelist(shared, <player>[, <player>])`

The ids of the scenes you may see among the first 50 the filter finds, separated by spaces. `<filter>` is one of:

- `recent` (the default, and any word not listed here): every scene but the cancelled ones, latest activity first
- `active`, `finished`: the scenes with that status, latest activity first
- `scheduled`: the scenes waiting to run, soonest first: those with a time that are not active or finished, and every paused one, the paused ones with no time last
- `upcoming`: `scheduled` without the scenes more than an hour past their time that never started
- `mine`, `shared`: the scenes a player is a member of; see [scenelist members]

`<from>` and `<to>` are UTC milliseconds. They limit `active`, `finished`, `scheduled` and `upcoming` to the scenes whose time falls between them, leaving out scenes with no time.

A bound that is not a number is `#-1 BAD ARGUMENT FORMAT TO SCENELIST`.

```sharp
> think scenelist(scheduled, soon)
```

Output: #-1 BAD ARGUMENT FORMAT TO SCENELIST

## Scenes by Member

`scenelist(mine[, <player>])`
`scenelist(shared, <player>[, <player>])`

- `mine`: the scenes you are a member of, cancelled ones left out; with `<player>`, every scene of that player's instead
- `shared`: the scenes two players are both members of, latest activity first, found among the first player's latest 200; with one name, you and that player

A player that is not found is `#-1 NO SUCH PLAYER`.

```sharp
> think scenelist(shared, Bob)
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
- `edits`: the ids of its versions, oldest first, separated by spaces: every version the game's retention has kept
- any other key set with [scenesetpose()]

Anything else is `#-1 UNKNOWN POSE FIELD`.

## scenemembers()

`scenemembers(<scene>[, <role>])`

The dbrefs of a scene's members, separated by spaces, or only those with `<role>`. A member whose object is gone is listed by name.

## scenemember()

`scenemember(<scene>, <player>[, <field>])`

One field of a player's membership; `role` when no field is given. The others are `showas` (their name in the scene, empty for their own), `current` (1 when the scene is their focus), `grantedat` (when they joined, in UTC milliseconds) and `name`. Anything else is `#-1 UNKNOWN MEMBER FIELD`, and a player who is not a member is `#-1 NOT FOUND`.

## scenefocus()

`scenefocus(<player>)`

The id of the player's current scene, or `#-1 NOT FOUND` when they have none.

::: seealso
- [scene system]
- [@scene]
:::
