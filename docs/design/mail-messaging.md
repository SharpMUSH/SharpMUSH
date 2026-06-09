# Mail & Messaging

## Overview

@mail on the web portal. Flat list (not threaded). Mirrors the in-game @mail
system exactly — web is just another view into the same data. No threading
overhaul required.

## Architecture

```
┌─────────────────────────────────────────────┐
│  @mail storage (game DB — character attrs)  │
│  Existing system, unchanged                 │
└────────────────────┬────────────────────────┘
                     │
        ┌────────────┼────────────┐
        ▼            ▼            ▼
   In-game        HTTP handler    NATS event
   @mail cmd      serves mail     on new mail
        │            │            │
        │            ▼            ▼
        │       Portal API    SignalR push
        │       (REST read)   ("new mail" badge)
        │            │            │
        │            ▼            ▼
        │       Web mail UI   Notification dot
        └─────── Same data ───────┘
```

## Data Source

@mail lives where it always has — in the game's object DB as character
attributes (or however SharpMUSH stores mail internally). The portal
does NOT maintain a separate mail collection.

**HTTP handler endpoints:**

```
GET /mush/mail/inbox?character={dbref}&page=1&limit=25
  → Flat list of messages, newest first
  → Each message: id, from, to, subject, date, read/unread, body

GET /mush/mail/{message_id}?character={dbref}
  → Full message body
  → Marks as read

POST /mush/mail/send
  → { from: dbref, to: [names/dbrefs], subject: str, body: str }
  → Validates permissions, sends via game engine

POST /mush/mail/{message_id}/delete?character={dbref}
  → Deletes from character's mailbox

POST /mush/mail/mark-read
  → { message_ids: [...] }
  → Bulk mark as read
```

## Web Portal Mail UI

### Inbox View (`/mail`)

```
┌─────────────────────────────────────────────────────────┐
│  📬 Mail                              [Compose] [⟳]     │
├─────────────────────────────────────────────────────────┤
│  ● From: Gandalf     | Meeting at dawn   | 2 hours ago │
│  ● From: Elrond      | Council summons   | 1 day ago   │
│    From: Frodo       | Re: The ring      | 3 days ago  │
│    From: Staff       | Welcome to game!  | 1 week ago  │
├─────────────────────────────────────────────────────────┤
│  Page 1 of 3                          [< 1 2 3 >]      │
└─────────────────────────────────────────────────────────┘

● = unread
```

- Flat list, no threading, no conversation grouping
- Sorted by date (newest first)
- Unread indicator (bold + dot)
- Click to open message body
- Pagination (not infinite scroll — matches @mail's page model)

### Message View

```
┌─────────────────────────────────────────────────────────┐
│  ← Back to Inbox                                        │
├─────────────────────────────────────────────────────────┤
│  From: Gandalf                                          │
│  To: Frodo, Aragorn                                     │
│  Subject: Meeting at dawn                               │
│  Date: June 5, 2025, 14:30                              │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  Meet me at the eastern gate at first light.            │
│  Bring the ring. Tell no one else.                      │
│                                                         │
├─────────────────────────────────────────────────────────┤
│  [Reply] [Reply All] [Delete]                           │
└─────────────────────────────────────────────────────────┘
```

- Body rendered as MString.ToHtml() (mail can contain ANSI formatting)
- Reply pre-fills To: field and prefixes subject with "Re: "
- Reply All includes all original recipients

### Compose

```
┌─────────────────────────────────────────────────────────┐
│  New Message                                            │
├─────────────────────────────────────────────────────────┤
│  To:      [autocomplete character names________]        │
│  Subject: [__________________________________ ]         │
├─────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────┐    │
│  │                                                 │    │
│  │ (plain text editor — no Markdown in mail)       │    │
│  │                                                 │    │
│  └─────────────────────────────────────────────────┘    │
├─────────────────────────────────────────────────────────┤
│                                        [Send] [Cancel]  │
└─────────────────────────────────────────────────────────┘
```

- To: field with character name autocomplete (searches known characters)
- Plain text body (mail is plain text / MString, not Markdown)
- No attachments (traditional @mail has no attachment concept)
- Send triggers POST → HTTP handler → game engine `@mail/send`

## Real-Time Notifications

### New Mail Event

When a character receives @mail in-game:
1. Game engine sends mail as normal
2. NATS event published: `portal.mail`

```json
{
  "type": "new_mail",
  "character_id": "#42",
  "from_name": "Gandalf",
  "subject": "Meeting at dawn",
  "timestamp": "2025-06-05T14:30:00Z"
}
```

3. SignalR hub forwards to character's connected web session
4. Portal UI: badge count increments, toast notification appears

### Badge / Indicator

- Navigation shows mail icon with unread count: `📬 3`
- Unread count fetched on page load, updated via SignalR push
- Clicking the badge navigates to `/mail`

## Pages / Whispers

**Decision: Pages do NOT appear on web.**

Pages (real-time whispers) are ephemeral — they appear in the terminal panel
as game output (via MString → HTML in the system channel). They are not stored,
not archived, and not displayed in the mail UI.

If a player is connected via web, they see pages in their terminal panel in
real-time (just as they would on a telnet client). If disconnected, pages are
lost (same as telnet behavior).

**Rationale:** Pages are the MUSH equivalent of a real-time DM. They're not
mail. Storing them or showing them in a persistent UI would change their
semantics and privacy expectations.

## Permissions

- A character can only read their own mail
- Staff (Wizard+) can read any character's mail via admin panel
- Send permissions follow game-level restrictions (@mail locks, HAVEN flag, etc.)
- Web compose validates via HTTP handler (same restrictions as in-game @mail)

## Configuration

```yaml
mail:
  page_size: 25                   # Messages per page in web UI
  max_recipients: 10              # Max To: recipients per message
  body_max_length: 10000          # Character limit on mail body
  unread_badge: true              # Show unread count in nav
  notification_toast: true        # Show toast on new mail
```
