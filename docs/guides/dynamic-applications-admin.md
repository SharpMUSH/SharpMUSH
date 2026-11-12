# Admin Guide: Dynamic Applications

## What a Dynamic Application is

A **Dynamic Application** is a portal page or widget whose entire interface is described by
JSON your game produces in softcode — a **Portal Schema Document**. You can stand up a
character-generation wizard, a "submit your background" form, a character sheet, or a
faction roster **without writing any C# or Razor**. Buttons and form submits call in-game
softcode and get a structured reply back.

There are two flavours:

- **Page** — a full, nav-linked page at `/apps/{slug}` (e.g. `/apps/chargen`).
- **Widget** — a unit you drop into a layout zone from the `/admin/layout` palette,
  alongside the built-in widgets.

Both are rendered by the same schema renderer; the only difference is where they appear.

## Building one in the portal

**Build & manage → Application builder** (`/build/apps`) makes a form, a sheet or a widget
step by step, with a live preview, and needs no softcode. Every change can be undone and
redone from its bar, and the draft is kept in the browser until it is saved.

1. **Start** — pick a template (blank, character sheet, request form, profile editor), or
   reopen one of your apps. The list shows the apps your character holds and the registered
   ones; **Open a saved one** takes any object and name. A draft you left unsaved is offered
   to resume.
2. **What it is** — its title and name (the slug), whether it is a form or a view, a page or
   a widget, and for a widget where it shows.
