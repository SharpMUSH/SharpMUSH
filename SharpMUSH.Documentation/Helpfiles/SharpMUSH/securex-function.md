<!-- help-article
{
  "corpus": "help",
  "id": "securex-function",
  "lookup": "securex()",
  "aliases": [
    "securex"
  ],
  "sections": []
}
-->
# securex()

`securex(<string>[, <keep>])`

Returns `<string>` with each of `% $ ^ \ [ ] ( ) { } , ;` turned into a space, like [secure()], except for the characters listed in `<keep>`.

In RhostMUSH the letter `a` in `<keep>` also keeps its colour substitutions. SharpMUSH code has none, so `a` keeps nothing more. securex() comes from RhostMUSH.

```sharp
> think securex(\[a\]\,b)
 a  b
> think securex(\[a\]\,b,\[\])
[a] b
```

Output: the brackets go unless they are kept.

::: seealso
- [secure()]
- [escape()]
- [escapex()]
:::
