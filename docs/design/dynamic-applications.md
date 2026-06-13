# Dynamic Applications: Schema-Driven Forms & Views (Area 21)

## Overview

A **Dynamic Application** is a portal page or widget whose entire UI is described by a
JSON **Portal Schema Document** that the game produces in softcode. An admin can stand up
a D&D character-generation wizard, a "submit your background" form, a rich character
sheet, or a faction roster — **without writing any C# or Razor**. The portal is a pure
renderer: softcode owns the schema, the data, the validation, and every side effect.

This generalizes two patterns the portal already proves in miniature:

- **Character profiles** are rendered from a JSON *schema* the game emits
  (`GET`PROFILE`SCHEMA`) plus per-character *data* (`GET`PROFILE`), both served through
  the in-game HTTP verb-router and consumed by a passive Blazor renderer
  (`SharpMUSH.Client/Services/ProfileService.cs` → `CharacterHeaderWidget`) that imposes
  **no game policy**.
- `SharpMUSH.Client/Pages/Admin/Config/DynamicConfig.razor` builds a full **editable form
  at runtime** from `PropertyMetadata` JSON — a control-type switch, change tracking, a
  sticky save bar, and structured-error binding back onto fields.

Area 21 fuses them into one declarative system. It realizes and supersedes the
"Declarative widgets (future)" idea sketched in `widget-system.md:184-189` and
`custom-widgets.md` — replacing "fetch a URL, render with a Mustache template" with a
typed schema + action model.

**User-facing documentation:** an admin guide (`../guides/dynamic-applications-admin.md`)
and a softcode author guide (`../guides/dynamic-applications-authoring.md`) accompany this
spec. They carry the step-by-step walkthroughs and screenshots (screenshots captured once
the registration UI and `/apps/{slug}` route land — see Area-21 TODO Phase 9).

### Design principles

1. **The portal renders; softcode decides.** No game policy lives in the client: no
   invented categories, labels, ordering, or branching. (Consistent with the profile
   renderer, which filters purely on softcode's per-field `visible` flag.)
2. **One schema, two `kind`s.** A single document grammar serves both **`form`** (input)
   and **`view`** (read-only display). The current profile renderer is just a `view`.
3. **Actions are HTTP round-trips.** Buttons and submits POST to the in-game HTTP handler
   and receive a structured JSON reply (validation errors, merged values, next schema, a
   redirect). Request/response — not fire-and-forget.
4. **No client-side branching.** There is no `show_if` predicate. Conditional fields,
   dynamic pages, and computed defaults are realized by **softcode-driven progression**:
   the client posts current state and re-renders whatever schema/data softcode returns.
5. **Schemas live in softcode and ship as packages.** Authored on a game object and served
   over `/http/...`, distributable via the Area-20 package manager.

## Architecture

```
┌─ softcode (game) ─────────────┐        ┌─ portal client (Blazor WASM) ──────────────┐
│ GET `<ROUTE>`SCHEMA  ─────────┼──/http─┼─► SchemaAppService.GetSchemaAsync           │
│ GET `<ROUTE>` (data)  ────────┼──/http─┼─► SchemaAppService.GetDataAsync             │
│ POST`<ROUTE>`SUBMIT (action) ◄┼──/http─┼── SchemaAppService.SubmitAsync              │
│   ↳ @respond json {ok,errors} │        │        ▲                                    │
└───────────────────────────────┘        │        │ kind=form/view                     │
        ▲ dispatched by                   │   SchemaFormRenderer / SchemaViewRenderer   │
   HttpHandlerCommandService              │   (reuse DynamicConfig switch + error bind) │
   via /http/{**path} (Program.cs:143)    │        ▲                                    │
                                          │   ┌────┴───────────────┐                    │
                                          │   │ /apps/{slug} page  │  SchemaWidget      │
                                          │   │ (registry-driven)  │  (in ZoneRenderer) │
                                          │   └────────▲───────────┘        ▲           │
                                          └────────────┼──────────────────── ┼──────────┘
                                                       │ GET /api/applications│
                                          ┌────────────┴──────────────────────┴────────┐
                                          │ ApplicationsController (admin) → ISharpDatabase│
                                          │ RegisteredApplication (Arango/Memgraph/Surreal)│
                                          └─────────────────────────────────────────────┘
```

Three moving parts: a **softcode endpoint set** (game-owned, route-dispatched), a pair of
**generic Blazor renderers** (schema in → UI out), and a **DB-backed application registry**
that links a slug/nav entry/zone placement to those endpoints. The transport already
exists — `SharpMUSH.Server/Program.cs:143` maps `/http/{**path}` to
`HttpHandlerCommandService.DispatchAsync`, which runs the matching `<METHOD>` attribute as
a command list; `@respond`/`@respond/type` set status and content-type. Nothing new is
needed on the server transport for actions.

## The Portal Schema Document

One document type, `kind: "form" | "view"`, generalizing the profile schema's
`sections[] → fields[]` shape.

### Common envelope

```jsonc
{
  "kind": "form",                              // or "view"
  "schema_version": 1,
  "title": "Character Generation",
  "data_source": "/http/chargen?objid=...",    // optional; view/prefill data endpoint
  "pages": [ /* one or more; single-page docs use exactly one page */ ]
}
```

### Pages, sections, elements

- **`pages[]`** — `{ key, title, order, sections[], next?, prev? }`. A multi-step wizard
  has several; a single-page form/view has exactly one. `next`/`prev` name sibling page
  keys for navigation affordances (the renderer shows controls; progression itself is
  softcode-driven — see below).
- **`sections[]`** — `{ name, order, visible_to?, elements[], columns? }`. Generalizes
  profile sections; rendered in `order`. **`columns`** controls layout: `1` (default) stacks
  elements one per row; `2+` lays them side-by-side in an N-column responsive grid (always a
  single column on mobile). An element's optional **`span`** (default `1`) lets it occupy more
  than one column — e.g. `span: 2` in a 2-column section spans the full row.
- **`elements[]`** — each element is either a **field** (an input in a `form`, a value in
  a `view`) or a **non-field display element** (markdown, image, table, keyvalue,
  divider, button).

### Field element

```jsonc
{
  "kind": "field",
  "key": "strength",
  "label": "Strength",
  "type": "number",            // text|textarea|mstring|number|select|multiselect|
                               //   boolean|radio|slider|date|hidden|computed
  "options": [ {"value":"str","label":"Strength"} ],   // select/radio/multiselect
  "default": 8,
  "help": "Roll 4d6 drop lowest.",
  "validation": { "required": true, "min": 3, "max": 18, "max_length": 120,
                  "pattern": "^[A-Za-z ]+$" },
  "visible_to": "public"       // softcode-owned audience tag (opaque to portal)
}
```

**Control mapping.** Each `type` maps to a MudBlazor control by extending the existing
switch in `DynamicConfig.razor:346-429` — which already covers `switch`→boolean,
`numeric`→number, `text`→text — with `select`/`radio`/`multiselect`/`slider`/`date`/
`textarea`/`mstring`. The `mstring` type reuses the existing markup render path
(`WrapAsHtmlClass` / the output-rendering pipeline) so ANSI/MXP styling survives.
`hidden` carries a value without a visible control; `computed` is display-only.

**No client-side conditional logic.** There is deliberately **no `show_if` predicate**.
A field's visibility is decided by softcode — statically per audience via `visible_to`, or
at render time via the same per-datum `visible` flag the data endpoint already returns
(`ProfileService.FieldValue(Value, Visible)`, `ProfileData.Fields[key]` at
`ProfileService.cs:33-41`). Branching like "show Background only after Class is chosen" is
driven by re-fetching from softcode (below), never by a client predicate.

**Validation hints are advisory UX only.** `required`/`min`/`max`/`pattern`/`max_length`
drive input affordances and immediate feedback. **Softcode is the authoritative
validator** and returns binding errors in the action response.

### Display elements (`view`, also usable in `form`)

```jsonc
{ "kind": "markdown",  "value": "## Welcome, adventurer" }
{ "kind": "image",     "src_field": "portrait", "alt": "Portrait" }
{ "kind": "table",     "rows_field": "inventory", "columns": [ {"key":"item","label":"Item"} ] }
{ "kind": "keyvalue",  "fields": [ "fullname", "alias", "faction" ] }
{ "kind": "divider" }
{ "kind": "button",    "label": "Roll Stats", "action": "roll" }
```

The current read-only profile renderer (a `keyvalue` over `sections`) is one
specialization of `view`. `src_field`/`rows_field` reference data keys from the
`data_source` payload.

### Actions (HTTP-handler POST)

Buttons and submits reference an action by name:

```jsonc
"actions": {
  "submit": {
    "transport": "http", "method": "POST",
    "route": "/http/chargen/submit",          // → <POST>`CHARGEN`SUBMIT softcode
    "payload": "fields",                       // collected field values as JSON body
    "on_success": { "navigate": "/character/%name%", "toast": "Created!" },
    "on_error":   { "bind_field_errors": true }
  },
  "roll": { "transport": "http", "method": "POST", "route": "/http/chargen/roll",
            "payload": "fields", "on_success": { "merge_fields": true } }
}
```

`transport` is **always `"http"` in v1**; the field is explicit so a future `"command"`
transport (fire-and-forget into the live session via SignalR) is a non-breaking addition.

**Response envelope.** The softcode `<POST>` handler validates and replies with
`@respond/type application/json; think json(...)`:

```jsonc
{
  "ok": true,                                  // false → errors bind to fields
  "errors":   { "strength": "Must be 3–18.", "_global": "Roll failed." },
  "fields":   { "strength": 14, "dexterity": 9 },   // merged back when merge_fields
  "schema":   { /* a replacement Portal Schema Document */ },
  "redirect": "/character/Gandalf",
  "message":  "Character created."
}
```

The `errors` shape **deliberately matches** the one `DynamicConfig.razor:585-599` already
parses — `_global` → snackbar, keyed entries → per-field errors — so the renderer reuses
that logic verbatim. `on_success.merge_fields` merges returned `fields`; a returned
`schema` **replaces** the current document and the renderer re-renders.

### Softcode-driven progression (the key principle)

The schema lets the portal *send* field state into the MUSH; **softcode drives what
happens next**. The client holds no branching logic. Two mechanisms, both `/http`
round-trips:

- **Partial submit.** Any action — a button, a "Next" control, or an explicit
  `triggers_action` on a field — POSTs the *current, possibly incomplete* field values to
  a softcode route. Softcode decides what comes back.
- **Returned schema.** The response envelope may include a `schema` member: a replacement
  or next-page Portal Schema Document. The renderer simply re-renders from it. This is how
  conditional fields, dynamic pages, and computed defaults are realized — softcode emits a
  new schema (plus merged `fields`) reflecting the choices so far, instead of the client
  evaluating a predicate.

### Data flow (mirrors the profile)

- **`view`** — fetch the schema endpoint + the `data_source`; render values by `key` using
  the `ProfileData.Fields[key] = {value, visible}` shape; show only `visible` data.
- **`form`** — fetch the schema (+ optional `data_source` to prefill); collect values;
  POST to the action route; bind the response envelope back onto fields.

## Authoring schemas in softcode

Schemas are emitted with nested `json(object,...)`/`json(array,...)`, exactly as the
profile schema does (`examples/packages/profile-handler/package.yaml:27-51` — note the
reusable `FN`FIELD` helper attribute and `GET`PROFILE`SCHEMA` body).

