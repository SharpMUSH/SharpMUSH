# Custom Compiled Blazor/WASM Plugin Components — Feasibility & Design Proposal

**Date:** 2026-06-22
**Status:** PROPOSAL — needs owner approval before any implementation. No production code in this change.
**Scope:** Feasibility research + candidate designs for letting a plugin ship **custom compiled Blazor/Razor components** into the running `SharpMUSH.Client` (Blazor WASM), ideally hot-loadable/unloadable like the server-side plugins.
**Relationship to shipped work:** This is the deferred "Phase 12" beyond Phase 11. Phase 11 (`IApplicationSource`) gave plugins **declarative/schema-driven** portal UI with **no browser-loaded plugin assemblies** (`docs/design/plugin-system.md`, `docs/superpowers/specs/2026-06-22-plugin-ui-navbar-design.md`). The question here is the hard piece Phase 11 explicitly deferred: *truly compiled* third-party UI.

---

## TL;DR feasibility verdict

- **Can Blazor WASM on .NET 10 load a truly-third-party component assembly at runtime and render it?** **Yes, partially.** The Mono-WASM runtime can fetch a `.wasm` (Webcil) assembly over the network and load it (`Assembly.Load(bytes)` / `LazyAssemblyLoader`), then resolve a component `Type` via reflection and render it with `<DynamicComponent Type=... />`. This is the documented, working "lazy load an RCL" path ([MS Learn: Lazy load assemblies](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-lazy-load-assemblies?view=aspnetcore-10.0)) and the Oqtane runtime-module pattern ([Oqtane: Assembly Loading in Blazor and .NET Core](https://www.oqtane.org/blog/!/11/assembly-loading-in-blazor-and-net-core)).
- **Can it hot-*unload* that assembly to reclaim memory, like the server's collectible ALC?** **No — not reliably today.** Collectible `AssemblyLoadContext.Unload()` does **not** actually reclaim assemblies in the Mono-WASM runtime; references are not cleared even after GC ([dotnet/runtime #44153](https://github.com/dotnet/runtime/issues/44153)), and `AssemblyLoadContext.Default.LoadFromStream` has been reported to corrupt loaded assemblies over time ([dotnet/runtime #43402](https://github.com/dotnet/runtime/issues/43402)). The browser-side story is **load-and-keep**: an assembly loaded into the WASM runtime stays for the lifetime of the page/tab. "Unload" in the browser realistically means *stop referencing the type and reload the page*, not free the assembly.
- **The real constraints:** runtime loading works **only** in the IL-interpreter configuration with **trimming off** (or with every needed type/assembly preserved). **AOT and aggressive trimming break it.** It also requires shipping/serving the plugin's `.wasm` plus its full dependency closure.

The honest bottom line: **compiled third-party WASM components are loadable and renderable on .NET 10, but they are *not* hot-unloadable, and enabling them constrains the whole client to interpreted + untrimmed.** That is a real cost the owner must weigh against what Phase 11's declarative path already delivers.

---

## How `SharpMUSH.Client` is built and served today (grounding)

Verified against the current branch:

- **Project:** `SharpMUSH.Client/SharpMUSH.Client.csproj` — `Microsoft.NET.Sdk.BlazorWebAssembly`, `net10.0`.
  - **`PublishTrimmed=false`** (already off — the trimming hazard is already avoided, for the FSharp.Core reason noted in the plugin docs).
  - **No `RunAOTCompilation`** → runs on the **IL interpreter** (the configuration that *can* load runtime assemblies).
  - **No `BlazorWebAssemblyLazyLoad`** items and **no** `AssemblyLoadContext` / `LazyAssemblyLoader` / `Assembly.Load` usage anywhere in the client today. All extensibility is static-registration or JSON-schema-driven.
- **Serving:** `SharpMUSH.Server` serves the WASM app as static files — `app.UseStaticFiles()` + `app.MapFallbackToFile("index.html")` (SPA fallback). The server does **not** publish-embed the client; the WASM output is deployed into the server's `wwwroot/`. The server already owns the static `_framework/` payload — so it is the natural place to *also* serve a plugin's `.wasm`.
- **Widget seam (already type-based!):** `IPortalWidget` (`SharpMUSH.Library/Models/Portal/Widgets/IPortalWidget.cs`) declares **`Type ComponentType { get; }`**, and `Components/Layout/ZoneRenderer.razor` already renders widgets with **`<DynamicComponent Type="@descriptor.ComponentType" Parameters="..." />`**. The render-by-`Type` machinery this proposal needs **already exists in the client** — the only missing piece is *getting a third-party `Type` into a descriptor at runtime*.
- **Schema seam (Phase 11 target):** `ApplicationCatalog` loads `/api/applications` at boot; widget-kind apps become synthetic `IPortalWidget`s (`ApplicationPortalWidget`); `SchemaWidget` + `SchemaFormRenderer`/`SchemaViewRenderer` **interpret** a `PortalSchemaDocument` (JSON) into MudBlazor — **no type instantiation, no assembly loading**. This is the "policy is data" path.
- **The boundary rule (Phase 10):** the client compile-references **zero** plugin types. `SharpMUSH.Client/Models/SceneEventMessage.cs` documents the principle verbatim: the plugin loads in a collectible ALC *on the server*, so the client mirrors the **wire shape** as its own DTO; "the boundary is serialization (JSON), not a shared assembly."

So today there are two seams a UI plugin can ride — the **static widget registry** (`Type`-based, build-time) and the **declarative schema** pipeline (JSON, runtime) — and **nothing** that pulls a compiled component over the wire.

---

## What "custom compiled component" would ADD over Phase 11

The declarative pipeline (Area 21 + Phase 11) renders a fixed vocabulary of `SchemaElement` kinds (`field`, `markdown`, `image`, `table`, `keyvalue`, `button`) and form/view layouts. It cannot express: bespoke interactive widgets (a live combat tracker, a custom map/canvas, a drag-reorder editor), third-party JS-interop components, or anything outside the schema vocabulary. A compiled component lets a plugin author **write arbitrary Razor/C#** and have it run in the portal — the full expressive power of Blazor, at the cost of shipping and trusting browser code.

The design tension: the host/client must keep referencing **zero plugin types** (Phase 10), yet a compiled component *is* a plugin type the client must instantiate. The resolution is the same one the widget seam already uses: the client references the **generic** `IPortalWidget`/`DynamicComponent` machinery and the plugin's concrete component is reached **only** as a `System.Type` discovered at runtime — never named in client source. The contract is "a component whose parameters are `[Parameter]`-bound from a JSON/`IDictionary` bag," which is a generic seam, not a plugin-specific reference.

---

## The hard facts (cited)

1. **Lazy load is build-time-marked but route-driven.** `BlazorWebAssemblyLazyLoad` marks an assembly to be *withheld from startup* and fetched on demand; `LazyAssemblyLoader.LoadAssembliesAsync(...)` "uses JS interop to fetch assemblies via a network call" and "loads assemblies into the runtime." The canonical example lazy-loads an **RCL referenced by the project** and feeds it to the `Router`'s `AdditionalAssemblies` ([MS Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-lazy-load-assemblies?view=aspnetcore-10.0)). This path assumes the assembly **is known at build time** (it's a project reference). It is *deferred* loading, not *unknown-at-build-time* loading.
2. **Truly-unknown-at-build-time loading is possible but lower-level.** Oqtane downloads a module DLL as a byte array and calls `Assembly.Load(bytes)`, then uses reflection to find types ([Oqtane](https://www.oqtane.org/blog/!/11/assembly-loading-in-blazor-and-net-core)). This does **not** require a project reference — it's the genuinely-third-party path. Caveat from the same source: *"if the assembly you are loading has any dependencies which do not yet exist on the client then the load operation will fail. So you need to implement a mechanism for informing the client about an assembly's dependencies so that they can all be downloaded in the appropriate order."*
3. **Render-by-Type is first-class.** `<DynamicComponent Type=... Parameters=... />` renders a component specified by `Type` with an `IDictionary<string,object>` parameter bag ([MS Learn: DynamicComponent](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/dynamiccomponent?view=aspnetcore-10.0)). After `Assembly.Load`, `assembly.GetType(name)` yields the `Type`; `StateHasChanged()` is needed to render it. **SharpMUSH already does the render-by-Type half** in `ZoneRenderer.razor`.
4. **Unload does not work in the browser.** Collectible-ALC `Unload()` "does not look like it is clearing any references" in Blazor WASM and the `WeakReference` stays alive after GC ([dotnet/runtime #44153](https://github.com/dotnet/runtime/issues/44153)); assemblies loaded via `LoadFromStream` can corrupt over time ([dotnet/runtime #43402](https://github.com/dotnet/runtime/issues/43402)). The Mono-WASM runtime does not reclaim loaded assemblies. **There is no browser equivalent of the server's `WeakReference`-dead unload proof.** Practical "unload" = drop type references + page reload.
5. **AOT / trimming are incompatible with runtime-unknown types.** The MS guidance is explicit that lazy loading "shouldn't be used for core runtime assemblies, which might be trimmed on publish and unavailable on the client." Loading a third-party assembly that calls into trimmed-away framework surface fails at runtime. AOT compiles a *fixed, known* set of assemblies to native WASM; an assembly fetched later runs interpreted and can hit missing AOT/trim-pruned dependencies. **SharpMUSH is already interpreted + untrimmed, so it is currently compatible — but this forecloses ever turning AOT/trim on for the client.**
6. **`.wasm`/Webcil packaging + fingerprinting.** In .NET 8+ assemblies ship as Webcil with a `.wasm` extension; .NET 10 fingerprints names for cache-busting ([MS Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-lazy-load-assemblies?view=aspnetcore-10.0)). A plugin's component assembly must be served in this format and the loader must know its (possibly fingerprinted) URL — i.e. the **server must hand the client a manifest** of plugin UI assembly URLs + dependency order, not just a bare name.

---

## Candidate approaches

### Approach A — Declarative-first: extend the schema vocabulary (no browser assembly)

Stay on Phase 11. Grow the `PortalSchemaDocument` element vocabulary (new `SchemaElement.Kind`s) and the `SchemaFormRenderer`/`SchemaViewRenderer` interpreters in the **first-party client**, so plugins express richer UI as **data** their own controller serves. Optionally add a small set of pre-approved interactive widgets (chart, kanban, timeline) the client ships and a plugin parameterizes by schema.

| Concern | How it works |
|---|---|
| Distribution | None — JSON over the plugin's existing Phase-9 controller. |
| Loading | None — no assembly crosses to the browser. |
| Rendering | The first-party interpreter renders MudBlazor from JSON (as today). |
| Hot reload/unload | Trivial — it's data; the overlay appears/vanishes with the plugin (Phase 11 already does this). |
| Trust | **Zero browser-code trust.** The client only runs first-party code; a hostile plugin can only emit declarative schema. |
| Zero-plugin-types | Fully preserved — the seam is `RegisteredApplication` + JSON. |
| Cost | Plugin authors are boxed into the vocabulary the client ships; novel interactivity needs a **client release**, not a plugin. |

**Verdict:** lowest risk, lowest power. Already the shipped direction. The right default unless a concrete need exceeds the schema.

### Approach B — Build-time-known RCLs, route-lazy-loaded (curated, first-party-ish)

The host treats a **known set** of plugin UI Razor Class Libraries as project references with `BlazorWebAssemblyLazyLoad` items, and the `Router`'s `OnNavigateAsync` lazy-loads a plugin RCL when its `/apps/{slug}` (or `/plugin/{id}/...`) route is hit, adding it to `AdditionalAssemblies`. This is exactly the MS canonical example.

| Concern | How it works |
|---|---|
| Distribution | The RCL ships **with the client build** (project reference) but is withheld until its route is visited. Server serves it from `_framework/`. |
| Loading | `LazyAssemblyLoader.LoadAssembliesAsync` on navigation; `<DynamicComponent>` or routable component renders it. |
| Rendering | Native — full Blazor power for the plugin's components. |
| Hot reload/unload | **No.** The set is fixed at client build; adding a plugin = **rebuild + redeploy the client**. No unload. |
| Trust | The RCLs are vetted at client build time (first-party or owner-curated) — acceptable trust. |
| Zero-plugin-types | **Bends.** The client csproj references the plugin RCLs, contradicting Phase 10's "client references zero plugin types / adding a plugin must not recompile the host." |
| Cost | Defeats the *dynamic* goal — it's deferred startup, not third-party hot-load. |

**Verdict:** real, supported, robust — but it is **curated lazy-loading, not third-party plugins.** Good for *first-party* heavy widgets you want out of the startup bundle; does not meet "ship UI from another repo without rebuilding the client."

### Approach C — Server-served third-party component assemblies, runtime-loaded (the actual goal)

The full dynamic path, mirroring server-side plugin distribution. The server, which already deposits managed plugin DLLs into `plugins/<id>/` (Phase 4) and serves the WASM static payload, additionally **exposes the plugin's UI `.wasm` + dependency closure + a manifest** at a known endpoint (e.g. `/plugin-ui/{id}/manifest.json` → `{ entryAssembly, dependencies[], components[], fingerprintedUrls }`). At runtime the client:

1. Reads `/api/applications` (Phase 11) which now also advertises **compiled** apps/widgets (a `RenderKind: Component` + the plugin id).
2. Fetches the manifest, downloads each `.wasm` (dependencies first), `Assembly.Load(bytes)` each (Oqtane pattern), resolves the named component `Type` by reflection, wraps it in a synthetic `IPortalWidget`/route descriptor, and renders via the **existing** `<DynamicComponent>`.

| Concern | How it works |
|---|---|
| Distribution | Server serves plugin UI `.wasm` + deps + manifest. Versioning/fingerprinting per server; integrity via SHA-256 (reuse Phase 4's hash + trust gate). |
| Loading | `Assembly.Load(bytes)` of the entry assembly + its dependency closure, ordered by the manifest (the Oqtane dependency caveat is mandatory here). |
| Rendering | Reflection → `Type` → `<DynamicComponent>`. Already half-built in `ZoneRenderer`. |
| Hot reload/unload | **Load: yes, at runtime, no client rebuild.** **Unload: no** — Mono-WASM can't reclaim ([#44153](https://github.com/dotnet/runtime/issues/44153)). "Unloading a plugin's UI" = stop rendering it + recommend a page reload; the assembly lingers until the tab closes. Re-loading a *changed* version in the same tab is unsafe (corruption risk, type-identity collisions). |
| Trust | **Maximum.** Third-party code runs in the user's browser with the app's full client privileges (JWT in memory, SignalR, DOM). SHA-256 guards integrity, not safety. There is **no browser sandbox** for the loaded .NET assembly beyond the WASM sandbox the whole app already shares. Must reuse Phase 4's two-part operator trust gate, and arguably be even more conservative (browser code, not just server code). |
| Zero-plugin-types | **Preserved** — the client never compile-references the plugin; it reaches the component only as a runtime `Type` behind the generic `IPortalWidget`/`DynamicComponent` seam, parameters passed as an `IDictionary`/JSON bag. This is the Phase-10 boundary holding for UI. |
| Cost | Forecloses client AOT/trim **forever**; adds a download/dependency-resolution subsystem to the client; "hot-unload" is a lie we must not tell. |

**Verdict:** this is the only approach that actually meets the stated goal (third-party compiled UI, loaded without rebuilding the client). It works for **load**; it does **not** deliver true hot-**unload** parity with the server. It is also the highest-trust, highest-complexity option.

### Approach D (hybrid, recommended) — Declarative by default, opt-in compiled via Approach C, no false unload

Keep **Approach A** as the default and only path most plugins use. Add **Approach C** as an explicitly **operator-gated, off-by-default** capability for plugins that genuinely need compiled UI, with honest lifecycle semantics:

- Reuse the Phase-4 trust posture: a compiled-UI plugin's assemblies are integrity-hashed and only served if the operator opts the plugin into a **`AllowBrowserCode`**-style allow-list (separate from, and stricter than, server `AllowManagedCode`, because this is code in the *user's* browser).
- Advertise compiled UI through the **same** `IApplicationSource`/`/api/applications` overlay as Phase 11, with a `RenderKind` discriminator (`Schema` vs `Component`). The NavBar/`/apps/{slug}` flow is unchanged; only the renderer differs (`SchemaWidget` vs a new `PluginComponentHost` that does the load-and-`DynamicComponent`).
- **Lifecycle honesty:** "enable/disable" a compiled-UI plugin toggles whether the client *renders* it; first enable loads its assemblies; **disable stops rendering but does not unload**, and changing a compiled-UI plugin's version requires a **page reload** to take effect. Document this as a hard limit, not a TODO.

This isolates the irreversible costs (no AOT/trim, browser-trust, no-unload) to the **opt-in** path while the 95% declarative case keeps full safety and true hot-reload.

---

## Recommendation

**Adopt Approach D (hybrid).** Concretely:

1. **Now:** invest in **Approach A** — grow the schema vocabulary / curated interactive widgets — because it covers most real plugin UI needs with zero new trust surface and true hot-reload, and it is the already-shipped, already-safe direction.
2. **Behind an explicit owner decision and an operator opt-in:** design **Approach C** as "Phase 12 — compiled plugin UI," reusing the Phase-11 application overlay and the Phase-4 trust/hash machinery, and **renaming the capability honestly** ("dynamic load, no unload; version change needs reload"). Build it only when a concrete plugin need exceeds the declarative vocabulary.
3. **Do not** pursue **Approach B** as the answer to the dynamic goal — it requires recompiling the client per plugin (violates the dynamic premise), though it remains the correct technique for *first-party* heavy widgets you simply want lazy-loaded.

The pivotal, non-obvious truth to put in front of the owner: **the server's signature feature — hot-unload with a `WeakReference`-dead proof — has no browser counterpart.** WASM plugin UI can be *added* live but not *reclaimed* live. Any design that promises symmetric hot-unload across server and client is promising something the Mono-WASM runtime does not provide today.

---

## Open design questions (for owner / brainstorming)

1. **Is compiled third-party UI actually wanted, or does extending the declarative schema (Approach A) cover the real cases?** What concrete plugin UI can't be expressed as schema today? (If none, build A and stop.)
2. **Is "load but never unload, version-change needs page reload" acceptable** for the portal's UX, given the server side advertises true hot-unload? How do we surface that asymmetry to operators without it reading as a bug?
3. **Trust:** running third-party compiled code in the *user's* browser (with access to the in-memory JWT, SignalR, DOM) is a strictly higher bar than server-side managed code. Do we gate it on a separate, stricter operator opt-in (`AllowBrowserCode`)? Per-plugin? Do we want any CSP / capability fencing, knowing the WASM sandbox is the only real boundary?
4. **AOT/trimming is foreclosed forever for the client if we ship Approach C.** Is the owner willing to permanently give up client-side AOT + trimming (startup size/speed) to allow compiled plugin UI? (Today both are already off, so the *current* cost is zero — but this locks it in.)
5. **Dependency closure & versioning:** how does the server compute and serve a plugin UI assembly's transitive dependency closure (the Oqtane "dependencies must exist first" failure mode), and how do we prevent two plugins shipping conflicting versions of the same dependency into one runtime (no isolation in the browser)?
6. **Where does the compiled component plug in — widget zones, routed `/apps/{slug}` pages, or both?** And what is the **generic parameter contract** (JSON/`IDictionary` bag) that keeps the client from ever naming a plugin type?
7. **Manifest + distribution channel:** reuse the Phase-4 package manager (`kind: managed` carrying UI `.wasm` + hashes) and have the server expose `/plugin-ui/{id}/manifest.json`? Or a separate channel? How does fingerprinting interact with the plugin-served URLs?

---

## Risks

- **No hot-unload (runtime limitation).** Mono-WASM does not reclaim loaded assemblies; collectible-ALC unload is a no-op in the browser ([#44153](https://github.com/dotnet/runtime/issues/44153)). True server-style unload parity is **not achievable**; reloading a changed assembly in-tab risks corruption/type-identity issues ([#43402](https://github.com/dotnet/runtime/issues/43402)).
- **AOT/trimming foreclosed.** Enabling runtime-unknown assembly loading is incompatible with trimming the client and with AOT for anything the plugin touches. Permanently caps client startup-perf options.
- **Browser-code trust.** Third-party compiled code runs with the portal's full client privileges; SHA-256 is integrity not safety; there is no extra sandbox. This is a materially higher trust bar than the server's managed-code gate and must be operator-gated and off by default.
- **Dependency-closure fragility.** Missing/locked transitive dependencies fail the load (Oqtane caveat); two plugins with conflicting dependency versions can collide in the single shared runtime.
- **Versioning / cache.** Fingerprinted Webcil names mean the client must consult a server manifest for URLs; a stale manifest or mismatched fingerprint silently fails to load.
- **Boundary erosion.** Approach B specifically would re-introduce a client→plugin compile reference (violating Phase 10); only Approaches A/C/D keep the zero-plugin-types rule. Any compiled path must enforce the generic-`Type`-only seam to avoid sliding back into first-party coupling.

---

## Sources

- [Lazy load assemblies in ASP.NET Core Blazor WebAssembly (.NET 10) — MS Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-lazy-load-assemblies?view=aspnetcore-10.0)
- [Dynamically-rendered ASP.NET Core Razor components (DynamicComponent) — MS Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/dynamiccomponent?view=aspnetcore-10.0)
- [Oqtane — Assembly Loading in Blazor and .NET Core](https://www.oqtane.org/blog/!/11/assembly-loading-in-blazor-and-net-core)
- [dotnet/runtime #44153 — Blazor Wasm AssemblyLoadContext Unload not working as expected](https://github.com/dotnet/runtime/issues/44153)
- [dotnet/runtime #43402 — Assemblies loaded via AssemblyLoadContext.Default.LoadFromStream corrupt over time](https://github.com/dotnet/runtime/issues/43402)
- [dotnet/aspnetcore #18702 — Clarification about assemblies and unload / reload?](https://github.com/dotnet/aspnetcore/issues/18702)
- In-repo: `docs/design/plugin-system.md` (Phases 9–11), `docs/superpowers/specs/2026-06-22-plugin-ui-navbar-design.md`, `SharpMUSH.Client/SharpMUSH.Client.csproj`, `SharpMUSH.Client/Components/Layout/ZoneRenderer.razor`, `SharpMUSH.Library/Models/Portal/Widgets/IPortalWidget.cs`, `SharpMUSH.Client/Models/SceneEventMessage.cs`.
