<!-- help-article
{
  "corpus": "help",
  "id": "lock-zone-command",
  "lookup": "@lock/zone",
  "aliases": [],
  "sections": [
    {"id": "chzone", "heading": "Chzone lock", "lookup": "@lock/zone chzone", "aliases": ["@lock/chzone"]},
    {"id": "chown", "heading": "Chown lock", "lookup": "@lock/zone chown", "aliases": ["@lock/chown"]},
    {"id": "parent", "heading": "Parent lock", "lookup": "@lock/zone parent", "aliases": ["@lock/parent"]},
    {"id": "link", "heading": "Link lock", "lookup": "@lock/zone link", "aliases": ["@lock/link"]},
    {"id": "open", "heading": "Open lock", "lookup": "@lock/zone open", "aliases": ["@lock/open"]}
  ]
}
-->
# @lock/zone

Objects which pass a SHARED player's @lock/zone control all the objects the shared player owns. If the zone_control_zmp_only @config option is off, anything passing the @lock/zone of other objects will control everything @chzoned to the object.

- [@lock/zone chzone]: who can @chzone an object to this zone
- [@lock/zone chown]: who can @chown this CHOWN_OK object
- [@lock/zone parent]: who can @parent something to this LINK_OK object
- [@lock/zone link]: who can @link this exit, or link an exit here
- [@lock/zone open]: who can @open an exit from this OPEN_OK room

::: seealso
- [@chzone]
- [SHARED]
- [zones]
- [ZONE MASTER ROOMS]
:::

## Chzone lock

If set, controls who can @chzone an object to this zone.

::: seealso
- [@chzone]
- [zones]
:::

## Chown lock

If set, controls who can change the owner of this CHOWN_OK object via @chown.

::: seealso
- [CHOWN_OK]
- [@chown]
:::

## Parent lock

Controls who can @parent something to this LINK_OK object.

::: seealso
- [@parent]
- [LINK_OK]
:::

## Link lock

Controls who can @link this unlinked exit, or who can @link an exit to this LINK_OK room/thing.

::: seealso
- [@link]
- [LINK_OK]
- [@power]
:::

## Open lock

Controls who can @open an exit from this OPEN_OK room.

::: seealso
- [@open]
- [@dig]
- [OPEN_OK]
- [@power]
:::