> **`ArgumentsOrdered` numeric-ordering requirement.** Any `json()` call with **ten or
> more arguments** must keep numeric argument order — `%10` must not sort before `%2`.
> Field-heavy schemas hit this constantly. The engine handles it via
> `ParserState.ArgumentsOrdered`, but authors should still prefer small reusable `FN`*`
> helper attributes (one element each, à la `FN`FIELD`) over hand-writing one giant
> `json()` expression. This keeps schemas readable and sidesteps argument-count footguns.

## Application registry (server, DB-backed)

A `RegisteredApplication` record links a portal entry point to its softcode endpoints:

```
RegisteredApplication {
  id, slug, displayName, icon,
  kind: Page | Widget,
  schemaUrl,          // GET → Portal Schema Document
  dataUrl?,           // GET → data payload (view / form prefill)
  submitRoute?,       // POST base for actions (the schema's actions may also be absolute)
  allowedRoles[],     // Guest|Player|Royalty|Wizard|God (architectural-decisions.md:77-100)
  navPlacement?,      // where the nav entry appears (Page kind)
  zones[]?,           // allowed widget zones (Widget kind)
  order
}
```

Persisted via `ISharpDatabase` across **all three providers** — ArangoDB, Memgraph,
SurrealDB — at parity, with per-provider integration tests, exactly like the Area-20
`sys_*` collections (`area-20-packages.md` Phase 2). Exposed through an
`ApplicationsController` at `/api/applications`, `[Authorize]` **Wizard+** (decision
10.3 — layout/admin editing is Wizard-and-up; `architectural-decisions.md:99`). On
registration, the portal validates that `schemaUrl` returns parseable JSON before saving
(the same defensive degrade `ProfileService.GetSchemaAsync` already performs on a bad
schema).

