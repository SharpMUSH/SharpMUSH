<!-- help-article
{
  "corpus": "help",
  "id": "storage-command",
  "lookup": "@storage",
  "aliases": [],
  "sections": [
    {"id": "panel", "heading": "The storage panel", "lookup": "@storage panel"},
    {"id": "history", "heading": "History kept in the world", "lookup": "@storage history", "aliases": ["@storage/history"]},
    {"id": "purge", "heading": "Purging history", "lookup": "@storage purge", "aliases": ["@storage/purge"]},
    {"id": "dictionaries", "heading": "Compression dictionaries", "lookup": "@storage dictionaries", "aliases": ["@storage/dictionaries"]}
  ]
}
-->
# @storage

`@storage`<br>
`@storage/history`<br>
`@storage/purge`<br>
`@storage/backups [<page>]`<br>
`@storage/dictionaries [<kind> [<id>]]`

Wizard-only. Reports where the world's disk goes, and what the version histories inside it hold. This is a SharpMUSH command; PennMUSH keeps its database in memory and has no file to report on.

- [@storage panel]: the bars and figures `@storage` shows, and why the file never shrinks
- [@storage history]: the kinds of history the world keeps
- [@storage purge]: running a retention pass, and what a policy never removes
- [@storage dictionaries]: how feed lines, attributes and mail are compressed, and going back to an earlier dictionary

`@storage/backups` lists the backup copies on disk, a page at a time, the same as `@backup/list`. `@storage/backups` and `@storage/dictionaries` are the only forms of `@storage` that take an argument.

## The storage panel

`@storage`

`@storage` alone draws a panel. Its bars show:

- **Map**: how far the world's file has grown against the map limit, the most it may ever grow to. The limit is a ceiling, not an allocation: it costs neither disk nor memory until it is used.
- **Feeds**: what feed lines, channel recall among them, take of the live data. A table below gives each feed kind's feeds, lines and size.
- **Next run**: the free space the next `@backup` checks for, against what the backup disk has.

**File size** is the logical file length. **Inside file** shows live and reusable bytes; no reusable pages does not mean the map is full. **On disk** shows physical allocation, which may be smaller for sparse files.

The other figures show the free disk beside the world, the backup copies kept, and the size of the next copy and of the backup directory at the height of a run. Earlier worlds still on disk, such as the `.previous` world a promoted import replaced, are listed until you delete them. Notes at the bottom say when the next backup will not fit, when backups share the world's disk, and how many readers a dead process left behind have been cleaned up since startup.

Deleting things frees space inside the file for reuse. It never shrinks the file. The file only gets smaller when it is replaced with a compacted copy: setting `SHARPMUSH_LIGHTNING_COMPACT_ON_START=true` on the server does that at its next start, keeping the original beside it as `<path>.precompact`.

## History kept in the world

`@storage/history`

`@storage/history` draws a table of the history the world keeps: each kind's records, their size, how many streams hold them, and the kind's retention policy. The kinds are:

- **wiki**: every revision of every wiki page and translation.
- **scene.edits**: every version of every scene pose.
- **scene.deleted**: poses deleted from a scene but still stored. Deleting a pose only hides it.
- **audit**: the log of staff actions.
- **rolls**: rolls kept by `@roll` and roll() with `record`.
- **activity**: poses counted per character and room per day, behind [activity()]. Each day is one stream.

A *stream* is one page's text in one language, or one pose.

## Purging history

`@storage/purge`

`@storage/purge` runs a retention pass now. It removes what each kind's policy allows. A record that is purged is gone from the live world. It survives only in an archive, if one is configured, and in backups taken before the pass. The pass works in small batches and the game keeps running throughout.

The policy is set on the server, not with `@config`. **The default keeps everything**, so `@storage/purge` purges nothing until a policy is configured. A policy never removes:

- the version a page or pose shows now.
- any pose version that `redo` can still reach.
- anything on a protected wiki page.

Undo stops at the oldest version that survives. A wiki rollback can only go back to a revision that survives. The policy for each kind is set on the server with `SHARPMUSH_HISTORY_<KIND>_MAX_AGE` (how old a record may get, such as `365d`) and, for `WIKI` and `SCENE_EDITS`, `SHARPMUSH_HISTORY_<KIND>_KEEP` (how many of the newest versions always stay). The kind is written in capitals with `_` for `.`, such as `SCENE_DELETED`, and `SHARPMUSH_HISTORY_INTERVAL` runs a pass on a schedule.

## Compression dictionaries

`@storage/dictionaries`<br>
`@storage/dictionaries <kind>`<br>
`@storage/dictionaries <kind> <id>`

Feed lines, attribute values and mail are stored compressed with zstd dictionaries; the kinds are `feed`, `attributes` and `mail`, and each has its own. Values under 64 bytes, and attribute values of that size are most of them, are stored as they are. Dictionary 0 of each kind ships with the server. At 100 values of a kind, and each time they grow fourfold, the hourly upkeep trains one from the world's own. It is kept only when it makes values it was not trained on at least 3% smaller and saves more than its own size; the values are then rewritten with it. Every dictionary stays in the world, so every value stays readable.

`@storage/dictionaries` sums up each kind: the dictionary in use, how many are kept, and the values compressed and stored as they are. `@storage/dictionaries <kind>` lists that kind's dictionaries (id, when trained, size, values using it) and its last 10 training runs: values held and read, bytes per value before and after, and whether it was kept.

`@storage/dictionaries <kind> <id>` goes back to dictionary `<id>`: new values use it, and every value is rewritten with it before the command answers. Later dictionaries stay. Only a value's size depends on its dictionary, never its text.

::: seealso
- [@backup]
- [@stats]
:::
