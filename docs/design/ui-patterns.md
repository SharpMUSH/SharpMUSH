# Modern UI Patterns for SharpMUSH Portal

A catalogue of UX patterns that make the difference between "functional web app"
and "delightful modern experience". Each pattern includes what it is, why it
matters, and how it applies to SharpMUSH specifically.

These are informed by what Linear, Discord, Notion, and GitHub do well — apps
that handle real-time collaboration, content authoring, and community interaction.

---

## 1. Skeleton Loading (Shimmer)

### What
Instead of a spinner or blank page while content loads, show grey placeholder
shapes that mirror the content structure. The shapes pulse/shimmer.

### Why
- Perceived performance improves dramatically (users feel the page is "almost ready")
- Prevents layout shift (content doesn't jump when it loads)
- Communicates WHAT is loading, not just THAT something is loading

### SharpMUSH Application

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

Where to use:
- Wiki page loading (skeleton shaped like heading + paragraphs)
- Character profile (avatar circle + text lines)
- Scene list (card placeholders)
- Home page widgets (each widget shows its own skeleton)
- Online players list (avatar circles + name bars)

MudBlazor: `<MudSkeleton>` component — supports Circle, Rectangle, Text shapes.

---

## 2. Optimistic Updates

### What
Update the UI immediately when the user takes an action, before the server
confirms. If the server rejects, revert and show an error.

### Why
- Makes the app feel instant (no waiting for round-trip)
- Critical for real-time features like chat, scene posing, reactions
- Users don't care about server confirmation for most actions

### SharpMUSH Application

| Action                  | Optimistic behavior                        | On failure          |
|-------------------------|--------------------------------------------|--------------------|
| Send scene pose         | Appears instantly in the scene log         | Revert + error toast|
| Wiki quick-edit (title) | Title changes immediately                  | Revert + toast     |
| Send mail               | Shows in "Sent" folder immediately         | Move to "Drafts"   |
| Mark mail read          | Badge count drops immediately              | Re-increment       |
| React to scene pose     | Reaction appears instantly                 | Remove + toast     |
| Toggle wiki "watch"     | Star fills immediately                     | Un-fill + toast    |

### Pattern

```csharp
// Simplified pattern for Blazor
private async Task SendPose(string text)
{
    // 1. Optimistically add to local state
    var tempPose = new Pose(Id: Guid.NewGuid(), Text: text, Pending: true);
    _poses.Add(tempPose);
    StateHasChanged();
    
    // 2. Send to server
    try
    {
        var confirmed = await _sceneService.PostPoseAsync(text);
        // 3a. Replace temp with confirmed (gets real ID, timestamp)
        _poses.Replace(tempPose, confirmed);
    }
    catch
    {
        // 3b. Revert on failure
        _poses.Remove(tempPose);
        _snackbar.Add("Failed to send. Check connection.", Severity.Error);
    }
    StateHasChanged();
}
```

---

## 3. Toast / Snackbar Notifications

### What
Brief, non-blocking messages that appear (usually bottom-left or bottom-right),
confirm an action, or report an error. Auto-dismiss after a few seconds.

### Why
- Doesn't interrupt workflow (unlike modal dialogs)
- Confirms actions without requiring user attention
- Can include an Undo action for destructive operations

### SharpMUSH Application

| Trigger                    | Toast content                               |
|----------------------------|---------------------------------------------|
| Wiki page saved            | "✓ Page saved" (auto-dismiss 3s)           |
| Wiki page deleted          | "Page deleted. [Undo]" (stays 8s)          |
| Mail sent                  | "✓ Message sent to Gandalf"                |
| Character approved         | "✓ Aldric approved and visible"            |
| Connection lost            | "⚠ Connection lost. Reconnecting..."       |
| Connection restored        | "✓ Reconnected" (auto-dismiss 2s)          |
| Permission denied          | "✗ You don't have permission to edit this" |
| Scene pose received        | (silent — appears in scene log directly)    |

Key rule: **Never toast for things that have their own UI feedback.** A new
scene pose doesn't need a toast — it appears in the scene. But saving a wiki
page in the editor needs confirmation because the user can't see the result.

MudBlazor: `ISnackbar` service — inject and call `Snackbar.Add(...)`.

---

## 4. Inline Editing (Click-to-Edit)

### What
Instead of navigating to a separate "Edit Page" form, clicking on content
transforms it into an editable field in-place. Save on blur or Enter.

### Why
- Reduces navigation (user stays in context)
- Feels direct and responsive
- Matches how users think ("I want to change THIS text")

### SharpMUSH Application

| Element               | Edit trigger        | Save trigger         |
|-----------------------|---------------------|----------------------|
| Wiki page title       | Click on title      | Enter or blur        |
| Character bio excerpt | Click pencil icon   | Blur or Ctrl+Enter   |
| Scene title           | Click on title      | Enter or blur        |
| Event name/time       | Click on field      | Blur                 |
| Nav link label (admin)| Click label         | Enter or blur        |

NOT everything should be inline-editable. Long-form content (wiki body, scene
poses) still uses a dedicated editor — but metadata and short fields are
perfect candidates.

```
DISPLAY MODE:                    EDIT MODE (after click):
┌─────────────────────┐         ┌─────────────────────┐
│ Combat Rules    ✏️   │   →    │ [Combat Rules      ]│  ← focused input
│ Category: rules     │         │ Category: rules     │
└─────────────────────┘         └─────────────────────┘
```

---

## 5. Presence Indicators (Who's Here)

### What
Visual indicators showing who is online, who is viewing the same page,
who is typing in a scene.

### Why
- Creates a sense of community ("this game is alive")
- Prevents edit conflicts (you can see someone else is editing)
- Encourages interaction ("Gandalf is online, I should reach out")

### SharpMUSH Application

```
ONLINE PLAYERS WIDGET:              SCENE VIEW:
┌───────────────────────┐          ┌───────────────────────┐
│ Online (12)            │          │ The Market Square      │
│                        │          │                        │
│ 🟢 Gandalf             │          │ Viewing: 👁 3          │
│ 🟢 Aldric              │          │ Typing: Gandalf ···    │
│ 🟡 Elena (idle 5m)     │          │                        │
│ 🔴 Marcus (DND)        │          │ [Gandalf poses...]     │
│ ⚫ Thor (offline)      │          │ [Aldric poses...]      │
│                        │          │                        │
│ + 8 more               │          │                        │
└───────────────────────┘          └───────────────────────┘
```

Presence states:
- 🟢 Online (connected via WebSocket or active on web in last 2 min)
- 🟡 Idle (no activity for 5+ min)
- 🔴 Do Not Disturb (player-set)
- ⚫ Offline (for friend lists / character profiles)

"Typing" indicator in scenes:
- Shows when someone has typed in the pose input but hasn't submitted
- Disappears after 3 seconds of no keystrokes
- Only shown in active scenes, not wiki/forums

Wiki collaborative editing:
- "👁 2 others viewing" badge on wiki pages
- "✏️ Elena is editing this page" warning if someone else has the editor open
- Not a hard lock — just a warning (prevents surprise conflict)

---

## 6. Contextual Menus (Right-Click / Kebab)

### What
Secondary actions revealed via right-click or a ⋯ (meatball) / ⋮ (kebab) icon.
Actions are specific to the element being interacted with.

### Why
- Keeps the primary UI clean (no button overload)
- Power users get fast access to secondary actions
- Touch-friendly via the icon trigger

### SharpMUSH Application

| Element              | Contextual actions                              |
|----------------------|-------------------------------------------------|
| Wiki page (in list)  | Open, Edit, Copy link, Watch, Delete (if admin) |
| Character (in list)  | View profile, Send mail, Copy link              |
| Scene pose           | Quote, React, Copy, Report, Delete (if owner)   |
| Mail message         | Reply, Forward, Archive, Delete, Mark unread     |
| Nav link (admin)     | Edit, Move up, Move down, Delete                |
| Online player        | View profile, Send mail, Whisper                |

### Design Rules (per NN/g research):

1. Only for SECONDARY actions — primary actions stay visible
2. Place the ⋯ icon NEAR the element it affects
3. Never hide a single action behind a menu — just show it
4. Consistent icon usage (⋯ everywhere, not ⋯ on some and ⋮ on others)
5. Include keyboard shortcut hints in the menu items
6. Accessible via keyboard (Tab → Enter opens, arrow keys navigate)

```
Right-click on a wiki page in list:
┌────────────────────────────┐
│ 📖 Open                    │
│ ✏️  Edit                Ctrl+E │
│ 🔗 Copy link                │
│ 👁 Watch for changes        │
│ ─────────────────────────── │
│ 🗑️ Delete              (admin) │
└────────────────────────────┘
```

---

## 7. Keyboard Shortcuts + Shortcut Teaching

### What
Global keyboard shortcuts for common actions. The UI teaches them gradually
by showing the shortcut next to the action in menus, tooltips, and toasts.

### Why
- Power users get 10x faster interaction
- Gradual discovery — users learn shortcuts organically without memorization
- Reduces mouse dependency for repetitive tasks

### SharpMUSH Shortcut Map

| Shortcut     | Action                          | Context         |
|--------------|---------------------------------|-----------------|
| Ctrl+K       | Open omnisearch                 | Global          |
| Ctrl+/       | Show shortcut cheat sheet       | Global          |
| Escape       | Close modal/overlay/search      | Global          |
| G then H     | Go to Home                      | Global (vim-style) |
| G then W     | Go to Wiki                      | Global          |
| G then S     | Go to Scenes                    | Global          |
| G then M     | Go to Mail                      | Global          |
| G then T     | Toggle terminal                 | Global          |
| E            | Edit current page               | Wiki view       |
| N            | New pose                        | Scene view      |
| R            | Reply                           | Mail view       |
| J / K        | Next / Previous item            | Lists           |
| ?            | Show page-specific help         | Any page        |

### Teaching Pattern (Linear-style)

When a user performs an action via mouse that has a shortcut, show the shortcut
in the confirmation toast:

```
Toast: "✓ Page saved (next time: Ctrl+S)"
```

After seeing this 3 times, the user learns the shortcut without ever reading docs.

MudBlazor doesn't have a built-in shortcut system, but Blazor's `@onkeydown`
with a global `KeyboardShortcutService` handles this cleanly.

---

## 8. Notification Center (Bell Icon)

### What
A centralized feed of things that happened relevant to this player. Accessed
via a bell icon in the topbar. Unread count badge.

### Why
- Aggregates signals from multiple subsystems (mail, scenes, wiki, events)
- Doesn't interrupt flow (unlike email or push notifications)
- Player checks when THEY want to, not when the system demands

### SharpMUSH Notification Types

| Source          | Notification                                     |
|-----------------|--------------------------------------------------|
| Mail            | "New message from Gandalf: 'About tomorrow...'" |
| Scenes          | "New pose in 'The Market Square'"               |
| Wiki (watched)  | "Page 'Combat Rules' was edited by Elena"       |
| Events          | "Reminder: 'Story Night' starts in 1 hour"      |
| Admin           | "Your character 'Aldric' was approved"          |
| System          | "Server maintenance in 30 minutes"              |
| Forum           | "New reply to 'Combat Balance Discussion'"      |

### Design

```
┌────────────────────────────────────────────┐
│ 🔔 Notifications (7 unread)                 │
├────────────────────────────────────────────┤
│                                              │
│ TODAY                                        │
│ 📨 Mail from Gandalf              2m ago    │
│    "About tomorrow's session..."             │
│ 🎭 New pose in The Market         15m ago   │
│    Elena wrote 3 new poses                   │
│ 📝 Wiki: Combat Rules edited      1h ago    │
│    by Marcus (+2 paragraphs)                 │
│                                              │
│ YESTERDAY                                    │
│ ✅ Character approved              12h ago   │
│    Aldric is now playable                    │
│ 📅 Event reminder                  18h ago   │
│    Story Night at 8pm                        │
│                                              │
│ [Mark all read]          [Notification settings] │
└────────────────────────────────────────────┘
```

Players configure which notifications they want (per-category toggles in settings).
No notification is mandatory except system/admin announcements.

---

## 9. Progressive Disclosure + Expandable Sections

### What
Show the minimum information first. Let users expand for more detail.
Don't overwhelm with everything at once.

### Why
- Reduces cognitive load (user sees only what they need)
- Power users can go deeper; casual users aren't overwhelmed
- Pages feel cleaner and more scannable

### SharpMUSH Application

**Character Profile:**
```
┌─────────────────────────────────────────────┐
│ ALDRIC THE WANDERER                          │
│ "A weathered traveler from the north."       │
│                                              │
│ ▸ Background (click to expand)               │
│ ▸ Abilities                                  │
│ ▸ Relationships                              │
│ ▾ Recent Scenes (expanded)                   │
│   • The Market Square (2 days ago)           │
│   • Council Meeting (5 days ago)             │
│   • Arrival at the Inn (1 week ago)          │
│   [Show all 12 scenes →]                     │
└─────────────────────────────────────────────┘
```

**Wiki Table of Contents:**
- Long pages get an auto-generated floating TOC sidebar
- Sections can be collapsed/expanded
- "Back to top" button appears after scrolling 2+ screens

**Admin Settings:**
- Grouped into cards/accordions by category
- Advanced options hidden behind "Show advanced" toggle
- Tooltips on every setting explaining what it does

---

## 10. Undo Instead of Confirm

### What
Instead of "Are you sure you want to delete this?" dialogs, just do the action
and offer an Undo. Users recover from mistakes faster than they answer
confirmation dialogs.

### Why
- Confirmation dialogs train users to click "Yes" without reading (banner blindness)
- Undo respects the user's intent (they clicked delete, they meant it)
- Faster workflow — no interruption for 99% of cases
- The 1% who made a mistake can undo within 8 seconds

### SharpMUSH Application

| Action          | Instead of confirm...        | Do this                    |
|-----------------|------------------------------|----------------------------|
| Delete wiki page| "Are you sure?" modal        | Delete + "Undo" toast (8s) |
| Archive mail    | Confirm dialog               | Archive + "Undo" toast     |
| Leave scene     | "Are you sure?" modal        | Leave + "Rejoin" toast     |
| Remove nav link | Confirm dialog               | Remove + "Undo" toast      |

**Exception:** Truly destructive, IRREVERSIBLE actions DO get a confirm:
- Deleting an account
- Purging all wiki revisions
- Removing a player's admin access

Rule: If we can soft-delete and recover → use Undo toast.
If it's permanent and unrecoverable → use confirm dialog.

---

## 11. Empty States That Guide

### What
When a section has no content yet (no wiki pages, no scenes, no mail),
don't show a blank page. Show a helpful message with a clear call-to-action.

### Why
- Blank pages feel broken ("Is this an error? Did something fail?")
- Empty states are onboarding opportunities
- First-time users need guidance, not silence

### SharpMUSH Empty States

**Wiki (no pages yet):**
```
┌─────────────────────────────────────────────┐
│         📝                                   │
│                                              │
│    No wiki pages yet                         │
│                                              │
│    The wiki is where your game's lore,       │
│    rules, and world-building live.           │
│                                              │
│    [+ Create your first page]                │
│                                              │
│    Need ideas? Common first pages:           │
│    • Setting Overview                        │
│    • Character Creation Guide                │
│    • House Rules                             │
└─────────────────────────────────────────────┘
```

**Mail inbox (empty):**
```
    📭  No messages yet
    
    Messages from other players and staff will appear here.
```

**Scenes (none active):**
```
    🎭  No active scenes
    
    Scenes are where roleplay happens.
    [+ Start a scene]  or  [Browse past scenes →]
```

Design rules:
- Illustration or emoji (not a raw "0 results" message)
- 1 sentence explaining what this section IS (for new users)
- A clear primary action button
- Optional secondary links (examples, documentation)

---

## 12. Breadcrumbs + "Where Am I?"

### What
A trail showing the user's current location in the hierarchy. Clickable
segments let them navigate up.

### Why
- Users who arrive via deep link (shared URL) need orientation
- Reduces "lost in the app" feeling
- Provides an alternative to back-button navigation

### SharpMUSH Application

```
Home  >  Wiki  >  Lore  >  Combat Rules

Home  >  Characters  >  Aldric

Home  >  Scenes  >  Active  >  The Market Square

Home  >  Admin  >  Layout  >  Widget Configuration
```

MudBlazor: `<MudBreadcrumbs>` component. Auto-generate from the route path
plus a `BreadcrumbService` that maps routes to human-readable names.

---

## 13. Dark Mode as Default + Theme Tokens

### What
Ship dark mode as default (this is a gaming community site, not a business app).
Build the theme using design tokens (CSS variables) so color shifts are trivial.

### Why
- MU* players skew toward evening/night usage (RPers play after work)
- Dark mode reduces eye strain for extended reading/writing sessions
- Token-based theming means admins can reskin with 10–15 CSS variable changes

### SharpMUSH Theming Architecture

```
Layer 1: MudBlazor Theme (programmatic)
  ├── Primary, Secondary, Tertiary colors
  ├── Surface, Background, AppBar colors
  ├── Typography scale
  └── Border radius, elevation

Layer 2: CSS Custom Properties (override layer)
  ├── --portal-accent: #7c4dff;
  ├── --portal-surface: #1e1e2e;
  ├── --portal-text: #cdd6f4;
  ├── --portal-sidebar-bg: #181825;
  └── --portal-terminal-bg: #11111b;

Layer 3: Admin "Theme Presets" (shipped)
  ├── Catppuccin Mocha (default)
  ├── Dracula
  ├── Nord
  ├── Solarized Dark
  ├── Tokyo Night
  └── Custom (admin defines their own)
```

Player-level customization (NOT admin):
- Pick from presets the admin has enabled
- Toggle dark/light
- Adjust font size (accessibility)
- Choose monospace or proportional for scene text

---

## 14. Responsive Breakpoints + Mobile-First Scenes

### What
The portal works on mobile. Not "sort of works" — actually usable for the
core loop: read scenes, write poses, check mail, browse wiki.

### Why
- Players check scenes on their phone during the day
- "I'll reply when I'm at my desktop" kills RP momentum
- AresMUSH doesn't do this well — competitive advantage

### Breakpoint Strategy

```
DESKTOP (1200px+):          TABLET (768-1199px):       MOBILE (<768px):
┌──┬──────────┬──┐         ┌──┬──────────┐            ┌──────────┐
│  │          │  │         │  │          │            │          │
│N │ Content  │R │         │N │ Content  │            │ Content  │
│A │          │I │         │A │          │            │          │
│V │          │G │         │V │          │            ├──────────┤
│  │          │H │         │  │          │            │ Terminal │
│  │          │T │         │  │          │            ├──────────┤
│  ├──────────┤  │         │  ├──────────┤            │ ☰ Nav    │
│  │ Terminal │  │         │  │ Terminal │            └──────────┘
└──┴──────────┴──┘         └──┴──────────┘
```

Mobile adaptations:
- Navigation becomes a bottom tab bar (Home, Scenes, Wiki, Mail, More)
- Right sidebar content moves into tabs within the page
- Terminal becomes a bottom sheet (slide up to expand)
- Scene pose input is sticky at the bottom (like a chat input)
- Long-press replaces right-click for contextual menus

---

## 15. Command Palette / Omnisearch (Ctrl+K)

### What
A modal search box that searches EVERYTHING and also accepts COMMANDS.
Think Spotlight (macOS), VS Code command palette, or Linear's Ctrl+K.

### Why
- Single entry point for everything (no need to know where things live)
- Power users type instead of clicking through menus
- Works as both search AND navigation AND action launcher

### Already Designed

(See `front-page-and-navigation.md` for full specification)

Key additional patterns to note:
- Recent items appear before you type (MRU list)
- Results are categorized with section headers
- Arrow keys navigate, Enter selects, Escape closes
- Results show the action type as a chip: [Wiki] [Char] [Scene] [Go to] [Command]

---

## 16. Connection State Awareness

### What
The UI clearly communicates the WebSocket connection state. When disconnected,
the user knows and can take action. When reconnecting, progress is shown.

### Why
- WASM + WebSocket apps can disconnect (network blip, server restart)
- Users blame themselves ("did I break something?") without clear state
- Prevents data loss (don't let users type a long pose if disconnected)

### Design

```
CONNECTED (normal):       No indicator — clean UI, don't show "good" state

RECONNECTING:
┌─────────────────────────────────────────────────────┐
│ ⚠️ Connection lost. Reconnecting...  [3 seconds]    │
└─────────────────────────────────────────────────────┘
  (yellow bar at top, auto-retry with backoff)

DISCONNECTED (failed):
┌─────────────────────────────────────────────────────┐
│ ❌ Disconnected. Your work is saved locally.         │
│ [Reconnect now]                     [Work offline]   │
└─────────────────────────────────────────────────────┘
  (red bar, stays until resolved)

RECONNECTED:
┌─────────────────────────────────────────────────────┐
│ ✓ Reconnected                                        │
└─────────────────────────────────────────────────────┘
  (green bar, auto-dismiss 2s)
```

During disconnection:
- Disable the "Send Pose" button (prevent lost messages)
- Queue typed text locally
- When reconnected, offer "Send queued messages?"

---

## 17. Micro-Interactions + Polish

### What
Small, delightful animations that make the UI feel alive without being
distracting. 60fps transitions, subtle hover effects, state changes.

### Why
- Makes the difference between "functional" and "polished"
- Provides feedback that the system heard the user's input
- Creates perceived quality (users trust well-animated UIs more)

### SharpMUSH Micro-Interactions

| Interaction                  | Animation                                  |
|------------------------------|--------------------------------------------|
| New pose arrives in scene    | Fade-in from bottom (200ms ease-out)       |
| Widget added to layout       | Scale up from 0.95 → 1.0 (150ms)          |
| Notification badge update    | Bounce/pulse the number (200ms)            |
| Toggle sidebar               | Slide + fade (250ms cubic-bezier)          |
| Hover on wiki link           | Subtle underline slide-in from left        |
| Delete item (before undo)    | Slide out to left + fade (300ms)           |
| Expand accordion section     | Height animate + content fade-in (200ms)   |
| Submit pose                  | Input field subtle flash/highlight (100ms) |
| Error state                  | Gentle shake (3 cycles, 300ms total)       |

Rules:
- Nothing longer than 300ms (feels sluggish above that)
- Prefer opacity + transform (GPU-accelerated, 60fps)
- Respect `prefers-reduced-motion` media query (disable all animations)
- Never animate layout shifts (content jumping = nausea trigger)

---

## Summary: Priority Implementation Order

| Priority | Pattern                      | Impact | Effort | Phase |
|----------|------------------------------|--------|--------|-------|
| P0       | Skeleton Loading             | High   | Low    | 1     |
| P0       | Toast/Snackbar               | High   | Low    | 1     |
| P0       | Dark Mode + Tokens           | High   | Low    | 1     |
| P0       | Empty States                 | High   | Low    | 1     |
| P0       | Connection State             | High   | Medium | 1     |
| P1       | Keyboard Shortcuts           | High   | Medium | 1     |
| P1       | Optimistic Updates           | High   | Medium | 2     |
| P1       | Presence Indicators          | High   | Medium | 2     |
| P1       | Notification Center          | High   | Medium | 2     |
| P1       | Breadcrumbs                  | Medium | Low    | 1     |
| P2       | Inline Editing               | Medium | Medium | 2     |
| P2       | Contextual Menus             | Medium | Medium | 2     |
| P2       | Progressive Disclosure       | Medium | Low    | 2     |
| P2       | Undo Instead of Confirm      | Medium | Medium | 2     |
| P2       | Responsive / Mobile          | High   | High   | 3     |
| P3       | Shortcut Teaching            | Low    | Low    | 3     |
| P3       | Micro-Interactions           | Medium | Medium | 3     |

---

## Anti-Patterns to Avoid

1. **Modal abuse** — Never use a modal for information display. Modals are
   for actions that require a decision.

2. **Loading spinners everywhere** — Use skeletons instead. Spinners say
   "something is happening" but not "what" or "how much longer."

3. **Infinite scroll without position memory** — If a user scrolls down a
   wiki list, navigates away, and comes back, they should return to where
   they were. Use virtual scrolling with URL state.

4. **Auto-save without indication** — If the wiki editor auto-saves, SHOW IT.
   A small "Saved ✓" or "Saving..." indicator. Users panic without it.

5. **Tabs that lose state** — If a user is writing a pose, switches to the
   wiki tab, and comes back, their draft MUST be there. Local state
   preservation is non-negotiable.

6. **Pagination for small lists** — Under 50 items? Just show them all.
   Pagination is for hundreds+ of items. Small lists with pagination feel
   hostile.

7. **Requiring login to browse** — Visitor mode (wiki, character gallery, scene
   archives) should be accessible without authentication. Login gates kill
   community discoverability.

8. **Flash of unstyled content (FOUC)** — Blazor WASM has a loading phase.
   Show a branded splash screen, not a white page that flashes into dark mode.