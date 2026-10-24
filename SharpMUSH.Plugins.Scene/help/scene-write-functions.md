<!-- help-article
{
  "corpus": "help",
  "id": "scene-write-functions",
  "lookup": "scene write functions",
  "aliases": [],
  "listedIn": ["function list objects"],
  "sections": [
    {"id": "scenecreate", "heading": "scenecreate()", "lookup": "scenecreate()", "aliases": []},
    {"id": "sceneinvite", "heading": "sceneinvite()", "lookup": "sceneinvite()", "aliases": []},
    {"id": "scenejoin", "heading": "scenejoin()", "lookup": "scenejoin()", "aliases": []},
    {"id": "sceneset", "heading": "sceneset()", "lookup": "sceneset()", "aliases": []},
    {"id": "sceneaddpose", "heading": "sceneaddpose()", "lookup": "sceneaddpose()", "aliases": []},
    {"id": "scenesetpose", "heading": "scenesetpose()", "lookup": "scenesetpose()", "aliases": []},
    {"id": "sceneeditpose", "heading": "sceneeditpose()", "lookup": "sceneeditpose()", "aliases": []},
    {"id": "sceneundo", "heading": "sceneundo()", "lookup": "sceneundo()", "aliases": ["sceneredo()"]},
    {"id": "scenemovepose", "heading": "scenemovepose()", "lookup": "scenemovepose()", "aliases": []},
    {"id": "scenedelpose", "heading": "scenedelpose()", "lookup": "scenedelpose()", "aliases": []},
    {"id": "sceneaddmember", "heading": "sceneaddmember()", "lookup": "sceneaddmember()", "aliases": []},
    {"id": "sceneunmember", "heading": "sceneunmember()", "lookup": "sceneunmember()", "aliases": []},
    {"id": "scenesetfocus", "heading": "scenesetfocus()", "lookup": "scenesetfocus()", "aliases": []},
    {"id": "sceneshowas", "heading": "sceneshowas()", "lookup": "sceneshowas()", "aliases": []},
    {"id": "sceneplot", "heading": "sceneplot()", "lookup": "sceneplot()", "aliases": []}
  ]
}
-->
# Scene Write Functions

These functions change the Scene System's records, as [@scene] does. Only wizards may use them, and they return `#-1 SIDE EFFECTS DISABLED FOR THIS FUNCTION` while the game turns side-effect functions off (see [@config]). They check nothing else: the scene package's commands decide who may do what. A player or a room may be named by name, `me`, `here` or dbref. A missing scene or pose is `#-1 NOT FOUND`.

- [scenecreate()]: make a scene
- [sceneinvite()]: invite a player to a scene
- [scenejoin()]: let a player join a scene
- [sceneset()]: set one key of a scene
- [sceneaddpose()]: add a pose
- [scenesetpose()]: set one key of a pose
- [sceneeditpose()]: edit a pose's content
- [sceneundo()]: step a pose back or forward through its versions
- [scenemovepose()]: move a pose
- [scenedelpose()]: delete a pose
- [sceneaddmember()]: add a member or change their role
- [sceneunmember()]: remove a member
- [scenesetfocus()]: set or clear a player's current scene
- [sceneshowas()]: set a member's name in a scene
- [sceneplot()]: make, change, link and unlink plots

## scenecreate()

`scenecreate(<room>, <owner>[, <title>[, <pitch>[, <scheduledfor>]]])`

Makes a scene owned by `<owner>` in `<room>`, public, and returns its id. An empty room makes a scene with no room yet. The scene's status is `new`, or `scheduled` when `<scheduledfor>` gives it a time in UTC milliseconds. A scheduled scene needs a title, a pitch and a time still to come, or the result is `#-1 A scheduled scene needs a title, pitch and future UTC millisecond time.` Making a scene raises the ``scene`status`` event (see [scene events]).

```sharp
> think scenecreate(here, me, Harbour Night)
```

Output: 12, the new scene's id.

## sceneinvite()

`sceneinvite(<scene>, <player>)`

