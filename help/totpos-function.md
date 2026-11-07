<!-- help-article
{
  "corpus": "help",
  "id": "totpos-function",
  "lookup": "totpos()",
  "aliases": [
    "totpos"
  ],
  "sections": []
}
-->
# totpos()

`totpos(<match>, <string>[, <type>[, <colour-aware>]])`

Returns the position, counting from 1, of every place `<match>` starts in `<string>`, separated by spaces. Matches may overlap. It returns nothing when there is none. Case matters.

- A `<type>` of 1 returns the position of every character of `<string>` that is one of the characters in `<match>`.
- A `<type>` of 2 returns those characters themselves, in order.

`<colour-aware>` is accepted for RhostMUSH code and changes nothing. The positions work as the list form of [creplace()]. totpos() comes from RhostMUSH.

```sharp
> think totpos(t,this is a test)
1 11 14
> think totpos(st,this is a test,2)
tsstst
```

Output: where `t` is, and every `s` or `t` in order.

::: seealso
- [pos()]
- [numpos()]
- [creplace()]
:::
