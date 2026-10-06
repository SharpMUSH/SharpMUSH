# Area 12: Admin Panel — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (12.1–12.4) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Admin layout (separate from player-facing layout — fixed structure)
- [ ] Role-gated access: Royalty+ for panel, Wizard+ for config, God for server
- [ ] Dashboard page: online count, active scenes, new registrations, recent audit (no pending reports: #1565 dropped the report queue)

### Player Management
- [ ] Account list with search/filter (name, role, status)
- [ ] Account detail: email, created, last login, linked characters
- [x] Character list (`/admin/characters`): search, online, flag and account filters
- [x] Character detail (`/admin/players/{id}`): flags, roles, attribute count, mail count, last connect/site
- [x] Actions: ban/unban, warn, boot, force password reset, unlink character (`/admin/moderation`)
- [ ] Role override (promote/demote — Wizard+ only, cannot exceed own role)

### Moderation
- [-] Report queue and report actions: dropped (#1565). Games take reports their own way; the panel standardizes only staff actions
- [x] Ban management (`/admin/moderation`): list, add, lift, expiry; host bans stay in the sitelock
- [x] Warn: fires the game's `PLAYER`WARN` event; no built-in warning record
- [x] Audit log: staff actions from the portal and in-game logged (who, what, target, when)
- [x] Audit log viewer (`/admin/moderation/audit`): filter by action / staff / text / date range

### Site Configuration
- [ ] Form-based config editor (sections: General, Limits, Features)
- [ ] Validation on form fields (type, range)
- [ ] Save → write to config store → hot-reload where possible
- [x] Inert listener options marked "Not used", naming what sets them (no option needs a restart, so no restart indicator; #1565)

### Server
- [x] Server page (`/admin/server`, `server.admin`): readiness, uptime, version, players, queue, bus, storage, last backup; refreshed every 10 s

### Layout Editor
- [ ] Handled by Area 13 (widget system) — just the route lives here

## Testing
- [ ] Royalty can see players/moderation, cannot see config
- [ ] Wizard can see everything except server settings
- [x] Only `server.admin` holders are offered the server page
- [x] Audit log: every action creates an entry
- [ ] Config save: values persist, hot-reload works for supported fields
- [x] Ban: banned account cannot log in
