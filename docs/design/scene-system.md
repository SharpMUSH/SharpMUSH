# Scene System Design

## Overview

The scene system provides structured, opt-in roleplay sessions with metadata,
participant tracking, pose logging, and web-based participation. Scenes are
explicitly created (not passive room logging), have a lifecycle (scheduled →
active → completed → published), and can be browsed/searched on the portal.

Design informed by Volund's SceneSys (SQL-backed, roles, sources, plots) and
AresMUSH (scene types, content warnings, sharing). Adapted for SharpMUSH's
graph DB, WebSocket architecture, and PennMUSH conventions.

## Core Concepts

### Scene ≠ Room (but usually correlates)

A scene is a discrete RP session. By default, it's tied to a room (the
location where RP happens). The character must physically be in that room
to participate — PennMUSH convention.

**Future consideration (Phase 2+): Virtual Scenes**

Virtual scenes have no room (web-only, for flashbacks, split-location RP,
collaborative writing). The data model accommodates this via a nullable
`location_dbref` — but Phase 1 ships with strict room-tied scenes only.

The presence/location question (can a character be in a web scene while
physically somewhere else on the grid?) is deferred. Phase 1 maintains
PennMUSH parity: your character is where they are. Period.

### Scene Lifecycle

```
SCHEDULED  → A scene has been announced for a future time
     │        (optional — scenes can skip straight to ACTIVE)
     ▼
ACTIVE     → RP is happening right now. Poses are being logged.
     │        Characters can join/leave.
     ▼
COMPLETED  → Scene has ended. Log is frozen (no new poses).
     │        Participants can still edit their own poses.
     ▼
PUBLISHED  → Scene log is cleaned up and made public.
     │        (Default: public. Admin-configurable.)
     │
     └──── PRIVATE (alt state) → Scene remains visible only to participants.
```

### Participant Roles (from Volund's model)

- **Runner / Storyteller** (type 2) — created the scene, runs NPCs, sets pace
- **Helper** (type 1) — co-GM, assists runner
- **Participant** (type 0) — regular player in the scene
- **Watcher** (read-only) — observing but not posing

### Actor Roles (from Volund's actrole concept)

