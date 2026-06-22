# Plugin Compiled WASM UI Components (Hybrid) + Forced Browser Refresh on Unload

**Date:** 2026-06-22
**Status:** Approved (design); implementing.
**Scope:** Library (descriptor + config) + Server (unload broadcast + UI-assembly serving) + Client (reload handler + component loader). Builds on Phase 11 (`IApplicationSource` + registry overlay) and Phase 4 (managed-package hash/trust).
**Owner decisions (settled):** accept giving up client AOT/trimming; accept the browser-trust bar (gated, off-by-default); **force a browser refresh whenever a plugin DLL is unloaded.**

## Principle

Keep declarative/schema-driven UI (Phase 11) as the default. Add **compiled Blazor components shipped by a
plugin**, loaded into the running WASM client at runtime, as an **operator-gated, off-by-default** capability.
In-browser hot-*unload* is impossible (Mono-WASM `AssemblyLoadContext.Unload()` is a no-op), so "unload" is
handled by a **forced page refresh** — which fully tears down and rebuilds the WASM runtime. The host/client
still reference **zero** plugin types (Phase 10): components are loaded + rendered by `Type` via reflection +
`<DynamicComponent>`, and communicate over HTTP/SignalR (serialization).

## 1. Forced browser refresh on plugin unload (priority; also covers declarative)

- When the server unloads a plugin (`PluginManager.UnloadAsync` / equivalent), it **broadcasts a generic
  "plugins changed" signal** to connected clients over SignalR — a generic `IGameHubClient` method (e.g.
  `ReceivePluginsChanged()`), NOT plugin-specific.
- The client handler forces a hard reload: `NavigationManager.NavigateTo(currentUri, forceLoad: true)` (or JS
  `location.reload()`), ideally after a brief toast ("a plugin changed — reloading…"). A hard reload reclaims
  any lingering loaded assemblies and re-fetches `/api/applications`.
- Generic and always-on: any plugin unload triggers it, declarative or compiled. (Load of a new plugin does
  **not** force a reload — the client just re-reads the catalog and lazy-loads on next navigation.)

## 2. Component descriptor — `RenderKind` discriminator

Extend `RegisteredApplication` (Library, general portal type — not a plugin contract):
- `RenderKind`: `"Schema"` (default, Phase 11) | `"Component"`.
- For `Component`: `ComponentAssemblyUrl` (where the client fetches the `.wasm`) + `ComponentTypeName`
  (the full Type name to render via `<DynamicComponent>`). Schema apps ignore these.

## 3. Trust gate + serving

- **Gate:** an off-by-default config option `allow_browser_code` (DatabaseOptions/SharpMUSHOptions). The
  registry overlay **omits `Component`-kind apps** from `/api/applications` when the gate is off; the client
  also refuses to load components when off (defense in depth).
- **Serving:** a server endpoint serves the plugin's UI assembly bytes (e.g. `GET /api/plugins/{pluginId}/ui/{assembly}`),
  reusing the **Phase-4 SHA-256 hash** from the managed-package manifest to verify the bytes before serving
  (or at install time). The plugin's `.wasm` UI assembly travels in its managed package like its other binaries.

## 4. Client component loader

- A `PluginComponentLoader` service: given `ComponentAssemblyUrl` + `ComponentTypeName`, fetch the bytes once,
  `Assembly.Load(bytes)`, resolve the `Type`, and hand it to `<DynamicComponent Type="..." />`. **Cache** loaded
  assemblies (no unload — they linger until the next refresh, by design). Gate-aware (no-op when `allow_browser_code`
  is off).
- `DynamicApplication.razor` (and/or the zone renderer) branches on `RenderKind`: `Schema` → existing
  `SchemaWidget`/generic renderer; `Component` → `PluginComponentLoader` + `<DynamicComponent>`.

## Testing

- **Forced refresh:** unit — `PluginManager` unload broadcasts `ReceivePluginsChanged` (mock `IHubContext`);
  bUnit/client — the handler triggers a `forceLoad` reload (mock JS/NavigationManager). Loader/unload tests stay green.
- **Gate:** `Component`-kind apps are absent from `/api/applications` when `allow_browser_code` is off, present when on.
- **Serving:** the UI-assembly endpoint returns the verified bytes and 404s an unknown/unverified assembly.
- **Component render:** bUnit — `PluginComponentLoader` resolves a known Type from a test assembly and renders it
  via `<DynamicComponent>` (the in-browser Mono `Assembly.Load` path itself is runtime-only — test what is
  testable in bUnit and document the runtime-only seam honestly).

## Non-goals

- In-browser hot-*unload* (impossible — handled by forced refresh).
- Making compiled components the default (declarative stays default; compiled is gated/off-by-default).
- Client AOT/trimming (explicitly given up to allow runtime assembly loading).
