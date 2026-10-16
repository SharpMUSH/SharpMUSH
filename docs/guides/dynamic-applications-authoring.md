# Softcode Author Guide: Dynamic Application Schemas

> **Status — implemented.** The rendering client, registry, and routes described here are
> built and tested (`docs/todo/area-21-applications.md`). The softcode contract below is the
> same HTTP-handler model the character-profile API uses
> (`examples/packages/profile-handler/package.yaml`). `examples/packages/chargen/` (character
> applications) and `examples/packages/jobs/` are complete examples: a form, lists, buttons and a
> sidebar, each built for the viewer, and integration tests drive their routes.
> **[SCREENSHOT]** markers await capture from a served deployment.

## The model in one paragraph

You write attributes on the game's **HTTP handler** object. A `GET` route returns a
**Portal Schema Document** (JSON describing the UI). An optional `GET` data route returns
values. `POST` routes are **actions** — they receive the form's current field values,
validate them, cause side effects, and return a structured JSON reply. The portal renders
your schema, collects input, posts it, and binds your reply back. **All decisions live in
your softcode**; the portal holds no game logic and no branching.

This is the exact pattern the read-only profile API already uses — you are generalizing
`GET`PROFILE`SCHEMA` / `GET`PROFILE` and adding `POST` actions.

## Routes

Routes are backtick children of the HTTP verb routers (the bundled `http-handler`
package). For an app served under `/http/chargen`:

```
GET `CHARGEN`SCHEMA     → the Portal Schema Document
GET `CHARGEN            → data values (view display / form prefill)   [optional]
POST`CHARGEN`SUBMIT     → submit action
POST`CHARGEN`ROLL       → a button action (e.g. roll stats)
POST`CHARGEN`NEXT       → a wizard "Next" partial submit
```

Each handler ends by emitting JSON:

```
@respond/type application/json; think json(object, ... )
```

## The schema document (what `GET`…`SCHEMA` returns)

```jsonc
{
  "kind": "form",                 // or "view"
  "schema_version": 1,
  "title": "Character Generation",
  "data_source": "/http/chargen?objid=...",   // optional
  "pages": [
    {
      "key": "abilities", "title": "Ability Scores", "order": 2,
      "sections": [
        { "name": "Rolls", "order": 1, "elements": [
          { "kind": "field", "key": "strength", "label": "Strength",
            "type": "number", "default": 8,
            "validation": { "required": true, "min": 3, "max": 18 } },
          { "kind": "button", "label": "Roll Stats", "action": "roll" }
        ]}
      ]
    }
  ],
  "actions": {
    "roll":   { "transport": "http", "method": "POST", "route": "/http/chargen/roll",
                "payload": "fields", "on_success": { "merge_fields": true } },
    "submit": { "transport": "http", "method": "POST", "route": "/http/chargen/submit",
                "payload": "fields",
                "on_success": { "navigate": "/character/%name%", "toast": "Created!" },
                "on_error":   { "bind_field_errors": true } }
  }
}
```

**Field types:** `text`, `textarea`, `mstring`, `number`, `select`, `multiselect`, `list`,
`boolean`, `radio`, `slider`, `date`, `hidden`, `computed`. `mstring` fields render your
ANSI/MXP markup. `select`/`radio`/`multiselect` carry an `options` array of
`{value,label}`. A `list` is free text, one value at a time, each shown as a chip that can be
removed; a value may hold a comma. Give it a JSON array in the data, and it posts one back:
`json(array,json(string,Sheet checked),json(string,Background read))`.

**Display elements (for `view`, also usable in `form`):** `markdown`, `image` (`src_field`),
`table` (`rows_field` + `columns`), `keyvalue` (`fields`; a value of `{text,link}` is a link),
`timeline` (`rows_field`), `graph` (`src_field` holding `{nodes,edges}`, `link`, `empty`), `divider`,
`button`. A form draws them exactly as a view does, so one `form` document can show a ticket's
history above its comment box. Markdown never passes raw HTML through, and links with a scheme
other than `http`/`https`/`mailto` are dropped.

**Tables.** A column may carry `link` (an href template; `{field}` is replaced by that row's
value, URL-escaped), `type: "chip"` (the cell as a chip), and `color_key` (the row field holding
its color). The table's `empty` text shows when there are no rows:

