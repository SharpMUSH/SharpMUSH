<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-economy",
  "lookup": "compatibility economy",
  "aliases": [],
  "sections": [
    {
      "id": "money-is-not-supported",
      "heading": "`money()` is not supported",
      "lookup": "compatibility economy money is not supported"
    }
  ]
}
-->
# Compatibility Economy

SharpMUSH has no built-in currency balances or penny charges. Games that need an economy can store balances in attributes; code ported from PennMUSH must account for this difference.

## `money()` is not supported

**A choice.**

**PennMUSH** keeps a penny balance on every player: `money()` reports it, and building, `give`,
`@pay` and `buy` spend it.<br>
**SharpMUSH** keeps no balance. `money()` returns `#-1 NOT SUPPORTED` and tells you why.<br>
**Why.** SharpMUSH does not track pennies, so there is no balance to report.<br>
**Workaround.** Keep balances in attributes. Code that only displays `money()` should test it for
`#-1` first.

```sharp
> think money(me)
#-1 NOT SUPPORTED
```
