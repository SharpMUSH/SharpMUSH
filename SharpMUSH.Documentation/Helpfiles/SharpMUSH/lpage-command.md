<!-- help-article
{
  "corpus": "help",
  "id": "lpage-command",
  "lookup": "lpage",
  "aliases": [],
  "sections": []
}
-->
# lpage

`lpage <message>`

lpage pages `<message>` to whoever you paged last. The whole argument is the message, so it may contain `=`. `/noansi` is accepted and changes nothing.

lpage comes from RhostMUSH. `page <message>` with no `=` does the same.

```sharp
> page Alice=Hello
You paged Alice with 'Hello'.
> lpage 2+2=4
You paged Alice with '2+2=4'.
```

::: seealso
- [page]
- [mrpage]
:::
