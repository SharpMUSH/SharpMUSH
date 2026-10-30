# Remote analysis contract

The contract for remote LSP and MCP analysis. Both are optional plugins, off until configured.

## Server setup

Publish `SharpMUSH.Server` normally. Shipped optional plugins are `plugins/mcp/` and `plugins/lsp/`.
Keep each complete directory (entry DLL, manifest, dependency manifest and private SDK closure).
Both modules are boot-time and independently removable/disabled; configuration defaults to false.
Removing/disabling a module or setting Enabled=false yields 404 at its reserved path, not SPA HTML.

```json
{
  "Mcp": { "Enabled": false, "Path": "/mcp" },
  "Lsp": { "Enabled": false, "Path": "/lsp" },
  "Analysis": { "MaxConcurrentAnalyses": 2, "MaxQueuedAnalyses": 8, "AnalysisTimeout": "00:00:05" },
  "AnalysisTransports": { "AllowedOrigins": [] }
}
```

Host `MushBasic` authenticates character name/password. Host `MushAnalysis` policy requires that
scheme and validates any Origin against the exact HTTP(S) allowlist. No Origin is required for native
clients; provided Origin is denied by default. No wildcard/request-Host-derived origin trust. The IP
limiter runs before named authorization, including rejected credentials. Character incarnation,
account ownership/active state and password version are refreshed before analysis. Analysis and
metadata never execute code or expose attribute values. Settings/restart/deployment need operator
approval independently of local source work.

`Analysis:*` governs one cooperative work pool shared by both transports. For compatibility,
`Mcp:MaxConcurrentAnalyses`, `Mcp:MaxQueuedAnalyses`, `Mcp:AnalysisTimeout` are fallback values;
explicit `Analysis:*` overrides them. Deadlines include queue time and metadata resolution. Work
retains its slot until actual termination; these are cooperative deadlines, not hard preemption.

MCP stays on ModelContextProtocol SDK **2.2.0**, stateless Streamable HTTP at `/mcp`. Its SDK supports
2024-11-05, 2025-03-26, 2025-06-18, 2025-11-25 and 2026-07-28; the latter uses server/discover and
per-request metadata, while older revisions use initialize. Clients can pin their own supported revision.
Those lifecycle/cancellation semantics remain SDK-owned and are not shared with the LSP dispatcher.
Explicit 2025-06-18, 2025-11-25 and 2026-07-28 lifecycle/tool-list tests pass; 2025-11-25 also
checks notification HTTP202 and unsupported protocol-header HTTP400. No SDK upgrade or custom MCP lifecycle rewrite is planned. Eight tools/arguments/result DTOs remain: validate,
format, hover, complete, signature_help, document_symbols, open_document, close_document. Intelligence
uses refreshed principal-filtered metadata. Owner-local functions are not exposed as ordinary global
calls. Format retains its existing literal-whitespace limitation. See mcp-document-limits.md for all
MCP byte/document/TTL/HTTP caps. Enabled GET without an SSE stream returns authenticated 405.

## Remote LSP

Endpoint `wss://<game>/lsp` requires host authentication/Origin policy before upgrade. Subprotocol
`sharpmush-lsp-v1` uses one UTF-8 JSON-RPC object per WebSocket text message, no batching. It is a
custom transport for LSP; use the stdio bridge for ordinary LSP clients. `/ws` remains the terminal.

Remote protocol targets the **LSP 3.17 subset**, with only implemented capabilities advertised.
First features: initialize/shutdown/exit, full-document didOpen/didChange/didClose, diagnostics,
completion, hover, signature help, cancellation. Positions are UTF-16. File extensions:
`.mush`, `.mu` (commands per line), `.mushfn`, `.fun` (function), `.mushcmd` (command list).
No remote formatting/rename/definitions/references, attribute synchronization, execution, server
filesystem reads or transparent session/document resume. Older document versions cannot replace
newer ones, and stale analysis cannot publish into a newer or closed document. Independent sessions
remain isolated even with the same principal and URI. Metadata is refreshed per analysis operation.

| Lsp setting | Default |
| --- | --- |
| MaxDocumentBytes | 5,242,880 UTF-8 bytes |
| MaxMessageBytes | 33,554,432 raw JSON bytes |
| MaxResponseBytes | 1,048,576 raw JSON bytes |
| MaxSessions | 16 |
| MaxSessionsPerPrincipal | 2 |
| MaxDocumentsPerSession | 8 |
| MaxBytesPerSession | 33,554,432 UTF-8 bytes |
| MaxStoredBytes | 134,217,728 UTF-8 bytes across LSP sessions |
| MaxPendingRequests | 8 per session, counting requests still being analysed (a reply being sent no longer counts) |
| MaxConcurrentReaders | 2 globally for fragmented message growth |
| MessageReadTimeout | 00:00:10 |
| IdleTimeout | 00:05:00 |
| SessionTimeout | 00:15:00 |

Limits account text/envelopes, not actual heap usage. Oversize/malformed frames or exceeded session
quotas terminate/reject the operation explicitly. Small single-frame cancellation/control messages bypass work and fragmented-buffer queues. Waiting
for the first bounded fragment uses the idle/session deadline; the message deadline starts when
continuation is required. Request overload errors are awaited and bounded by the receive loop. If
accepted document synchronization cannot obtain analysis admission, the session closes explicitly;
clients must reconnect, initialize and reopen current text. It never silently retains an unanalyzed update. A per-analysis deadline after admission publishes a
generation-checked warning and keeps the session open; edit the document to retry. This avoids
reconnect loops for slow documents without presenting timed-out analysis as a clean validation.
Disconnect, idle/session deadline and host stop cancel work and release documents; uncooperative
workers still retain their execution admission until termination. Responses have bounded result counts.

## Stdio bridge and editor

Build/publish `SharpMUSH.LspBridge`. Launch:

```sh
dotnet /path/to/SharpMUSH.LspBridge.dll --endpoint wss://game.example/lsp
```

Provide existing character credentials through the child environment variables
`SHARPMUSH_LSP_CHARACTER` and `SHARPMUSH_LSP_PASSWORD`, preferably injected from editor SecretStorage.
Do not put passwords in settings JSON, URLs, arguments, logs or committed files. The bridge accepts
only WSS endpoints without userinfo/query/fragment and verifies TLS normally; there is no insecure
certificate bypass. It uses Content-Length framed UTF-8 LSP on stdio, translating to the WS protocol.
Logging is stderr only. Bridge/connection loss exits; client restart opens a fresh authenticated
session and must initialize/reopen documents. The initial bridge requires .NET 11, like the existing
example; a Node/self-contained distribution is separate packaging work. Browser WebSocket clients
cannot supply arbitrary Basic headers; no browser-ticket/query-token mechanism is provided.

The VS Code example is a desktop stdio client with explicit offline/remote modes, all five
extensions, endpoint-bound SecretStorage-driven remote launch and a strict tsconfig. Packaging uses
project-local tools (Node22 or newer for VSCE4); no global tool install is needed. The portal's static Monaco metadata
is not a remote LSP integration. No global editor/network setup is changed by these code changes.
