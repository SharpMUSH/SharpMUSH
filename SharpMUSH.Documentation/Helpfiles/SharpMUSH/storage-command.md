<!-- help-article
{
  "corpus": "help",
  "id": "storage-command",
  "lookup": "@storage",
  "aliases": [],
  "sections": [
    {"id": "panel", "heading": "The storage panel", "lookup": "@storage panel"},
    {"id": "history", "heading": "History kept in the world", "lookup": "@storage history", "aliases": ["@storage/history"]},
    {"id": "purge", "heading": "Purging history", "lookup": "@storage purge", "aliases": ["@storage/purge"]}
  ]
}
-->
# @storage

`@storage`<br>
`@storage/history`<br>
`@storage/purge`<br>
`@storage/backups [<page>]`

Wizard-only. Reports where the world's disk goes, and what the version histories inside it hold. This is a SharpMUSH command; PennMUSH keeps its database in memory and has no file to report on.

`@storage/backups` lists the backup copies on disk, a page at a time, as `@backup/list` does.

- [@storage panel]: the bars and figures `@storage` shows, and why the file never shrinks
- [@storage history]: the kinds of history the world keeps
- [@storage purge]: running a retention pass, and what a policy never removes

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

::: seealso
- [@backup]
- [@stats]
:::
