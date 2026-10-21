<!-- help-article
{
  "corpus": "help",
  "id": "bullets-function",
  "lookup": "bullets()",
  "aliases": [],
  "sections": [
    {
      "id": "options",
      "heading": "Options",
      "lookup": "bullets() options"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "bullets() examples"
    }
  ]
}
-->
# bullets()

`bullets(<list>[, <delimiter>[, <options>]])`

Each item of *<list>* on its own line after a bullet or a number. *<delimiter>* splits the list; a space when none is given. An item too long for its line wraps under its own text, not under the bullet; numbers line up on their right. In the web portal it is a bulleted or numbered list.

- [bullets() options]: borders, theme, width, style, start and marker
- [bullets() examples]: wrapped, numbered, roman and marked lists

::: seealso
- [GRID()]
- [TREE()]
- [LAYOUT FUNCTIONS]
:::

## Options

Options (the last argument, which may be empty):
- `"border":"<style>"` and the border pieces - see [LAYOUT BORDERS]. They set the border of every box and rule inside that names none.
- `"theme":"<theme>"` - colours for the layout and everything inside it; see [LAYOUT THEMES].
- `"width":<n>` - see [LAYOUT FUNCTIONS].
- `"style":"<style>"` - `bullet` (a round bullet, sent as `*` to a client without Unicode; the default), `dash`, `star`, `number`, `alpha`, `roman` or `none`.
- `"start":<n>` - the first number, letter or numeral. 1 by default.
- `"marker":"<text>"` - your own marker in front of every item.

## Examples

A Unicode client sees round bullets where these examples show `*`.

```sharp
> think bullets(Be kind to other players|No spam|Have fun,|,{{"width":20}})
* Be kind to other
  players
* No spam
* Have fun
> think bullets(Mannaz Raya Tomas,,{{"style":"number","start":9}})
 9. Mannaz
10. Raya
11. Tomas
> think bullets(North South,,{{"style":"roman"}})
 i. North
ii. South
> think bullets(a b,,{{"marker":"->"}})
-> a
-> b
```
