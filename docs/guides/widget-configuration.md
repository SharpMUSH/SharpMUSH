# Widget Configuration Reference

Every widget placed through **Admin → Layout** (`/admin/layout`) carries its own JSON config blob.
This page documents the config each out-of-the-box widget accepts.

This is a reference for the widgets that ship with SharpMUSH. For building your own, see
[Custom Widgets](../design/custom-widgets.md) and
[Dynamic Applications](dynamic-applications-admin.md).

## How configuration works

A layout is a set of **zones**, each holding an ordered list of **placements**. A placement names a
widget and carries that instance's config:

```json
{
  "widgetName": "QuickLinks",
  "order": 0,
  "span": 6,
  "config": { "links": [{ "label": "Discord", "url": "https://discord.gg/example" }] }
}
```

| Field | Meaning |
| --- | --- |
| `widgetName` | Machine name of the widget (the **Name** column in the tables below). |
| `order` | Ascending sort order within the zone. |
| `span` | Width in columns of a 12-column grid, 1–12. Omitted or `0` means full width. |
| `config` | The per-widget blob documented on this page. `null` means "use defaults". |

Two things follow from this shape:

- **The same widget can appear more than once**, in the same zone or different ones, each with its
  own config. Quick Links in the top bar and Quick Links in the footer are two independent instances.
- **Keys are camelCase.** The stored blob is serialized with `JsonNamingPolicy.CamelCase`
  (`LayoutSerialization.Options`), so it is `showToGuests`, never `show_to_guests`.

Every widget ignores config it does not recognise, and falls back to its defaults when the blob is
malformed rather than failing the page.

### Where you type it

Click a placed widget in the layout editor and it opens a **raw JSON text box** — except the Spacer,
which gets a numeric height field.

You do not have to bring this page with you. Above that box, the dialog lists the keys the selected
widget accepts, with type, default, and a one-line description, and an **Insert template** button
seeds the editor with every key at its default. That table is generated from the widget's config
model, so it cannot fall out of step with the code. This page is the longer form: worked examples,
precedence rules, and how the pieces fit together.

### Layout-level settings

Sidebar and footer behaviour is not per-widget — it lives in the layout's `settings`:

```json
{
  "leftSidebarEnabled": true,
  "rightSidebarEnabled": false,
  "footerEnabled": true,
  "leftSidebarWidth": "280px",
  "rightSidebarWidth": "280px"
}
```

## Widgets at a glance

