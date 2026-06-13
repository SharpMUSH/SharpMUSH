# Character Profiles Design

## Overview

Character profiles are **wiki pages in the `Character:` namespace** with a structured
header injected from the game's HTTP handler. This gives profiles all wiki
features (history, revisions, search, wiki-links, Markdown editing) while the
game retains authority over structured data (demographics, stats, fields).

> **Note (Area 21):** The profile schema below is the canonical `kind:"view"` instance of
> the **Portal Schema Document** defined in `dynamic-applications.md`. The structured
> header is one specialization of the general schema-driven renderer — a `view` of
> `sections[] → keyvalue fields`. New profile field types and the eventual schema-aware
> profile *editor* should be expressed as that document type rather than a bespoke shape.

## Architecture

```
┌─────────────────────────────────────────────────────┐
│               Character:Gandalf                      │
├─────────────────────────────────────────────────────┤
│  ┌─── STRUCTURED SECTION (from MUSH HTTP handler) ──┐│
│  │  Full Name: Gandalf the Grey                     ││
│  │  Race: Maia    Faction: Free Peoples             ││
│  │  Status: Active                                  ││
│  │  [Stats/Skills rendered per game schema]         ││
│  └──────────────────────────────────────────────────┘│
│                                                      │
│  ┌─── FREEFORM SECTION (wiki page content) ─────────┐│
│  │  ## Background                                   ││
│  │  One of five Istari sent to Middle-earth...      ││
│  │                                                  ││
│  │  ## Relationships                                ││
│  │  **Frodo** - Ring-bearer, trusted companion      ││
│  │  **Saruman** - Former ally, now adversary        ││
│  │                                                  ││
│  │  ## RP Hooks                                     ││
│  │  - Often found smoking pipeweed near hobbits     ││
│  └──────────────────────────────────────────────────┘│
│                                                      │
│  ┌─── GALLERY (portal-managed) ─────────────────────┐│
│  │  [portrait1.png] [portrait2.png] [action.png]    ││
│  └──────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────┘
```

## Data Source: HTTP Handler as Profile API

The game's `http_handler` object serves as the sole authority for structured
profile data. The portal is a consumer/renderer, not a data owner.

### Endpoints (served by the HTTP handler MUSHcode)

Implemented today by the `profile-handler` package (read-only). Routes are under the live
`/http/...` handler prefix (not the obsolete `/mush/...`):

```
GET /http/profile/schema
  → Returns field definitions, sections, types, display order
  → Cached by portal (invalidated on schema change event)

GET /http/profile?objid={objid}
  → Returns structured field values for that character (addressed by stable objid)
  → Viewer identity passed via JWT (determines visibility)
  → Response includes: fields, values, and per-field visibility metadata

POST /http/profile?objid={objid}   (PLANNED — not yet implemented)
  → Would update structured fields from a web editor
  → Handler validates permissions and stores (sets attributes on character)
  → Profile editing is currently out of scope; the handler is read-only today
```

### Schema Endpoint Response Shape

```json
{
  "sections": [
    {
      "name": "Demographics",
      "order": 1,
      "fields": [
        {
          "key": "fullname",
          "label": "Full Name",
          "type": "text",
          "editable_by": "self",
          "max_length": 120
        },
        {
          "key": "age",
          "label": "Age",
          "type": "text",
          "editable_by": "self"
        },
        {
          "key": "faction",
          "label": "Faction",
          "type": "text",
          "editable_by": "staff"
        }
      ]
    },
    {
      "name": "Stats",
      "order": 2,
      "visible_to": ["player", "staff"],
      "fields": [
        {
          "key": "strength",
          "label": "Strength",
          "type": "number",
          "editable_by": "staff",
          "display": "bar",
          "max": 10
        }
      ]
    }
  ]
}
```

### Profile Data Response Shape

```json
{
  "character": "Gandalf",
  "dbref": "#42",
  "account_linked": true,
  "last_active": "2025-06-05T14:30:00Z",
  "fields": {
    "fullname": { "value": "Gandalf the Grey", "visible": true },
    "age": { "value": "Unknown (ancient)", "visible": true },
    "faction": { "value": "Free Peoples", "visible": true },
    "strength": { "value": 7, "visible": true }
  },
  "hidden_sections": ["Admin Notes"]
}
```

### Data Format Decisions

- **Plain text values by default** — fields are sent as plain text (ANSI stripped)
- **MString fields opt-in** — schema can mark a field as `"format": "mstring"` to
  send the raw MString (for fields that use ANSI styling intentionally, like a
  colored title or styled description)
- **Markdown fields** — schema can mark a field as `"format": "markdown"` for fields
  the game knows contain Markdown (rare for structured data, common for descriptions)
- **The HTTP handler decides** what format each field uses — portal renders accordingly

## Profile as Wiki Page

### Namespace Convention

All character profiles live in the wiki `Character:` namespace:
- `Character:Gandalf` — Gandalf's profile page
- The namespace is reserved — only the character owner (or staff) can edit

### Compositing Renderer

