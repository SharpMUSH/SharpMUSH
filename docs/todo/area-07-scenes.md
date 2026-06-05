# Area 7: Scene System — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (7.1–7.6) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Define scene data model (Scenes, Poses, Participants, ActorRoles collections)
- [ ] Implement scene lifecycle: create → active → completed
- [ ] Implement scheduled scenes (scheduled_start, RSVP — see Area 17)
- [ ] Temp room creation (SCENE_ROOM flag, auto-set)
- [ ] Per-player temp room limit (default 3, configurable)
- [ ] Grace period before temp room recycle
- [ ] Character return-to-previous-location on scene end
- [ ] Dual pose routing: MUSH pose → room → scene; web pose → scene → room
- [ ] Pose storage: MString + plain text for search
- [ ] Soft-delete on poses (edit/remove without permanent loss)
- [ ] Denormalized counts (pose_count, participant_count on scene)
- [ ] Scene visibility: public (default) + participant veto to private
- [ ] NATS events for scene state changes (`portal.scene.live`)

## Web UI
- [ ] Active scenes list (`/scenes/active`)
- [ ] Scene archive list (`/scenes`) with pagination
- [ ] Live scene panel (real-time pose display via SignalR)
- [ ] Pose input (web pose submission → scene → room)
- [ ] Scene archive detail (`/scenes/42`) — read-only rendered poses
- [ ] Scene creation form (title, description, scheduled_start optional)
- [ ] Join/leave scene controls

## In-Game Commands
- [ ] `+scene/create <title>` — create scene (temp room)
- [ ] `+scene/join <scene#>` — teleport to scene's room
- [ ] `+scene/leave` — return to previous location
- [ ] `+scene/end` — complete the scene
- [ ] `+scene/schedule <title>=<time>/<desc>` — scheduled scene (event)
- [ ] `+scene/rsvp <scene#>` — RSVP to scheduled scene
- [ ] `+scenes` — list active scenes

## Testing
- [ ] Scene lifecycle: create, pose, end, archive
- [ ] Temp room limit: hit limit, verify rejection
- [ ] Grace period: scene ends, room persists briefly, then recycles
- [ ] Dual routing: MUSH pose appears on web; web pose appears in MUSH room
- [ ] Visibility: public scene readable by all; private only by participants + staff
- [ ] SCENE_ROOM flag: set on temp rooms, queryable via hasflag()
