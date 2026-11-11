<!-- help-article
{
  "corpus": "help",
  "id": "connstats-function",
  "lookup": "conntotal()",
  "aliases": [
    "CONNMAX()",
    "CONNLAST()",
    "CONNNUM()",
    "CONNLEFT()",
    "conntotal",
    "connmax",
    "connlast",
    "connnum",
    "connleft"
  ],
  "sections": []
}
-->
# conntotal()

`conntotal(<player>)`<br>
`connmax(<player>)`<br>
`connlast(<player>)`<br>
`connnum(<player>)`<br>
`connleft(<player>)`

These report a player's time on the game, from the record the game keeps in their CONNINFO attribute when a session ends.

- conntotal() is the seconds online in every session, the longest one still open included.
- connmax() is the longest session in seconds, including one still open.
- connlast() is how many seconds the last session to end lasted.
- connnum() is the number of sessions, including those still open.
- connleft() is when the last session ended, in seconds since 1970 (as secs() gives); 0 if none has ended.

A session runs from login to QUIT or LOGOUT. Time spent connected more than once at a time is counted once. The portal's background session does not count, and a caller who may not see hidden players does not see the player's hidden sessions counted. A name that matches no player gives `#-1 PLAYER NOT FOUND`.

These functions come from TinyMUX, and a TinyMUX import keeps each player's record.

```sharp
> think connnum(me) [connlast(me)] [exptime(conntotal(me))]
12 1840 2d 3h 11m 2s
```

::: seealso
- [CONN()]
- [CONNLOG()]
- [TOTCMDS()]
:::
