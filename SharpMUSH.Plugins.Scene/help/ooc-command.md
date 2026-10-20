<!-- help-article
{
  "corpus": "help",
  "id": "ooc-command",
  "lookup": "ooc",
  "aliases": ["ooc/noeval"],
  "listedIn": ["Commands"],
  "sections": []
}
-->
# ooc

`ooc[/noeval] <message>`<br>
`ooc[/noeval] :<action>`<br>
`ooc[/noeval] ;<action>`

Speaks out of character to the room you are in. The room hears `<OOC> Name: message`. A message that starts with `:` poses instead (`<OOC> Name waves`), and one that starts with `;` semiposes, with no space after the name. The `/noeval` switch stops the message from being evaluated.

It is spoken the way [say] and [pose] are: the room's Speech lock applies, your [@speechmod] changes the words once (with `"`, `:` or `;` as `%1`), the name is the one you speak under, and a gagged player cannot use it.

When you are focused on the active scene of the room you are standing in, the line is also written into that scene as a pose of type `ooc`, and live scene views show it. The pose is the line the room heard without the `<OOC>` marker, under your own name rather than your scene persona. You must also be approved when you speak: the scene package's ``FUN`IS`APPROVED`` decides, or without the package, [isapproved()]. Anywhere else nothing is written into a scene.

```
> ooc :has to leave in five minutes.
```

Output: the room sees `<OOC> Alice has to leave in five minutes.`

::: seealso
- [scene system]
- [say]
- [pose]
:::
