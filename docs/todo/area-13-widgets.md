# Area 13: Widget System — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (13.1–13.4) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks

### Core Infrastructure
- [ ] Define `IPortalWidget` interface (Name, DisplayName, Description, DefaultSize, AllowedZones, ConfigType)
- [ ] Define `WidgetZone` enum (TopBar, LeftSidebar, RightSidebar, MainContent, Footer)
- [ ] Define `WidgetSize` enum (Small, Medium, Large)
- [ ] Layout JSON schema (zones → ordered widget instances with config)
- [ ] Layout loading service (read from config, cache, provide to components)
- [ ] Zone renderer component (iterates widgets in a zone, renders each)
- [ ] Widget config parameter injection (JsonElement per instance)
- [ ] Empty sidebar auto-hide logic (no widgets → zone hidden, main expands)
- [ ] Responsive collapse (desktop: all visible; tablet: drawers; mobile: stacked)

### Built-in Widgets
- [ ] Active Scenes widget (list, real-time via SignalR)
- [ ] Recent Wiki Edits widget (list, SignalR push on new edits)
- [ ] Online Characters widget (list, real-time connect/disconnect)
- [ ] Quick Links widget (admin-configured links, static)
- [ ] Welcome Text widget (Markdown rendered, static)
- [ ] Upcoming Events widget (scheduled scenes query)
- [ ] System Status widget (player count, uptime, periodic refresh)
- [ ] Character Switcher widget (dropdown in TopBar)

### Admin Layout Editor (`/admin/layout`)
- [ ] Visual zone representation (drag-and-drop areas)
- [ ] Widget palette (available widgets to place)
- [ ] Drag widget into zone / between zones
- [ ] Reorder within zone (drag)
- [ ] Remove widget (X button or drag back to palette)
- [ ] Per-widget config panel (slides in on click)
- [ ] Toggle sidebars on/off
- [ ] Preview button (opens site in new tab with draft layout)
- [ ] Publish button (save → NATS event → cache invalidation)

## Testing
- [ ] Layout loads correctly from JSON config
- [ ] Widgets render in correct zones in correct order
- [ ] Empty sidebar hides correctly
- [ ] Responsive: sidebars collapse at breakpoints
- [ ] Admin editor: drag, drop, reorder, configure, preview, publish
- [ ] Layout change propagates to all connected clients
