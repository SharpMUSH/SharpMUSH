# SharpMUSH remote code tools: scope and proposed design

Status: planning proposal for owner review, 2026-10-11. Local repairs are implemented in the isolated checkout; the architecture below is not implemented or approved for deployment.

## Intended journey

An owner runs SharpMUSH in Docker behind their existing HTTPS proxy. A builder connects an editor or MCP client to that particular game, authenticates as a permitted character, and receives diagnostics and intelligence using the game's active catalog. Editing local documents does not evaluate code or mutate game attributes. Offline stdio analysis remains a useful, explicitly identified fallback. Later, a builder can pull an authorized attribute into a file and push an explicit edit with conflict protection.

## Boundary decisions recommended for the first release

Keep syntax analysis and catalog contracts in reusable libraries, the live analyzer and world identity in the host, and authentication/permissions in existing host services. Ship MCP and a future remote LSP as separate optional plugins using the existing plugin loader and endpoint contributor contracts. Keep the current MCP route, tool names, authentication semantics and disabled-by-default production setting compatible. Extract MCP before adding a second transport.

Use a bundled VS Code MCP adapter as the first remote editor slice. It calls the existing analysis tools over HTTPS and supplies native editor diagnostics, hover, completion and function signature help. This is an editor adapter, not an LSP server, and does not provide unsupported LSP features. Add remote LSP over authenticated WebSocket plus a packaged stdio bridge only when full LSP interoperability is required. Both transports consume the same analysis/catalog boundary.

Treat transport document sessions as bounded, principal-owned temporary data. Do not grant game mutation merely because a client can analyze code. Future attribute sync uses explicit permissions and an atomic expected-revision write through the existing service/Mediator/store chain.

## Acceptance boundary

The first remote release succeeds when a packaged editor connects through a disposable HTTPS proxy to a disposable game, reports valid/invalid code correctly, offers a live built-in and plugin-defined catalog, keeps two characters' documents isolated, handles reconnect and changed document versions, and leaves the game unchanged. The operator can disable the plugin at boot and the game/offline LSP still starts. No live-world fixture, public listener, system package install, trust-store change or persistent test credentials are required.

All five existing file extensions and their current parse-mode mappings must be read from the existing LSP configuration and tested; do not invent a sixth dialect. Formatting is offered only after literal-preservation tests pass. Textual rename is not advertised as a safe remote refactor.

## Deferred scope

No executing code via editor/MCP, generic arbitrary filesystem access, hot plugin unloading, OAuth rollout, portal redesign, world migration or automatic bidirectional sync in the first slice. The plan records prerequisites for full LSP, catalog refresh and attribute sync rather than silently including them in the initial release.

## Evidence

The task-root BASELINE.md, REMOTE-CONNECTIONS.md and PLUGIN-ASSESSMENT.md describe inspected source and completed checks. Current local stdio harness: 42/42. Current affected tests: metadata 3/3, standalone parser 1/1, analysis 50/50, LSP handlers 21/21. Earlier baseline MCP endpoint tests: 12/12 and full integration: 696/696; those older runs are not post-refactor or post-metadata evidence. No remote editor or remote LSP has been demonstrated.