## Client components

1. **Generic renderers** — `SchemaFormRenderer.razor` and `SchemaViewRenderer.razor` in
   `SharpMUSH.Client/Components/Schema/`. They reuse `DynamicConfig.razor`'s control
   switch (`:346-429`), change tracking (`RecalculateChanges`, `:537-542`), sticky save
   bar (`:122-154`), and structured-error parsing (`:581-616`); each render is wrapped in
   the existing `WidgetErrorBoundary`.
2. **`SchemaAppService`** — generalizes `ProfileService`: `GetSchemaAsync(url)`,
   `GetDataAsync(url)`, `SubmitAsync(route, payload)`, all via the named `"api"` HttpClient
   hitting relative `http/...` paths (as `ProfileService.cs:48-49,71-72` does). C# records
   mirror the envelope (schema document, data payload, action result), reusing the
   `FieldValue`/`ProfileData` shapes.
3. **Full-page route `/apps/{slug}`** — resolves the registry entry, picks the form or view
   renderer by the schema's `kind`, and is role-gated by `allowedRoles`. Nav entries are
   surfaced in `NavMenu.razor` from the registry, role-gated.
4. **Dynamic widget** — one compile-time `SchemaWidgetDescriptor`/`SchemaWidget`,
   registered in `SharpMUSH.Client/Program.cs` alongside the existing five descriptors
   (`:61-65`). Its `JsonElement Config` carries `{ schemaUrl, dataUrl }`. It plugs into the
   existing `IWidgetRegistry` + `ZoneRenderer` (`DynamicComponent`, `ZoneRenderer.razor:11-12`)
   with **no changes to the widget core**. Registered Applications of `kind: Widget` appear
   in the layout palette at `/admin/layout`.
