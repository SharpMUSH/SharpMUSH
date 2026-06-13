# Area 17: Events & Calendar — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (17.1–17.4) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks

### Scene Model Extension
- [ ] Add `scheduled_start: DateTime?` to scene schema
- [ ] Add `scheduled_duration: TimeSpan?` to scene schema (display only)
- [ ] Add `rsvp_list: [{character_id, status, timestamp}]` to scene schema
- [ ] "scheduled" state in scene lifecycle (before "active")
- [ ] No temp room created until scene actually starts (organizer triggers)

### Commands
- [ ] `+scene/schedule <title>=<time>/<desc>` — create with state "scheduled"
- [ ] `+scene/rsvp <scene#>` — add to rsvp_list as "attending"
- [ ] `+scene/rsvp/interested <scene#>` — add as "interested"
- [ ] `+scene/rsvp/cancel <scene#>` — remove from rsvp_list
- [ ] `+scene/start <scene#>` — organizer transitions scheduled → active (creates room)
- [ ] `+events` — list upcoming scheduled scenes (alias for filtered +scenes)
- [ ] `+events/mine` — list scenes I'm RSVP'd to

### HTTP Handler
- [ ] GET /http/scenes/upcoming → scheduled scenes (future start, state=scheduled)
- [ ] POST /http/scene/{id}/rsvp → { character_id, status } (simple toggle)
- [ ] RSVP via HTTP handler directly (no terminal routing needed for one-field toggle)

### NATS Events
- [ ] `portal.scene.live` type=scene_scheduled (new event created)
- [ ] `portal.scene.live` type=scene_rsvp (RSVP change)
- [ ] `portal.scene.live` type=scene_started (scheduled scene begins → notify RSVP'd)

### Web UI
- [ ] Upcoming Events widget (query: scheduled_start > now, state=scheduled, sorted)
- [ ] Event detail view (reuses scene page at `/scenes/42`, shows scheduling info when state=scheduled)
- [ ] RSVP buttons on event detail (Attending / Interested / Cancel)
- [ ] Notification to RSVP'd characters when scene starts

## Testing
- [ ] Create scheduled scene: state is "scheduled", no room created yet
- [ ] RSVP: character appears in rsvp_list, status correct
- [ ] Start: state transitions to "active", temp room created, RSVP'd notified
- [ ] +events: shows only future scheduled scenes
- [ ] Upcoming widget: correct query, sorted by start time, respects max_shown
- [ ] Cancellation: organizer can delete/cancel before start
