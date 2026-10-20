<!-- help-article
{
  "corpus": "help",
  "id": "scene-type-functions",
  "lookup": "scene type functions",
  "aliases": ["scene detail functions"],
  "listedIn": ["function list objects"],
  "sections": [
    {"id": "scenetypes", "heading": "scenetypes()", "lookup": "scenetypes()", "aliases": []},
    {"id": "scenetype", "heading": "scenetype()", "lookup": "scenetype()", "aliases": []},
    {"id": "scenetypecheck", "heading": "scenetypecheck()", "lookup": "scenetypecheck()", "aliases": []},
    {"id": "scenedetails", "heading": "scenedetails()", "lookup": "scenedetails()", "aliases": []},
    {"id": "scenedetail", "heading": "scenedetail()", "lookup": "scenedetail()", "aliases": []},
    {"id": "scenedetailcheck", "heading": "scenedetailcheck()", "lookup": "scenedetailcheck()", "aliases": []},
    {"id": "scenedetailvalue", "heading": "scenedetailvalue()", "lookup": "scenedetailvalue()", "aliases": []}
  ]
}
-->
# Scene Type and Detail Functions

The scene package defines the game's pose types and scene details on its Scene Logger object, one attribute of JSON each: ``TYPE`<KEY>`` for a pose type (see [@scene types]) and ``DETAIL`<KEY>`` for a scene detail. A scene detail is a piece of text the game names and explains that a scene can hold, such as the setting it takes place in; the scene keeps its value under the detail's key. These functions read the definitions, and check one before it is written. Without the scene package there are none.

- [scenetypes()]: the pose type keys
- [scenetype()]: one field of a pose type
- [scenetypecheck()]: whether JSON reads as a pose type
- [scenedetails()]: the scene detail keys
- [scenedetail()]: one field of a scene detail
- [scenedetailcheck()]: whether JSON reads as a scene detail
- [scenedetailvalue()]: a value as a detail keeps it

A JSON object cannot be typed with braces, which are taken as grouping; build it with [json()].

## scenetypes()

`scenetypes()`

The keys of the pose types, separated by spaces, lowest `order` first and then by key. An attribute that does not read as a type is left out; `@scene/types` says why.

## scenetype()

`scenetype(<key>[, <field>])`

One field of a pose type; `label` when no field is given. The others are `presentation`, `tone`, `icon`, `hidden` (1 or 0) and `order`. A key the game does not define is `#-1 NO SUCH POSE TYPE`, and any other field is `#-1 UNKNOWN TYPE FIELD`.

## scenetypecheck()

`scenetypecheck(<key>, <json>)`

Empty when `<json>` would read as the pose type `<key>`, otherwise `#-1` and the reason. A key is 1 to 32 letters, digits, `-` and `_`, starting with a letter or digit. The fields are listed in [@scene types].

```
> think scenetypecheck(radio, json(object,hidden,json(string,yes)))
```

Output: #-1 hidden must be true or false

## scenedetails()

`scenedetails()`

The keys of the scene details, separated by spaces, lowest `order` first.

## scenedetail()

`scenedetail(<key>[, <field>])`

One field of a scene detail; `label` when no field is given. The others are:

- `hint`: one line saying what goes in it
- `choices`: the values it allows, separated by `|`, or empty when any text will do
- `many`: 1 when a scene may give it several values, kept separated by `|`
- `listed`: 1 when it is shown beside the title in scene lists
- `tone`: the tone its values are shown in, or empty
- `order`: its place in lists, lowest first
- `hidden`: 1 when it is kept for code only and never shown
- `sort`: the key of the detail whose number puts scenes in this one's order, or empty

A key the game does not define is `#-1 NO SUCH SCENE DETAIL`, and any other field is `#-1 UNKNOWN DETAIL FIELD`.

## scenedetailcheck()

`scenedetailcheck(<key>, <json>)`

Empty when `<json>` would read as the scene detail `<key>`, otherwise `#-1` and the reason. A key is 1 to 32 letters, digits, `-` and `_`, starting with a letter, and cannot be one of the scene's own fields, such as `title` or `status`. The JSON fields are those [scenedetail()] reads: `label`, `hint` and `tone` are text, `choices` a list of text, `many`, `listed` and `hidden` true or false, `order` a whole number (50 when left out), and `sort` another detail's key.

## scenedetailvalue()

`scenedetailvalue(<key>, <value>)`

`<value>` as the detail `<key>` keeps it: trimmed, or for a detail that takes several values, each one trimmed, blanks left out, joined by `|`. When the detail has choices, each value is written as its choice is, and a value it does not allow is `#-1` and a message naming the choices. A key the game does not define keeps the value trimmed. A key that cannot be a detail is `#-1` and the reason.

```
> think scenedetailvalue(verse, canon | AU)
```

Output: Canon|AU, when `verse` takes several values and its choices are Canon and AU.

::: seealso
- [scene functions]
- [scene write functions]
:::
