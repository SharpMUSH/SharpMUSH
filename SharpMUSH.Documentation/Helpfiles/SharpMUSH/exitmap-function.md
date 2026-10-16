<!-- help-article
{
  "corpus": "help",
  "id": "exitmap-function",
  "lookup": "exitmap()",
  "aliases": [
    "exitmap"
  ],
  "sections": [
    {"id": "exits", "heading": "Which exits are listed", "lookup": "exitmap() exits"},
    {"id": "examples", "heading": "Examples", "lookup": "exitmap() examples"}
  ]
}
-->
# exitmap()

`exitmap(<room>[, <viewer>])`

Returns a JSON object of the exits in `<room>`, each exit's name to the number of the room it leads to. It is what a client's mapper wants, and what the `exits` of the GMCP package Room.Info holds, so a package can send it through [oob()] without working out exits itself.

You may map a room you can examine or are standing in, as with [lexits()]. Mapping it as another viewer needs that too, or control of the viewer.

- [exitmap() exits]: which exits a viewer sees, and how they are named and numbered
- [exitmap() examples]: a map of a new room

## Which exits are listed

Only the exits `<viewer>` can see are listed: the ones `look` would show them. A DARK exit is left out unless the room or the exit is LIGHT or the viewer can see everything, and reality layers apply as they do to look. An exit's lock does not hide it, as it does not in look. `<viewer>` is you when it is left out.

Each exit appears under its name up to the first `;`, so `North;n` is `North`. When two exits share a name, without regard to case, the first is kept, as typing the name takes it. An exit linked to HOME leads to the viewer's own home, a variable exit is -2, and an unlinked exit is -1.

## Examples

```sharp
> @dig/teleport Garden=North;n,South;s
> think exitmap(here)
{"South":1234}
> think json_query(exitmap(here), get, South)
1234
```

Output: an object with one entry, the exit back to the room you came from, numbered by that room's dbref.

::: seealso
- [lexits()]
- [oob()]
- [look]
:::
