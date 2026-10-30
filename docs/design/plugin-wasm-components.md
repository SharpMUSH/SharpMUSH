# Compiled plugin components in the portal

A portal application is declarative by default: the server describes it as a schema and the WASM
client renders it with its generic widgets. A plugin may instead ship a **compiled Blazor
component**, which the running client loads at runtime. This is off unless the operator turns on
`allow_browser_code`.

The host and client reference no plugin types. A component is loaded and rendered by its `Type`
name through reflection and `<DynamicComponent>`, and talks to the server over HTTP and SignalR.

## Descriptor

`RegisteredApplication.RenderKind` is `"Schema"` (the default) or `"Component"`. A `Component`
application also carries `ComponentAssemblyUrl` (where the client fetches the assembly bytes) and
`ComponentTypeName` (the full name of the type to render). Schema applications ignore both.

## Trust gate and serving

- With `allow_browser_code` off, the registry overlay leaves `Component` applications out of
  `/api/applications`, `GET /api/plugins/{pluginId}/ui/{assembly}` answers 404, and the client's
  loader does nothing.
- With it on, `PluginsUiController` serves the plugin's UI assembly from its managed package after
  checking the bytes against the package's install-time SHA-256. An unknown, unlisted or altered
  assembly answers 404.

## Client loader

`PluginComponentLoader` fetches an assembly once, `Assembly.Load`s it, resolves the type and hands it
to `<DynamicComponent>`. `DynamicApplication.razor` chooses the schema renderer or the loader by
`RenderKind`. Loaded assemblies are cached and never unloaded: Mono-WASM cannot unload an
`AssemblyLoadContext`.

## Unloading a plugin reloads the browser

When the server unloads a plugin it sends the generic `ReceivePluginsChanged` hub message to every
connected client; `ConnectionStateService` raises `OnPluginsChanged` and the portal shell reloads the
page. A full reload rebuilds the WASM
runtime, drops any loaded plugin assembly and reads `/api/applications` again. Loading a new plugin
does not reload: the client reads the catalog again and loads components when they are first shown.

## Trimming

The client is IL-trimmed. `SharpMUSH.Client.csproj` roots the surface a plugin component compiles
against (`TrimmerRootAssembly`: the component-model assemblies, JSInterop, MudBlazor, Contracts and the
MarkupString assemblies), so trimming never removes members a runtime-loaded component uses. The BCL stays
trimmed; a component that reflects into an otherwise unused BCL member can fail, and that risk is
accepted. The client is not AOT-compiled.
