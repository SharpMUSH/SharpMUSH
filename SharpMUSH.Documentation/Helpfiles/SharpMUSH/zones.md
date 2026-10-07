<!-- help-article
{
  "corpus": "help",
  "id": "zones",
  "lookup": "zones",
  "aliases": [
    "ZONE OBJECTS",
    "ZONE MASTER OBJECTS",
    "ZONE MASTER THINGS",
    "ZMOs",
    "ZMTs"
  ],
  "sections": [
    {
      "id": "zone-commands",
      "heading": "Zone commands",
      "lookup": "zones zone commands"
    }
  ],
  "redirects": {
    "ZONES2": "zones zone commands"
  }
}
-->
# Zones

Zones are areas of the MUSH that can have the same user-defined commands without having to `@parent` every object in the zone or make the commands MUSH-wide globals.

The default zone is NOTHING. Any building done by a player defaults to belonging to the same zone that the player belongs to. Every zone is defined by a Zone Master Object (ZMO). The ZMO is an ordinary MUSH object owned by some player. A wizard may change the zone of an object or player to a ZMO.

If the ZMO is a room, it is called a "Zone Master Room." Most of the statements about ZMOs also apply to zone master rooms; for details, see [ZONE MASTER ROOMS].

## Zone commands

`$-commands` on a ZMO are treated as global within that zone. The game attempts to match `$-commands` for the ZMO of the player's location, as well as `$-commands` for the player's own zone.

If you want restricted global commands defined over only a small area, you can define that area to be part of a zone, and place the desired `$-commands` upon the ZMO. If you want players to be able to use special commands for a culture they belong to, the `$-commands` should go on the ZMO, and the players `@chzoned` to it so they can use the commands anywhere.


::: seealso
- [@chzone]
- [zone masters]
:::
