<!-- help-article
{
  "corpus": "help",
  "id": "backup-command",
  "lookup": "@backup",
  "aliases": [],
  "sections": [
    {"id": "copy", "heading": "How a copy is taken", "lookup": "@backup copy"},
    {"id": "retention", "heading": "Retention and scheduled copies", "lookup": "@backup retention"}
  ]
}
-->
# @backup

`@backup`<br>
`@backup/list`

Wizard-only. Takes a copy of the world, into a timestamped directory under the game's backup
directory, and reports the name and size of what it wrote. This is a SharpMUSH command; PennMUSH
has no equivalent, because PennMUSH holds the database in memory and `@dump` writes it out.
`@dump` does the same as `@backup`.

`@backup/list` reports the copies currently on disk, newest first, without taking one.

- [@backup copy]: messages, events, and why the copy is a consistent snapshot
- [@backup retention]: how many copies are kept, and copies taken on an interval

## How a copy is taken

Everyone sees dump_message as the copy starts and dump_complete when it is written, and the dump
and backup events are raised (see [event dump] and [event backup]). A failed copy tells connected
wizards and royalty instead.

The game keeps running throughout. The copy is a snapshot as of the moment it starts: everything
committed before then is in it, nothing after. It is written aside and moved into place only once
complete, so a backup tool reading the directory never sees a half-written one.

LMDB copies its environment with its own routine, so a copy is a point-in-time snapshot by
construction.

## Retention and scheduled copies

Older copies are deleted as new ones arrive, keeping a configured number. How many, where they go,
and whether one is also taken automatically on an interval are deployment settings on the server
process, not `@config` options. See `deploy/README.md`. A scheduled copy warns everyone five minutes
and one minute before it, with dump_warning_5min and dump_warning_1min.

::: seealso
- [@dump]
- [@dbck]
- [@shutdown]
:::
