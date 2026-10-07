<!-- help-article
{
  "corpus": "help",
  "id": "zone-masters",
  "lookup": "zone masters",
  "aliases": [
    "ZMPs",
    "SHARED PLAYERS"
  ],
  "sections": [
    {
      "id": "shared-player-examples",
      "heading": "Shared player examples",
      "lookup": "zone masters shared player examples"
    }
  ],
  "redirects": {
    "SHARED PLAYERS2": "zone masters shared player examples"
  }
}
-->
# Zone Masters

SHARED PLAYERS

Shared players are player objects which are used to mediate shared control of objects. A shared player is an object of type PLAYER which has the SHARED flag set. They are created like ordinary players, and can connect, build, etc. The only difference is that objects owned by a shared player are controlled by anything that passes the `@lock`/zone of the shared player.

Anyone who passes the `@lock`/zone of the shared player can `@chown` objects to it. This, however, does not refund the original creator's money or quota, as does normal.

Shared players used to be known as Zone Masters. The term was changed to emphasize the fact that they are not related to Zone Master Objects, which are used to allow area-specific `$-commands`.

## Shared player examples

Some suggested uses of shared players:

1. If you are working on a building project with several people, it may be useful to create a shared player and `@lock`/zone it to all of you. That way, all of the players working on the project will be able to modify the building, as long as the shared player owns all the objects being built.

2. If local wizards are desired, a shared player may be created and zone locked to the local wizards. Players building within that zone should be `@chowning` to the shared player, or logged in as it while creating objects. The local wizard will then be able to control anything within that domain as long as the object in question is owned by the shared player.


::: seealso
- [SHARED]
- [LOCKING]
- [zone masters]
:::
