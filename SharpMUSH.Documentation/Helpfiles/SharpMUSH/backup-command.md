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

Wizard-only. `@backup` takes a copy of the world, into a timestamped directory under the game's backup directory, and reports the name and size of what it wrote. `@dump` does the same. PennMUSH has no @backup: it holds the database in memory, and its `@dump` writes it out.

`@backup/list` reports the copies on disk, newest first, without taking one.

- [@backup copy]: messages, events, and why the copy is a consistent snapshot
- [@backup retention]: how many copies are kept, and copies taken on an interval

## How a copy is taken

Everyone sees dump_message as the copy starts and dump_complete when it is written, and the dump and backup events are raised (see [event dump] and [event backup]). A failed copy tells connected wizards and royalty instead.

The game keeps running throughout. The copy holds the world as it was the moment the copy started: everything saved before then is in it, nothing after. It is written aside and moved into place only once complete, so a backup tool reading the directory never sees a half-written one.

## Retention and scheduled copies

Older copies are deleted as new ones arrive, keeping 2 unless the server is set to keep more. Where the copies go, how many are kept, and whether one is also taken on an interval are set on the server process, not with `@config`: `SHARPMUSH_BACKUP_PATH`, `SHARPMUSH_BACKUP_KEEP` and `SHARPMUSH_BACKUP_INTERVAL` (such as `6h`). A scheduled copy warns everyone five minutes and one minute before it, with dump_warning_5min and dump_warning_1min.

::: seealso
- [@dump]
- [@dbck]
- [@shutdown]
:::
