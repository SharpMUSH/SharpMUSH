# hello-ui

A worked **UI plugin shipped end-to-end as a plugin package** (Phase 4). It ties together three
seams of the plugin system in a single assembly, then distributes that assembly through the package
manager with SHA-256-verified install.

The plugin source lives at [`examples/plugins/hello-ui/`](../../plugins/hello-ui/); this directory is the
**plugin-package manifest** that carries its compiled DLL.

## The three seams

| Seam | Phase | What `HelloUiPlugin` does |
|------|-------|---------------------------|
| `IServiceRegistrar` | 9 | `services.AddControllers().AddApplicationPart(thisAssembly)` — exposes `HelloUiController` to the host's MVC pipeline so its attribute routes (`api/hello-ui/*`) are served across the plugin's AssemblyLoadContext. |
| `IApplicationSource` | 11 | Returns one full-page Area-21 `RegisteredApplication` (`Slug: "hello-ui"`, `Kind: Page`, `NavPlacement: "Examples"`) whose `SchemaUrl`/`DataUrl` point back at its own controller. The registry overlay (`PluginApplicationRegistryDecorator`) unions it into `/api/applications` while the plugin is loaded. |
| `kind: plugin` package | 4 | This `package.yaml` carries the `.dll` + `.deps.json` + `plugin.json` with their SHA-256 hashes. `PluginPackageInstaller` verifies each byte, checks that `plugin.json`'s id is `hello-ui`, then deposits them into `hello-ui/` in the installed-plugins folder once the administrator confirms the install. |

Because the plugin registers services and contributes UI, it is **load-once**: it is picked up at server
boot, and a clean uninstall takes effect on the next restart. The browser loads **no** plugin code — the
WASM client renders the page generically from the schema JSON the controller returns.

## End-to-end flow

```
build  examples/plugins/hello-ui  ──►  HelloUiPlugin.dll (+ .deps.json, plugin.json)
   │
package  this package.yaml signs each file's SHA-256  (kind: plugin)
   │
install  administrator confirms (allow_plugin_code) ─► verify hashes ─► deposit into <world>.plugins/hello-ui/
   │
boot     PluginLoaderService loads the DLL; PluginCatalog collects its IServiceRegistrar + IApplicationSource
   │
appear   NavBar shows "Hello UI" under a new "Examples" section; the app renders at /apps/hello-ui
   │
render   the page fetches  GET /api/hello-ui/schema  +  GET /api/hello-ui/data  → read-only view
```

## Build → package → install

```bash
# 1. Build the plugin (deterministic Release build).
dotnet build examples/plugins/hello-ui/HelloUiPlugin.csproj -c Release

# 2. Recompute the carried files' hashes and paste them into this package.yaml's `binaries:` block.
#    (The .dll/.deps.json digests are build-environment specific; a deterministic-build CI normally
#    rewrites this block before tagging. The manifest parser validates only the 64-hex SHAPE — the
#    installer verifies the bytes actually match at install time.)
cd examples/plugins/hello-ui/bin/Release/net11.0
sha256sum HelloUiPlugin.dll HelloUiPlugin.deps.json plugin.json

# 3. Install through the package manager: from a package remote, or zip this package.yaml with the three
#    files (all at the top level) and use "Upload plugin" on the portal's Plugins tab. On the review page,
#    tick "I trust this author" (allow_plugin_code = true on the apply request). The installer writes
#    nothing until that is confirmed and every SHA-256 matches. SHARPMUSH_PLUGIN_INSTALL=false on the
#    server refuses plugin packages altogether.
```

After a restart (the Plugins tab's Restart button, or `@shutdown/reboot`), "Hello UI" appears in the NavBar
under **Examples** and the page renders at `/apps/hello-ui`. Uninstalling removes `hello-ui/` from the
installed-plugins folder (and unloads the plugin if it is loaded +
unloadable); the app vanishes from the NavBar — no orphaned rows, because plugin apps are an in-memory
overlay that is never persisted.

## The schema JSON the client renders

`GET /api/hello-ui/schema` returns a read-only Area-21 **Portal Schema Document** (snake_case; `kind: "view"`):

```jsonc
{
  "kind": "view",
  "schema_version": 1,
  "title": "Hello UI",
  "pages": [
    {
      "key": "p1", "title": "Welcome", "order": 1,
      "sections": [
        {
          "name": "About this example", "order": 1, "columns": 1,
          "elements": [
            { "kind": "markdown", "value": "This page is served **entirely by a managed plugin DLL**. ..." },
            { "kind": "field", "key": "greeting",   "label": "Greeting",  "type": "text" },
            { "kind": "field", "key": "served_by",  "label": "Served by", "type": "text" },
            { "kind": "table", "label": "Plugin seams demonstrated", "rows_field": "seams",
              "columns": [ { "key": "seam", "label": "Seam" }, { "key": "purpose", "label": "Purpose" } ] }
          ]
        }
      ]
    }
  ]
}
```

`GET /api/hello-ui/data` returns the values the view binds to — each field is `{ value, visible }`, and the
table's rows are a JSON array under the key the table element's `rows_field` names:

```jsonc
{
  "fields": {
    "greeting":  { "value": "Hello from the hello-ui managed package!", "visible": true },
    "served_by": { "value": "HelloUiController (api/hello-ui/data)",     "visible": true },
    "seams": {
      "value": [
        { "seam": "IServiceRegistrar",     "purpose": "AddControllers().AddApplicationPart(thisAssembly)" },
        { "seam": "IApplicationSource",    "purpose": "Contributes the /apps/hello-ui page + NavBar entry" },
        { "seam": "kind: plugin package", "purpose": "Distributes the DLL with a SHA-256-verified install" }
      ],
      "visible": true
    }
  }
}
```

## Note on trust

A plugin package distributes arbitrary compiled C# that, once loaded, runs in **full server trust** —
there is no sandbox, exactly as for a plugin dropped into a plugins folder by hand. SHA-256 verification
guards **integrity** (the bytes are what this manifest committed to), not trust. Whoever runs the server and
installs the DLL takes responsibility for it: each install needs the administrator's `allow_plugin_code`
confirmation, and `SHARPMUSH_PLUGIN_INSTALL=false` turns plugin installs off for the whole server.
