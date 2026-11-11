<!-- help-article
{
  "corpus": "help",
  "id": "pose-function",
  "lookup": "pose()",
  "aliases": [],
  "sections": []
}
-->
# pose()

`pose(<object>, <text>[, <prefix>])`

Returns what `<object>` saying or posing `<text>` looks like, without anyone hearing it. `<text>` is read as [SPEAK()] reads it: `:` poses, `;` poses with no space, and anything else is said. `<prefix>` goes in front of the result.

This function comes from TinyMUX.

```sharp
> think pose(me,:waves.)
Alice waves.
> think pose(me,Hello,<OOC>)
<OOC>Alice says, "Hello"
```

::: seealso
- [SPEAK()]
- [POSE]
- [SAY]
:::
