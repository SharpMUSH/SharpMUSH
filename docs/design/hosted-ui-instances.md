# Plain UI definitions and application instances — local first slice

Layouts already allow repeated widget placements with independently stored JSON `Config` values. Applications previously used `RegisteredApplication.Slug` as their sole identity, with no reusable definition/config distinction.

This change keeps the existing unique slug and `/apps/{slug}` route as the instance identity. A Hosted page adds a `DefinitionId`, a positive `ConfigVersion`, and an independently cloned JSON object `Config`. Multiple slugs can reference one UI definition; changing one config does not alter the definition or another instance. The optional fields preserve existing schema and compiled-component applications and old stored records.

A `kind: ui` package installs a versioned `ui.json` definition plus hash-verified public browser assets through the existing package upload/plan/apply/uninstall workflow. It contributes no arbitrary C# assembly and creates no game objects. A definition is installed once; administrators create/edit independent application instances through the existing application registry and editor. Instances are separate records rather than repeated package installs. Removing a definition is guarded against live instance references.

The reusable .NET/Razor host mounts a plain HTML application in an independent frame and hands it the authenticated instance configuration over a per-mount bridge. State is mount-local only. The initial bridge exposes bounded read-only topology. Assets contain no instance config; anonymous application metadata also omits config. All existing auth schemes, policies and engine permissions remain in place. Browser-code and owner-install gates are reused.

The graph proof takes an explicit room-dbref selection and includes disconnected rooms. Exit dbrefs identify directed edges; room names are labels. Room positions are read from ordinary attributes (X, Y and Z unless the instance names others), the ones the `gmcp-room` package sends to a mapper; there are no map IDs, spatial indexes, virtual locations or generation mechanisms. The reader never evaluates dynamic destination softcode. Config schema/version incompatibility is explicit rather than an implicit reset or migration.

See `examples/packages/builder-graph/README.md` for the exact initial package/config contract, example instances, limits and local verification commands. This is a local implementation, not a deployed or installed package.

## Bounded review evidence

Independent read-only review found and verified fixes for navigation inheriting frame bootstrap/replies and forced uninstall losing configs when asset deletion failed. The bridge now uses verified `srcdoc` with a child-created document-bound port. The Chromium proof includes a redirect before first load and a replacement document while an old topology response is pending. The production authentication TestServer smoke uses disposable memory-only tokens and synthetic backing services; it does not prove real database-backed login/world integration. No persistent application-state contract is added.