One player can portray multiple characters/NPCs in a scene:
- Alice (player) has roles: "Alice" (her PC) + "Guard Captain" (NPC she's running)
- Each pose is attributed to a specific role, not just the player

## Data Model (Separate Collections)

### Scenes Collection

```
{
  scene_id: string (UUID),
  title: string,
  pitch: string | null,           // Short description / hook
  outcome: string | null,         // Summary after completion
  scene_type: string,             // "social", "action", "vignette", "event"
  status: enum,                   // scheduled, active, completed, published, private
  location_dbref: string | null,  // Room DBRef (null = virtual scene, Phase 2+)
  location_name: string,          // Snapshot of room name at scene start
  content_warnings: string[],     // ["violence", "mature themes"]
  
  created_at: datetime,
  scheduled_for: datetime | null,
  started_at: datetime | null,
  finished_at: datetime | null,
  published_at: datetime | null,
  
  log_ooc: bool,                  // Whether OOC poses are included in published log
  visibility: enum,               // public (default), participants_only
  
  // Denormalized stats (updated on pose)
  pose_count: int,
  last_activity_at: datetime | null
}
```

### Participants Collection

```
{
  participant_id: string (UUID),
  scene_id: string,               // FK → Scenes
  character_id: string,           // Character DBRef
  character_name: string,         // Snapshot at join time
  
  role: enum,                     // runner, helper, participant, watcher
  status: enum,                   // active, left, idle_removed
  
  joined_at: datetime,
  left_at: datetime | null,
  
  // Per-participant stats
  pose_count: int,
  last_pose_at: datetime | null
}
```

### Actor Roles Collection

```
{
  actrole_id: string (UUID),
  participant_id: string,         // FK → Participants
  scene_id: string,               // FK → Scenes (denormalized for query efficiency)
  role_name: string,              // "Gandalf", "Guard Captain", "Narrator"
  is_primary: bool,               // True for the player's own character
  
  created_at: datetime
}
```

### Poses Collection

```
{
  pose_id: string (UUID),
  scene_id: string,               // FK → Scenes (partition key for queries)
  actrole_id: string,             // FK → Actor Roles (who is posing as whom)
  participant_id: string,         // FK → Participants (denormalized)
  
  pose_type: enum,                // ic, ooc, emit, spoof, system
  text: string,                   // Raw MString (ANSI markup preserved)
  text_plain: string,             // Plain text (ANSI stripped, for search)
  
  created_at: datetime,
  edited_at: datetime | null,
  is_deleted: bool,               // Soft delete (for edit history)
  
  // Ordering
  sequence: int                   // Auto-increment per scene (stable sort order)
}
```

### Plots Collection (Story Arcs)

```
{
  plot_id: string (UUID),
  title: string,
  pitch: string | null,
  outcome: string | null,
  
  date_start: datetime | null,
  date_end: datetime | null,
  
  runners: [{ character_id, character_name, role }],
  scene_ids: string[]             // Scenes linked to this plot
}
```

### Index Strategy

```
Poses:
  - (scene_id, sequence) — primary read path: "get poses for scene, ordered"
  - (scene_id, created_at) — time-based queries
  - (participant_id, created_at) — "all poses by this character"
  - Full-text on text_plain — scene search

Scenes:
  - (status, last_activity_at) — "active scenes sorted by recent activity"
  - (location_dbref, status) — "scene in this room"
  - Full-text on title, pitch — omnisearch

Participants:
  - (scene_id, role) — "who's in this scene"
  - (character_id, scene_id) — "is this character in this scene"
  - (character_id, joined_at DESC) — "recent scenes for character"
```

## Web-Based Participation

### The WebSocket Is the MUSH Session

A character's web connection IS a MUSH session. When they pose from the web,
it goes through the same pipeline as a telnet pose:

```
Web pose editor → SignalR → Server → Game engine (as if typed in-game)
  ↓
Game engine processes pose → emits to room
  ↓
Room emit → NATS event → SignalR hub → all web clients in that scene
              ↓
         Also → telnet echo to telnet clients in the room
```

The game doesn't distinguish web poses from telnet poses. Both arrive as
text input on a session. Both produce the same output.

### Dual Channel Architecture

Each character's WebSocket carries two logical streams:

```
SignalR Hub Groups per character session:
  
  system:{session_id}
    ├── Command output (look, inventory, help)
    ├── Pages / whispers
    ├── Notifications (mail, alerts)
    └── System messages (connect/disconnect notices)
  
  scene:{scene_id}
    ├── IC poses (from all participants)
    ├── OOC asides
    ├── Scene metadata changes (join/leave, title change)
    └── Pose edits/deletions
```

The portal UI routes these to different panels:
- **Terminal panel** — system channel (traditional MUD output)
- **Scene panel** — clean pose stream (novel-like reading experience)

A player can have both open simultaneously: run commands in the terminal,
read/write poses in the scene panel.

### Scene Panel vs Terminal

The scene panel is NOT a terminal. It's a purpose-built RP interface:

```
┌─────────────────────────────────────────────┐
│  Scene: A Meeting at Rivendell              │
│  Location: Council of Elrond  │ 4 posing    │
├─────────────────────────────────────────────┤
│                                             │
│  [Avatar] Gandalf                    14:23  │
│  Gandalf rises from his seat, staff in      │
│  hand. "The Ring must be destroyed."        │
│                                             │
│  [Avatar] Frodo                      14:25  │
│  The hobbit looks down at the golden band   │
│  on the table. "I will take it," he says    │
│  quietly. "Though I do not know the way."   │
│                                             │
│  [Avatar] Elrond                     14:26  │
│  "Then you shall be the Ring-bearer."       │
│                                             │
├─────────────────────────────────────────────┤
│  [Pose editor - textarea]                   │
│  ┌─────────────────────────────────────┐    │
│  │                                     │    │
│  │                                     │    │
│  └─────────────────────────────────────┘    │
│  [Pose] [OOC] [Emit] [Spoof▾]    [Submit]  │
└─────────────────────────────────────────────┘
```

Features:
- Clean reading flow (no command output noise)
- Pose type buttons (IC, OOC, emit, spoof)
- Character name/avatar next to each pose
- Timestamps (hover for full datetime)
- Edit own poses (pencil icon, visible only to author)
- Soft-delete own poses (within time window)

## In-Game Commands

### Scene Management

```
+scene/create [title]           — Start a scene in current room
+scene/create <title>=<pitch>   — Start with description/hook
+scene/end                      — End the current scene
+scene/title <title>            — Change scene title
+scene/type <type>              — Set scene type (social/action/vignette)
+scene/warn <warning>           — Add content warning

+scene/join                     — Join the scene in your current room
+scene/leave                    — Leave the scene (stay in room)
+scene/invite <player>          — Invite someone to join
+scene/boot <player>            — Remove someone from scene (runner only)
```

### Scene Viewing

```
+scene                          — Show current scene info
+scene/list                     — List active scenes
+scene/log [N]                  — Show last N poses (catch-up)
+scene/search <query>           — Search scene archives
```

### Pose Tracker (inspired by Volund's +pot)

```
+pot                            — Show pose order tracker
+pot/last                       — Show who posed last and when
```

### Scene Publishing

```
+scene/publish                  — Publish scene to public archive
+scene/private                  — Keep scene visible only to participants
+scene/edit <pose#>=<new text>  — Edit a pose in the log
+scene/delete <pose#>           — Soft-delete a pose from log
```

## Visibility & Publishing (Decision 7.5C)

### Admin Default: Public

Out of the box, completed scenes default to **published** (publicly visible
in the scene archive). The admin can change this default in configuration.

### Player Override

- Any participant can mark a scene as "private" before or after completion
- Any participant can "unpublish" a scene they participated in (removes from
  public archive, but log still accessible to participants)
- Runner can set scene visibility at creation time

### Content Warnings

Scenes with content warnings are:
- Listed in the archive with warnings visible BEFORE opening
- Optionally hidden behind a click-through acknowledgment
- Filterable (players can filter out certain content warning types)

## Live Scene Indicators (Decision 7.6A)

### "What's Happening Now" Widget

The home page shows active scenes:

```
┌─────────────────────────────────────────────┐
│  🎭 Active Scenes                           │
├─────────────────────────────────────────────┤
│  A Meeting at Rivendell                     │
│  📍 Council of Elrond · 4 participants      │
│  Last activity: 2 minutes ago               │
│                                             │
│  Patrol at the Border                       │
│  📍 Northern Watchtower · 2 participants    │
│  Last activity: 8 minutes ago               │
├─────────────────────────────────────────────┤
│  2 active scenes · 6 players online         │
└─────────────────────────────────────────────┘
```

### Privacy in Scene Listing

- Only **public** scenes appear in the "active scenes" list
- Scenes marked private are invisible to non-participants
- Participant names shown only if scene is public AND character is not "anonymous"
- Anonymous participation: character appears as "Someone" in the listing

### Joining

The scene list shows a "Go" link for physical scenes — clicking it
indicates interest. The player must actually move their character to
the room to participate (no auto-teleport by default; admin can configure).

## NATS Event Schema

Scene events published to `portal.scene.live`:

```json
{
  "type": "pose",
  "scene_id": "scene-42",
  "pose_id": "pose-abc",
  "actrole_id": "role-xyz",
  "character_name": "Gandalf",
  "pose_type": "ic",
  "text": "\u001b[1mGandalf\u001b[0m strokes his beard.",
  "sequence": 147,
  "timestamp": "2025-06-05T14:30:00Z"
}

{
  "type": "scene_meta",
  "scene_id": "scene-42",
  "event": "participant_joined",
  "character_name": "Frodo",
  "role": "participant",
  "timestamp": "2025-06-05T14:28:00Z"
}

{
  "type": "scene_meta",
  "scene_id": "scene-42",
  "event": "status_changed",
  "old_status": "active",
  "new_status": "completed",
  "timestamp": "2025-06-05T16:00:00Z"
}
```

## Scene Archive (Web Portal)

### Browse View (`/scenes`)

- Paginated list of published scenes
- Sort by: date, popularity (views/likes), activity
- Filter by: participant, type, plot, date range, content warnings
- Each entry shows: title, participants, date, pose count, type badge

### Scene Reader (`/scenes/{scene_id}`)

- Full scene log rendered as clean prose
- Character names styled (colored per character for readability)
- OOC asides either hidden or shown in muted style (based on log_ooc flag)
- Participant sidebar (who was in this scene, their roles)
- Related scenes (same plot, same participants)
- "Like" / bookmark functionality

### Search

Scene content searchable via omnisearch:
- Full-text search on pose text (plain text version)
- Filter by character, date range, scene type
- Results show matched pose in context (surrounding poses)

## Relationship to Other Systems

### Character Profiles

Profile page shows "Recent Scenes" tab — list of published scenes
this character participated in, most recent first.

### Wiki

Scene logs can link to wiki pages (character names → profiles,
location names → wiki articles about those places).
Wiki pages can embed scene references: `[[Scene:42|A Meeting at Rivendell]]`.

### Plots

Scenes can be linked to plots (story arcs). The plot page shows all
linked scenes in chronological order — a reading order for an ongoing story.

### Presence

Active scenes appear in the presence system. "Who's online" shows
which characters are in active scenes and which are idle.

## Configuration

```yaml
scenes:
  default_visibility: public      # public or participants_only
  scene_types:
    - social
    - action
    - vignette
    - event
  idle_timeout_minutes: 120       # Auto-end scene after this much inactivity
  max_pose_edit_minutes: 60       # Players can edit poses within this window
  ooc_color: "%xh%xc"            # Color for OOC asides in-game
  content_warnings:               # Pre-defined content warning options
    - violence
    - mature themes
    - character death
    - dark themes
  pose_order_tracking: true       # Enable +pot pose-order tracker
  auto_join_on_pose: true         # Auto-add to participants when someone poses
  virtual_scenes_enabled: false   # Phase 2+: scenes without room attachment
  long_disconnect_threshold_seconds: 300  # 5 minutes (configurable)
```
