<!-- help-article
{
  "corpus": "help",
  "id": "chzone-command",
  "lookup": "@chzone",
  "aliases": [],
  "sections": [
    {
      "id": "zone-control-and-inheritance",
      "heading": "Zone control and inheritance",
      "lookup": "@chzone zone control and inheritance"
    },
    {
      "id": "several-zones",
      "heading": "Several zones",
      "lookup": "@chzone several zones"
    }
  ],
  "redirects": {
    "@chzone2": "@chzone zone control and inheritance"
  }
}
-->
# @chzone

`@chzone[/preserve] <object>=<zone>`<br>
`@chzone <object>=none`<br>
`@chzone/add[/preserve] <object>=<zone>`<br>
`@chzone/remove <object>=<zone>`

The first form of this command changes the zone of `<object>` to `<zone>`. This puts the object on that zone and may (if the zone_control_zmp_only @config option is off) allow anyone who passes the Zone @lock of `<zone>` to control `<object>`. Any kind of object can be @chzoned, and any kind of object can be used as a zone.

The second form of this command removes `<object>` from every zone, leaving it unzoned.

`/add` and `/remove` put `<object>` in one more zone or take it out of one, leaving its other zones as they are. See [@chzone several zones].

If a player is @chzoned, any objects he creates from that point on will automatically be on the same zone. Objects the player already owns are not affected.

You must control `<object>`, and either control `<zone>` or pass its @lock/chzone.

## Zone control and inheritance

To see the Zone of an object, you can use either 'brief' or 'examine' to examine it. The Zone is listed on the same line as the Owner of the object.

If `<zone>` does not have a Zone @lock when something is @chzoned to it, the lock is automatically set to ``_`<zone>`` (see [LOCKING] for more info).

Whenever an object besides a player is @chzoned to a zone object, the WIZARD, ROYALTY, and TRUST flags will be reset, as will all @power's (for security purposes). For similar reasons, it is strongly recommended that you do not @chzone admin- or wizard-owned objects to any zone that less privileged players have access to. Wizards can use the `/preserve` switch to prevent this reset.

## Several zones

An object can be in more than one zone. Its zones are kept in order; the first is the one `zone()` reports, and an object in no zone that is added to one has it as its first. Every zone counts: its `$-commands`, its Zone @lock's control, and its zone messages on entering and leaving.

- `@chzone/add <object>=<zone>` adds `<zone>` after the others. It asks what `@chzone` asks, and it resets the same flags and powers unless `/preserve` is given.
- `@chzone/remove <object>=<zone>` takes `<zone>` out. When it was the first, the next one becomes the first.
- `@chzone <object>=<zone>` leaves `<object>` in `<zone>` alone, and `=none` in none.

Examine lists every zone on the Owner line, the first first.

Example:

```sharp
> @chzone/add Lamp=Market
```
Output: `Zone changed.`, and Lamp is in Market as well as its other zones.

::: seealso
- [zones]
- [@chzoneall]
- [ZONE()]
:::
