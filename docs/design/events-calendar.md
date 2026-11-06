# Events & Calendar

## Overview

Events are scheduled scenes. There is no separate event system: the scene package
(`examples/packages/scene`) announces a scene before it happens, records who has RSVP'd, and
starts it when its owner says so. Everything a scene has (title, pitch, members, privacy, log)
an event has too.

## Lifecycle

```
1. +scene/schedule <when>/<title>=<pitch>
   → a scene with status "scheduled", a time, and no room yet

2. +scene/rsvp <id>   (or +scene/tag <id>)
   → the player joins the scene's "attending" role; +scene/unrsvp (+scene/untag) withdraws it

3. +scene/start <id>
   → the owner starts it in the room they are in; it begins recording
```

Nothing starts on its own: the time tells people when to come, and the owner starts the scene.
A scheduled or paused scene records nothing and is not listed as live. Rescheduling a running scene
pauses it. A finished or cancelled scene can't be rescheduled.

## In-Game Commands

```
+scene/schedule <when>/<title>=<pitch>   Announce a scene
+scene/reschedule <id>=<when>            Move it
+scene/unschedule <id>                   Cancel it (or +scene/cancel <id>)
+scene/pause [<id>][=<when>]             Pause a running scene, optionally with a new time
+scene/start [<id>]                      Start a scheduled scene here, or resume a paused one
+scene/rsvp <id>                         RSVP (or +scene/tag <id>)
+scene/unrsvp <id>                       Withdraw it (or +scene/untag <id>)
+scene/upcoming [<days>|all]             What is scheduled (or +scenes, +events)
```

`<when>` is read in the player's `+timezone`, or UTC. The scene package's help on scheduling lists
every form it takes.

## Portal

- The Scenes page schedules a scene by running `+scene/schedule` as the acting character, so the
  portal and the game share one set of rules.
- The portal has no RSVP control, and the scene package serves no RSVP route. Players RSVP in the
  game.
- `api/scenes/calendar.ics` is an iCalendar feed of the public scenes that have a time.
  `POST api/scenes/calendar/link` gives the acting character a private feed,
  `api/scenes/calendar/{secret}.ics`, which carries every timed scene that character may see
  (`?scope=mine` for only the scenes they run or have joined). Making a new link retires the old one.

## Why Not a Separate Events System

- Scenes already have title, pitch, members, lifecycle and archives. Events need the same things.
- An event is a scene that hasn't started yet. When it starts, it is an ordinary running scene, with
  no data copied between systems.
- One set of commands and one listing serve both.
