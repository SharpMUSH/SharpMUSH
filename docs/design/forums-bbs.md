# Forums / BBS

## Overview

Bulletin boards are softcode: the `bboards` package (`examples/packages/bboards`). The engine
knows nothing about boards. Players use the `+bbread`/`+bbpost` commands Myrddin's board players
know, and the portal's Boards application (the `bboards-app` package, at `/apps/boards/`) shows
the same boards through routes the package adds to the HTTP handler. The player's guide is
`guides/bboards` on the documentation site.

## Storage

Everything lives in the game's object database:

- The **BBoards** object (WIZARD) holds the commands, the settings (`DATA`CONFIG`*`, shown and
  changed through `+config`) and the portal routes' code.
- Each **board** is an object. Boards nest, and are found by number path (`2/1`) or name.
- Each **post** is an object inside its board. Comments and replies are attributes on the post.
- Each character's **`BBOARD`** attribute tree records what they have read. Only wizards write it.

## Why Softcoded

- **Admin customization:** Different games want different BBS formats. Staff
  can modify display, add/remove features, change command syntax without
  touching engine code.
- **On-MUSH storage:** Attributes on objects. Standard PennMUSH pattern.
  Backup, restore, and migration work the same as everything else.
- **No engine coupling:** The engine doesn't need to know BBS exists. It's
  just objects with attributes and commands that read/write them.

## Portal routes

The package adds these routes to the HTTP handler:

```
GET  /http/boards/schema?at={path}   → the page's layout for that board, post or comment
GET  /http/boards/data?at={path}     → what the page shows there
GET  /http/boards/nav                → the sidebar's board list
POST /http/boards/act                → a button on the page: post, comment, edit, pin, follow, ...
```

The HTTP handler hands each route the signed-in character as `%<viewer>`. The schema and nav
routes pass it to the BBoards object's functions; the data and act routes `@force` the WIZARD
BBoards object, which does the work for that character. Either way the code checks `%<viewer>`
against the same locks the commands use, so the portal knows no command syntax and repeats no
checks.

## Permissions

Each board has four user locks: `bbread`, `bbpost`, `bbcomment` and `bbmoderate`. A board without
one of them takes it from the nearest parent board that has it, and reading a board also needs the
read lock of every board above it. The locks decide what a character
sees and may do, in the game and in the portal alike.

Two permissions reach every board: `softcode.bboards.admin` (create, configure, order, lock and
remove boards, moderate and read everything) and `softcode.bboards.moderate` (pin, close, move,
edit and remove posts and comments on every board the holder can read). The package's
`bboard-admin` and `bboard-moderator` roles grant them.

## Notices

`+bbnotify <board>=on|off` turns new-post notices for one board on or off, and
`+bbnotify on|off` for every board.