5. **Action execution** — a POST to the declared route flows through the existing
   `/http/{**path}` route → `HttpHandlerCommandService.DispatchAsync` → the `<POST>`
   verb-router → the `<POST>`<ROUTE>` attribute. No new server transport.

## Admin setup (the linking step)

Standing up a Dynamic Application is explicitly **two parts**:

**(a) Install the softcode** that defines the routes + schema. Either hand-author the
attributes on the HTTP handler object, or install an Area-20 package — e.g. a `chargen`
package providing `GET`CHARGEN`SCHEMA`, `GET`CHARGEN`, and `POST`CHARGEN`SUBMIT`. (Routes
are backtick children of the verb routers from the bundled `http-handler` package; see
`area-20-packages.md` Phase 9.)

**(b) Register the Application** in the portal admin: name, icon, `kind`, the
schema/data/submit endpoints, allowed roles, and nav/zone placement → the DB registry
record. The portal validates the schema endpoint returns parseable JSON before saving.

## Permissions

- **Field/section visibility is softcode-owned** — the per-datum `visible` flag, exactly
  as `CharacterHeaderWidget` filters on `Visible`. The portal invents no categories,
  labels, or ordering.
- **App-level access uses the role hierarchy** (`architectural-decisions.md:77-100`):
  `allowedRoles` gates both the nav entry and the `/apps/{slug}` route; registration is
  Wizard+ (decision 10.3). The viewer's JWT identity is already forwarded to the HTTP
  handler, so softcode does the **authoritative** per-field gating regardless of what the
  client renders.

