# Widget System

## Overview

Widgets are Blazor components placed into zones by admins. Five zones:
TopBar, LeftSidebar, RightSidebar, MainContent, Footer. Admin drags
widgets between zones, reorders, configures per-instance settings.

> **Per-widget config keys live in
> [docs/guides/widget-configuration.md](../guides/widget-configuration.md).** This page covers the
> architecture; that one is the reference for what each shipped widget accepts.

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
    string Name { get; }                     // Machine key used in WidgetPlacement.WidgetName
    string DisplayName { get; }              // SharedResource key for the palette label
    WidgetSize DefaultSize { get; }          // Small, Medium, Large
    WidgetZone[] AllowedZones { get; }       // Where this widget can be placed
    Type ComponentType { get; }              // The Razor component that renders it
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

Each widget is a Razor component paired with a descriptor class carrying that
metadata. `ZoneRenderer` instantiates the component through `DynamicComponent`
and passes the placement's config plus the zone name:

```csharp
@code {
    [Parameter] public JsonElement? Config { get; set; }
    [Parameter] public string Zone { get; set; } = string.Empty;
}
```

Every widget must declare both parameters even if it ignores them, or
`DynamicComponent` throws on the unmatched parameter.

`ConfigType` is descriptive metadata: it records the config shape for
developers and for the reference guide. Nothing generates an editor from it —
the layout editor opens a raw JSON box for every widget but the Spacer.

## Built-in Widgets

Registered in `SharpMUSH.Client/Program.cs`; descriptors live in
`SharpMUSH.Client/Widgets/`. Config keys for each are documented in
[the configuration guide](../guides/widget-configuration.md).

| Widget | Name | Zones | Config |
| --- | --- | --- | --- |
| Game Stats | `Stats` | Main | — |
| Active Scene | `ActiveScene` | Main, Left, Right | — |
| Recent Wiki Activity | `RecentWikiActivity` | Main, Left, Right | — |
| Online Characters | `OnlineCharacters` | Main, Left, Right | — |
| Quickstart | `Quickstart` | Main, Left, Right | — |
| Character Directory | `CharacterDirectory` | Main, Left, Right | — |
| Wiki Index | `WikiIndex` | Main | — |
| Character Gallery | `CharacterGallery` | Main, Right | `CharacterTargetConfig` |
| Wiki Body | `WikiBody` | Main | `WikiBodyConfig` |
| Quick Links | `QuickLinks` | Top, Left, Right, Footer | `QuickLinksConfig` |
| Welcome Text | `WelcomeText` | Main | `WelcomeTextConfig` |
| Spacer | `Spacer` | Main, Left, Right, Footer | `SpacerConfig` |
| Schema Widget | `SchemaWidget` | Main, Left, Right | `SchemaWidgetConfig` |

Widget-kind Dynamic Applications (Area 21) join the palette at startup under
their own slug, bridged in as `ApplicationPortalWidget`.

Two widgets read a character from the cascading `ProfilePageContext` rather than
from a parameter, so they work identically whether a page positions them
directly or an admin places them through the layout editor: Wiki Body and
Character Gallery. The Schema Widget uses the same context to fill `{objid}` and
`{character}` tokens in its routes.

## Layout Configuration

A layout is stored as one JSON blob per scope, keyed by zone. `LayoutSerialization`
is the single definition of that shape, shared by every database provider, and it
serializes camelCase:

```json
{
  "zones": {
    "TopBar": [
      { "widgetName": "QuickLinks", "order": 0, "span": 12, "config": { "links": [] } }
    ],
    "MainContent": [
      { "widgetName": "WelcomeText", "order": 0, "span": 12, "config": { "markdown": "# Welcome" } },
      { "widgetName": "Stats", "order": 1, "span": 8, "config": null }
    ]
  },
  "settings": {
    "leftSidebarEnabled": true,
    "rightSidebarEnabled": false,
    "footerEnabled": true,
    "leftSidebarWidth": "280px",
    "rightSidebarWidth": "280px"
  }
}
```

**Key points:**
- A widget can appear in multiple zones (e.g., Quick Links in TopBar AND Footer)
- Each instance has its own config
- `span` lays a zone out on a 12-column grid; omitted or `0` means full width
- Empty sidebar → auto-hidden (main content fills the space)
- Layout JSON saved to the layout store, cached, invalidated on admin save

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

## Layout Scopes

Layouts are per-scope, not one arrangement for the whole site. `LayoutScopes`
holds the editable set, and each scope exposes only the zones it renders:

| Scope | Zones | Drives |
| --- | --- | --- |
| `global` | TopBar, LeftSidebar, RightSidebar, Footer | The shell chrome (`MainLayout`) |
| `home` | MainContent | The front page |
| `wiki-index` | MainContent | The wiki landing page |
| `profile` | MainContent, RightSidebar | `/character/{name}` |

A page composes itself from a scope with `<ScopedZone Scope="..." Zone="..." />`,
which reloads live when an admin saves that scope. Each scope has a built-in
default layout (`LayoutService.GetDefaultLayout`) used until an admin saves one;
the defaults are listed in [the configuration guide](../guides/widget-configuration.md#layout-scopes-and-their-defaults).

Pages like `/play` (terminal) and `/admin` use their own fixed layouts regardless
of the widget layout config.

## Custom Widgets

Shipped as **Dynamic Applications** (Area 21) rather than uploaded assemblies: an
application declares a schema/data route pair served by softcode HTTP handlers,
and one of kind *Widget* is registered into the palette at startup under its own
slug. It renders through the shared `SchemaWidget`, so it needs no per-placement
config. See [Dynamic Applications](../guides/dynamic-applications-admin.md).

Plugins can also contribute compiled Razor components; see
[custom-widgets.md](custom-widgets.md) and [plugin-system.md](plugin-system.md).
