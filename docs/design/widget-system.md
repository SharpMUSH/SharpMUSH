# Widget System

## Overview

Widgets are Blazor components placed into zones by admins. Five zones:
TopBar, LeftSidebar, RightSidebar, MainContent, Footer. Admin drags
widgets between zones, reorders, configures per-instance settings.

## Zone Model

```
┌──────────────────────────────────────────────────────────────┐
│  TopBar: [Nav Links] [Active Scenes Badge] [Search] [User]   │
├──────────┬───────────────────────────────────┬───────────────┤
│          │                                   │               │
│  Left    │         MainContent               │    Right      │
│  Sidebar │                                   │    Sidebar    │
│          │  (page content + main widgets)    │               │
│          │                                   │               │
├──────────┴───────────────────────────────────┴───────────────┤
│  Footer: [Quick Links] [Game Status] [Credits]               │
└──────────────────────────────────────────────────────────────┘
```

**Zone behavior:**
- TopBar: always present, horizontal, compact widgets only
- LeftSidebar: collapsible, vertical stack. If empty → hidden, content full-width
- RightSidebar: collapsible, vertical stack. If empty → hidden, content expands
- MainContent: always present, primary content area + widgets below page content
- Footer: always present, horizontal or stacked depending on widget count

**Responsive collapse:**
- Desktop (>1200px): all zones visible
- Tablet (768-1200px): sidebars collapse to hamburger/drawer
- Mobile (<768px): single column, sidebars become sections below main content

## Widget Interface

```csharp
public interface IPortalWidget
{
    string Name { get; }
    string DisplayName { get; }
    string Description { get; }
    WidgetSize DefaultSize { get; }          // Small, Medium, Large
    WidgetZone[] AllowedZones { get; }       // Where this widget can be placed
    Type? ConfigType { get; }                // Optional config schema (null = no config)
}

public enum WidgetZone
{
    TopBar,
    LeftSidebar,
    RightSidebar,
    MainContent,
    Footer
}

public enum WidgetSize
{
    Small,      // 1/3 width or compact
    Medium,     // 1/2 width or standard card
    Large       // Full width
}
```

Each widget is a Razor component that implements `IPortalWidget` metadata
and renders its own content. Widgets receive their config via a parameter:

```csharp
@code {
    [Parameter] public JsonElement? Config { get; set; }
}
```

## Built-in Widgets

### Active Scenes
- **Zones:** MainContent, LeftSidebar, RightSidebar
- **Shows:** List of in-progress scenes (title, participant count, last activity)
- **Config:** max_shown (default: 5)
- **Updates:** Real-time via SignalR (scene start/end/new pose)

### Recent Wiki Edits
- **Zones:** MainContent, LeftSidebar, RightSidebar
- **Shows:** Last N wiki edits (page name, editor, timestamp)
- **Config:** max_shown (default: 10)
- **Updates:** On page load + SignalR push on new edits

### Online Characters
- **Zones:** LeftSidebar, RightSidebar, MainContent
- **Shows:** Currently connected characters (name, idle time)
- **Config:** show_idle_time (bool), max_shown (default: 20)
- **Updates:** Real-time via SignalR (connect/disconnect/idle)

### Quick Links
- **Zones:** TopBar, LeftSidebar, RightSidebar, Footer
- **Shows:** Admin-configured list of links (internal or external)
- **Config:** links: [{label, url, icon?, new_tab?}]
- **Updates:** Static (changes on admin save only)

### Welcome Text
- **Zones:** MainContent
- **Shows:** Markdown-rendered welcome message for the front page
- **Config:** markdown (string), show_to_guests (bool)
- **Updates:** Static (changes on admin save only)

### Upcoming Events
- **Zones:** MainContent, RightSidebar
- **Shows:** Next N events/scheduled scenes from calendar (when BBS/events exist)
- **Config:** max_shown (default: 5), days_ahead (default: 7)
- **Updates:** On page load
- **Note:** Placeholder until Events system is built (Area 16)

### System Status
- **Zones:** Footer, LeftSidebar
- **Shows:** Player count, uptime, game version
- **Config:** show_uptime (bool), show_version (bool)
- **Updates:** Periodic (every 60s via SignalR)

### Character Switcher
- **Zones:** TopBar
- **Shows:** Dropdown of user's characters, current character highlighted
- **Config:** None
- **Updates:** Static per session (changes require re-login to add new chars)

## Layout Configuration

Layout is stored as a JSON structure in site config:

```json
{
  "zones": {
    "topBar": [
      { "widget": "QuickLinks", "config": { "links": [...] } },
      { "widget": "CharacterSwitcher" }
    ],
    "leftSidebar": [
      { "widget": "OnlineCharacters", "config": { "max_shown": 15 } },
      { "widget": "RecentWikiEdits" }
    ],
    "rightSidebar": [],
    "mainContent": [
      { "widget": "WelcomeText", "config": { "markdown": "# Welcome..." } },
      { "widget": "ActiveScenes" }
    ],
    "footer": [
      { "widget": "SystemStatus" },
      { "widget": "QuickLinks", "config": { "links": [...] } }
    ]
  },
  "settings": {
    "leftSidebarEnabled": true,
    "rightSidebarEnabled": false,
    "footerEnabled": true
  }
}
```

**Key points:**
- A widget can appear in multiple zones (e.g., Quick Links in TopBar AND Footer)
- Each instance has its own config
- Empty sidebar → auto-hidden (main content fills the space)
- Layout JSON saved to config store, cached, invalidated on admin save

## Layout Editor (Admin Panel)

Located at `/admin/layout`.

**UI:**
- Visual representation of zones (drag-and-drop areas)
- Widget palette on the side (available widgets to drag in)
- Click widget → config panel slides in from right
- Reorder via drag within zone
- Remove via X button or drag back to palette
- Toggle sidebars on/off
- "Preview" button opens site in new tab with draft layout
- "Publish" saves and broadcasts layout change via NATS

**No per-page layouts in v1.** One layout for the entire site. Pages like
`/play` (terminal) or `/admin` use their own fixed layouts regardless of
the widget layout config.

## Custom Widgets (Future — Area 18)

Deferred. When implemented:
- Admin uploads a Razor component (compiled .dll or Razor class library)
- Or: declarative JSON widget (title + REST endpoint + template)
- Registered in widget palette alongside built-ins
- Same zone/config system applies

For v1, only built-in widgets ship. The interface is designed so custom
widgets can be added later without changing the zone/layout infrastructure.