| Widget | Name | Zones | Config |
| --- | --- | --- | --- |
| [Wiki Body](#wiki-body) | `WikiBody` | Main | `slug`, `namespace`, `category`, `locale`, `character` |
| [Quick Links](#quick-links) | `QuickLinks` | Top, Left, Right, Footer | `links[]` |
| [Welcome Text](#welcome-text) | `WelcomeText` | Main | `markdown`, `showToGuests` |
| [Spacer](#spacer) | `Spacer` | Main, Left, Right, Footer | `height` |
| [Character Gallery](#character-gallery) | `CharacterGallery` | Main, Right | `character` |
| [Schema Widget](#schema-widget) | `SchemaWidget` | Main, Left, Right | `schemaUrl`, `dataUrl` |
| [Character Directory](#widgets-with-no-configuration) | `CharacterDirectory` | Main, Left, Right | — |
| [Wiki Index](#widgets-with-no-configuration) | `WikiIndex` | Main | — |
| [Game Stats](#widgets-with-no-configuration) | `Stats` | Main | — |
| [Active Scene](#widgets-with-no-configuration) | `ActiveScene` | Main, Left, Right | — |
| [Recent Wiki Activity](#widgets-with-no-configuration) | `RecentWikiActivity` | Main, Left, Right | — |
| [Online Characters](#widgets-with-no-configuration) | `OnlineCharacters` | Main, Left, Right | — |
| [Quickstart](#widgets-with-no-configuration) | `Quickstart` | Main, Left, Right | — |
| [Application widgets](#application-backed-widgets) | *the app's slug* | app-defined | — |

## Configurable widgets

### Wiki Body

Renders one wiki page inline. A page's identity is (namespace, category, slug), so `slug` alone
addresses `main:general:{slug}` and the other keys narrow it.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `slug` | string | — | Page slug. Set this to render an arbitrary wiki page. |
| `namespace` | string | `main` | Wiki namespace. |
| `category` | string | `general` | Category segment. |
| `locale` | string | reader's preference | Locale to render. |
| `character` | string | — | Shorthand for a character biography: same as `slug` = the name with `namespace` = `character`. Ignored when `slug` is set. |

**Which page is shown**, in precedence order:

1. `slug` from the config — an explicit page, always.
2. `character` from the config — the Character-namespace page whose slug is that name.
3. The character from the cascading profile page context — the route's character on
   `/character/{name}`.

Explicit config outranks the page context, which is what lets a fixed page sit on a character
profile. With none of the three, the widget renders nothing.

`namespace` only applies to case 1: a character biography is by definition in the `character`
namespace, so a `namespace` set alongside `character` (or alongside no page at all) is ignored.

```json
{ "slug": "house-rules", "namespace": "main", "category": "policies" }
```

```json
{ "character": "Gandalf" }
```

Placed with **no config** in the `profile` scope, it is the character biography — this is how the
default profile layout uses it.

### Quick Links

A list of links, rendered as a nav menu in the sidebars and as chips in the top bar and footer.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `links` | array | `[]` | The links to show. Empty renders nothing (admins see a prompt to configure it). |
| `links[].label` | string | required | Link text. |
| `links[].url` | string | required | Target, internal or external. |
| `links[].icon` | string | a generic link icon | A MudBlazor icon SVG path. |
| `links[].newTab` | bool | `false` | Open in a new tab. |

```json
{
  "links": [
    { "label": "Discord", "url": "https://discord.gg/example", "newTab": true },
    { "label": "House Rules", "url": "/wiki/main/policies/house-rules" }
  ]
}
```

### Welcome Text

A Markdown block, typically the front page greeting.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `markdown` | string | — | Markdown source. Empty renders nothing. |
| `showToGuests` | bool | `true` | Whether signed-out visitors see it. Set `false` for a members-only notice. |

```json
{ "markdown": "# Welcome\n\nNew here? Start with the [Quickstart](/wiki/main/general/quickstart).", "showToGuests": true }
```

### Spacer

Empty reserved space, for pushing widgets apart. Width comes from the placement's `span`; only the
height is configured. This is the one widget with a typed editor in the layout dialog.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `height` | int | `24` | Height in pixels. The editor clamps to 0–600; a non-positive value falls back to the default. |

```json
{ "height": 48 }
```

### Character Gallery

A character's image gallery, with upload controls for viewers who may edit that profile.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `character` | string | — | Character to show. Only consulted when no profile page context is cascading. |

On `/character/{name}` the route's character wins and no config is needed — that is how the default
profile layout uses it. Placed anywhere else without `character`, it renders nothing.

```json
{ "character": "Gandalf" }
```

### Schema Widget

Renders a Portal Schema Document fetched from softcode HTTP handler routes. Use this to place a
schema-driven view by hand; a registered Widget-kind
[Dynamic Application](dynamic-applications-admin.md) is easier and needs no config.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `schemaUrl` | string | — | Route returning the schema document. Without it — and without a matching application slug — the widget shows "schema unavailable". |
| `dataUrl` | string | — | Optional route returning the data bound into the schema. |

Both routes may contain page-context tokens, filled from the cascading profile context:

- `{objid}` — the viewed character's object id. If it cannot be resolved, the data fetch is skipped
  and the schema renders alone.
- `{character}` — the viewed character's name.

```json
{ "schemaUrl": "/api/portal/roster/schema", "dataUrl": "/api/portal/roster/data?owner={objid}" }
```

## Widgets with no configuration

These take no config; place them and they render. Setting a config blob on one is harmless and
ignored.

| Widget | Name | Shows |
| --- | --- | --- |
| Character Directory | `CharacterDirectory` | Every character, with search and profile links. |
| Wiki Index | `WikiIndex` | The wiki landing page: hero, client-side search, category grid. |
| Game Stats | `Stats` | At-a-glance tiles: players, scenes, recent changes, characters. |
| Active Scene | `ActiveScene` | The most recent active scene, with a join link. |
| Recent Wiki Activity | `RecentWikiActivity` | The most recently edited wiki pages. |
| Online Characters | `OnlineCharacters` | Currently connected characters, linking to profiles. |
| Quickstart | `Quickstart` | Static "new here?" links. |

## Application-backed widgets

A [Dynamic Application](dynamic-applications-admin.md) of kind *Widget* appears in the palette under
its own display name, and its placement's `widgetName` is the application slug. It needs no config:
its schema and data routes come from the application registry, resolved by that slug.

This also means an **unrecognised `widgetName` is not an error**. The registry treats it as an
application slug and renders it through the Schema Widget, so a placement keeps working even when
the startup application snapshot was empty. If no such application is registered server-side, the
widget shows "schema unavailable".

## Layout scopes and their defaults

Widgets are arranged per scope, not once for the whole site. Each scope exposes only the zones it
actually renders.

| Scope | Zones an admin can edit | Drives |
| --- | --- | --- |
| `global` | TopBar, LeftSidebar, RightSidebar, Footer | The shell chrome around every page. |
| `home` | MainContent | The front page. |
| `wiki-index` | MainContent | The wiki landing page. |
| `profile` | MainContent, RightSidebar | `/character/{name}`. |

Until an admin saves a scope, these built-in defaults apply — all of them with both sidebars off:

- **`global`** — TopBar: `QuickLinks`. Everything else empty.
- **`home`** — MainContent: `Stats` (span 12), `ActiveScene` (8), `OnlineCharacters` (4),
  `RecentWikiActivity` (8), `Quickstart` (4).
- **`wiki-index`** — MainContent: `WikiIndex`.
- **`profile`** — MainContent: `character-header` (the seeded Widget application), then `WikiBody`.
  RightSidebar: `CharacterGallery`.

Note that the profile defaults place `WikiBody` and `CharacterGallery` with **no config**: both take
their character from the route.
