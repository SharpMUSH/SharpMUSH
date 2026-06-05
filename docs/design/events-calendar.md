# Events & Calendar

## Overview

Events ARE scheduled scenes. No separate event system. A scene with a
`scheduled_start` in the future is an event. The scene system already has
everything needed: title, description, participants, rooms. We just add
scheduling fields and RSVP.

## Model: Scene + Scheduling Fields

The scene data model (see scene-system.md) gains:

```
Scene {
    ...existing fields...
    scheduled_start: DateTime?       // null = immediate (normal scene)
    scheduled_duration: TimeSpan?    // estimated length (display only)
    rsvp_list: [{                    // who's interested
        character_id: string,
        status: "interested" | "attending",
        timestamp: DateTime
    }]
}
```

**That's it.** A "scheduled scene" IS an event. An "event" IS a scene that
hasn't started yet.

## Lifecycle

```
1. Player creates scheduled scene:
   +scene/schedule <title>=<start_time>/<description>
   → Scene created with state: "scheduled", scheduled_start set
   → No temp room created YET (room created when scene actually starts)

2. Others RSVP:
   +scene/rsvp <scene#>
   → Added to rsvp_list with status "attending"

3. Organizer starts the scene (at or after scheduled time):
   +scene/start <scene#>
   → Temp room created (or specified grid room)
   → State: "scheduled" → "active"
   → RSVP'd characters notified ("Scene X is starting!")
   → Characters can +scene/join as normal

4. Scene proceeds as any other scene.

5. Scene ends normally.
```

**No auto-start.** The organizer manually starts the scene. Scheduled time is
advisory — it tells people when to show up, but the organizer triggers the
actual start. (Games are social; strict auto-start is antisocial.)

## Web Portal: Events View

### Upcoming Events (`/scenes/upcoming` or sidebar widget)

```
┌─────────────────────────────────────────────────────────┐
│  📅 Upcoming Events                                      │
├─────────────────────────────────────────────────────────┤
│  Tomorrow, 8:00 PM                                      │
│    The Council of Elrond                                 │
│    Organizer: Elrond  │  4 attending  │  [RSVP]         │
│                                                         │
│  Saturday, 3:00 PM                                      │
│    Summer Festival                                      │
│    Organizer: Gandalf  │  7 attending  │  [✓ Attending] │
│                                                         │
│  Next Wednesday, 9:00 PM                                │
│    Combat Training                                      │
│    Organizer: Aragorn  │  2 attending  │  [RSVP]        │
└─────────────────────────────────────────────────────────┘
```

### Event Detail (`/scenes/42` — same URL as any scene, just in "scheduled" state)

```
┌─────────────────────────────────────────────────────────┐
│  📅 The Council of Elrond                                │
├─────────────────────────────────────────────────────────┤
│  Status: Scheduled                                      │
│  When: Tomorrow, June 6, 2025 at 8:00 PM               │
│  Duration: ~2 hours (estimated)                         │
│  Organizer: Elrond                                      │
│  Location: Council Chamber (or TBD)                     │
├─────────────────────────────────────────────────────────┤
│  Description:                                           │
│  Representatives of all free peoples gather to decide   │
│  the fate of the One Ring. Serious RP expected.         │
├─────────────────────────────────────────────────────────┤
│  Attending (4):                                         │
│    Elrond (organizer), Gandalf, Frodo, Aragorn          │
│  Interested (2):                                        │
│    Legolas, Gimli                                       │
├─────────────────────────────────────────────────────────┤
│  [RSVP: Attending] [RSVP: Interested] [Cancel RSVP]    │
└─────────────────────────────────────────────────────────┘
```

## Widget: Upcoming Events

The "Upcoming Events" widget (from widget-system.md) simply queries:

```
SELECT FROM scenes
WHERE scheduled_start IS NOT NULL
  AND scheduled_start > NOW()
  AND state = "scheduled"
ORDER BY scheduled_start ASC
LIMIT {config.max_shown}
```

**Config:** `max_shown` (default 5), `days_ahead` (default 14).

This is a read of the scene collection — no separate events collection needed.

## In-Game Commands

All softcoded (or engine-supported, using same scene infrastructure):

```
+scene/schedule <title>=<time>/<desc>    Create scheduled scene (event)
+scene/rsvp <scene#>                     RSVP as "attending"
+scene/rsvp/interested <scene#>          RSVP as "interested"
+scene/rsvp/cancel <scene#>              Cancel RSVP
+scene/start <scene#>                    Organizer starts the scheduled scene
+events                                  List upcoming scheduled scenes
+events/mine                             List events I'm RSVP'd to
```

`+events` is literally `+scenes` filtered to `scheduled_start IS NOT NULL AND state = "scheduled"`.

## NATS Events

```json
// Scene scheduled (new event)
{
  "subject": "portal.scene.live",
  "type": "scene_scheduled",
  "scene_id": 42,
  "title": "The Council of Elrond",
  "scheduled_start": "2025-06-06T20:00:00Z",
  "organizer": "Elrond"
}

// RSVP change
{
  "subject": "portal.scene.live",
  "type": "scene_rsvp",
  "scene_id": 42,
  "character": "Gandalf",
  "status": "attending"
}

// Scheduled scene started
{
  "subject": "portal.scene.live",
  "type": "scene_started",
  "scene_id": 42,
  "title": "The Council of Elrond"
}
```

All use existing `portal.scene.live` subject. No new NATS subject needed.

## Web RSVP

RSVP from web triggers the same game action as the in-game command:

```
POST /mush/scene/42/rsvp
  → { character_id: "#42", status: "attending" }
  → HTTP handler executes RSVP logic
  → NATS event published
  → Web UI updates in real-time
```

RSVP is simple enough to go through HTTP handler directly (no need to route
through terminal for a one-field toggle).

## Why Not a Separate Events System

- Scenes already have: title, description, participants, lifecycle states,
  rooms, archives. Events need the same things.
- "Event" is just a scene that hasn't started yet. The only new data is
  `scheduled_start` and `rsvp_list`.
- One collection, one set of commands (with aliases), one UI path.
- When a scheduled scene starts, it becomes a normal active scene. No state
  migration, no data copying between separate systems.
- Calendar widget is just a query filter on scenes. Done.