```jsonc
{ "kind": "table", "rows_field": "jobs", "empty": "No open jobs.", "columns": [
  { "key": "id",     "label": "#",      "link": "/apps/jobs/{id}" },
  { "key": "status", "label": "Status", "type": "chip", "color_key": "status_color" },
  { "key": "title",  "label": "Title" } ] }
```

Colors are `default`, `primary`, `secondary`, `tertiary`, `info`, `success`, `warning`,
`error`, `dark`; anything else is `default`.

A row's `group` field splits the table: a heading row with the group's name is drawn wherever the
group changes, so rows arrive already in group order (Pinned, then Posts). A table's `reorder` lets
the viewer move rows within one group, by dragging or with the up and down buttons on each row:

```jsonc
{ "kind": "table", "rows_field": "posts", "columns": [ ... ],
  "reorder": { "group": "Pinned", "action": "act", "key": "key", "values": { "op": "pinorder" } } }
```

Only rows whose `group` is `reorder.group` move. A move dispatches `action` with `values` plus
`item` (the moved row's `key` field) and `position` (its new place in the group, from 1). The
route answers with the usual envelope; return `data` so the table redraws in the new order.

**Timelines.** `{ "kind": "timeline", "rows_field": "entries", "empty": "No comments." }` over rows
like:

```jsonc
{ "author": "Ada", "time": 1760000000, "body": "Fixed in **#12**.",
  "format": "markdown",                       // or "code": verbatim, no markdown
  "tag": "Staff only", "tag_color": "warning",
  "actions": [ { "label": "Delete", "action": "delete_comment", "values": { "comment": 4 },
                 "confirm": "Delete this comment?" } ] }
```

`time` is unix seconds (number or string), shown in the viewer's local time. An entry action
dispatches the named action with its `values` merged over the form's field values.

`"format": "mstring"` takes a body made with `json(markupstring, <text>)`: the text with its colour
and markup, drawn as the terminal draws it. A `markdown` element takes the same `format` and value.

Rows can also form conversations:

```jsonc
{ "author": "[3] Bo", "body": "Agreed.", "group": "12",   // rows sharing a group read as one thread
  "reply_to": "re [1] Ada",                               // a small tag naming what it answers
  "anchor": "c3",                                         // its id: /apps/boards/5/2#c3 scrolls to it
  "unread": true,                                         // marked new
  "children_hidden": 4, "more": "/apps/boards/5/2/1",     // "4 more replies", linking there
  "links": [ { "label": "Reply", "href": "/apps/boards/5/2/3" } ] }
```

The first row of a group sits at the margin and the rest are indented under it; a new group starts
below a rule. Group rows in the order they are read: the timeline does not sort them. `more` and
each link's `href` are followed only when they are relative or `http`/`https`/`mailto`.

**Buttons** take `confirm` (asked in a dialog first), `values` (merged over the field values
posted — two buttons can share a route and differ by intent), `color`, and `variant`
(`filled`/`outlined`/`text`):

```jsonc
{ "kind": "button", "label": "Close job", "action": "update", "values": { "intent": "close" },
  "confirm": "Close this job?", "color": "error", "variant": "filled" }
```

In a `view`, buttons and timeline actions post their `values` alone.

**Fields** may carry `triggers_action`: the action dispatched whenever the value changes — a
filter select that re-fetches its table:

```jsonc
{ "kind": "field", "key": "status", "type": "select", "label": "Show",
  "options": [ {"value":"open","label":"Open"}, {"value":"all","label":"All"} ],
  "triggers_action": "filter" }
```

**Layout.** A section stacks its elements one per row by default. To place fields
side-by-side, set the section's `columns` to 2+ (it becomes a responsive grid, single-column
on mobile); give an element `span: N` to make it occupy N of those columns:

```jsonc
{ "name": "Ability Scores", "order": 2, "columns": 3, "elements": [
  { "kind": "field", "key": "str", "label": "STR", "type": "number" },
  { "kind": "field", "key": "dex", "label": "DEX", "type": "number" },
  { "kind": "field", "key": "con", "label": "CON", "type": "number" },
  { "kind": "field", "key": "notes", "label": "Notes", "type": "textarea", "span": 3 }
]}
```

## The action response envelope (what `POST` routes return)

