<!-- help-article
{
  "corpus": "help",
  "id": "notice-function",
  "lookup": "notice()",
  "aliases": [],
  "sections": [
    {
      "id": "screen-readers",
      "heading": "Screen readers and colour",
      "lookup": "notice() screen readers"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "notice() examples"
    }
  ]
}
-->
# notice()

`notice(<source>, <text>[, <kind>])`

A message from a system, such as `+job` or `+scene`: *<source>* as a badge, then *<text>*. *<kind>* is one of [BADGE()]'s kinds, and colours the badge. Every kind but `info` (the default) and `muted` also puts a word before the text, so a reader without colour, or a screen reader, still knows what kind of message it is:

- `info` - nothing; the message is news
- `ok` - `Done:`; the thing asked for happened
- `warn` - `Warning:`; something typed was wrong (no such job, a missing title) or needs a second look
- `error` - `Error:`; a real error: the player may not do this, or something broke
- `muted` - nothing, in grey

- [notice() screen readers]: what a screen reader is sent, and the colours
- [notice() examples]: a package name, and one notice of each kind

::: seealso
- [BADGE()]
- [LAYOUT THEMES]
- [LAYOUT FUNCTIONS]
:::

## Screen readers and colour

A client that says it is a screen reader is sent the badge without its brackets and the word in lower case, `JOBS error: Only a Job Admin can do that.`, and an info notice as `JOBS: Job 12 is due Friday.` The portal keeps the brackets from its screen reader the same way.

The word takes the badge's colour. When the game sets `layout_theme`, the kinds take that theme's colours, as [BADGE()]'s do. An empty *<source>* leaves the badge out.

## Examples

A package says its own name once and uses it for every message, so its players learn which system is talking. Keep the name short and in capitals.

```sharp
> think notice(JOBS,Job 12 is due Friday.)
[JOBS] Job 12 is due Friday.
> think notice(JOBS,Job 12 is closed.,ok)
[JOBS] Done: Job 12 is closed.
> think notice(WIKI,There is no page called Lore/Dragons.,warn)
[WIKI] Warning: There is no page called Lore/Dragons.
> think notice(JOBS,Only a Job Admin can do that.,error)
[JOBS] Error: Only a Job Admin can do that.
```