Saves an invitation for the player and returns the scene id. It does not make them a member: [scenejoin()] does that, using the invitation up. A player who already has a seat in the scene is `#-1 That player already participates.`

## scenejoin()

`scenejoin(<scene>, <player>)`

Makes the player a member, as `owner` when they own the scene and `participant` otherwise, and returns the scene id. Joining uses up their invitation, and a player who already has a seat stays as they are. It is refused:

- for a scene that is not public, unless they own it, are invited, or are already listed in it: `#-1 NOT FOUND`
- for an `invite-only` scene, unless they own it or are invited: `#-1 This scene is invite-only. Ask its owner for an invitation.`
- when every seat is taken: `#-1 This scene has reached its player cap.`

## sceneset()

`sceneset(<scene>, <key>, <value>)`

Sets one key of a scene, as `@scene/set` does, and returns the scene id. The keys and the values they take are in [@scene creating]. A value those keys refuse is `#-1` and the reason, such as `#-1 A limited scene needs a player cap.`

## sceneaddpose()

`sceneaddpose(<scene>, <author>, <show as>, <origin>, <type>, <source>, <tags>, <content>)`

Adds a pose at the end of the scene, as `@scene/addpose` does, and returns its id; [@scene poses] explains each argument. `<tags>` is one argument, with the tags separated by spaces. The content keeps its colours and layout. A type that cannot be a pose type is `#-1` and the reason. Live scene views show the pose.

## scenesetpose()

`scenesetpose(<pose id>, <key>, <value>)`

Sets one key of a pose, as `@scene/setpose` does, and returns the pose id. A `type` that cannot be a pose type is `#-1` and the reason.

## sceneeditpose()

`sceneeditpose(<pose id>, <editor>, <content>)`

Replaces the pose's content with a new version, recording `<editor>` as its last editor, and returns the pose id. Versions undone before the edit are dropped. An edit that cannot be made is `#-1` and the reason. Live scene views show the edit.

## sceneundo()

`sceneundo(<pose id>)`<br>
`sceneredo(<pose id>)`

`sceneundo()` shows the pose's version before the current one again, and `sceneredo()` the one after it. Each returns the pose id, or the reason when there is no such version, such as `Already at the oldest version.` Live scene views show the change. [sceneedits()] lists the versions.

## scenemovepose()

`scenemovepose(<pose id>[, <after>])`

Puts the pose right after the pose `<after>`, or first in its scene when `<after>` is empty, and returns the pose id. Two poses of different scenes return `The two poses are not in the same scene.`

## scenedelpose()

`scenedelpose(<pose id>)`

Marks the pose deleted and returns its id. It keeps its place, content and history; see `@scene/delete` in [@scene pose history].

## sceneaddmember()

`sceneaddmember(<scene>, <player>, <role>)`

Adds the player to the scene with `<role>`, or changes their role, keeping their focus and persona, and returns the player's dbref. `owner`, `gm` and `participant` take a seat; when the scene's player cap has none left the result is `#-1 This scene has reached its player cap.`

## sceneunmember()

`sceneunmember(<scene>, <player>)`

Removes the player from the scene, every role they had in it, and returns the scene id.

## scenesetfocus()

`scenesetfocus(<player>[, <scene>])`

Makes the scene the player's current one and returns its id. With no scene it clears their focus and returns nothing.

## sceneshowas()

`sceneshowas(<scene>, <player>, <name>)`

Sets the name the member is shown under in the scene and returns it. An empty name goes back to their own. A player who is not a member is `#-1 NOT FOUND`.

## sceneplot()

`sceneplot(<op>, <plot>[, <id>])`

Works on plots and returns the plot's id. `<op>` is one of:

- `create` or `update`: `<plot>` is `<title>|<description>|<owner>`. With `<id>`, the plot of that id is changed; without it, a new plot is made.
- `link`, `unlink`: `<plot>` is a plot id and `<id>` a scene id, which is filed under the plot or taken out of it. A missing plot or scene is `#-1 NOT FOUND`.

Any other `<op>` is `#-1 BAD ARGUMENT FORMAT TO SCENEPLOT`.

::: seealso
- [scene functions]
- [scene type functions]
:::
