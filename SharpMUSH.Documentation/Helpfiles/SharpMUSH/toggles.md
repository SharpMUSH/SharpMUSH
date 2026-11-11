<!-- help-article
{
  "corpus": "help",
  "id": "toggles",
  "lookup": "toggles",
  "aliases": [],
  "sections": []
}
-->
# Toggles

Toggles are RhostMUSH's second set of flags. Here each toggle is a flag of the same name, so `@set`, `hasflag()` and lock keys take it as well. Two names differ: MONITOR is the HEAR_CONNECT flag, and KEEPALIVE is SharpMUSH's own flag. A game imported from RhostMUSH has every other toggle as a flag, set or not; CLUSTER is not kept.

The toggle commands and functions take RhostMUSH's names and touch only these flags. On a game that was not imported from RhostMUSH, MONITOR and KEEPALIVE are the only toggles.

```sharp
> @create Box
> @toggle Box=MONITOR
> think hasflag(Box,HEAR_CONNECT) [ltoggles(Box)]
1 MONITOR
```

::: seealso
- [@toggle]
- [@toggledef]
- [hastoggle()]
- [ltoggles()]
- [listtoggles()]
- [toggle()]
:::