```jsonc
{
  "ok": true,                                   // false → bind errors
  "errors":   { "strength": "Must be 3–18.", "_global": "Roll failed." },
  "fields":   { "strength": 14, "dexterity": 9 },   // merged when on_success.merge_fields
  "schema":   { /* a replacement Portal Schema Document */ },
  "data":     { "fields": { "entries": { "value": [ /* rows */ ], "visible": true } } },
  "redirect": "/character/Gandalf",
  "message":  "Character created."
}
```

- `_global` errors raise a snackbar; keyed errors attach to the matching field.
- `merge_fields` fills the named fields with returned values.
- `on_success.reset_fields: true` clears every input first, then merges the returned `fields`
  (post a comment, get an empty box back).
- A returned **`data` replaces the page's data payload**: tables and timelines redraw in place.
- A returned **`schema` replaces the whole document** and the portal re-renders.
- A `redirect` to `/apps/...` moves within the portal and reloads that application.

> The `errors` shape is identical to the one the existing admin config form already
> consumes, so this contract is battle-tested.

## Sub-paths and the query string

An application answers every address under its slug. In the registered `schema_url` and
`data_url`, `{path}` is replaced by the sub-path after the slug (each segment URL-escaped), and the
page's query string is appended to both:

| Address | `data_url: http/jobs/{path}` fetches |
|---|---|
| `/apps/jobs` | `http/jobs/` |
| `/apps/jobs/12` | `http/jobs/12` |
| `/apps/jobs/12?mine=1` | `http/jobs/12?mine=1` |

A `{path}` in the query works too (`http/jobs?id={path}`). Moving between these addresses — a
table `link`, a `redirect` — refetches schema and data. Your route reads the rest as usual.

## Softcode-driven progression (no client branching)

The portal has **no `show_if`, no conditional logic**. You drive everything by returning
new state:

- **Conditional fields / dynamic pages:** when a "Next" or button action posts the current
  fields, return a `schema` reflecting the choices so far. Example: if the player chose the
  Wizard class on page 1, your `POST`CHARGEN`NEXT` returns a schema whose Background page now
  includes a `spell_list` field. The portal just renders what you send.
- **Computed defaults:** compute them in softcode and return them in `fields`.
- **Per-viewer visibility:** in `view` data, mark each value `{ "value": …, "visible": 0/1 }`
  (exactly as `FUN`PROFILE`FIELD` does for profiles). Hidden values are not rendered.

## Building the JSON: the `FUN`*` helper idiom

Don't hand-write one enormous `json()` call. Define small helper attributes that each emit
one element, then compose them — the profile handler does this with `FUN`PROFILE`FIELD`:

```
FUN`PROFILE`FIELD: json(object,value,json(string,get(%0/PROFILE`%1)),visible,json(boolean,true))
```

> ### ⚠️ The `≥10-argument` ordering footgun
> `json(object, k1,v1, k2,v2, …)` with **ten or more arguments** must keep its arguments
> in numeric order. The engine does this for you (`ParserState.ArgumentsOrdered` sorts
> `%0,%1,…,%10,%11` numerically, not lexically — without it `%10` would sort before `%2`
> and scramble your key/value pairs). You don't have to do anything special, **but** the
> safest habit is to keep each `json()` call small by composing `FUN`*` helpers (one
> element per helper) rather than writing a single 30-argument object. This also keeps
> schemas readable and diff-friendly.

Validate your schema round-trips: a unit test like `SeededProfileSchema_EvaluatesToValidJson`
parses the `think` payload and asserts it's valid JSON — do the same for new schemas.

## Distributing as a package

Ship the route attributes as an **attach-mode** Area-20 package (managing attributes on
the existing HTTP handler, like `profile-handler`). Then an admin installs the package and
registers the application — see `dynamic-applications-admin.md`.

> **[SCREENSHOT] The rendered wizard built from this schema — abilities page with the
> "Roll Stats" button having merged rolled values into the fields.**

See also: the design spec **`../design/dynamic-applications.md`** and the live reference
implementation **`../../examples/packages/profile-handler/package.yaml`**.

## Shared presentation transitions and optional drafts

