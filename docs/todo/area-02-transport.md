# Area 2: Transport (SignalR / NATS) — TODO

## Pre-Implementation
- [x] Review & confirm decisions (2.1–2.5) with project owner
- [x] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [x] Set up SignalR hub (`/hubs/game`) with JSON protocol — `GameHub.cs`, mapped in `Program.cs`
- [x] Implement authentication on hub (JWT bearer) — `[Authorize]` on hub; `AccessTokenProvider` in `GameHubConnectionFactory`
- [x] Implement NATS subscription bridge (server subscribes to NATS subjects, forwards to SignalR groups) — `NatsBridgeService.cs` (`game.output.*` → `char:{dbref}`, `game.room.*` → `room:{dbref}`)
- [x] Define SignalR groups: per-character (`char:`), per-room (`room:`), per-scene (`scene:` via `JoinScene`/`LeaveScene`), broadcast (`Clients.All`)
- [x] Implement subject filtering (broad NATS subjects, server filters by payload before forwarding) — wildcard subscribe + dbref extraction + null-payload guard in `NatsBridgeService`
- [x] Wire up game output → NATS → SignalR → client terminal panel — bridge forwards `ReceiveOutput`; `ConnectionStateService` surfaces `OnOutputReceived`
- [x] Portal-issued game commands — `POST api/commands` (`CommandsController` → `PortalCommandService`) runs one line as the session's bound character on the engine queue and answers with its captured output and an optional `Result` expression (#1485). The hub's `SendCommand` → `GameCommandMessage` publish, which nothing consumed, is retired
- [x] Softcode Editor console — `POST api/commands/eval` evaluates an expression with `%0`-`%9` as the character, or as an object the character controls (the editor's unsaved buffer, run as `u()` would run it), on the same queue and per-account limit (#1578)
- [x] Implement reconnection handling (SignalR auto-reconnect + state transitions) — `ExponentialBackOffRetryPolicy` in `GameHubConnectionFactory`; `ConnectionStateService` tracks Reconnecting/Connected

## NATS Subjects
Portal-feature subjects are created with their owning features; none of these features publish yet:
- [ ] `portal.presence` — connect/disconnect/idle (blocked on presence feature)
- [ ] `portal.scene.live` — scene state changes, new poses (blocked on server-side scenes; client uses `InMemorySceneService`)
- [ ] `portal.page.viewers` — who's viewing what wiki page
- [ ] `portal.scene.log` — pose archive events
- [ ] `portal.mail` — new mail notification (blocked on area 09)
- [ ] `portal.notify` — general notifications
- [ ] `portal.wiki.changes` — wiki edit events (see area 05)
- [ ] `portal.bbs.new_post` — new BBS post (blocked on area 16)

## Testing
- [x] Hub unit tests: connect/disconnect groups, room + scene group join/leave — `GameHubTests.cs`, `GameHubWriteOpsTests.cs`
- [x] Portal command end to end (HTTP → engine queue → captured output) — `PortalCommandApiTests.cs` (SharpMUSH.Tests.Integration)
- [x] Test reconnection state transitions — `ConnectionStateServiceTests.cs` (13 tests)
- [x] Test NATS → SignalR forwarding with subject routing — `NatsBridgeServiceTests.cs`
- [x] Load test: NATS throughput — `NatsPerformanceValidation.cs` (marked `[Explicit]`, run on demand)

## Follow-ups
- `portal.*` subjects as their owning features land
