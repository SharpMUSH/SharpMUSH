# Admin Guide: Dynamic Applications

> **Status — implemented; screenshots pending a deployment.** The feature this guide
> describes is built and tested (`docs/todo/area-21-applications.md`). The REST surface was
> verified live against a bootstrapped server + ArangoDB: registering a valid app returns
> 200 and round-trips; a bogus schema URL is rejected ("Schema endpoint validation failed");
> an unknown kind is rejected. The **[SCREENSHOT]** markers below are still placeholders —
> capturing them needs a served WASM deployment and a browser, which the build environment
> here lacks; capture them from a real instance using the steps in each section.

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

> **[SCREENSHOT] Admin → Packages, the `chargen` package on the browse screen with its
> README and version dropdown.**

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

> **[SCREENSHOT] The `/admin/applications` registration form, filled in for a `chargen`
> Page app, with the "schema endpoint validated ✓" indicator.**

## After registration

- **Page apps** appear in the nav for users whose role is in *Allowed roles*, linking to
  `/apps/{slug}`.

  > **[SCREENSHOT] A rendered multi-page form at `/apps/chargen` — page 1 (Race & Class),
  > showing the Next control.**

- **Widget apps** appear in the **Layout editor** palette (`/admin/layout`); drag them into
  a zone like any built-in widget. Each placement carries its own schema/data URLs.

  > **[SCREENSHOT] The `/admin/layout` palette with a registered Widget app, and the same
  > widget rendered in the Right Sidebar.**

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
