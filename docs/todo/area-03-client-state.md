# Area 3: Client State — TODO

## Pre-Implementation
- [x] Review & confirm decisions (3.1–3.4) with project owner
- [x] Identify any decisions that need revision based on current codebase state — the app shipped as Blazor WASM **standalone** (not InteractiveAuto), so "per-circuit scoped services" became app-wide singletons, which is the correct equivalent for a single-runtime WASM app

## Implementation Tasks
- [x] Define DI service architecture — state services registered in `SharpMUSH.Client/Program.cs` (singletons; one runtime per browser tab in WASM standalone)
- [x] Implement `CharacterStateService` (current character, room) — `CharacterStateService.cs`; persists last character to localStorage
- [x] Implement `ConnectionStateService` (SignalR connection status, reconnect state) — `ConnectionStateService.cs`
- [x] Implement `NotificationService` (badge counts, toast queue) — `NotificationService.cs`
- [x] Render mode — Blazor WebAssembly standalone (decision supersedes the original InteractiveAuto note; no server-side circuits exist)
- [x] Implement per-widget error boundaries (one widget crash doesn't kill page) — `WidgetErrorBoundary.razor` wrapping every widget in `ZoneRenderer.razor`
- [x] localStorage wrapper for: theme preference (`ThemeService`), layout/sidebar state (`LayoutService`), last character (`CharacterStateService`), locale (`Program.cs`) — direct JSInterop, no Blazored dependency
- [x] No Flux/Redux — services + events pattern only (all services expose `On*Changed` events)

## Testing
- [x] Per-tab state isolation — N/A by architecture: WASM standalone gives each browser tab its own runtime; there is no shared circuit to isolate
- [x] Test error boundary: crash one widget, verify others survive — `ZoneRendererErrorBoundaryTests` (throwing widget shows error alert; sibling widget still renders)
- [x] Test localStorage persistence across reloads — `CharacterStateServiceTests`, `ThemeServiceTests`, `LayoutServiceTests` (restore, malformed-data fallback, overwrite)
- [x] Character switch updates dependent services — `CharacterStateServiceTests` (events fire); terminal reconnect flow lives in `MainLayout.SwitchCharacterAsync`

## Follow-ups
- Integration-style test of the full character-switch cascade (`MainLayout.SwitchCharacterAsync`: OTT fetch → terminal disconnect → reconnect) — currently exercised manually
