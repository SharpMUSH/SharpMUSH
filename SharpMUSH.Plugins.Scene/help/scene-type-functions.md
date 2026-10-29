<!-- help-article
{
  "corpus": "help",
  "id": "scene-type-functions",
  "lookup": "scene type functions",
  "aliases": ["scene detail functions"],
  "listedIn": ["function list objects"],
  "sections": [
    {"id": "scenetype", "heading": "scenetype()", "lookup": "scenetype()", "aliases": []},
    {"id": "scenetype-check", "heading": "scenetype() check", "lookup": "scenetype check", "aliases": []},
    {"id": "scenedetail", "heading": "scenedetail()", "lookup": "scenedetail()", "aliases": []},
    {"id": "scenedetail-check", "heading": "scenedetail() check", "lookup": "scenedetail check", "aliases": []},
    {"id": "scenedetail-value", "heading": "scenedetail() value", "lookup": "scenedetail value", "aliases": []}
  ]
}
-->
# Scene Type and Detail Functions

The scene package defines the game's pose types and scene details on its Scene Logger object, one attribute of JSON each: ``TYPE`<KEY>`` for a pose type (see [@scene types]) and ``DETAIL`<KEY>`` for a scene detail. A scene detail is a piece of text the game names and explains that a scene can hold, such as the setting it takes place in; the scene keeps its value under the detail's key. These functions read the definitions, and check one before it is written. Without the scene package there are none.

- [scenetype()]: the pose type keys, or one field of a pose type
- [scenetype check]: whether JSON reads as a pose type
- [scenedetail()]: the scene detail keys, or one field of a scene detail
- [scenedetail check]: whether JSON reads as a scene detail
- [scenedetail value]: a value as a detail keeps it

A JSON object cannot be typed with braces, which are taken as grouping; build it with [json()].

## scenetype()

`scenetype([<key>[, <field>]])`

With no key, the keys of the pose types, separated by spaces, lowest `order` first and then by key. An attribute that does not read as a type is left out; `@scene/types` says why.

With a key, one field of that pose type; `label` when no field is given. The others are `presentation`, `tone`, `icon`, `hidden` and `order`. `hidden` lists where a reader who has not chosen starts with the type hidden: `reading`, `playing`, both (`reading playing`), or nothing. A key the game does not define is `#-1 NO SUCH POSE TYPE`, and any other field is `#-1 UNKNOWN TYPE FIELD`.

```sharp
> think scenetype()
```

Output: ic ooc narration

```sharp
> think scenetype(ooc, hidden)
```

Output: reading

## scenetype() check

`scenetype(<key>, check, <json>)`

Empty when `<json>` would read as the pose type `<key>`, otherwise `#-1` and the reason. The key does not have to be defined yet. A key is 1 to 32 letters, digits, `-` and `_`, starting with a letter or digit. The fields are listed in [@scene types].

```sharp
> think scenetype(radio, check, json(object,hidden,json(string,yes)))
```

Output: #-1 hidden must be true, false, "reading" or "playing"

## scenedetail()

`scenedetail([<key>[, <field>]])`

With no key, the keys of the scene details, separated by spaces, lowest `order` first.

With a key, one field of that scene detail; `label` when no field is given. The others are:

- `hint`: one line saying what goes in it
- `choices`: the values it allows, separated by `|`, or empty when any text will do
- `many`: 1 when a scene may give it several values, kept separated by `|`
- `listed`: 1 when it is shown beside the title in scene lists
- `tone`: the tone its values are shown in, or empty
- `order`: its place in lists, lowest first
- `hidden`: 1 when it is kept for code only and never shown
- `sort`: the key of the detail whose number puts scenes in this one's order, or empty

A key the game does not define is `#-1 NO SUCH SCENE DETAIL`, and any other field is `#-1 UNKNOWN DETAIL FIELD`. The fields `check` and `value` take a third argument; see [scenedetail check] and [scenedetail value].

## scenedetail() check

`scenedetail(<key>, check, <json>)`

Empty when `<json>` would read as the scene detail `<key>`, otherwise `#-1` and the reason. The key does not have to be defined yet. A key is 1 to 32 letters, digits, `-` and `_`, starting with a letter, and cannot be one of the scene's own fields, such as `title`, `status` or `cast`. The JSON fields are those [scenedetail()] reads: `label`, `hint` and `tone` are text, `choices` a list of text, `many`, `listed` and `hidden` true or false, `order` a whole number (50 when left out), and `sort` another detail's key.

```sharp
> think scenedetail(title, check, json(object,label,Title))
```

Output: #-1 'title' is a scene field already, so it can't be a detail

## scenedetail() value

`scenedetail(<key>, value, <value>)`

`<value>` as the detail `<key>` keeps it: trimmed, or for a detail that takes several values, each one trimmed, blanks left out, joined by `|`. When the detail has choices, each value is written as its choice is, and a value it does not allow is `#-1` and a message naming the choices. A key the game does not define keeps the value trimmed. A key that cannot be a detail is `#-1` and the reason.

```sharp
> think scenedetail(verse, value, canon | AU)
```

Output: Canon|AU, when `verse` takes several values and its choices are Canon and AU.

::: seealso
- [scene functions]
- [scene write functions]
:::
