# Modern UI Patterns for SharpMUSH Portal

A design specification for UX patterns that elevate the portal from functional
tool to polished modern experience. Each pattern is defined with implementation
specifics, edge cases, accessibility requirements, and inter-pattern
dependencies.

Reference applications: Linear (keyboard-first, real-time), Discord (presence,
community), Notion (content authoring, inline editing), GitHub (progressive
disclosure, contextual actions).

## Design Principles

These patterns serve four goals, in priority order:

1. **Protect creative flow** — Writers spend hours composing poses and lore.
   Never interrupt them. Never lose their work.
2. **Communicate system state** — Real-time apps have complex state (connected,
   syncing, conflicting). The UI must make state obvious without being noisy.
3. **Reward mastery** — New users get guided; experienced users get speed.
   The same UI serves both without toggle switches.
4. **Feel alive** — A MU* community portal must feel inhabited. Presence,
   activity feeds, and subtle motion signal that this is a living world.

## Pattern Format

Each pattern follows: Definition → Rationale → Specification → Edge Cases →
Accessibility → Related Patterns.

---

## 1. Skeleton Loading (Shimmer)

**Definition:** Render placeholder shapes (grey pulsing rectangles, circles,
text lines) that mirror the final content structure while data loads. The
skeleton's layout MUST match the loaded content's layout exactly — no shift.

**Rationale:** MU* portals load heterogeneous content (wiki pages of varying
length, character profiles with optional sections, scene logs with mixed media).
A spinner tells the user nothing. A skeleton tells them "a heading, then 3
paragraphs, then a sidebar" — setting accurate expectations.

### Specification

```
LOADING STATE:                         LOADED STATE:
┌─────────────────┐                   ┌─────────────────┐
│ ████████████    │                   │ Combat Rules     │
│ ██████████████  │                   │ The combat system│
│ █████████       │    →              │ uses d20 rolls...│
│ ██████████████  │                   │ for initiative.  │
│ █████           │                   │ See also: Magic  │
└─────────────────┘                   └─────────────────┘
```

**Component:** `<MudSkeleton>` — shapes: Circle, Rectangle, Text.

**Implementation rules:**

1. Each page/widget defines its OWN skeleton component (not one global skeleton)
2. Skeleton MUST have identical dimensions to loaded content (use fixed heights
   or CSS aspect-ratio where needed)
3. Show skeleton for minimum 200ms even if data arrives faster (prevents flash)
4. Skeleton animates with CSS `@keyframes shimmer` — left-to-right gradient sweep
5. Skeleton disappears via opacity fade (150ms), not instant swap

**Where to apply:**

| Surface               | Skeleton shape                                    |
|-----------------------|---------------------------------------------------|
| Wiki page             | H1 bar (60%) + 4 text lines (80-100% varying)    |
| Character profile     | Circle (avatar) + H2 bar + 3 text lines           |
| Scene list            | 5x card outlines (image rect + 2 text lines each) |
| Home widget           | Widget-specific (each widget owns its skeleton)    |
| Online players        | 8x (circle + short bar) stacked                   |
| Notification dropdown | 5x (icon circle + 2 text lines) stacked           |

**Edge cases:**
- If data takes >5 seconds, overlay a subtle "Still loading..." text on the skeleton
- If data FAILS, replace skeleton with an error empty-state (not a frozen skeleton)
- Never show skeleton for data that's already cached locally (WASM can cache)

**Accessibility:**
- Skeleton regions get `aria-busy="true"` and `aria-label="Loading content"`
- Remove `aria-busy` when content appears
- Screen readers announce "Content loading" then "Content loaded" via live region

---

## 2. Optimistic Updates

**Definition:** Immediately reflect user actions in the UI before server
confirmation. Maintain a pending state marker on optimistic items. Revert
cleanly on server rejection with user-visible feedback.

**Rationale:** Scene posing is the core loop. A player typing a 3-paragraph
pose and hitting Enter must see it appear INSTANTLY — not after a 200ms+ round
trip. This is especially critical over SignalR/WebSocket where latency varies.
The "pending" visual state (slight opacity, animated dot) communicates that
the server hasn't confirmed yet without blocking the user from continuing.

### Specification

| Action                  | Optimistic behavior                        | Pending indicator       | On failure              |
|-------------------------|--------------------------------------------|-------------------------|-------------------------|
| Send scene pose         | Appears in scene log immediately           | Subtle opacity 0.7 + ⟳ | Revert + error toast    |
| Wiki quick-edit (title) | Title changes immediately                  | Saving... badge         | Revert + toast          |
| Send mail               | Shows in "Sent" folder immediately         | "Sending..." label      | Move to "Drafts" + toast|
| Mark mail read          | Badge count drops immediately              | None (too fast)         | Re-increment badge      |
| React to scene pose     | Reaction appears instantly                 | None (cheap operation)  | Remove + toast          |
| Toggle wiki "watch"     | Star fills immediately                     | None                    | Un-fill + toast         |
| Scene join              | Player appears in participant list         | "Joining..." label      | Remove + toast          |

### Implementation Pattern

```csharp
// The pattern for any optimistic action in Blazor
private async Task SendPose(string text)
{
    // 1. Generate temp ID, add to local state with Pending flag
    var tempPose = new PoseViewModel
    {
        Id = Guid.NewGuid(),
        Text = text,
        Author = _currentCharacter,
        Timestamp = DateTimeOffset.UtcNow,
        State = PoseState.Pending  // drives the opacity/indicator
    };
    _poses.Add(tempPose);
    StateHasChanged();

    // 2. Send to server
    try
    {
        var confirmed = await _sceneHub.PostPoseAsync(text);
        // 3a. Replace temp with server-confirmed version
        var index = _poses.IndexOf(tempPose);
        _poses[index] = confirmed with { State = PoseState.Confirmed };
    }
    catch (Exception ex)
    {
        // 3b. Revert + notify
        _poses.Remove(tempPose);
        Snackbar.Add("Pose failed to send. Your text has been copied to clipboard.",
            Severity.Error, config => config.Action = "Retry");
        await Clipboard.WriteTextAsync(text);  // NEVER lose user text
    }
    StateHasChanged();
}
```

### Edge Cases

- **Rapid-fire actions:** User sends 3 poses in 2 seconds. Each gets its own
  pending entry. They confirm in order (server guarantees ordering via sequence
  number). If pose #2 fails but #1 and #3 succeed, only #2 reverts.
- **Offline queue:** If connection is lost, optimistic items queue locally.
  On reconnect, flush the queue in order. If any fail, present the full queue
  to the user with retry/discard options.
- **Conflict:** User edits wiki title optimistically, but another user edited
  it first. Server rejects with 409 Conflict. Show toast: "Title was changed
  by Elena. [View their version] [Overwrite]"
- **Large payloads:** Wiki body edits are NOT optimistic (too complex to revert).
  Only metadata (title, category, tags) is optimistic.

### Accessibility

- Pending items get `aria-label="Sending..."` and `aria-live="polite"`
- On confirmation: `aria-live` region announces "Pose sent" (not shown visually)
- On failure: `aria-live="assertive"` announces the error

**Related patterns:** Toast/Snackbar (failure feedback), Connection State
(queue behavior during disconnect)

---

## 3. Toast / Snackbar Notifications

**Definition:** Brief, non-modal messages that slide in from a screen edge
(bottom-left for actions, bottom-right for system events), confirm completed
actions or report errors, and auto-dismiss after a timeout. Some include
action buttons (Undo, Retry, View).

**Rationale:** Modal dialogs steal focus and interrupt flow. For a writer in
the middle of composing a pose, a modal that says "Page saved!" is an
aggression. Toasts confirm without demanding attention — peripheral vision
registers them, and the user's cursor never leaves their text.

### Specification

**Positioning:**
- Bottom-left: User-initiated action confirmations (save, send, delete)
- Bottom-right: System events (connection changes, incoming notifications)
- Top-center: ONLY for critical blocking errors (auth expired, server down)

