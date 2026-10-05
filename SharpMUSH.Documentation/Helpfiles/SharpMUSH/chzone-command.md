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
    }
  ],
  "redirects": {
    "@chzone2": "@chzone zone control and inheritance"
  }
}
-->
# @chzone

`@chzone[/preserve] <object>=<zone>`<br>
`@chzone <object>=none`

The first form of this command changes the zone of `<object>` to `<zone>`. This puts the object on that zone and may (if the zone_control_zmp_only @config option is off) allow anyone who passes the Zone @lock of `<zone>` to control `<object>`. Any kind of object can be @chzoned, and any kind of object can be used as a zone.

The second form of this command removes `<object>` from its current zone, leaving it unzoned.

If a player is @chzoned, any objects he creates from that point on will automatically be on the same zone. Objects the player already owns are not affected.

You must control `<object>`, and either control `<zone>` or pass its @lock/chzone.

## Zone control and inheritance

To see the Zone of an object, you can use either 'brief' or 'examine' to examine it. The Zone is listed on the same line as the Owner of the object.

If `<zone>` does not have a Zone @lock when something is @chzoned to it, the lock is automatically set to ``_`<zone>`` (see [LOCKING] for more info).

Whenever an object besides a player is @chzoned to a zone object, the WIZARD, ROYALTY, and TRUST flags will be reset, as will all @power's (for security purposes). For similar reasons, it is strongly recommended that you do not @chzone admin- or wizard-owned objects to any zone that less privileged players have access to. Wizards can use the `/preserve` switch to prevent this reset.


::: seealso
- [zones]
- [@chzoneall]
- [ZONE()]
:::
