# Forums / BBS

## Overview

BBS is entirely softcoded. Storage lives on-MUSH (game object DB — attributes
on a BBS object or set of objects, however the softcoder designs it). The web
portal reads board data via HTTP Handler (same pattern as profiles). Posting
from web goes through the terminal connection — the player types `+bbs/post`
in their terminal panel.

## Architecture

```
┌───────────────────────────────────────────────────────┐
│  MUSH Object DB                                       │
│  BBS Master Object → Board Objects → Post Attributes  │
│  (entirely softcoded, admin-customizable)             │
└───────────────────────┬───────────────────────────────┘
                        │
           ┌────────────┼────────────┐
           ▼            ▼            ▼
      In-game        HTTP Handler    NATS event
      +bbs cmd       serves read     on new post
           │            │            │
           │            ▼            ▼
           │       Portal API    SignalR push
           │       (read-only)   (new post badge)
           │            │            │
           │            ▼            ▼
           │       Web BBS UI    Notification
           └─────── Same data ───────┘
```

## Why Softcoded

- **Admin customization:** Different games want different BBS formats. Staff
  can modify display, add/remove features, change command syntax without
  touching engine code.
- **On-MUSH storage:** Attributes on objects. Standard PennMUSH pattern.
  Backup, restore, and migration work the same as everything else.
- **No engine coupling:** The engine doesn't need to know BBS exists. It's
  just objects with attributes and commands that read/write them.

## HTTP Handler (Read-Only)

The HTTP handler exposes board data for the web portal to display (PLANNED — not yet
implemented; routes under the live `/http/...` handler prefix):

```
GET /http/bbs/boards
  → List of boards: name, description, post count, last post date, read perms

GET /http/bbs/boards/{board_name}?page=1&limit=25
  → List of posts: id, author, subject, date, read/unread

GET /http/bbs/posts/{board_name}/{post_id}
  → Post detail: author, subject, date, body (MString)
```

**Permissions:** HTTP handler checks the requesting character's access against
the board's read lock. If the character can't read the board in-game, the API
returns 403.

**Body format:** Post bodies are MString (they can contain ANSI formatting
from in-game). Web renders via `.ToHtml()`.

## Writing (Terminal Only)

**No separate web write API.** To post from the web portal:

1. Player has terminal panel open (they're connected to the game)
2. Player types `+bbs/post <board>=<subject>/<body>` in terminal
3. Softcode handles it exactly as if they typed it from telnet
4. NATS event fires on new post → web UI updates

**Why:** BBS post commands are softcoded. Different games customize the syntax,
validation, formatting, and behavior. Routing through terminal means the
softcode handles everything — the portal doesn't need to know the command
syntax or replicate validation.

**UX convenience:** The web BBS view can have a "New Post" button that:
- Switches focus to the terminal panel
- Pre-fills the command: `+bbs/post <board>=`
- Player types subject/body and hits enter

This is a UI shortcut, not a separate API path. The post still routes through
the game engine's softcode.

## Web BBS UI

### Board List (`/bbs`)

```
┌─────────────────────────────────────────────────────────┐
│  📋 Bulletin Boards                                      │
├─────────────────────────────────────────────────────────┤
│  Board           │ Posts │ Last Post     │ Description   │
│  ─────────────── │ ───── │ ──────────── │ ───────────── │
│  Announcements   │  12   │ 2 hours ago  │ Staff news    │
│  General         │  47   │ 30 min ago   │ Open discuss  │
│  RP Requests     │  23   │ 1 day ago    │ LFG/LFP       │
│  Bug Reports     │   8   │ 3 days ago   │ Report bugs   │
└─────────────────────────────────────────────────────────┘
```

### Post List (`/bbs/{board}`)

```
┌─────────────────────────────────────────────────────────┐
│  ← Boards    General Discussion              [New Post] │
├─────────────────────────────────────────────────────────┤
│  # │ Subject              │ Author    │ Date            │
│  ──│──────────────────────│───────────│─────────────────│
│  47│ Summer event idea    │ Gandalf   │ 30 min ago      │
│  46│ New area feedback    │ Frodo     │ 2 hours ago     │
│  45│ Looking for RP       │ Aragorn   │ 1 day ago       │
├─────────────────────────────────────────────────────────┤
│  Page 1 of 2                          [< 1 2 >]        │
└─────────────────────────────────────────────────────────┘
```

### Post View (`/bbs/{board}/{id}`)

```
┌─────────────────────────────────────────────────────────┐
│  ← General    #47: Summer event idea                    │
├─────────────────────────────────────────────────────────┤
│  Author: Gandalf                                        │
│  Date: June 5, 2025, 14:30                              │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  I was thinking we could do a summer festival event     │
│  next weekend. Maybe in the town square area?           │
│                                                         │
│  Anyone interested in helping organize?                 │
│                                                         │
├─────────────────────────────────────────────────────────┤
│  [Reply in Terminal]                                     │
└─────────────────────────────────────────────────────────┘
```

- "Reply in Terminal" → focuses terminal, pre-fills `+bbs/post general=Re: Summer event idea/`
- Body rendered as MString.ToHtml() (preserves ANSI formatting from in-game)

## Real-Time Updates

When a new post is created in-game:
1. Softcode writes the post to the BBS object
2. Softcode (or a hook) publishes NATS event: `portal.bbs.new_post`

```json
{
  "board": "general",
  "post_id": 47,
  "author": "Gandalf",
  "subject": "Summer event idea",
  "timestamp": "2025-06-05T14:30:00Z"
}
```

3. SignalR forwards to connected web clients
4. Web UI: badge on BBS nav item, toast notification if configured

## Permissions

- Board read permissions enforced by HTTP handler (checks character's game-level access)
- Board write permissions enforced by softcode (on `+bbs/post` command)
- Web UI hides boards the character can't read (HTTP handler returns filtered list)
- Guest access: depends on board locks. Public boards may allow guest read.
  Staff boards require Player+ or specific flags.

## Configuration

None in the portal config. BBS is entirely softcoded. Board creation, naming,
permissions, formatting — all managed in-game by staff via `+bbs/create`,
`+bbs/lock`, etc. The web portal just renders what the HTTP handler returns.
