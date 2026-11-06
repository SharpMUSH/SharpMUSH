<!-- help-article
{
  "corpus": "help",
  "id": "totcmds-function",
  "lookup": "totcmds()",
  "aliases": [
    "totcmds"
  ],
  "sections": []
}
-->
# totcmds()

`totcmds(<player>)`

totcmds() returns how many commands `<player>` has typed: the total kept in their TOTCMDS attribute from the sessions that have ended, plus each connection's count so far. A session's count is added to TOTCMDS when it ends, by QUIT or LOGOUT.

While the player is connected but hidden, a caller who may not see hidden players gets `-1`, and so does a name that matches no player.

totcmds() comes from RhostMUSH.

```sharp
> think totcmds(me)
1532
```

::: seealso
- [cmds()]
:::
