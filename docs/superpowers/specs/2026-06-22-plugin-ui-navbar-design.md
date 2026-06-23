# Plugin UI Contribution + Data-Driven NavBar Sections

**Date:** 2026-06-22
**Status:** Approved (design); implementing.
**Scope:** Plugin framework (new `IApplicationSource` seam) + Server (registry overlay) + Client (NavMenu sections). Reuses Area 21 (Dynamic Applications) and the Phase 9 controller seam.

## Principle

A plugin contributing UI = a plugin contributing **Area 21 `RegisteredApplication`(s)** *and* serving their
schema/data from its **own controller** (Phase 9 `AddControllers().AddApplicationPart(...)`). The client is
unchanged in spirit: the existing catalog (`/api/applications`), `DynamicApplication.razor` (`/apps/{slug}`),
and nav rendering consume it. Schema-driven only — **no browser-loaded plugin assemblies.**

## 1. Seam — `IApplicationSource`

New generic seam in `SharpMUSH.Library.Plugins` (alongside `IFlagSource`/`IMigrationSource`/etc.):

```csharp
public interface IApplicationSource
{
    IEnumerable<RegisteredApplication> GetApplications();
}
```

- `RegisteredApplication` lives in `SharpMUSH.Library.Models.Portal.Applications` (host-shared) — a general
  portal type, not a plugin-specific contract, so it does **not** violate the Phase-10 rule.
- `PluginCatalog` collects implementers into an `ApplicationSources` bucket (mirror the existing `I*Source`
  collection + the `IsUnloadablePlugin` treatment — contributing apps is a load-once seam).

## 2. In-memory registry overlay

Plugin apps are a **read-only overlay** present only while the plugin is loaded.
- A thin decorator over `IApplicationRegistryService` (or the impl) merges the catalog's plugin apps into
  `GetApplicationsAsync()` / `GetApplicationAsync(slug)`: DB-backed admin apps ∪ plugin-contributed apps.
- Plugin apps are **not** persisted, **not** admin-editable, and `UpsertApplicationAsync`/`RemoveApplicationAsync`
  ignore plugin-owned slugs (or surface a clear error). Slug collisions: DB/admin wins, or log + skip — pick
  one and make it explicit (recommend: built-in/DB wins, plugin overlay skipped with a warning).
- `/api/applications` returns the merged set unchanged in shape.

## 3. Data-driven NavBar sections

`NavPlacement` (already on `RegisteredApplication`/`PortalApplication`) names a **section**.
- Today `NavMenu.razor` renders `<ApplicationNavLinks/>` only inside the hardcoded **Build** group.
- Change: render each accessible app's link under the section its `NavPlacement` names.
  - Built-in name (`Play`/`World`/`Build`/`Manage`) → slots into that existing group.
  - A **new** name → becomes its own data-driven group in the sidebar, ordered by `Order` (section order =
    min `Order` of its apps; ties broken by section name).
- The four built-in groups stay in code; plugin/app links + any new sections merge in. Access filtered by
  `MinimumRole` (already done in `ApplicationNavLinks`).

## 4. Lifecycle

Plugin load → apps overlay the registry → nav links + `/apps/{slug}` appear, schema/data served by the
plugin's controller. Unload → overlay apps removed → nav links gone. The client just re-reads
`/api/applications`. No DB writes, no orphaned rows.

## 5. End-to-end shape for a UI plugin

A plugin ships: (a) `IApplicationSource` returning its `RegisteredApplication`(s) with `SchemaUrl`/`DataUrl`
pointing at its own routes; (b) a controller (via `AddApplicationPart`) serving those schema/data endpoints;
(c) optionally a `NavPlacement` naming a new section. All inside the plugin assembly.

## Testing

- **Seam/overlay:** a test plugin's `IApplicationSource` apps appear in `GetApplicationsAsync()` and in
  `/api/applications`, and are absent when the plugin is not loaded/unloaded; admin DB apps still editable;
  slug-collision rule holds.
- **Nav:** built-in `NavPlacement` slots into the correct group; a novel `NavPlacement` forms a new ordered
  section; `MinimumRole` hides inaccessible links (bUnit on `NavMenu`).
- **Wire:** `/apps/{slug}` renders a plugin app via the plugin's schema endpoint.
- Plugin loader/unload tests stay green (the new seam is load-once, like the others).

## Files (indicative)

- New: `SharpMUSH.Library/Plugins/IApplicationSource.cs`; a registry-overlay decorator; a test fixture plugin
  contributing an app.
- Changed: `PluginCatalog` (+ `ApplicationSources`, `IsUnloadablePlugin`); `IApplicationRegistryService` wiring
  / `Startup`; `SharpMUSH.Client/Layout/NavMenu.razor` (+ possibly `ApplicationNavLinks`) for data-driven
  sections.

## Non-goals

- No custom compiled Blazor/WASM plugin components (declarative/schema-driven only — a later phase).
- No change to how DB-backed admin applications are authored.
