<!-- help-article
{
  "corpus": "help",
  "id": "scene-command",
  "lookup": "@scene",
  "aliases": ["@scene/noeval"],
  "listedIn": ["@-wizard"],
  "sections": [
    {
      "id": "scene-reading",
      "heading": "Reading scenes",
      "lookup": "@scene reading",
      "aliases": ["@scene/list", "@scene/get"]
    },
    {
      "id": "scene-creating",
      "heading": "Creating and changing scenes",
      "lookup": "@scene creating",
      "aliases": ["@scene/create", "@scene/set"]
    },
    {
      "id": "scene-poses",
      "heading": "Writing poses",
      "lookup": "@scene poses",
      "aliases": ["@scene/addpose", "@scene/setpose", "@scene/editpose"]
    },
    {
      "id": "scene-pose-history",
      "heading": "Pose history and order",
      "lookup": "@scene pose history",
      "aliases": ["@scene/undo", "@scene/redo", "@scene/move", "@scene/delete"]
    },
    {
      "id": "scene-members",
      "heading": "Members and focus",
      "lookup": "@scene members",
      "aliases": ["@scene/member", "@scene/unmember", "@scene/focus", "@scene/showas", "@scene/notice"]
    },
    {
      "id": "scene-plots",
      "heading": "Plots",
      "lookup": "@scene plots",
      "aliases": ["@scene/plot", "@scene/plot/create", "@scene/plot/link", "@scene/plot/unlink"]
    },
    {
      "id": "scene-types",
      "heading": "Pose types",
      "lookup": "@scene types",
      "aliases": ["@scene/types"]
    }
  ]
}
-->
# @scene

`@scene[/<switch>][/noeval] <target>[=<value>]`

`@scene` reads and changes the Scene System's records directly. Only wizards may use it, and it checks nothing else: the scene package's `+scene` commands decide who may do what, then run it. Players use those commands, not this one.

`@scene <scene id>` shows a scene: a line with its id, status, title, owner, room and number of poses, then a line for each member with their role, `(as <name>)` when they have a persona, and `*` when the scene is their focus.

Where an argument names a player or a room, a name, `me`, `here` or a dbref works. `/noeval` takes the arguments as typed, without evaluating them. Messages start with `[SCENE]`, as [notice()] writes them. A switch whose arguments are missing does nothing and returns `#-1 BAD ARGUMENTS TO SCENE COMMAND`.

- [@scene reading]: lists and keys, with `/list` and `/get`
- [@scene creating]: new scenes and their keys, with `/create` and `/set`
- [@scene poses]: `/addpose`, `/setpose` and `/editpose`
- [@scene pose history]: `/undo`, `/redo`, `/move` and `/delete`
- [@scene members]: `/member`, `/unmember`, `/focus`, `/showas` and `/notice`
- [@scene plots]: `/plot`, `/plot/create`, `/plot/link` and `/plot/unlink`
- [@scene types]: the pose types, with `/types`

The same changes can be made from softcode with the [scene write functions].

## Reading scenes

- `@scene/list [<filter>]`
- `@scene/get <scene id>[/<key>]`

`/list` says how many scenes match, then shows each one's summary line. The filter is `active`, `finished`, `scheduled`, `upcoming`, `mine` (scenes you are a member of), or empty for every scene but the cancelled ones; [scenelist()] says what each holds. It shows at most 50.

`/get` shows a scene's summary line, or with a key, that key's value. The keys are `status`, `public` and `istemp` (1 or 0), `scheduledfor`, `startedat` and `lastactivityat` (times), `room`, `owner` and `starter` (names), `posecount`, and any other key the scene holds, such as `title`, which is empty when unset.

```
> @scene/get 12/status
```

Output: `[SCENE] 12/status = active`

## Creating and changing scenes

- `@scene/create <room>,<owner>[,<title>]`
- `@scene/set <scene id>/<key>=<value>`

`/create` makes a scene owned by `<owner>` in `<room>`, public and with the status `new`, and says its id. Leave the room empty for a scene with no room yet.

`/set` sets one key of a scene; an empty value clears it. These keys are the scene's own fields:

- `status`: any word; see [scene events] for the ones the package uses
- `public`, `istemp`: `1`, `true`, `yes` or `on` for yes, anything else for no
- `scheduledfor`: a UTC time in milliseconds; empty clears it
- `room`, `owner`: a dbref
- `plot`: a plot id, which files the scene under that plot
- `joinpolicy`: `open`, `invite-only`, or `limited`, which needs a player cap
- `playercap`: 0 (no cap) to 10000, never below the members already seated

