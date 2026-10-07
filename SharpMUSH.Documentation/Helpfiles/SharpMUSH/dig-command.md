<!-- help-article
{
  "corpus": "help",
  "id": "dig-command",
  "lookup": "@dig",
  "aliases": [],
  "sections": [
    {
      "id": "room-and-exit-examples",
      "heading": "Room and exit examples",
      "lookup": "@dig room and exit examples"
    }
  ],
  "redirects": {
    "@dig2": "@dig room and exit examples"
  }
}
-->
# @dig

`@dig[/teleport] <room name>[=<exit to>, <exit from>, <room dbref>, <to dbref>, <from dbref>]`

This command creates a new room named `<room name>`. Creating a room consumes one building-quota slot when quotas are enabled; SharpMUSH has no penny balance and does not charge `room_cost`. If the `/teleport` switch is given, you will be teleported to the room after it's created, as per the @teleport command.

Output: the new room's dbref, as `dig()` returns it.

If `<exit to>` is given, the MUSH will automatically open an exit from your current location to the new room named `<exit to>`, if you have permission. You can also specify `<exit from>`, to create an exit from the new room back to your current location. Opening each exit consumes another quota slot when quotas are enabled; `exit_cost` does not charge pennies. A quota refusal partway through leaves the room and any exits already created in place. The exit names may contain multiple aliases, separated with semicolons, as per [@name].

Wizards and objects with the pick_dbref power can also specify the dbrefs of garbage objects to use when creating the room and the to and from exits.

## Room and exit examples

```sharp
> @dig Kitchen
```
This command will create a new room named 'Kitchen'. You will be informed what the dbref of this room is.
```sharp
> @dig Kitchen=Kitchen \<N\>;n;north;kitchen;k
```
This will create the room as above, and also open an exit leading to it named `Kitchen \<N\>` with the aliases n, north, kitchen and k. It will NOT create an exit coming back from the Kitchen room.
```sharp
> @dig Kitchen=Kitchen \<N\>;n;north;kitchen;k, Out \<S\>;s;south;out;o
```
This will do just the same as the above, except it will also create an exit named `Out \<S\>` with the aliases s, south, out and o coming back from the kitchen to whatever room you are currently in.


::: seealso
- [@open]
- [@link]
- [EXITS]
- [@create]
- [database]
- [DIG()]
:::
