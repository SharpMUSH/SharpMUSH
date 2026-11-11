<!-- help-article
{
  "corpus": "help",
  "id": "lastcreate-function",
  "lookup": "lastcreate()",
  "aliases": [
    "lastcreate"
  ],
  "sections": []
}
-->
# lastcreate()

`lastcreate([<object>[, <type>]])`

Returns the dbref of the last object `<object>` created, or `#-1` if it has created none. `<object>` is you when left out. With `<type>` (`room`, `thing`, `exit` or `player`; only the first letter counts), it returns the last object of that type.

An object's creations are counted when it runs @create, @dig, @open, @clone, @pcreate or the functions that do the same. You must control `<object>`, or be a wizard or royalty.

This function comes from TinyMUX and RhostMUSH. A TinyMUX or RhostMUSH import keeps each object's record.

```sharp
> @create Lamp
Created: Object #52.
> think lastcreate(me,thing)
#52
```

::: seealso
- [CREATE()]
- [DIG()]
- [NEXTDBREF()]
:::
