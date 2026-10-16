<!-- help-article
{
  "corpus": "help",
  "id": "context-function",
  "lookup": "context()",
  "aliases": [
    "context"
  ],
  "sections": [
    {"id": "attribute", "heading": "Formatting with an attribute", "lookup": "context() attribute"},
    {"id": "examples", "heading": "Examples and errors", "lookup": "context() examples"}
  ]
}
-->
# context()

`context(<player>[, <count>[, <osep>[, <object>/<attribute>]]])`

Returns the commands `<player>`'s context stack puts first, in the order the command palette shows them: urgent ones, then by how recently their contexts were pushed. It offers only entries `<player>` could use, by the rules in [palette entries]. Pins and staples are left out.

`<count>` is how many, 5 by default and at most 20. `<osep>` goes between them, by default a middle dot (`chr(183)`) with a space either side.

Without an attribute, it returns the entries whose every `<slot>` was filled from a context, each as a command link (see [cmdlink()]) with the context's info, or the line, as its hover text. Code that may not make links gets the labels alone.

- [context() attribute]: passing each entry to an attribute, and its named arguments
- [context() examples]: whose stack code may read, examples, and errors

## Formatting with an attribute

With `<object>/<attribute>`, every entry is passed to that attribute, complete or not, and the results are joined with `<osep>`. The attribute gets these named arguments (see [named arguments]):

| Name | Value |
|---|---|
| `%<label>` | The entry's label |
| `%<line>` | The command line, slots filled from contexts where they could be |
| `%<ready>` | 1 when every slot is filled |
| `%<urgent>` | 1 when it is urgent now |
| `%<contexts>` | The contexts that raised it, newest first, space-separated |
| `%<id>` | The entry, as `<objid>/<name>` |
| `%<system>` | The system it belongs to |
| `%<info>` | The newest raising context's info, else the entry's own, else empty |

## Examples and errors

Code may ask for the player whose command it is running (`%#`), or a player its object controls.

```sharp
> @context/push me=scene_here,[json(object,scene,12)],,Scene 12 is running in this room.
> think context(me,5,%b|%b)
Join the scene here | Scenes here
> &FN`ITEM me=%<label>: %<line> (%<contexts>)
> think context(me,5,%r,me/FN`ITEM)
Join the scene here: +join 12 (scene_here)
Scenes here: +scenes (#15:1792044786423)
> think context(me,1)
Join the scene here
```

Output: the labels, as links in a client that shows them. The attribute's results, one per line.

Errors: `#-1 NO SUCH PLAYER`, `#-1 PERMISSION DENIED`, `#-1 ARGUMENT MUST BE POSITIVE INTEGER`.

::: seealso
- [@context]
- [palette entries]
- [cmdlink()]
:::
