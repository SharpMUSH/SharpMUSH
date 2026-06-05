# Area 12: Admin Panel — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (12.1–12.4) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Admin layout (separate from player-facing layout — fixed structure)
- [ ] Role-gated access: Royalty+ for panel, Wizard+ for config, God for server
- [ ] Dashboard page: online count, active scenes, new registrations, pending reports, recent audit

### Player Management
- [ ] Account list with search/filter (name, role, status)
- [ ] Account detail: email, created, last login, linked characters
- [ ] Character detail: flags, attribute count, mail count
- [ ] Actions: ban/unban, force password reset, unlink character
- [ ] Role override (promote/demote — Wizard+ only, cannot exceed own role)

### Moderation
- [ ] Report queue (reported content with context)
- [ ] Report actions: dismiss, delete content, warn, ban
- [ ] Ban management: list, add, remove, expiry
- [ ] Audit log: all staff actions logged (who, what, target, when)
- [ ] Audit log viewer: search, filter by action type / staff / date range

### Site Configuration
- [ ] Form-based config editor (sections: General, Limits, Features)
- [ ] Validation on form fields (type, range)
- [ ] Save → write to config store → hot-reload where possible
- [ ] "Requires restart" indicator on fields that can't hot-reload

### Layout Editor
- [ ] Handled by Area 13 (widget system) — just the route lives here

## Testing
- [ ] Royalty can see players/moderation, cannot see config
- [ ] Wizard can see everything except server settings
- [ ] God can see server settings
- [ ] Audit log: every action creates an entry
- [ ] Config save: values persist, hot-reload works for supported fields
- [ ] Ban: banned account cannot log in
