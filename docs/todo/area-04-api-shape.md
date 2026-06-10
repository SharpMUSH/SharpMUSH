# Area 4: API Shape — TODO

## Pre-Implementation
- [x] Review & confirm decisions (4.1–4.5) with project owner
- [x] Identify any decisions that need revision based on current codebase state — the `/mush/...` HTTP bridge was superseded by SignalR + NATS (bidirectional), see below

## Implementation Tasks
- [x] Define REST controller base (auth, error handling, response envelope) — `ApiControllerBase.cs` (`ApiResponse<T>` envelope, identity helpers, `Problem4xx/5xx` helpers)
- [x] Implement cursor-based pagination helper (for feeds) — `CursorPagination.cs` (opaque Base64 cursors, 1–200 clamp)
- [x] Implement offset-based pagination helper (for stable lists) — `OffsetPagination.cs` (`PagedResult<T>`, `FromSlice` overload)
- [x] Game engine bridge — `IGameEngineBridge` interface exists; the `/mush/...` HTTP handler was superseded by SignalR (`GameHub`) + NATS, which provide the bidirectional channel the HTTP design was approximating
- [x] Implement SignalR hub methods for write operations + push — `GameHub` (`SendCommand`, `JoinRoom/LeaveRoom`, `JoinScene/LeaveScene`, `SendToCharacterAsync`, `SendToRoomAsync`, `BroadcastSystemMessageAsync`)
- [x] Standardize error response format (problem details RFC 7807) — `ProblemDetailsExceptionHandler.cs` global handler + `ApiControllerBase.Problem*()` helpers
- [x] Rate limiting on public endpoints — fixed-window `public-api` policy (30 req/min) applied to all auth endpoints via `[EnableRateLimiting]`
- [x] API versioning strategy — `Asp.Versioning` configured (URL segment + `x-api-version` header, default 1.0 assumed); attributes to be added when a v2 endpoint first ships

## Testing
- [x] Test pagination edge cases (empty results, last page, cursor round-trip, clamping) — `CursorPaginationTests` (14), `OffsetPaginationTests` (16)
- [x] Test error responses (400, 401, 403, 404, 409, 422, 500) — `ApiControllerBaseTests`; HTTP-level 401/403/404/409 covered by `AuthHttpControllerTests` + `WikiHttpControllerTests` + `WikiControllerProtectionTests`
- [x] Test rate limiting triggers and backoff — `RateLimiterSmokeTests` (permit enforcement, 429, queue behavior)

## Follow-ups
- Add `[ApiVersion("1.0")]` attributes when introducing the first breaking change
- Endpoint-level rate-limit integration test (hammering a real auth endpoint past the window) — skipped to keep the shared test session under the limiter threshold
