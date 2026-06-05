# Area 3: Client State — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (3.1–3.4) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Define DI service architecture (scoped services per circuit/tab)
- [ ] Implement `CharacterStateService` (current character, room, scene)
- [ ] Implement `ConnectionStateService` (SignalR connection status, reconnect state)
- [ ] Implement `NotificationService` (badge counts, toast queue)
- [ ] Set up InteractiveAuto render mode
- [ ] Implement per-widget error boundaries (one widget crash doesn't kill page)
- [ ] localStorage wrapper for: theme preference, sidebar state, last character
- [ ] No Flux/Redux — services + events pattern only

## Testing
- [ ] Verify each tab/circuit gets independent state
- [ ] Test error boundary: crash one widget, verify others survive
- [ ] Test localStorage persistence across page reloads
- [ ] Test character switch updates all dependent services
