<!-- help-article
{
  "corpus": "help",
  "id": "size-function",
  "lookup": "size()",
  "aliases": [
    "size"
  ],
  "sections": []
}
-->
# size()

`size(<player>[, <type>])`

size() is RhostMUSH's measure of the memory a player and what they own take up. SharpMUSH does not measure memory, so size() returns 0, as objmem() and playermem() do. It returns `#-1` when `<player>` matches nothing or you do not control it. Type 3 takes any object; types 0 to 2 take a player.

Only wizards may use size().

```sharp
> think size(me)
0
```

::: seealso
- [objmem()]
- [playermem()]
:::
