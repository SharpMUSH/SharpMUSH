# Area 21: Schema-Driven Forms, Views & Dynamic Applications — TODO

Design: `docs/design/dynamic-applications.md`. Depends on Area 13 (widget system),
Area 6 (HTTP handler / profile schema), Area 20 (package manager).

## Status (2026-06-13) — implemented & green

Phases 1–8 landed and the full solution builds (warnings-as-errors). Backend registry
runs on all three providers (registry tests pass on Arango via Podman); schema
serialization, the view-renderer bUnit test, the chargen-schema JSON-validity test, and the
example-package manifest all pass.

**Implementation refinements vs. the design doc:**
- Access uses a single **`MinimumRole`** (the `PortalRole` hierarchy), not an
  `allowedRoles[]` list — a literal allow-list would wrongly exclude God when Wizard is
  listed. Nav + `/apps/{slug}` gate hierarchically; the controller validates the schema
  endpoint before persisting.
- Writes are `[Authorize(Roles = nameof(PortalRole.Wizard))]`, matching the existing
  WikiController convention; note this excludes God (#1) — a portal-wide quirk to revisit.
- `mstring` fields render as text for now (markup-pipeline integration deferred).

Refinements since: `mstring` field values now render as portal markup (terminal pipeline);
the form renderer does an advisory required-field check before a final submit. The REST
surface was verified live against a bootstrapped server + ArangoDB (valid register 200 +
round-trip; bogus schema URL rejected by validation; unknown kind rejected).

**Still open:** Phase 9 **screenshots** — the guides' `[SCREENSHOT]` markers still need
capture from a served WASM deployment with a browser (the dev `dotnet run` here serves the
API but not the WASM, and no browser is installed); richer display elements; the v2
deferrals below.

## Pre-Implementation
- [ ] Confirm decisions with project owner (transport = HTTP `<POST>`; admin registers
      both Page and Widget; schemas live in softcode, served over `/http`; registry
      DB-backed and global) — affirmed during planning 2026-06-13
- [ ] Confirm the response envelope contract (`ok`/`errors`/`fields`/`schema`/`redirect`/
      `message`) and that `errors` matches `DynamicConfig.razor`'s parser exactly
- [ ] Confirm "no client-side branching" stance (softcode-driven progression only)

## Implementation Tasks

### Phase 1: Schema Contract & Client Models
- [ ] Define the Portal Schema Document C# records (envelope, page, section, field
      element, display elements, action) — generalize `ProfileService` records, reuse
      `FieldValue`/`ProfileData` shapes
- [ ] Define the action response-envelope record; reuse the `{errors:{_global,key}}` shape
- [ ] `SchemaAppService` (`GetSchemaAsync`/`GetDataAsync`/`SubmitAsync`) via the named
      `"api"` HttpClient on relative `http/...` paths
- [ ] Defensive degrade on unparseable schema/data (mirror `ProfileService.GetSchemaAsync`)
- [ ] Unit tests: envelope (de)serialization; error-envelope binding; malformed inputs

### Phase 2: Generic Renderers
- [ ] `SchemaViewRenderer.razor` — render `view` docs (keyvalue/markdown/image/table/
      divider); subsume the profile read-only renderer
- [ ] `SchemaFormRenderer.razor` — render `form` docs; extend the
      `DynamicConfig.razor:346-429` control switch with select/radio/multiselect/slider/
      date/textarea/mstring
- [ ] Reuse change tracking (`RecalculateChanges`), sticky save bar, structured-error bind
- [ ] `mstring` fields render via the markup pipeline (`WrapAsHtmlClass`)
- [ ] Multi-page navigation affordances (next/prev) — progression driven by softcode
      (partial submit + returned `schema`), not client predicates
- [ ] Wrap renders in `WidgetErrorBoundary`
- [ ] bUnit tests: each field type renders the right control; a `view` renders values by
      key; an error envelope binds `_global`→snackbar and keyed→field; a returned `schema`
      replaces the document

### Phase 3: Actions & Softcode-Driven Progression
- [ ] Button/submit dispatch → `SchemaAppService.SubmitAsync` → `/http` POST
- [ ] `on_success`: `navigate`, `toast`, `merge_fields`; returned `schema` replaces doc
- [ ] `on_error`: `bind_field_errors`
- [ ] `triggers_action` on a field (partial submit)
- [ ] Integration test: a partial submit returns a replacement schema and the renderer
      re-renders (the class-branch chargen scenario)

