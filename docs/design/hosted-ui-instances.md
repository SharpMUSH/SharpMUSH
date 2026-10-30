# Plain UI definitions and application instances

An application's identity is its unique slug and `/apps/{slug}` route. A Hosted page also carries a `DefinitionId`, a positive `ConfigVersion`, and its own cloned JSON object `Config`. Several slugs can use one UI definition; changing one instance's config changes neither the definition nor another instance. Schema and compiled-component applications leave these fields empty.

A `kind: ui` package installs a versioned `ui.json` definition plus hash-verified public browser assets through the package upload/plan/apply/uninstall workflow. It contributes no C# assembly and creates no game objects. A definition is installed once; administrators create and edit independent application instances through the application registry and editor. Instances are separate records, not repeated package installs. Removing a definition is refused while an instance uses it.

The .NET/Razor host mounts a plain HTML application in its own sandboxed frame and hands it the authenticated instance configuration over a per-mount bridge. State is mount-local only. The bridge exposes bounded topology reads and one write: importing a room document, which the host shows to the player and makes only once they confirm it there, outside the frame. Assets contain no instance config; anonymous application metadata omits config. The usual auth schemes, policies and engine permissions apply, and the browser-code and owner-install gates are the same ones other packages use.

The graph view takes an explicit room-dbref selection and includes disconnected rooms. Exit dbrefs identify directed edges; room names are labels. Room positions are read from ordinary attributes (X, Y and Z unless the instance names others), the ones the `gmcp-room` package sends to a mapper; there are no map IDs, spatial indexes, virtual locations or generation mechanisms. The reader never evaluates destination softcode. A config schema or version an instance does not match is reported as a conflict, never silently reset.

The bridge uses a verified `srcdoc` document with a port the child document creates, so a navigation or a replaced document cannot inherit the frame's bootstrap or replies. A forced uninstall keeps instance configs when deleting the assets fails. No persistent application-state contract exists.

See `examples/packages/builder-graph/README.md` for the package and config contract, example instances and limits.
