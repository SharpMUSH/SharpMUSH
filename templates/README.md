# SharpMUSH plugin templates

Starter scaffolds for authoring [SharpMUSH](https://github.com/SharpMUSH/SharpMUSH)
plugins of all three kinds. Each is delivered **both** as a "Use this template"
GitHub-template directory **and** as a `dotnet new` template, so you can scaffold a
fresh, ready-to-fill repo either way.

SharpMUSH extends at three layers (see the
[extensibility overview](../docs/design/extensibility-overview.md)); these templates
cover the two runtime layers plus the portal:

| Template | `dotnet new` short-name | Kind | What it is |
|---|---|---|---|
| [`plugin-softcode/`](plugin-softcode/) | `sharpmush-softcode` | `kind: softcode` | A YAML package of objects + attributes + a global `@function`. Game **policy**, no C#, no recompile. |
| [`plugin-application/`](plugin-application/) | `sharpmush-application` | `kind: application` (+ softcode routes) | A Dynamic Application (Area 21): softcode schema/submit routes plus a portal page registration. |
| [`plugin-dll/`](plugin-dll/) | `sharpmush-plugin` | C# DLL (`kind: managed`) | A compiled `net10.0` plugin: `[SharpPlugin] : PluginBase` with a command + function, distributed as a managed package. |

Authoring background:

- Plugin model & load semantics: [`docs/design/plugin-system.md`](../docs/design/plugin-system.md)
- Worked author guide: [`docs/guides/writing-a-plugin.md`](../docs/guides/writing-a-plugin.md)
- Where each layer fits: [`docs/design/extensibility-overview.md`](../docs/design/extensibility-overview.md)
- The `package.yaml` manifest reference: [`examples/packages/README.md`](../examples/packages/README.md)

## Use them via `dotnet new`

Install one (or all) of the templates from a local checkout, then scaffold:

```bash
# Install (point at the template directory containing .template.config/):
dotnet new install ./templates/plugin-softcode
dotnet new install ./templates/plugin-application
dotnet new install ./templates/plugin-dll

# Scaffold a softcode package:
dotnet new sharpmush-softcode -n my-package \
  --Author "Ada Lovelace" --Description "My first package" --Owner ada

# Scaffold a Dynamic Application (routes + portal page):
dotnet new sharpmush-application -n my-app \
  --Author "Ada Lovelace" --Owner ada

# Scaffold a C# DLL plugin (-n sets the assembly name; pass the id + namespace too):
dotnet new sharpmush-plugin -n MyPlugin \
  --PluginNamespace MyPlugin --PluginId my-plugin \
  --PluginCmd MYPLUGIN --PluginFn myplugin_add \
  --Author "Ada Lovelace" --Owner ada
```

`-n` sets the directory name and the primary identifier (the **package id** for
softcode, the **application slug** for application, the **assembly/DLL name** for the
DLL plugin). For the DLL plugin, also pass `--PluginNamespace` (the C# namespace,
usually the same as `-n`), `--PluginId` (the lowercase-hyphen plugin id / `plugins/`
directory), and the sample `--PluginCmd` / `--PluginFn` names. Run
`dotnet new <short-name> --help` to see every parameter and its default.

To uninstall a template: `dotnet new uninstall ./templates/plugin-softcode`.

> **Install one at a time.** Each template directory carries its own
> `.template.config/template.json`; install the specific template directory, not the
> `templates/` root. (A `dotnet new install ./templates` would try to read the whole
> tree as one pack.)

## Use them via GitHub "Use this template"

Each template directory is also a self-contained repo skeleton. To publish one of
these as a GitHub template repo:

1. Copy the chosen `templates/plugin-*/` directory into a new repository (drop the
   `.template.config/` folder — it is only for `dotnet new`).
2. In the new repo's **Settings → General**, tick **Template repository**.
3. Authors then click **Use this template → Create a new repository**, then
   find-and-replace the placeholder tokens by hand. The tokens per template:
   - **softcode**: `PACKAGE_ID`, `AUTHOR_NAME`, `PACKAGE_DESCRIPTION`, `OWNER`
   - **application**: `PACKAGE_ID`, `PACKAGE_UID` (uppercase id, for route attr paths),
     `AUTHOR_NAME`, `PACKAGE_DESCRIPTION`, `OWNER`
   - **DLL**: `PLUGIN_NAME` (assembly), `PLUGIN_NAMESPACE`, `PLUGIN_ID`,
     `PLUGINCMDTOKEN` (sample command), `PLUGINFNTOKEN` (sample function),
     `AUTHOR_NAME`, `PACKAGE_DESCRIPTION`, `OWNER`

The `dotnet new` route does this substitution automatically; the GitHub-template
route is a manual find-and-replace of the same tokens.

## After scaffolding

- **Softcode / application**: fill in `package.yaml`, then validate locally and let
  the bundled `validate.yml` CI check it on push. Release as a git tag
  `<package-dir>/v<semver>`.
- **DLL**: the project references the `SharpMUSH.Plugin.Abstractions` + generator
  NuGets (being produced in parallel — placeholder versions). Until they publish,
  build against an in-repo SharpMUSH checkout via the commented `ProjectReference`
  block in the `.csproj`. The `build-and-release.yml` CI does a deterministic build,
  computes the SHA-256 hashes, rewrites the managed `package.yaml`, and cuts a release.

Each template's own README has the full per-kind walkthrough.
