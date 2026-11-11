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
    },
    {
      "id": "quota-is-counted",
      "heading": "Quota is counted, not stored",
      "lookup": "compatibility economy quota is counted"
    }
  ]
}
-->
# Compatibility Economy

SharpMUSH has no built-in currency balances or penny charges. Games that need an economy can store balances in attributes; code ported from PennMUSH must account for this difference.

## `money()` is not supported

**A choice.**

**PennMUSH** keeps a penny balance on every player: `money()` reports it, and building, `give`, `@pay` and `buy` spend it.<br>
**SharpMUSH** keeps no balance. `money()` returns `#-1 NOT SUPPORTED` and tells you why, and `buy` says the same. `inventory` does not end with PennMUSH's `You have <number> Pennies.` line. `examine` has no `Pennies:` field after the zone.<br>
**Why.** SharpMUSH does not track pennies, so there is no balance to report.<br>
**Workaround.** Keep balances in attributes. Code that only displays `money()` should test it for `#-1` first.

```sharp
> think money(me)
#-1 NOT SUPPORTED
```

## Quota is counted, not stored

**A choice.**

**PennMUSH** keeps a player's remaining quota in its `RQUOTA` attribute, debited on each build and refunded on each destroy. `examine` lists it, and `get()`, `hasattr()` and the other attribute functions read it.<br>
**SharpMUSH** stores the limit and counts what the player owns whenever it checks. `examine` lists `RQUOTA` where PennMUSH does, with the same owner, flags and value, but the value is counted as `examine` runs and is never stored. The attribute functions do not find it, and setting `RQUOTA` does not change the quota. Because it is a count of objects, it can differ from PennMUSH's when `quota_cost` is not 1, and it goes below zero for a player whom quota does not limit (a wizard, or a holder of `No_Quota`) who owns more objects than their quota.<br>
**Why.** A count cannot drift from what the player owns, and a stored remainder can.<br>
**Workaround.** Read quota with `@quota` or `quota()`, and change it with `@quota/set`.

```sharp
> think hasattr(me,RQUOTA)
0
```