Any other key, such as `title` or `summary` (the pitch), is kept as text. A value these keys refuse warns that there is no such scene. Making a scene or changing its status raises the ``scene`status`` event, and changing any other key but `plot` raises ``scene`change`` (see [scene events]).

## Writing poses

- `@scene/addpose <scene id>=<author>,<show as>,<origin>,<type>,<source>,<tags>,<content>`
- `@scene/setpose <pose id>/<key>=<value>`
- `@scene/editpose <pose id>=<editor>,<content>`

`/addpose` adds a pose at the end of the scene and says its id. `<show as>` is the name it is shown under, empty for the author's own. `<origin>` is the room it was written in. `<type>` is a pose type such as `ic` or `ooc`, `ic` when empty (see [@scene types]). `<source>` is a word saying what wrote it, and `<tags>` a list of words separated by spaces. Everything after the sixth comma is the content, with its commas, colours and spaces kept.

`/setpose` sets one key of a pose: `showas`, `authorname`, `author`, `origin`, `originname`, `source`, `type`, `tags`, or any other key, kept as text. An empty value clears it. It cannot change the content.

`/editpose` replaces the content with a new version, kept in the pose's history, and records `<editor>` as its last editor.

Live scene views show each new pose and edit as it happens.

## Pose history and order

- `@scene/undo <pose id>`
- `@scene/redo <pose id>`
- `@scene/move <pose id>=[<after pose id>]`
- `@scene/delete <pose id>`

Every edit of a pose is kept as a version, which [sceneedits()] lists. `/undo` shows the version before the current one again, and `/redo` the one after it; each warns when there is none. An edit made after an undo drops the versions that were undone.

`/move` puts the pose right after another pose of the same scene, or first in the scene when nothing follows the `=`.

`/delete` marks the pose deleted. It keeps its place, its content and its history until the game's retention for deleted poses, when one is set, removes it (see [@storage]).

## Members and focus

- `@scene/member <scene id>/<role>=<player>`
- `@scene/unmember <scene id>=<player>`
- `@scene/focus <player>[=<scene id>]`
- `@scene/showas <scene id>/<player>=[<name>]`
- `@scene/notice <scene id>/<player>=<text>`

`/member` adds the player to the scene with a role, or changes their role, keeping their focus and persona. A role is any word. `owner`, `gm` and `participant` take a seat, which the scene's player cap counts: a full scene refuses another, and warns that there is no such scene. `/unmember` removes the player from the scene.

`/focus` makes the scene the player's current one, which the scene package and [ooc] write their poses into. A player has one focus at a time; nothing after the `=` clears it.

`/showas` sets the name the player is shown under in that scene. An empty name goes back to their own.

`/notice` tells one member `<text>`, from the object running the command, and shows it in their live scene view. Nobody else in the scene sees it.

## Plots

- `@scene/plot <plot id>`
- `@scene/plot/create <owner>,<title>[,<description>]`
- `@scene/plot/link <plot id>=<scene id>`
- `@scene/plot/unlink <plot id>=<scene id>`

A plot gathers the scenes of one story. `/plot` shows a plot's id, title, owner and description. `/plot/create` makes one and says its id; it needs an owner and a title. `/plot/link` files a scene under a plot, and `/plot/unlink` takes it out again; both warn when the plot or the scene is missing.

```
> @scene/plot/link 3=12
```

Output: `[SCENE] Done: Linked scene #12 into plot #3.`

## Pose types

- `@scene/types`

A pose type says how the portal draws a pose and whether readers see it unless they ask. The scene package defines each one as a ``TYPE`<KEY>`` attribute of JSON on its Scene Logger object. `/types` shows a table of the types it read, with each one's key, label, presentation, tone, icon, whether it starts hidden, and its order. A second table lists each type attribute it left out, and why. Without the scene package there are no types.

The JSON fields are `label` (text), `presentation` (`prose`, `band`, `message`, `aside` or `notice`), `tone` (a tone that colours text; see [tone()]), `icon` (`radio`, `phone`, `chat`, `dice`, `book`, `scroll`, `megaphone`, `eye`, `mask`, `music`, `star` or `bolt`), `hidden` (true or false) and `order` (a whole number; 50 when left out). A pose whose type is not defined is drawn as in character.

The [scene type functions] read the types from softcode.

::: seealso
- [scene system]
- [scene functions]
- [scene write functions]
:::