**Timing:**
- Success confirmations: 3 seconds auto-dismiss
- Warnings: 5 seconds auto-dismiss
- Errors without action: 6 seconds auto-dismiss
- Errors with action (Retry/Undo): 8 seconds, or until user interacts
- System critical: stays until resolved or dismissed

**Content rules:**
- Maximum 2 lines of text
- Lead with status icon: ✓ (success), ⚠ (warning), ✗ (error)
- Include the OBJECT of the action: "Page 'Combat Rules' saved" not "Page saved"
- Action buttons: maximum 1 per toast (Undo OR Retry, never both)

| Trigger                    | Toast content                              | Duration | Action    |
|----------------------------|--------------------------------------------|----------|-----------|
| Wiki page saved            | "✓ 'Combat Rules' saved"                  | 3s       | —         |
| Wiki page deleted          | "🗑 'Combat Rules' deleted"                | 8s       | [Undo]    |
| Mail sent                  | "✓ Sent to Gandalf"                       | 3s       | [View]    |
| Character approved         | "✓ Aldric approved and visible"           | 4s       | —         |
| Connection lost            | "⚠ Connection lost. Reconnecting..."      | sticky   | —         |
| Connection restored        | "✓ Reconnected"                           | 2s       | —         |
| Permission denied          | "✗ Permission denied: edit requires Staff" | 6s       | —         |
| Scene pose (own)           | (silent — appears in scene log)            | —        | —         |
| Scene pose (other player)  | (silent — real-time via SignalR)            | —        | —         |
| Shortcut teaching          | "✓ Saved (tip: Ctrl+S next time)"         | 4s       | —         |

**Key rule:** Never toast for things that have their own visible UI feedback.
A new scene pose doesn't need a toast — it appears in the scene log. A wiki
save in the editor needs confirmation because the editor is still showing the
edit form.

**Stacking:** Maximum 3 visible toasts. If a 4th arrives, the oldest
auto-dismisses. Toasts stack vertically with 8px gap.

### Edge Cases

- **Rapid succession:** User saves wiki 5 times in 10 seconds. Don't show
  5 toasts — debounce to show only the latest: "✓ Saved (5th revision)"
