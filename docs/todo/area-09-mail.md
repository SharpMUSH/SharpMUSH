# Area 9: Mail & Messaging — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (9.1–9.4) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
> Note: mail shipped as a REST controller (`MailController`, `[Route("api/mail")]`), not the
> `/http` softcode handler originally sketched here; the active character comes from the JWT.
> Routes below reflect what was built — see `docs/design/mail-messaging.md`.
- [ ] REST: GET /api/mail?folder=INBOX (folder messages, newest first)
- [ ] REST: GET /api/mail/folders (folder names)
- [ ] REST: GET /api/mail/{folder}/{number} (full message, mark as read)
- [ ] REST: POST /api/mail (validate, route to game engine @mail)
- [ ] REST: DELETE /api/mail/{folder}/{number}
- [ ] NATS event on new mail (`portal.mail`)
- [ ] SignalR push: new mail notification → badge + toast
- [ ] Unread count: fetch on page load, update via SignalR

## Web UI
- [ ] Inbox view (`/mail`) — flat list, unread indicators, pagination
- [ ] Message view (`/mail/{id}`) — header + MString.ToHtml() body
- [ ] Compose view (`/mail/compose`) — To: autocomplete, subject, plain text body
- [ ] Reply / Reply All (pre-fill To: and subject)
- [ ] Delete confirmation
- [ ] Nav badge with unread count
- [ ] Toast notification on new mail (configurable)

## Testing
- [ ] Inbox pagination: correct ordering, page boundaries
- [ ] Read/unread state: mark on open, bulk mark-read
- [ ] Send: valid recipients, invalid recipients (error), permission denied (HAVEN flag)
- [ ] Notification: in-game mail triggers web badge update
- [ ] Permission: character can only read own mail; Wizard+ can read any