When the portal renders `Character:Gandalf`:

1. Fetch structured data from HTTP handler (cached per Decision 6.1C)
2. Render structured section as read-only HTML (from schema + data)
3. Render wiki page body as Markdown below the structured section
4. Render gallery below everything

The wiki page body is standard Markdown, editable by the character's player.
The structured section is read-only on the wiki page (edit in-game or via
the schema-aware form editor).

### Auto-Creation

When a character is created, a stub `Character:CharacterName` wiki page is
auto-created with a template:

```markdown
## Background

_Write your character's background here._

## RP Hooks

- 

## Relationships

```

### Wiki-Link Integration

From any wiki page: `[[Character:Gandalf]]` links to the profile.
From scene logs: character names can auto-link to profiles.
From the structured section: faction names can link to wiki pages
(`[[Faction:Free Peoples]]`).

## Visibility & Permissions (Decision 6.5A)

The HTTP handler controls ALL visibility for the structured section:

```
Anonymous viewer  → public fields only (name, faction, status)
Logged-in player  → public + "player-visible" fields
Character owner   → all fields + edit capability
Staff             → all fields + admin notes + edit capability
```

The portal passes the viewer's identity (from their JWT) to the HTTP handler.
The handler returns only the fields the viewer is allowed to see.

The wiki freeform section follows wiki permissions:
- Character owner can edit their own page
- Staff can edit any character page
- Viewing respects the character's "public" flag (if a character is marked
  private/unapproved, their wiki page may be hidden from non-staff)

## Editing Flow

### Structured Fields (via web)

```
Player clicks "Edit Profile" on their character page
  → Form rendered from schema (field types determine input widgets)
  → Player edits fields marked editable_by: "self"
  → Submit → POST /http/profile?objid={objid} with changed fields (planned)
  → HTTP handler validates and sets attributes on the game object
  → NATS event fired → portal cache invalidated → page re-renders
```

### Structured Fields (in-game)

```
Player types: @set me=FULLNAME:Gandalf the White
  → Attribute set directly on character object
  → NATS event fired → portal cache invalidated
  → Next web view shows updated data
```

### Freeform Content (wiki section)

Standard wiki editing flow:
- Web: click "Edit" on the freeform section → wiki editor → save
- In-game: `@wiki/edit Character:Gandalf` → opens in-game wiki editor (if implemented)

### Gallery (portal-managed)

- Upload images via web portal (drag-and-drop on profile page)
- Images stored via IFileStorage (local filesystem behind interface)
- Gallery metadata in portal DB: `{ character_id, filename, caption, order, is_profile_image }`
- Reorder via drag-and-drop
- Set profile image / icon from gallery

## Default HTTP Handler Setup

### Bootstrap Object

On first run (or via admin setup wizard), SharpMUSH creates a default
HTTP handler object with baseline profile endpoints:

```
Object: #HTTPHANDLER (type: THING, owner: #1)
Attributes:
  HTTP`PROFILE`SCHEMA   — MUSHcode that returns the default schema JSON
  HTTP`PROFILE`GET      — MUSHcode that reads attributes and returns JSON
  HTTP`PROFILE`SET      — MUSHcode that validates and sets profile attributes

Config:
  http_handler = #HTTPHANDLER
```

The default schema provides a minimal set of fields:
- Demographics: Full Name, Alias, Age, Concept
- Status: Approval status, faction/group memberships
- Description: the character's @desc (sent as MString)

Games customize by editing the handler's MUSHcode — adding fields, changing
visibility rules, computing derived values.

### Import / Setup Mechanism

Admin panel provides:
1. "Initialize HTTP Handler" button — creates the handler object with defaults
2. "Reset to Defaults" — overwrites handler attributes with stock code
3. "Export Handler" / "Import Handler" — share handler configurations between games

The handler object is a standard MUSH object — admin can `@edit` it in-game,
or modify via the web admin panel's attribute editor.

## Profile Discovery

### Character Directory

The portal provides a character directory at `/characters`:
- Lists all public, approved characters
- Filterable by: faction, status, "online now", "recently active"
- Search by name, alias, or full-text on profile wiki content
- Thumbnail = profile icon from gallery

### Omnisearch Integration

Profile pages are indexed by the omnisearch system:
- `Ctrl+K` → type character name → jumps to profile
- Also searches freeform wiki content on character pages

### SEO / Embeds

Public character profiles get:
- Clean URLs: `/wiki/Character:Gandalf`
- Open Graph meta tags (for Discord/social embeds): name, profile image, concept
- Server-side rendered (SSR via InteractiveAuto) for crawlers

## Relationship to Other Systems

### Scenes

Scene archives link participant names to their profiles.
Profile pages can show "Recent Scenes" (scenes this character participated in).

### Wiki

Profile IS a wiki page — inherits categories, tags, revision history.
Characters can be categorized: `[[Category:Free Peoples]]`, `[[Category:Wizards]]`.

### Presence

Profile page shows online/offline status (from presence system).
"Last seen" timestamp from character's last activity.