- **Undo timing:** If the user clicks Undo after the toast would have
  dismissed (but it's still animating out), honor the Undo. The toast's
  action remains valid for 2s after visual dismissal.
- **Offline toasts:** If disconnected, queue action toasts and show them
  with "[offline]" suffix when actions are retried on reconnect.

### Accessibility

- Toasts rendered in an `aria-live="polite"` region (assertive for errors)
- Toast text is announced by screen readers
- Action buttons are keyboard-focusable (Tab reaches them)
- Auto-dismiss pauses while the toast is hovered or focused

**MudBlazor:** `ISnackbar` service — `Snackbar.Add(message, severity, config)`

**Related patterns:** Undo Instead of Confirm (toast is the undo vehicle),
Connection State (sticky toast for disconnect)

---

## 4. Inline Editing (Click-to-Edit)

**Definition:** Transform display-mode content into an editable field in-place
on user interaction (click, double-click, or pencil icon). Save on blur, Enter,
or Ctrl+Enter. Cancel on Escape. No page navigation required.

**Rationale:** MU* content is heavily metadata-driven — page titles, character
names, scene summaries, room descriptions. Navigating to a full edit form for
a one-word title change is hostile. Inline editing respects the user's context
and keeps them on the page they're already reading.

### Specification

| Element                | Trigger           | Input type      | Save       | Cancel  |
|------------------------|-------------------|-----------------|------------|---------|
| Wiki page title        | Click on title    | Single-line     | Enter/blur | Escape  |
| Wiki page category     | Click on chip     | Autocomplete    | Selection  | Escape  |
| Wiki page tags         | Click on tag area | Chip input      | Blur       | Escape  |
| Character short desc   | Click pencil icon | Single-line     | Enter/blur | Escape  |
| Scene title            | Click on title    | Single-line     | Enter/blur | Escape  |
| Scene summary          | Click pencil icon | Multi-line (3)  | Ctrl+Enter | Escape  |
| Event name             | Click on name     | Single-line     | Enter/blur | Escape  |
| Event date/time        | Click on date     | Date picker     | Selection  | Escape  |
| Nav link label (admin) | Click on label    | Single-line     | Enter/blur | Escape  |

**NOT inline-editable** (use dedicated editor):
- Wiki page body (too complex — needs Markdown toolbar, preview)
- Scene poses (already have the compose input)
- Character full biography
- Any content that needs formatting controls

### Visual States

```
DISPLAY:              HOVER:                   EDIT:                    SAVING:
┌──────────────┐    ┌──────────────┐        ┌──────────────┐        ┌──────────────┐
│ Combat Rules │    │ Combat Rules ✏│        │[Combat Rules]│        │ Combat Rules ⟳│
└──────────────┘    └──────────────┘        └──────────────┘        └──────────────┘
                    (pencil appears           (focused input,          (brief saving
                     on hover)                blue border)             indicator)
```

### Edge Cases

- **Concurrent edit:** If another user changes the same field while this user
  is editing inline, show conflict on save attempt (not during edit — don't
  interrupt their typing)
- **Validation failure:** If server rejects (e.g. title too long, duplicate
  name), show inline error message below the field. Don't revert to display
  mode — let the user fix their input.
- **Permission check:** Don't show the pencil/hover affordance at all if the
  user lacks edit permission. The field just looks like static text.
- **Empty value:** If user clears the field and blurs, revert to previous
  value + show toast "Title cannot be empty"

### Accessibility

- Edit trigger has `role="button"` and `aria-label="Edit [field name]"`
- Input field gets focus immediately on entering edit mode
- Escape returns focus to the display element
- Screen reader announces: "Editing page title" → "Title saved" / "Edit cancelled"

**Related patterns:** Optimistic Updates (inline saves are optimistic),
Toast/Snackbar (save confirmation + error reporting)

---

## 5. Presence Indicators (Who's Here)

**Definition:** Real-time visual indicators showing player online status,
page co-viewers, and typing activity. Powered by SignalR presence channels
with heartbeat-based state transitions.

**Rationale:** A MU* without visible community feels dead. The portal must
radiate life — players should feel the pulse of others online, see activity
in scenes, and know when someone is composing a response. This is the #1
differentiator between "a website about a game" and "a living game world
accessible via the web."

### Specification

**Presence states:**

| State   | Icon | Criteria                                          | Transition to        |
|---------|------|---------------------------------------------------|----------------------|
| Online  | 🟢   | WebSocket connected + activity within 2 minutes   | → Idle after 5m      |
| Idle    | 🟡   | Connected but no input/click/scroll for 5 minutes | → Online on activity |
| DND     | 🔴   | Player-set (manual toggle)                        | → stays until unset  |
| Offline | ⚫   | WebSocket disconnected or no heartbeat for 30s    | → Online on connect  |

**Heartbeat protocol:**
- Client sends heartbeat every 15 seconds over SignalR
- Server marks player offline after 2 missed heartbeats (30s)
- State transitions broadcast to subscribers via NATS → SignalR fan-out
- Idle detection: client-side timer resets on keydown, click, scroll, touchstart

**Co-viewing (wiki/profiles):**
- When a user navigates to a page, join that page's presence channel
- Display: "👁 3 viewing" badge in page header
- If a user has the editor open: "✏️ Elena is editing" (warning, not lock)
- Leave channel on navigate away or disconnect

### Visual Layout

```
ONLINE PLAYERS WIDGET:              SCENE VIEW HEADER:
┌───────────────────────┐          ┌───────────────────────────────────┐
│ Online (12)        ▾  │          │ The Market Square                  │
│                       │          │ Players: 🟢🟢🟡  Viewing: 👁 5     │
│ 🟢 Gandalf     [⋯]   │          └───────────────────────────────────┘
│ 🟢 Aldric      [⋯]   │
│ 🟡 Elena  (5m) [⋯]   │
│ 🔴 Marcus (DND)[⋯]   │          WIKI PAGE HEADER:
│                       │          ┌───────────────────────────────────┐
│ + 8 more              │          │ Combat Rules           👁 2 viewing│
│                       │          │ ✏️ Elena is editing this page      │
│ [Set status ▾]        │          └───────────────────────────────────┘
└───────────────────────┘
```

### Edge Cases

- **Tab visibility:** When browser tab is hidden (visibilitychange API), client
  still sends heartbeats but reports idle state. Don't mark users offline just
  because they switched tabs.
- **Multiple tabs:** A player may have 2+ character tabs open. Each is its own
  presence entry. The "Online Players" widget deduplicates by player (shows
  their "most active" character).
- **Privacy:** Players can set "Appear offline" in settings. Server still
  tracks them (for admin purposes) but broadcasts them as Offline to other
  players.
- **Large games:** If 100+ players online, the widget shows top 10 + "and 90
  more" link to full player list page. Don't render 100 DOM elements in a
  sidebar widget.

### Accessibility

- Status icons have `aria-label`: "Online", "Idle for 5 minutes", "Do not disturb"
- Typing indicator is in an `aria-live="polite"` region
- The online count badge is `aria-label="12 players online"`

**Related patterns:** Connection State (offline detection feeds presence),
Notification Center (presence changes can trigger notifications for "friends")

---

## 6. Contextual Menus (Right-Click / Kebab)

**Definition:** Secondary action sets revealed via right-click (desktop),
long-press (mobile), or explicit ⋯ (meatball) icon. Actions are scoped to the
element being interacted with. The menu appears adjacent to the trigger point.

**Rationale:** MU* portals have many entity types (wiki pages, characters,
scenes, poses, mail messages) each with 4–8 possible actions. Showing all
actions as visible buttons creates visual noise. Contextual menus keep the
surface clean for reading while making actions accessible on demand.

### Specification

| Element              | Primary actions (always visible) | Contextual (menu) actions                    |
|----------------------|---------------------------------|----------------------------------------------|
| Wiki page (in list)  | Title (link)                    | Edit, Copy link, Watch, History, Delete*     |
| Wiki page (viewing)  | Edit button                     | Copy link, Watch, History, Export, Delete*    |
| Character (in list)  | Name (link)                     | Send mail, Copy link, Add to scene           |
| Character (profile)  | Send mail button                | Copy link, Report, Block                     |
| Scene pose           | (none — clean reading)          | Quote, React, Copy text, Report, Delete**    |
| Mail message         | (none in list)                  | Reply, Forward, Archive, Mark unread, Delete |
| Nav link (admin)     | (none — just the link)          | Edit label, Move up/down, Remove             |
| Online player entry  | Name (link to profile)          | Send mail, Whisper, Invite to scene          |
| Notification item    | Click (navigates to source)     | Mark read, Mute this type, Remove            |

*Admin only. **Owner or admin only.

**Trigger consistency:**
- ⋯ icon for all list items (positioned right-aligned within the row)
- Right-click anywhere on the element opens the same menu
- On mobile: long-press on the element
- The ⋯ icon appears on hover only (desktop), always visible (mobile/tablet)

**Menu design rules (per NN/g research):**

1. Group related actions with divider lines
2. Destructive actions at the bottom, separated by a divider, in red text
3. Include keyboard shortcut hints right-aligned in each row
4. Maximum 8 items per menu (beyond that, use a submenu or rethink)
5. Icons on the left of each action label (consistent sizing)
6. Menu appears near the click/trigger point, not in a fixed position

### Menu Layout Template

```
┌─────────────────────────────────┐
│ 📖 Open in new tab              │
│ ✏️  Edit                  Ctrl+E │
│ 🔗 Copy link              Ctrl+L │
│ 👁 Watch for changes             │
│ 📋 View history                  │
│─────────────────────────────────│
│ 🗑️ Delete                  (red) │
└─────────────────────────────────┘
```

### Edge Cases

- **Overflow:** If menu would render off-screen, flip direction (open upward
  or leftward). MudBlazor `<MudMenu>` handles this via `AnchorOrigin`.
- **Nested scenes:** In a scene with 50+ poses, the ⋯ icon on each pose is
  only shown on hover. This prevents 50 icons cluttering the scene log.
- **Touch targets:** On mobile, the menu trigger area must be at least 44x44px
  even if the icon appears smaller.
- **Keyboard nav:** Once menu is open, arrow keys move selection, Enter
  activates, Escape closes and returns focus to trigger element.

### Accessibility

- Trigger: `role="button"`, `aria-haspopup="menu"`, `aria-expanded="true/false"`
- Menu: `role="menu"` with `role="menuitem"` children
- Focus trapped within open menu
- Escape closes menu and returns focus to trigger
- Shortcut text is `aria-hidden` (screen reader reads the label, sighted users see the hint)

**Related patterns:** Keyboard Shortcuts (menu shows shortcut hints as teaching),
Inline Editing (some menu items trigger inline edit mode)

---

## 7. Keyboard Shortcuts + Shortcut Teaching

**Definition:** A comprehensive keyboard shortcut system with two-key sequences
(vim-style "leader" keys), single-key context-sensitive shortcuts, and a
progressive teaching mechanism that shows shortcuts contextually rather than
requiring the user to read documentation.

**Rationale:** MU* players are TEXT people. They already communicate primarily
via keyboard. A portal that forces mouse-driven navigation for common actions
(switch to wiki, open scene, send mail) wastes their fluency. Keyboard shortcuts
are not a power-user luxury — for this audience, they're a primary input mode.

### Specification

**Global shortcuts (work everywhere, any page):**

| Shortcut      | Action                          | Notes                    |
|---------------|---------------------------------|--------------------------|
| Ctrl+K        | Open omnisearch                 | Focus in search input    |
| Ctrl+/        | Show shortcut cheat sheet       | Overlay, Escape to close |
| Escape        | Close topmost overlay           | Cascading: menu→modal→search |
| G then H      | Navigate to Home                | 500ms timeout for 2nd key|
| G then W      | Navigate to Wiki                |                          |
| G then S      | Navigate to Scenes              |                          |
| G then M      | Navigate to Mail                |                          |
| G then C      | Navigate to Characters          |                          |
| G then A      | Navigate to Admin (if staff)    |                          |

**Context-sensitive shortcuts (only active on specific pages):**

| Shortcut | Context     | Action                                     |
|----------|-------------|--------------------------------------------|
| E        | Wiki view   | Enter edit mode                            |
| S        | Wiki edit   | Save (triggers Ctrl+S behavior)            |
| N        | Scene view  | Focus the pose input (New pose)            |
| Enter    | Pose input  | Submit pose (when input is focused)        |
| R        | Mail view   | Reply to current message                   |
| A        | Mail list   | Archive selected message                   |
| J        | Any list    | Move selection down                        |
| K        | Any list    | Move selection up                          |
| X        | Any list    | Toggle selection (for bulk actions)        |
| ?        | Any page    | Show page-specific help popover            |

**Two-key sequence handling:**
- After pressing "G", show a subtle overlay hint: "G → H:Home W:Wiki S:Scenes M:Mail"
- Timeout after 500ms if no second key — cancel sequence, no action
- If the user presses an invalid second key, flash the hint and cancel

### Teaching Mechanism (Linear-style)

The system teaches shortcuts progressively through 3 channels:

1. **Confirmation toasts:** When a user performs an action via mouse that has
   a shortcut, append the shortcut to the confirmation:
   ```
   "✓ Page saved (tip: Ctrl+S)"
   ```
   Only show this tip the first 3 times. After that, stop (user either learned
   it or doesn't care).

2. **Contextual menu hints:** Every contextual menu item shows its shortcut
   right-aligned. Users see "Edit  Ctrl+E" every time they use the menu, and
   eventually skip the menu entirely.

3. **First-visit hints:** On first visit to a major section, show a subtle
   banner: "Tip: Press ? for keyboard shortcuts on this page" — dismissable,
   never shows again after dismissed.

**Tracking:** Store shortcut-teaching state in localStorage:
```json
{
  "shortcuts_shown": {
    "ctrl+s": 3,   // shown 3 times, stop showing
    "ctrl+k": 1,   // shown once, show 2 more times
    "g_w": 0       // never shown yet
  },
  "first_visit_dismissed": ["wiki", "scenes"]
}
```

### Edge Cases

- **Input focus:** ALL single-key shortcuts are disabled when focus is in a
  text input, textarea, or contenteditable. Only Ctrl+/Meta+ combos work in
  inputs.
- **Terminal conflict:** When the game terminal is focused, ALL shortcuts are
  disabled — terminal captures everything. Escape exits terminal focus first.
- **Mac vs Windows:** Show ⌘ on Mac, Ctrl on Windows/Linux. Detect via
  `navigator.platform` at startup.
- **Custom shortcuts:** NOT in V1. Revisit if users request (adds significant
  complexity for marginal benefit).

### Accessibility

- Cheat sheet overlay is a proper dialog with focus trap
- Shortcuts don't override browser/AT defaults (no Ctrl+A, no Ctrl+F override)
- All shortcut-driven actions are also reachable via mouse/touch
- Screen readers announce the hint overlay content

**Implementation:** `KeyboardShortcutService` registered as scoped service.
Listens on `document.addEventListener('keydown')` via JS interop. Routes
keypresses through a state machine (handles two-key sequences). Checks active
element before dispatching single-key shortcuts.

**Related patterns:** Command Palette/Omnisearch (Ctrl+K is the primary shortcut),
Contextual Menus (show shortcut hints)

---

## 8. Notification Center (Bell Icon)

**Definition:** A unified notification feed accessible via a bell icon in the
topbar. Shows unread count as a badge. Clicking opens a dropdown panel with
categorized, chronological notifications. Each notification links to its source.

**Rationale:** MU* players participate in multiple concurrent activities
(scenes, wiki edits, mail threads, forum discussions). Without aggregation,
they'd need to poll each section manually. The notification center acts as a
single "what happened while I was away" surface.

### Specification

**Notification sources and templates:**

| Source          | Template                                             | Priority |
|-----------------|------------------------------------------------------|----------|
| Mail received   | "📨 {sender}: '{subject_preview}'"                   | Normal   |
| Scene activity  | "🎭 {count} new poses in '{scene_name}'"            | Normal   |
| Wiki (watched)  | "📝 '{page}' edited by {author} ({delta})"          | Low      |
| Event reminder  | "📅 '{event}' starts in {time}"                     | High     |
| Char approved   | "✅ {character} approved — now playable"             | High     |
| System message  | "🔧 {message}"                                       | Critical |
| Forum reply     | "💬 New reply in '{thread}'"                         | Normal   |
| Staff action    | "⚙️ {action} by {admin}"                            | High     |

**Behavior:**
- Badge shows unread count (max display: "99+")
- Clicking bell opens dropdown (not a new page)
- Dropdown: max height 70vh, scrollable, grouped by date (Today/Yesterday/Older)
- Each item: icon + text + relative timestamp + unread dot
- Clicking a notification: navigates to source, marks as read
- "Mark all read" button at the top
- "Notification preferences" link to settings page

**Aggregation rules:**
- Multiple poses in the same scene within 30 minutes → single notification:
  "🎭 8 new poses in 'The Market Square'"
- Multiple wiki edits by the same person → single notification:
  "📝 'Combat Rules' edited 3 times by Elena"
- Never aggregate across different sources

**Persistence:**
- Notifications stored server-side (survive across sessions)
- Unread state synced via SignalR (real-time badge updates)
- Retention: 30 days, then auto-purge
- Maximum 500 stored notifications per player

### Edge Cases

- **Flood protection:** If a wiki bot makes 100 edits in a minute, the player
  gets ONE notification ("100 edits to wiki by System"), not 100.
- **Self-notification:** Never notify a player about their OWN actions (their
  own pose in a scene, their own wiki edit).
- **Muted sources:** Player can mute specific scene notifications, specific
  wiki pages, or entire categories.
- **Offline backfill:** When a player reconnects after being offline, badge
  shows accumulated unread count. Don't show 50 individual toast notifications
  for catch-up — that's what the bell panel is for.

### Accessibility

- Bell icon: `aria-label="Notifications, 7 unread"`
- Dropdown: `role="dialog"`, `aria-label="Notification center"`
- Each item: `role="listitem"`, unread items have `aria-label` suffix "unread"
- Badge count announced via `aria-live="polite"` when it changes

**Related patterns:** Toast/Snackbar (real-time alerts for high-priority items),
Presence Indicators (online status changes can generate notifications)

---

## 9. Progressive Disclosure + Expandable Sections

**Definition:** Present minimal information by default. Provide clear affordances
to reveal more detail on demand. The initial view prioritizes scanability; the
expanded view provides completeness.

**Rationale:** A character profile might have 15 sections. A wiki page might
reference 20 related pages. Showing everything simultaneously overwhelms users
and makes it impossible to find what they came for. Progressive disclosure lets
casual browsers scan quickly while dedicated readers dive deep.

### Specification

**Disclosure levels:**

| Level    | What's shown                          | Trigger to expand       |
|----------|---------------------------------------|-------------------------|
| Summary  | 1-2 line preview, key metadata        | Click anywhere on card  |
| Standard | Full primary content, collapsed extras| Click section headers   |
| Complete | Everything, all sections expanded     | "Expand all" link       |

**Application by surface:**

**Character Profile:**
- Summary: Name + short desc + status + avatar (visible in lists/search)
- Standard: + full bio + visible attributes (collapsed: relationships, scene
  history, equipment, notes)
- Complete: All sections expanded

**Wiki Page:**
- Standard: Full page content with auto-generated TOC (floating right sidebar
  on desktop). Long sections (>800 words) get a "Show more" fold at 400 words.
- Table of Contents: Always visible on desktop, collapsible on mobile.
  Highlights current section on scroll (scroll-spy).

**Scene Archive:**
- Summary: Title + date + participants + word count (in scene lists)
- Standard: Full scene log
- Search results: Summary + highlighted match + context (±2 poses)

**Admin Settings:**
- Grouped into accordion cards by category (Server, Theme, Permissions, etc.)
- Only one group open at a time (accordion behavior)
- "Advanced" toggle within each group hides rarely-used options

### Visual Pattern

```
CHARACTER PROFILE:
┌─────────────────────────────────────────────┐
│ ALDRIC THE WANDERER                          │
│ "A weathered traveler from the north."       │
│ Online • Member since Jan 2024               │
│                                              │
│ ▾ Biography (expanded by default)            │
│   Aldric came from the frozen wastes of...   │
│   [full text]                                │
│                                              │
│ ▸ Attributes (click to expand)               │
│ ▸ Relationships (3)                          │
│ ▸ Scene History (12 scenes)                  │
│ ▸ Wiki Contributions (5 pages)               │
│                                              │
│ [Expand all ↓]                               │
└─────────────────────────────────────────────┘

WIKI PAGE:
┌──────────────────────────────────┬──────────┐
│ # Combat Rules                    │ Contents │
│                                   │ • Basics │
│ The combat system uses...         │ • Init ← │
│ [full content]                    │ • Damage │
│                                   │ • Magic  │
│ This section is quite long so     │          │
│ it gets truncated after 400...    │          │
│ [Show more ↓]                     │          │
└──────────────────────────────────┴──────────┘
```

### Edge Cases

- **Deep links with anchors:** If a URL contains `#section-name`, auto-expand
  that section and scroll to it (even if it would normally be collapsed).
- **Print/export:** When printing or exporting to PDF, expand ALL sections
  automatically (use `@media print` + JS).
- **State persistence:** Remember which sections a user has expanded on a
  per-page basis in localStorage. If they always expand "Relationships" on
  character profiles, keep it expanded for them.
- **Animation budget:** Expand/collapse animates height over 200ms with
  content fade-in. Never animate if `prefers-reduced-motion` is set.

### Accessibility

- Expandable headers: `role="button"`, `aria-expanded="true/false"`,
  `aria-controls="section-id"`
- Content regions: `id` matching `aria-controls`, `role="region"`
- "Expand all" button announces "All sections expanded" via live region
- Tab order follows visual order (expanded content is in tab flow)

**Related patterns:** Skeleton Loading (collapsed sections don't need skeletons),
Breadcrumbs (TOC provides within-page navigation)

---

## 10. Undo Instead of Confirm

**Definition:** For reversible destructive actions, execute immediately and
offer a time-limited Undo affordance (via toast). Reserve confirmation dialogs
exclusively for truly irreversible operations.

**Rationale:** Confirmation dialogs are theater. Users click "Yes" reflexively
after the third one — they provide zero actual protection while taxing every
interaction with a 2-click overhead. Undo inverts the cost model: 99% of
deletions are intentional (zero overhead), and the 1% mistake is recoverable
(low-stress single click within the grace period).

### Specification

**Undo-eligible actions (soft-delete, recoverable):**

| Action               | Undo window | Recovery mechanism                   |
|----------------------|-------------|--------------------------------------|
| Delete wiki page     | 8 seconds   | Soft-delete flag, restore on undo    |
| Archive mail         | 5 seconds   | Move to Archive, move back on undo   |
| Leave scene          | 5 seconds   | Remove from participants, re-add     |
| Remove nav link      | 8 seconds   | Mark removed, restore on undo        |
| Delete forum post    | 8 seconds   | Soft-delete, restore on undo         |
| Remove wiki watch    | 5 seconds   | Remove subscription, re-add          |
| Dismiss notification | 3 seconds   | Mark dismissed, un-mark on undo      |

**Confirm-dialog actions (irreversible, high-impact):**

| Action                       | Confirm style                           |
|------------------------------|-----------------------------------------|
| Delete player account        | Type "DELETE" to confirm                 |
| Purge wiki revision history  | Explicit checkbox + confirm button       |
| Remove admin access          | Confirm dialog with consequence text     |
| Nuke character (full delete) | Type character name to confirm           |
| Wipe all notifications       | Simple confirm dialog                    |

### Implementation

```csharp
// Undo pattern
private async Task DeleteWikiPage(WikiPage page)
{
    // 1. Soft-delete immediately
    page.IsDeleted = true;
    page.DeletedAt = DateTime.UtcNow;
    await _wikiService.SoftDeleteAsync(page.Id);
    
    // 2. Remove from UI
    _pages.Remove(page);
    StateHasChanged();
    
    // 3. Show undo toast (8 seconds)
    Snackbar.Add($"🗑 '{page.Title}' deleted", Severity.Normal, config =>
    {
        config.Action = "Undo";
        config.ActionColor = Color.Primary;
        config.Onclick = async _ =>
        {
            // 4. Restore on undo
            await _wikiService.RestoreAsync(page.Id);
            _pages.Insert(0, page with { IsDeleted = false });
            StateHasChanged();
        };
        config.VisibleStateDuration = 8000;
    });
    
    // 5. After grace period, hard-delete (or leave soft-deleted for admin recovery)
    // Handled server-side: soft-deleted items purged after 30 days
}
```

### Edge Cases

- **Multiple undos:** User deletes 3 pages in quick succession. Each gets its
  own undo toast (stacked). Undoing page #2 doesn't affect #1 or #3.
- **Navigate away:** If user navigates away during the undo window, the undo
  opportunity is lost (toast disappears). The soft-delete remains for admin
  recovery via the admin panel's "Deleted items" view.
- **Undo after undo:** If a user undoes a delete, then immediately deletes
  again, it's a fresh delete with a fresh undo window.
- **Concurrent access:** If user A deletes a page and user B is viewing it,
  user B sees a "This page has been deleted" banner (not a 404). If user A
  undoes, B's page refreshes automatically via SignalR.

### Accessibility

- Undo toast action button: full keyboard accessibility (Tab-reachable)
- Confirm dialogs: focus trapped, Escape cancels, Enter doesn't auto-confirm
  the destructive action (require explicit button click or typing)
- Screen reader announces: "Page deleted. Press Tab then Enter to undo."

**Related patterns:** Toast/Snackbar (undo lives in a toast), Optimistic Updates
(delete is optimistic — shows removed immediately)

---

## 11. Empty States That Guide

**Definition:** When a section has no content (empty wiki, empty inbox, no
scenes), display a purposeful illustration + explanation + primary action
instead of a blank page or "0 results" message.

**Rationale:** Every MU* game starts empty. A new game admin sees blank wiki,
zero characters, no scenes. If those pages feel dead, the admin feels
overwhelmed. Empty states are the most important onboarding surface — they tell
new users what each section IS and what to do first.

### Specification

**Structure of every empty state:**
1. Visual (icon or illustration) — not too large, centered
2. Headline — what IS this section (1 short sentence)
3. Explanation — why you'd use it (1-2 sentences, optional for obvious sections)
4. Primary CTA — the one action to do next (button, prominent)
5. Secondary guidance — suggestions, links to help (subtle, below CTA)

**Per-section empty states:**

| Section          | Icon | Headline                  | CTA                     | Secondary                        |
|------------------|------|---------------------------|-------------------------|----------------------------------|
| Wiki             | 📝   | No wiki pages yet         | [+ Create first page]   | Suggestions: Setting, Rules, FAQ |
| Scenes (active)  | 🎭   | No active scenes          | [+ Start a scene]       | [Browse archived scenes →]       |
| Mail inbox       | 📭   | No messages               | (no CTA — passive)      | "Messages from players appear here" |
| Characters (own) | 👤   | No characters             | [+ Create a character]  | [Read character creation guide →]|
| Forum            | 💬   | No discussions yet        | [+ Start a topic]       | (none)                            |
| Notifications    | 🔔   | All caught up             | (no CTA)                | "New activity will appear here"  |
| Search results   | 🔍   | No results for "{query}"  | [Search wiki instead?]  | Suggestions for broadening query |
| Watched pages    | 👁   | Not watching anything     | [Browse wiki →]         | "Click ⭐ on any page to watch"  |

**Design rules:**
- Icon/illustration: subtle, monochrome (follows theme), not garish clip art
- Never show "0 items" as a raw number — always a human sentence
- CTA is a real `<MudButton>`, not a text link
- The empty state disappears the moment content exists (even 1 item removes it)
- Search empty states ALWAYS suggest: check spelling, broaden terms, try
  alternate names

### Edge Cases

- **Permission-gated sections:** If the user can't create content (e.g. wiki
  is staff-only), the empty state shows different text: "No pages published
  yet. Staff can create pages in the admin panel." No CTA for non-staff.
- **Loading vs. empty:** Show skeleton FIRST (loading), then empty state if
  data returns empty. Never show an empty state while still loading.
- **Filtered empty:** If a list is empty because of an active filter, show:
  "No results with current filters. [Clear filters]" — not the generic
  empty state (that would be confusing when there IS content, just filtered out).

### Accessibility

- Illustrations have `aria-hidden="true"` (decorative)
- Headline is a proper heading (`<h2>` or `<h3>`)
- CTA button has descriptive label (not just "Create")

**Related patterns:** Skeleton Loading (precedes empty state), Progressive
Disclosure (first-visit guidance flows into this)

---

## 12. Breadcrumbs + Route Awareness

**Definition:** A clickable path trail showing the user's position in the
content hierarchy. Combined with page title in the browser tab and a
`BreadcrumbService` that auto-generates trails from route metadata.

**Rationale:** Users arrive via deep links (someone shares a wiki page URL,
a scene link in Discord, a mail notification link). Without breadcrumbs,
they land on a page with no orientation — they don't know what section they're
in or how to navigate up. Breadcrumbs provide instant spatial awareness.

### Specification

**Route → breadcrumb mapping:**

| Route                               | Breadcrumb trail                         |
|-------------------------------------|------------------------------------------|
| /wiki/combat-rules                  | Home > Wiki > Combat Rules               |
| /wiki/lore/magic-system             | Home > Wiki > Lore > Magic System        |
| /characters/aldric                  | Home > Characters > Aldric               |
| /scenes/active/market-square        | Home > Scenes > Active > Market Square   |
| /scenes/archive/2024/council        | Home > Scenes > Archive > 2024 > Council |
| /mail/inbox/msg-42                  | Home > Mail > Inbox > "About tomorrow"   |
| /admin/layout/widgets               | Home > Admin > Layout > Widgets          |

**Design rules:**
- Each segment is clickable (navigates to that level)
- Current page (last segment) is NOT a link — just bold/highlighted text
- "Home" is always the first crumb (never omitted)
- On mobile: collapse to "... > Parent > Current" (show only 2 levels)
- Maximum 5 visible segments before ellipsis collapse

**Auto-generation:** The `BreadcrumbService` reads `[Breadcrumb]` attributes
on page components:

```csharp
[Route("/wiki/{slug}")]
[Breadcrumb("Wiki", "/wiki")]  // parent
public partial class WikiPage : ComponentBase
{
    // page title becomes final crumb automatically
}
```

### Edge Cases

- **Dynamic titles:** Wiki pages and characters have user-defined names. The
  breadcrumb pulls the page title from loaded data (shows skeleton for that
  crumb segment while loading).
- **Orphaned pages:** If a wiki page has no category, the trail is just
  "Home > Wiki > Page Title" (skip the category level).
- **Admin sections:** Admin breadcrumbs only visible to admin users. A non-admin
  deep-linking to an admin page gets redirected to login/home.

### Accessibility

- Rendered in a `<nav aria-label="Breadcrumb">` element
- Uses `<ol>` with `<li>` items (ordered list — semantic hierarchy)
- Current page crumb has `aria-current="page"`
- Separator (>) is `aria-hidden="true"` (decorative)

**Related patterns:** Progressive Disclosure (breadcrumbs + TOC together provide
full spatial awareness)

---

## 13. Dark Mode as Default + Theme Token Architecture

**Definition:** Ship dark mode as the default palette. Build the entire
visual system on CSS custom properties (design tokens) with a 3-layer override
architecture so admins can reskin the portal by changing 12–15 variables.

**Rationale:** MU* players skew evening/night usage (roleplay happens after work
hours). Dark mode is not an accessibility accommodation here — it's the primary
experience. The token architecture ensures that admin theming doesn't require
CSS knowledge — they pick colors in a UI, the tokens propagate everywhere.

### Specification

**3-layer token architecture:**

```
Layer 1: MudBlazor MudTheme (C# object, compile-time)
  ├── Palette.Primary / Secondary / Tertiary
  ├── Palette.Surface / Background / AppBar
  ├── Typography (font family, scale)
  └── LayoutProperties (border radius, elevation, spacing)

Layer 2: CSS Custom Properties (runtime override layer)
  ├── --sharp-accent: var(--mud-palette-primary);
  ├── --sharp-surface-0: #1e1e2e;      (deepest background)
  ├── --sharp-surface-1: #181825;      (sidebar, cards)
  ├── --sharp-surface-2: #313244;      (elevated surfaces)
  ├── --sharp-text-primary: #cdd6f4;
  ├── --sharp-text-secondary: #a6adc8;
  ├── --sharp-terminal-bg: #11111b;
  ├── --sharp-border: #45475a;
  └── --sharp-success/warning/error/info colors

Layer 3: Admin Theme Presets (stored in DB, loaded at runtime)
  ├── Catppuccin Mocha (default)
  ├── Dracula
  ├── Nord
  ├── Solarized Dark / Light
  ├── Tokyo Night
  ├── High Contrast (accessibility)
  └── Custom (admin defines via color picker UI)
```

**Player-level customization (NOT layout — just visual):**
- Pick from presets the admin has enabled (dropdown in user settings)
- Toggle dark ↔ light (if admin enables light mode option)
- Font size adjustment: Small / Normal / Large / XL (scales via rem)
- Monospace vs. proportional for scene text (preference)
- "Compact mode" toggle (reduces padding/margins by 25%)

**Admin theme editor UI (Blazor admin panel):**
- Color pickers for each token (live preview)
- Import/export theme as JSON
- Preview panel showing how the portal looks with current selections
- "Apply to all users" vs. "Add as option" (let players opt in)

### Edge Cases

- **FOUC prevention:** Blazor WASM loads async. Before the WASM bundle downloads,
  the page shows the loading screen. Theme tokens must be injected in the HTML
  `<head>` (server-rendered `<style>` block), not via Blazor interop — so the
  loading screen itself is themed.
- **Contrast ratio:** All text+background combinations must meet WCAG AA (4.5:1
  for normal text, 3:1 for large text). The admin theme editor shows a
  contrast-check indicator (✓ or ⚠) next to each text/bg pair.
- **System preference:** Respect `prefers-color-scheme` on first visit if admin
  has enabled both dark and light. After the user sets a preference, honor that
  over system preference.
- **Terminal theming:** The game terminal uses its OWN background token
  (`--sharp-terminal-bg`) separate from page surfaces. This ensures the terminal
  always feels distinct (it's a "window into the game world").

### Accessibility

- High Contrast preset meets WCAG AAA (7:1 ratio)
- Font size preference persisted in localStorage (instant, no server call)
- All themes tested with color blindness simulators (deuteranopia, protanopia)
- Focus outlines use `--sharp-accent` and are never removed (only styled)

**Related patterns:** Skeleton Loading (shimmer color follows theme tokens),
All patterns inherit colors from these tokens

---

## 14. Responsive Design + Mobile Scene Experience

**Definition:** The portal adapts across 3 breakpoints (desktop, tablet, mobile)
with layout transformations that preserve functionality. Scene reading/posing
on mobile is a first-class experience, not a degraded desktop view.

**Rationale:** Players check scenes on their phone during lunch, commute, or
before bed. "I'll reply when I get to my desktop" kills RP momentum. A portal
that works on mobile keeps scenes active 24/7. AresMUSH's web portal is
desktop-only in practice — this is a clear competitive advantage.

### Specification

**Breakpoints:**

| Breakpoint   | Width        | Layout                                    |
|--------------|--------------|-------------------------------------------|
| Desktop      | ≥1200px      | Left nav + content + optional right panel |
| Tablet       | 768–1199px   | Collapsible left nav + full-width content |
| Mobile       | <768px       | Bottom tab bar + full-width content       |

**Desktop layout:**
```
┌──┬────────────────────────┬──────┐
│  │                        │      │
│N │      Main Content      │ Right│
│A │                        │ Panel│
│V │                        │      │
│  ├────────────────────────┤      │
│  │      Terminal          │      │
└──┴────────────────────────┴──────┘
```

**Mobile layout:**
```
┌──────────────────────────┐
│      Main Content        │
│                          │
│                          │
├──────────────────────────┤
│  [Pose input - sticky]   │
├──────────────────────────┤
│ 🏠  🎭  📝  ✉️  ⋯      │
│ Home Scene Wiki Mail More│
└──────────────────────────┘
```

**Mobile-specific adaptations:**

| Feature          | Desktop                        | Mobile                           |
|------------------|--------------------------------|----------------------------------|
| Navigation       | Left sidebar (always visible)  | Bottom tab bar (5 items max)     |
| Right panel      | Visible alongside content      | Swipe/tab within page            |
| Terminal         | Fixed bottom panel             | Bottom sheet (swipe up)          |
| Scene posing     | Fixed input at bottom of scene | Sticky input (chat-app style)    |
| Contextual menus | Right-click / ⋯ icon          | Long-press or ⋯ icon             |
| Wiki TOC         | Floating right sidebar         | Collapsible top section          |
| Omnisearch       | Modal overlay                  | Full-screen takeover             |
| Notifications    | Dropdown panel                 | Full-screen slide                |

**Scene on mobile (priority):**
- Pose input is ALWAYS visible (sticky bottom, above tab bar)
- Scene log scrolls above the input (newest at bottom, chat-style)
- Virtual keyboard doesn't push content off screen (use `visualViewport` API)
- Compose mode: input expands to 3 lines, toolbar appears above it
- Submit: Enter key or Send button (configurable in settings)

### Edge Cases

- **Virtual keyboard:** On iOS/Android, the virtual keyboard pushes content up.
  Use `window.visualViewport.height` to detect keyboard state and adjust layout.
  The pose input must remain visible even with keyboard open.
- **Orientation:** Landscape mobile is essentially a tablet — use tablet layout
  rules (show more content width, keep sidebar collapsed).
- **PWA install:** The portal should be installable as a PWA. Provide manifest.json
  with appropriate icons. Home screen launch → standalone mode (no browser chrome).
- **Offline mobile:** If disconnected on mobile, show cached content (last viewed
  pages) and queue poses locally. Show clear "offline" indicator.

### Accessibility

- Touch targets: minimum 44x44px for all interactive elements
- Bottom tab bar: proper `<nav>` with `role="tablist"` semantics
- Swipe gestures always have a non-gesture alternative (button)
- Screen reader: mobile layout has same semantic structure as desktop

**Related patterns:** Connection State (especially important on mobile networks),
Keyboard Shortcuts (disabled on mobile — touch replaces keyboard)

---

## 15. Command Palette / Omnisearch (Ctrl+K)

**Definition:** A unified modal search + command launcher activated by Ctrl+K.
Searches all content types simultaneously and accepts action commands.
Fully specified in `front-page-and-navigation.md`.

**Rationale:** Power users (and MU* players are ALL keyboard users) need a
single entry point that searches everything without knowing where things live.
The palette serves as navigation, search, AND action launcher.

### Additional Implementation Notes (beyond the full spec)

**MRU (Most Recently Used):**
- Before the user types anything, show 5 most recent items they accessed
- Items sorted by recency, not frequency
- Clear MRU via "Clear recent" link at bottom
- MRU stored in localStorage (not server — privacy)

**Result categories and precedence:**
1. Actions/commands (if input starts with ">": show commands only)
2. Pages the user has visited before (prioritized via recency)
3. Wiki pages (full-text search on title + body)
4. Characters (name + short description)
5. Scenes (title + participant names)
6. Navigation targets (route labels)

**Keyboard behavior:**
- Arrow Up/Down: move selection through results
- Enter: activate selected result
- Tab: switch between result categories
- Escape: close palette (first press clears input if non-empty)
- Type immediately: no need to "focus" the input, it's focused on open

### Edge Cases

- **Empty query with context:** If opened while on a wiki page, show
  "Related pages" as default suggestions (not just MRU).
- **Slow search:** If federated search takes >200ms for any provider,
  show results from fast providers immediately (wiki titles respond faster
  than full-text body search). Late results append below.
- **No results:** Show "No results for '{query}'. [Search wiki body] [Create
  wiki page '{query}']" — turn dead-ends into action opportunities.

**Related patterns:** Keyboard Shortcuts (Ctrl+K is the gateway shortcut),
Empty States (no-results state within the palette)

---

## 16. Connection State Awareness

**Definition:** A persistent, context-appropriate UI element that communicates
the SignalR/WebSocket connection state. Invisible when healthy, prominent when
degraded, and actionable when failed.

**Rationale:** Real-time MU* portals DEPEND on the WebSocket for scene posing,
presence, and notifications. A silently disconnected portal is a broken portal.
Users must know: Am I connected? Will my pose go through? What happens to text
I type while offline?

### Specification

**States and visual treatment:**

| State          | Visual                                              | Behavior                      |
|----------------|-----------------------------------------------------|-------------------------------|
| Connected      | (invisible — clean UI when healthy)                 | All features active           |
| Reconnecting   | Yellow banner top: "Reconnecting... (2/5)"          | Queue outgoing, show stale    |
| Disconnected   | Red banner: "Disconnected. [Reconnect]"             | Queue + show offline badge    |
| Reconnected    | Green flash: "✓ Reconnected. 3 queued sent." (2s)  | Flush queue, resume normal    |

**Reconnection strategy (exponential backoff with jitter):**
1. Immediate retry (0ms)
2. After 1s + random(0-500ms)
3. After 3s + random(0-1s)
4. After 10s + random(0-2s)
5. After 30s + random(0-3s)
6. Stop. Show "Disconnected" banner with manual [Reconnect Now] button.

**Queue behavior during disconnect:**
- Pose input STAYS enabled (user can still compose — never disable input)
- Composed poses queue locally (IndexedDB for persistence across refresh)
- Queue count visible: "3 messages queued"
- On reconnect: flush in order, confirm: "✓ 3 queued poses sent"
- If a queued item fails on flush: keep it in queue, show error on that item

**Stale data indicators:**
- During disconnect, presence dots get a "?" overlay (stale data)
- Notification badge freezes (shows last known count)
- Real-time widgets show "Last updated: 2 minutes ago" subtitle

### Edge Cases

- **Server restart (thundering herd):** When the server bounces, all clients
  disconnect simultaneously. Add random jitter (0-3s) to first reconnect
  attempt to avoid overwhelming the server on restart.
- **Auth expiry:** If reconnect fails with 401, don't retry — show:
  "Session expired. [Log in again]" (different from network disconnect).
- **Background tab:** Continue heartbeats in background, but don't show banner
  (user can't see it). Show banner on focus if still disconnected.
- **Stale queue:** If offline >10 minutes, show queue review UI on reconnect:
  "You have 5 queued messages (oldest: 12m ago). [Send all] [Review first]"
- **NEVER lose user text:** Even if the app crashes, IndexedDB preserves queued
  poses. On next load, show: "You have unsent messages from your last session."

### Accessibility

- Banner: `role="status"` (polite) for reconnecting, `role="alert"` for disconnect
- Reconnect button is keyboard accessible
- Queue count announced via `aria-live` when it changes
- "Work offline" mode clearly announces reduced functionality

**Related patterns:** Optimistic Updates (the queue IS optimistic updates for
disconnected state), Toast/Snackbar (reconnected confirmation)

---

## 17. Micro-Interactions + Motion Design

**Definition:** A constrained motion language — subtle animations on state
transitions that provide feedback, guide attention, and communicate hierarchy.
All animations respect `prefers-reduced-motion` and stay under 300ms.

**Rationale:** The difference between "functional" and "polished" is motion.
A pose appearing with a 200ms fade-in feels like it arrived. A pose appearing
instantly feels like it was always there (confusing in a real-time stream).
Motion communicates CHANGE — critical for a live-updating portal.

### Specification

**Animation budget:** Every animation has a cost. Budget total page animation
time (sum of all concurrent animations) to under 500ms per interaction event.

| Interaction                  | Animation                        | Duration | Easing             |
|------------------------------|----------------------------------|----------|--------------------|
| New pose arrives             | Fade-in from opacity 0→1         | 200ms    | ease-out           |
| Widget added to layout       | Scale 0.95→1.0 + fade-in         | 150ms    | ease-out           |
| Notification badge update    | Scale pulse 1.0→1.2→1.0          | 200ms    | ease-in-out        |
| Sidebar toggle               | Width slide + content fade       | 250ms    | cubic-bezier(.4,0,.2,1) |
| Hover on clickable card      | Subtle shadow elevation          | 100ms    | ease-out           |
| Hover on wiki link           | Underline slide-in from left     | 150ms    | ease-out           |
| Delete item (before undo)    | Slide-out left + fade            | 250ms    | ease-in            |
| Expand accordion             | Height auto-animate + content fade| 200ms   | ease-out           |
| Submit pose                  | Input border flash (accent color)| 100ms    | linear             |
| Error state                  | Gentle shake (3 cycles)          | 300ms    | ease-in-out        |
| Toast appear                 | Slide-in from edge + fade        | 200ms    | ease-out           |
| Toast dismiss                | Slide-out + fade                 | 150ms    | ease-in            |
| Page transition              | Content fade (exit 100ms, enter 150ms) | 250ms total | ease  |
| Skeleton shimmer             | Gradient sweep left→right (loop) | 1500ms   | linear (repeating) |

**Rules:**
1. Nothing longer than 300ms (feels sluggish above that)
2. Prefer `opacity` + `transform` (GPU-accelerated, guaranteed 60fps)
3. NEVER animate `height`, `width`, or `top/left` on content that causes reflow
4. Use CSS `will-change` sparingly (only on elements that actually animate)
5. All animations defined in CSS (not JS) — Blazor doesn't control frame timing
6. Loading animations (skeleton shimmer, spinners) are allowed to loop
7. Everything else: play once, then settle

**`prefers-reduced-motion` handling:**
```css
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
    scroll-behavior: auto !important;
  }
}
```
Result: animations still technically "run" (state changes happen) but are
imperceptible. No janky pop-in, no frozen states.

### Edge Cases

- **Performance:** If the client is a low-end device (detect via
  `navigator.hardwareConcurrency < 4`), reduce animation complexity
  (disable shadows, simplify transitions to opacity-only).
- **Rapid state changes:** If a notification badge updates 5 times in 1 second,
  don't play 5 pulse animations. Debounce: only animate the final state after
  200ms of stability.
- **Scroll position:** NEVER animate scroll position changes initiated by the
  system (e.g. new pose arrives). Only animate scroll when USER initiates
  (clicking "back to top" or TOC link → smooth scroll).

### Accessibility

- All motion respects `prefers-reduced-motion`
- No essential information conveyed ONLY via animation (always pair with
  state change that's visible even without motion)
- No auto-playing or looping animations in content areas (only in loading states)

**Related patterns:** Skeleton Loading (shimmer is the primary looping animation),
Toast/Snackbar (enter/exit animations)

---

## Summary: Priority Implementation Order

| Priority | Pattern                      | Impact | Effort | Phase | Dependencies         |
|----------|------------------------------|--------|--------|-------|----------------------|
| P0       | Dark Mode + Theme Tokens     | High   | Low    | 1     | None (foundational)  |
| P0       | Skeleton Loading             | High   | Low    | 1     | Theme tokens         |
| P0       | Toast/Snackbar               | High   | Low    | 1     | None                 |
| P0       | Connection State             | High   | Medium | 1     | SignalR infra        |
| P0       | Empty States                 | High   | Low    | 1     | None                 |
| P0       | Breadcrumbs                  | Medium | Low    | 1     | Router               |
| P1       | Keyboard Shortcuts           | High   | Medium | 1     | JS interop           |
| P1       | Optimistic Updates           | High   | Medium | 2     | Connection State     |
| P1       | Presence Indicators          | High   | Medium | 2     | SignalR + NATS       |
| P1       | Notification Center          | High   | Medium | 2     | SignalR              |
| P1       | Omnisearch (Ctrl+K)          | High   | Medium | 2     | Keyboard Shortcuts   |
| P2       | Inline Editing               | Medium | Medium | 2     | Optimistic Updates   |
| P2       | Contextual Menus             | Medium | Medium | 2     | None                 |
| P2       | Progressive Disclosure       | Medium | Low    | 2     | None                 |
| P2       | Undo Instead of Confirm      | Medium | Medium | 2     | Toast/Snackbar       |
| P2       | Responsive / Mobile          | High   | High   | 3     | All P0+P1 patterns   |
| P3       | Shortcut Teaching            | Low    | Low    | 3     | Keyboard Shortcuts   |
| P3       | Micro-Interactions           | Medium | Medium | 3     | Theme tokens         |

**Reading the table:** Phase 1 is the foundation — these patterns make the portal
feel professional on day one. Phase 2 adds the real-time community features that
differentiate us from AresMUSH. Phase 3 is polish that rewards long-term users.

---

## Anti-Patterns to Avoid

These are explicit prohibitions. If you catch yourself reaching for one of these
during implementation, stop and use the correct pattern instead.

1. **Modal abuse** — Never use a modal for information display. Modals are for
   actions that require a decision (and most of those should use Undo-toast
   instead). Modals: confirm destructive irreversible actions ONLY.

2. **Loading spinners** — Use skeletons instead. Spinners say "something is
   happening" but not what or how long. The ONLY acceptable spinner is inside
   a button that just submitted (e.g. "Saving..." button state).

3. **Infinite scroll without position memory** — If a user scrolls down a wiki
   list, navigates away, and comes back, they MUST return to their scroll
   position. Use virtual scrolling with scroll offset in URL state or sessionStorage.

4. **Auto-save without indication** — If the wiki editor auto-saves, SHOW IT.
   A small "Saved ✓" or "Saving..." indicator. Users panic without visual
   confirmation. See Pattern 3 (Toast) for feedback mechanism.

5. **Tabs that lose state** — If a user is writing a pose, switches to the wiki
   tab, and comes back, their draft MUST be there. Component state preservation
   is non-negotiable. Use `@rendermode InteractiveWebAssembly` keep-alive
   behavior or persist to sessionStorage.

6. **Pagination for small lists** — Under 50 items? Just show them all with
   virtual scrolling. Pagination is for 100+ items. Small lists with pagination
   feel hostile and bureaucratic.

7. **Requiring login to browse** — Visitor mode (wiki, character gallery, scene
   archives, game info) MUST be accessible without authentication. Login gates
   kill community discoverability. Visitors become players — don't block them.

8. **Flash of unstyled content (FOUC)** — Blazor WASM has a loading phase. Show
   a branded, THEMED splash screen (using the same CSS tokens), not a white page
   that flashes into dark mode. Theme must load before any content renders.

9. **Notification spam** — Never notify a player about their own actions. Never
   send 50 individual notifications when 1 aggregated notification suffices.
   See Pattern 8 aggregation rules.

10. **Disabled buttons without explanation** — If a button is disabled, provide
    a tooltip explaining WHY. "Save" greyed out with no explanation is hostile.
    "Save (no changes to save)" or "Save (missing required field: Title)" guides.

---

## Pattern Interactions Map

Patterns don't exist in isolation. This map documents how they compose during
key user flows.

### Submitting a Pose (optimistic + connection + toast + presence)

```
1. User types pose, hits Enter
2. (Pattern 2)  Optimistic: pose appears instantly in scene log, dimmed
3. (Pattern 11) Connection check: is WebSocket live?
   ├── YES → send to server, await confirmation
   │   ├── (Pattern 2)  Server ACK → remove dimming, pose is "real"
   │   └── (Pattern 2)  Server NACK → revert, show error toast
   └── NO  → queue in IndexedDB
       └── (Pattern 16) Show "queued" indicator on pose
4. (Pattern 5)  Other players see their presence update ("in scene")
5. (Pattern 3)  No toast for success (too frequent — visual noise)
6. (Pattern 17) Pose fades in (200ms ease-out) for all viewers
```

### Editing a Wiki Page (inline + undo + skeleton + toast)

```
1. User clicks title field (or pencil icon)
2. (Pattern 4)  Title transforms to editable input
3. User edits, blurs or presses Enter/Ctrl+S
4. (Pattern 2)  Optimistic: new title shown immediately
5. (Pattern 3)  Toast: "Page updated" with [Undo] (5s window)
6. If user clicks Undo:
   ├── (Pattern 10) Revert: old title restored
   └── (Pattern 3)  Toast: "Change reverted"
7. (Pattern 1)  Related content (sidebar links using this page name) shows
                 skeleton while refetching
```

### First Visit to Empty Game (empty states + progressive disclosure + theming)

```
1. Visitor arrives at game URL (not logged in)
2. (Pattern 13) Theme loaded from <head> style — dark mode, branded splash
3. (Pattern 1)  Skeleton for home page content
4. (Pattern 11) Home page has wiki content? If not → empty state:
                "Welcome to [Game Name] — this world is being built!"
5. (Pattern 9)  Navigation shows only relevant sections (hide empty ones from
                visitor view — don't show an empty "Scenes" section)
6. (Pattern 12) Breadcrumb: "Home" only (they're at root)
```

### Reconnecting After Network Drop (connection + queue + toast + presence)

```
1. (Pattern 16) WebSocket drops — yellow banner appears: "Reconnecting..."
2. (Pattern 5)  All presence dots get "?" overlay (stale data)
3. User continues typing a pose (input NOT disabled)
4. (Pattern 16) Queue builds: "2 messages queued" badge
5. WebSocket reconnects:
   ├── (Pattern 16) Green banner: "✓ Reconnected. 2 queued sent." (2s auto-dismiss)
   ├── (Pattern 5)  Presence dots refresh (? overlay removed)
   └── (Pattern 3)  If any queued message failed: error toast per-item
```