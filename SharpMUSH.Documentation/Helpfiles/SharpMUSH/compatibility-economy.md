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

**PennMUSH** keeps a player's remaining quota in its `RQUOTA` attribute, debited on each build and refunded on each destroy, so `examine` lists `RQUOTA` with the other attributes.<br>
**SharpMUSH** stores the limit and counts what the player owns whenever it checks. There is no `RQUOTA` attribute, so `examine` lists none.<br>
**Why.** A count cannot drift from what the player owns, and a stored remainder can.<br>
**Workaround.** Read quota with `@quota` or `quota()`, never with `get(<player>/RQUOTA)`.<br>
**Example.** The parity case `admin.wizard` in `tools/parity/scenarios/20-admin-commands.scn` runs it on both servers.