### Phase 4: Application Registry (server, DB-backed)
- [ ] `RegisteredApplication` model (id, slug, displayName, icon, kind, schemaUrl, dataUrl,
      submitRoute, allowedRoles, navPlacement, zones, order)
- [ ] Persist via `ISharpDatabase` — **all three providers** (Arango/Memgraph/Surreal) at
      parity, per-provider integration tests (mirror the Area-20 `sys_*` pattern)
- [ ] `ApplicationsController` (`/api/applications`, `[Authorize]` Wizard+ per 10.3): list,
      get, create, update, delete
- [ ] Validate `schemaUrl` returns parseable JSON before save
- [ ] Tests: CRUD on each backend; role gate rejects < Wizard; bad schemaUrl rejected

### Phase 5: Full-Page Applications
- [ ] `/apps/{slug}` page — resolve registry entry, pick renderer by `kind`, role-gate
- [ ] Surface nav entries from the registry in `NavMenu.razor`, role-gated
- [ ] 404 for unknown slug; 403 (or hide) for insufficient role
- [ ] bUnit/integration tests: routing, role gating, renderer selection

### Phase 6: Dynamic Widget
- [ ] `SchemaWidgetDescriptor`/`SchemaWidget` registered in `Program.cs` (config:
      `{schemaUrl, dataUrl}`); no changes to the widget core
- [ ] `kind: Widget` applications appear in the `/admin/layout` palette
- [ ] Tests: widget renders a `view` from config; appears in palette; error-bounded

### Phase 7: Admin Registration UI
- [ ] `/admin/applications` — list/create/edit/delete registered applications
- [ ] Form: name, icon, kind, schema/data/submit endpoints, allowed roles, placement
- [ ] Live "validate schema endpoint" check before save (reuses Phase 4 validation)
- [ ] Tests: create flow, validation errors surfaced

### Phase 8: Example Package
- [ ] `chargen` example package (`examples/packages/`) — attach-mode attributes on the
      HTTP handler: `GET`CHARGEN`SCHEMA`, `GET`CHARGEN`, `POST`CHARGEN`SUBMIT`,
      `POST`CHARGEN`ROLL`, `POST`CHARGEN`NEXT`; reusable `FN`*` schema helpers
- [ ] Validate via `ExamplePackageTests`; schema round-trips through `FunctionParse`
      (≥10-arg `ArgumentsOrdered` ordering holds, à la
      `SeededProfileSchema_EvaluatesToValidJson`)

### Phase 9: User-Facing Documentation (with screenshots)
- [ ] Admin guide: registering an Application, the two-part setup (install softcode →
      register), role gating, nav/zone placement — `docs/guides/dynamic-applications-admin.md`
- [ ] Softcode author guide: the schema vocabulary, the action response envelope, the
      `FN`*` helper idiom, the `ArgumentsOrdered` footgun, softcode-driven progression —
      `docs/guides/dynamic-applications-authoring.md`
- [ ] Worked walkthrough: build the D&D char-gen wizard end-to-end
- [ ] **Screenshots** of: the `/admin/applications` registration form, a rendered
      multi-page form, a rendered view, an inline validation error, the layout-palette
      entry for a `kind: Widget` app. (Captured once Phases 5–7 run; placeholders until
      then — see the guide stubs.)
- [ ] Link the guides from the design doc and the portal in-app help index

## Testing (summary)
- [ ] Schema (de)serialization + error-envelope binding (Phase 1)
- [ ] Renderer control coverage + returned-schema re-render (Phases 2–3)
- [ ] Registry CRUD + role gate on all three providers (Phase 4)
- [ ] Routing/role gating for `/apps/{slug}` (Phase 5)
- [ ] Dynamic widget render + palette (Phase 6)
- [ ] Example package validates + schema round-trips through the parser (Phase 8)

## Non-Goals (v1)
- [ ] (Not doing) Client-side branching predicates — progression is softcode-driven
- [ ] (Not doing) SignalR/`command` transport — HTTP only (field reserved)
- [ ] (Not doing) In-portal schema authoring/editing — schemas are softcode
- [ ] (Not doing) Per-user persistence of in-progress form state

## Deferred to v2
- [ ] `command` transport (fire-and-forget into the live session)
- [ ] Per-page/per-user layout overrides for dynamic widgets
- [ ] Richer display elements (charts, tabs, repeatable field groups)