## Worked examples

### 1. D&D character generation (`form`, multi-page, softcode-driven branch)

A three-page wizard:

1. **Race & Class** — two `select` fields. A "Next" control POSTs the partial fields to
   `/http/chargen/next`.
2. **Ability Scores** — six `number` fields plus a `{ "kind":"button", "action":"roll" }`.
   `roll` POSTs current fields to `/http/chargen/roll`; softcode rolls 4d6-drop-lowest and
   returns `{ ok:true, fields:{...}, ... }` with `on_success.merge_fields` filling them in.
3. **Background** — an `mstring` field, plus a `submit` action to `POST`CHARGEN`SUBMIT`,
   which `@create`s and configures the character and returns `{ ok:true, redirect:
   "/character/Gandalf" }`.

**The branch is softcode-driven, with no client predicate:** when the player picks a class
on page 1, the "Next" POST returns a **replacement `schema`** whose Background page now
contains class-specific fields (spell list for a wizard, rage uses for a barbarian). The
renderer just re-renders the returned document.

### 2. Profile-as-view (backward-compatibility)

Re-express today's `GET`PROFILE`SCHEMA` as a `kind:"view"` document: each profile section
becomes a `section`, each profile field a `keyvalue` field. The same `data_source`
(`GET`PROFILE`) payload feeds it. This demonstrates the profile is the canonical `view`
instance of the Portal Schema Document — the new renderer subsumes the bespoke one.

### 3. Package + registry pairing

A `chargen` package's `package.yaml` (attach-mode attributes on the HTTP handler, à la
`profile-handler`) ships the `GET`/`POST` route attributes; the matching
`RegisteredApplication` record (`kind: Page`, `slug: "chargen"`, `schemaUrl:
"http/chargen/schema"`, `submitRoute: "http/chargen"`, `allowedRoles: [Player]`,
`navPlacement: main`) links it into the portal. Installing the package + creating the
record = a live `/apps/chargen` wizard.

## Non-goals (v1)

- **No code in this pass** — this document is the binding spec; implementation is tracked
  in `docs/todo/area-21-applications.md`.
- **No client-side business logic** — no schema-level conditional/branching predicates;
  visibility and progression are softcode-driven (partial-submit round-trips + returned
  schemas). The client only renders, collects, posts, and binds errors.
- **No SignalR/command transport** — HTTP request/response only in v1 (the `transport`
  field reserves the extension point).
- **No in-portal schema authoring UI** — schemas are softcode; the portal registers and
  links them, it does not edit them.
- **No per-user persistence of in-progress form state** — refresh restarts the form
  (softcode may, of course, persist partial state itself via partial submits).

## Relationship to other areas

- **Area 6 (Profiles)** — the profile schema is the canonical `view` instance; the new
  renderer generalizes `CharacterHeaderWidget`.
- **Area 13 (Widget system)** — the dynamic widget plugs into `IPortalWidget`/zones with no
  core changes; `kind: Widget` apps appear in the `/admin/layout` palette.
- **Area 20 (Package manager)** — schemas + route handlers ship as packages and install via
  the existing attach-mode bundled-package machinery.
