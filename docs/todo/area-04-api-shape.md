# Area 4: API Shape — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (4.1–4.5) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Define REST controller base (auth, error handling, response envelope)
- [ ] Implement cursor-based pagination helper (for feeds: scenes, wiki edits)
- [ ] Implement offset-based pagination helper (for stable lists: mail, BBS)
- [ ] Define HTTP handler interface for game engine bridge (`/mush/...`)
- [ ] Implement SignalR hub methods for write operations + push
- [ ] Standardize error response format (problem details RFC 7807)
- [ ] Rate limiting on public endpoints (search, registration)
- [ ] API versioning strategy (header or path — decide at implementation time)

## Testing
- [ ] Test pagination edge cases (empty results, last page, cursor expiry)
- [ ] Test error responses (400, 401, 403, 404, 500)
- [ ] Test rate limiting triggers and backoff