3. **Lay it out** — pages, sections and elements in an outline. Add an element from the
   palette under each section, drag a row or use its arrows to move it, and its **⋯** menu to
   duplicate or delete it. Select one to change it in its settings: what it shows, its rules
   (required, limits), what a click on it does (a command, with the clicked value put in by
   `{value}`, `{label}` or a column's key, or a dialog), and its width in the section. A
   field's key follows its label until you change the key yourself. The preview draws the
   draft at a page's, a widget's or a phone's width, with made-up values or with what the
   attributes hold on your own character. A row that has a problem carries a dot, and the
   problems are listed under the outline.
4. **Values** — for each value, the attribute it is read from on the viewed character, with
   what that attribute holds on your character now, and for a form, the attribute each field
   is saved to on whoever sends it (the same one unless you untick **Save to the same
   attribute**).
5. **Save** — lists what still stops the save, with a link to the step that fixes it, checks
   that you control the object, and writes these attributes on it (`me`, or its dbref), each
   marked new or replacing what is there:

| Attribute | Holds |
|-----------|-------|
| ``DATA`APP`<SLUG>`` | The Portal Schema Document |
| ``DATA`APP`<SLUG>`SOURCES`` | JSON object: field key → attribute read from the viewed character |
| ``DATA`APP`<SLUG>`SAVE`` | JSON object: field key → attribute written on the sender (forms) |
| ``DATA`APP`<SLUG>`REQUIRED`` | The keys a sent form must fill, space-separated (forms) |

With `applications.admin`, **Show it as a page** (or **as a widget**) adds the record described in
Part B below, with its routes filled in. The server serves a registered built application
itself, at `api/built/<slug>/schema`, `/data` and `/act`: each value is read as the viewer,
so an attribute they could not read with `get()` shows as hidden, and a sent form writes as
the sender, so it changes only what they could change with `&`. Only an application
registered this way is served, so nobody can point the routes at another object.

The attributes are plain JSON you can also edit by hand.

### Editing the game's own apps

The start page also lists **The game's apps**: registered applications whose layout is kept as
it is, as JSON, beside the route that serves it. For an application whose schema URL is
`http/<path>`, that is ``DATA`<PATH>`` on the HTTP handler, read by the route with `v()`. The
character profile is one: `http/profile/schema` is served by ``GET`PROFILE`SCHEMA`` from
``DATA`PROFILE`SCHEMA``. An application that builds its layout in softcode keeps no such
attribute and is not listed; change it in the Softcode Editor.

Opening one goes straight to **Lay it out**, then **Save**, which writes the layout back over
the attribute it came from, as you, so it needs the same control of the handler that `&`
would. Nothing else changes: the application's own routes still supply its values and take
its actions, so a field you add shows a value only once its route sends one under that key.
The preview shows made-up values.

When a package installed the layout, the list says so, and whether it has been changed here.
The package keeps managing the attribute, and an upgrade keeps your version as a local change.
When the upgrade changes the layout too, the package manager asks which to keep; the automatic
upgrade of a bundled package at startup keeps yours. For the character profile, **Reset** on the
Profile handler page (`/admin/profiles`) puts the shipped layout back.

## The two-part setup

Standing up an application is always two steps. **Both are required** — installing the
softcode without registering the app leaves it unreachable; registering without the
softcode produces an app whose schema endpoint returns nothing.

### Part A — install the softcode (the schema + routes)

The schema and its action handlers are softcode attributes on the game's **HTTP handler**
object (the same object that already serves the character profile API). You can either:

- **Install a package** (recommended) — e.g. a `chargen` package via **Admin → Packages**.
  This adds the route attributes (`GET`CHARGEN`SCHEMA`, `GET`CHARGEN`, `POST`CHARGEN`SUBMIT`,
  …) in dependency order. See the softcode author guide for how these are written.
- **Hand-author** the attributes on the HTTP handler object in-game.


### Part B — register the application

Go to **Admin → Applications** (`/admin/applications`, Wizard+ only) and create a record:

| Field | Meaning |
|-------|---------|
| **Display name** | What players see in nav / the widget palette |
| **Slug** | URL segment for a Page app → `/apps/{slug}` |
| **Icon** | Material icon shown in nav / palette |
| **Kind** | `Page` (nav-linked route) or `Widget` (zone placement) |
| **Schema URL** | `GET` endpoint returning the Portal Schema Document (e.g. `http/chargen/schema`) |
| **Data URL** | *(optional)* `GET` endpoint returning values to display/prefill |
| **Submit route** | *(optional)* base `POST` route for the schema's actions |
| **Allowed roles** | Guest / Player / Royalty / Wizard / God — gates the nav entry and the route |
| **Placement** | Nav location (Page) or allowed zones (Widget) |

On save, the portal **fetches the Schema URL and verifies it returns parseable JSON**
before accepting the record. A red error means the softcode isn't installed, the route is
wrong, or the schema has a syntax error — fix Part A and retry.


## After registration

- **Page apps** appear in the nav for users whose role is in *Allowed roles*, linking to
  `/apps/{slug}`.


- **Widget apps** appear in the **Layout editor** palette (`/admin/layout`); drag them into
  a zone like any built-in widget. Each placement carries its own schema/data URLs.


## Permissions

- **Who can register apps:** Wizard and above (the same bar as layout and theme editing).
- **Who can use an app:** controlled per-app by *Allowed roles*, which gates both the nav
  entry and the `/apps/{slug}` route.
- **What fields a user sees inside an app:** decided by **softcode**, not the portal. The
  viewer's identity is passed to the HTTP handler, which returns only the fields/values
  that viewer may see. The portal renders exactly what it's given and invents nothing.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| "Schema endpoint did not return valid JSON" on save | Softcode not installed, wrong route, or schema syntax error | Verify Part A; test the route in-game / via the browser |
| App nav entry missing for a player | Their role isn't in *Allowed roles* | Adjust *Allowed roles*, or confirm the player's game flags |
| Fields look empty in a `view` | The data endpoint returned no `visible` values for this viewer | Expected if softcode hides them; check the handler's visibility logic |
| A wizard step never advances | The action's `POST` route returned `ok:false` or an error | Inspect the handler's response envelope; check field validation messages |

See also: **`dynamic-applications-authoring.md`** (writing the softcode schemas) and the
design spec **`../design/dynamic-applications.md`**.
