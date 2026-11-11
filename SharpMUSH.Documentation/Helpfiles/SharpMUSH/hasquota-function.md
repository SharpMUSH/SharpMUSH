<!-- help-article
{
  "corpus": "help",
  "id": "hasquota-function",
  "lookup": "hasquota()",
  "aliases": [
    "hasquota"
  ],
  "sections": []
}
-->
# hasquota()

`hasquota(<player>[, <amount>[, <type>]])`

Returns 1 if `<player>` has at least `<amount>` quota left, and 0 if not. `<amount>` is 1 when left out. It returns 1 when nothing limits what the player builds: quotas are off, or the player needs no quota.

`<type>` may be `thing`, `room`, `exit` or `player` (only the first letter counts). Every type uses the same quota, so the type changes nothing; any other type gives `#-1 BAD TYPE SPECIFIER`.

You must control `<player>` or be able to examine it.

This function comes from TinyMUX and RhostMUSH.

```sharp
> think hasquota(me,5)
1
```

::: seealso
- [QUOTA()]
- [@QUOTA]
:::
