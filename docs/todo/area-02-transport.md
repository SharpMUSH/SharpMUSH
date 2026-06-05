# Area 2: Transport (SignalR / NATS) — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (2.1–2.5) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Set up SignalR hub (`/hubs/game`) with JSON protocol
- [ ] Implement authentication on hub (JWT bearer from query string or header)
- [ ] Implement NATS subscription bridge (server subscribes to NATS subjects, forwards to SignalR groups)
- [ ] Define SignalR groups: per-character, per-room, per-scene, broadcast
- [ ] Implement subject filtering (broad NATS subjects, server filters by schema before forwarding)
- [ ] Wire up game output → NATS → SignalR → client terminal panel
- [ ] Wire up client input → SignalR → game engine command processing
- [ ] Implement reconnection handling (SignalR auto-reconnect + state resync)

## NATS Subjects
- [ ] `portal.presence` — connect/disconnect/idle
- [ ] `portal.scene.live` — scene state changes, new poses
- [ ] `portal.page.viewers` — who's viewing what wiki page
- [ ] `portal.scene.log` — pose archive events
- [ ] `portal.mail` — new mail notification
- [ ] `portal.notify` — general notifications
- [ ] `portal.wiki.changes` — wiki edit events
- [ ] `portal.bbs.new_post` — new BBS post

## Testing
- [ ] Integration test: client connects, sends command, receives game output
- [ ] Test reconnection after brief disconnect
- [ ] Test NATS → SignalR forwarding with subject filtering
- [ ] Load test: multiple simultaneous connections
