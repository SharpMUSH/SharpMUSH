<!-- help-article
{
  "corpus": "help",
  "id": "parents-function",
  "lookup": "parents()",
  "aliases": [],
  "sections": []
}
-->
# parents()

`parents(<object>[, <objids>])`

Returns the parent of `<object>`, then that parent's parent, and so on, without `<object>` itself. When `<objids>` is a nonzero number, it returns objids instead of dbrefs.

The chain stops at an object you cannot examine, unless you control `<object>`, when you see all of it. It returns `#-1` when `<object>` is not found or has no parent.

[lparent()] includes the object itself. parents() comes from RhostMUSH.

```sharp
> think parents(#20)
#21 #22
> think lparent(#20)
#20 #21 #22
```

Output: `#20`'s parent is `#21`, whose parent is `#22`.

::: seealso
- [lparent()]
- [parent()]
:::
