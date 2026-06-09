# Area 1: Authentication & Identity — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (1.1–1.5) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Verify ASP.NET Identity is wired up (already partially built)
- [ ] Verify AccountController endpoints: register, login, refresh, logout
- [ ] Verify JWT generation with character claims (role, dbref, account_id)
- [ ] Implement refresh token rotation (httpOnly cookie)
- [ ] Implement character-switching endpoint (new JWT with different character claims)
- [ ] Add role claim derivation from game flags (WIZARD→Wizard, ROYALTY→Royalty, #1→God)
- [ ] Account-level role = max role among linked characters
- [ ] Add registration flow (account creation + first character link)
- [ ] Add "link existing character" flow (character password verification)

## Testing
- [ ] Unit tests: JWT generation, role derivation, token refresh
- [ ] Integration tests: login flow, character switch, expired token handling
- [ ] Verify no character password required after account auth