`SchemaApplicationHost` owns document, data, typed draft, errors and action admission for both page applications and schema widgets. The standalone form/view components also delegate actions to this host. Schema version 1 supports `form` and `view`; unsupported replacement versions/kinds produce a visible diagnostic.

An accepted response follows this order:

1. On success, `reset_fields: true` removes the draft (`reset_fields: ["key"]` removes only those keys), then returned `fields` merge when reset or `merge_fields` requests it.
2. On success **or failure**, replacement `data` is authoritative for keys it supplies, then replacement `schema` selects the resulting form/view presentation. Defaults seed missing keys only. Explicit null, false, zero and empty text suppress defaults.
3. Failure errors bind against the resulting form. Errors for hidden, removed or read-only targets join the accessible global summary. Failure never runs success reset/merge, toast or navigation.
4. On success an allowed redirect wins over local rendering and retires that request. Unsafe schemes are rejected.

Removed field values remain local draft so Back can restore them; actions post only the current form's declared fields plus explicit button values. A read-only view posts button values only. Hidden fields remain compatible transport metadata and are untrusted like every other posted value. The server must derive identity and recheck authorization independently.

Each host serializes actions and disables editing while pending. Disposal, navigation, replacement documents/data and active-character changes retire its response token. A late transport completion cannot change the current instance. A fresh host supplies an opaque `X-SharpMUSH-Form-Instance` nonce; it selects a tab within the verified actor, and grants no actor authority.

Jobs 1.12 adds an optional nonmodal adapter: `+request/draft new`, `set <field>=<literal text>`, `review`, `back`, `file`, and `cancel`. Ordinary commands remain available between edits. `set` preserves brackets, substitutions and semicolons as literal data; `softcode` takes exactly `true` or `false`. Existing `+request` and modal `@input` retain their routes and cancellation behavior. The Jobs new-request portal now uses the same review/file callbacks.

The internal `@appdraft[/show] <object>/<declaration>=<action>,<JSON fields>` adapter reads a JSON field array. Its fixed actions are new/set/review/back/file/cancel. Field types are text, textarea, select, boolean, number, ISO date, list and multiselect; rules currently supported by the adapter are required and max_length. No truthy string conversion occurs: boolean and numeric values must have their JSON types; text/date/select values are strings; lists contain strings. Unknown posted field names fail. Defaults apply only to absent fields. `appfields(<declaration>,<fields>)` applies the same structural validation for legacy one-shot entry points.

Sibling VALIDATE and FILE command attributes receive trusted named `draft_actor`, literal JSON `draft_fields`, and allowlisted `draft_action`; they answer by setting `appdraft_result` to ordinary action-response JSON. Jobs' FORM declaration drives its portal fields, terminal review and shared required/length checks. Its callbacks check bucket visibility, filing permission and whether the bucket still takes jobs immediately before filing, then invoke the existing filing routine. Application persistence remains ordinary softcode. Each callback enters its own attribute frame: `hasarg(draft_actor,supplied)`, `hasarg(draft_fields,supplied)` and `hasarg(draft_action,supplied)` are true. Inherited caller environment values remain visible but are not supplied callback inputs; no numbered aliases are created. The current attribute identity names the callback, and the caller's argument frame and identity resume after it returns.

Drafts expire after 30 idle minutes. There are at most 64 instances per actor, 4,096 globally, 64 declared fields and 64 KiB of field JSON per draft. Terminal instances retire on disconnect, logout and character rebind. Web instances are inaccessible under a different actor; client retirement and expiry discard abandoned presentation state. They do not support cross-client resume. File serializes with the instance's edits and binds its receipt to actor, instance, operation ID and field digest. Matching retries return the previous response; conflicting payloads or stale review operations fail. A failed File can return to edit, then a new review creates a fresh operation. Receipt memory is process-local: restart loses drafts/receipts, and no exactly-once promise spans process failure. If a callback starts filing but fails to answer, retry is fenced with an explicit check-the-world diagnostic.

Authoring cost for this proof: add a declared scalar/list field once; the portal projects it, the terminal loops the declaration, and structural validation reads the same declaration. A required/length change is one declaration edit. Conditional review changes WEB`DRAFT`ANSWER; world checks remain ordinary VALIDATE callback edits. Terminal-only wording belongs in FORM`DISPLAY. Unsupported field/rule shapes fail explicitly rather than defining another workflow language.
