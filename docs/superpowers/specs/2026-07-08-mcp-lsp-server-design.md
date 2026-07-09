# SharpMUSH MCP Server (in-server, LSP-analysis reuse) — Design

**Date:** 2026-07-08
**Status:** Approved design, pending implementation plan
**Branch:** `feature/mcp-lsp-server`

## Summary

Expose SharpMUSH's live MUSH-code analysis (diagnostics, hover, completion,
signature help, document symbols, formatting) as **Model Context Protocol (MCP)
tools**, hosted **inside the running SharpMUSH game server** and reached over an
authenticated HTTP endpoint.

Because the MCP runs in-process within `SharpMUSH.Server`, its tools call the
analysis services directly through the game's own dependency-injection container.
The analysis therefore reflects the **live world** — the real registered function
and command libraries — with no separate process, no LSP socket, and no LSP
client round-trip.

The endpoint is authenticated with **character + password** (HTTP Basic),
verified through the same mechanism the `connect` command uses.

## Goals

- An MCP client (Claude Code, editors, other tooling) can validate and introspect
  MUSH softcode against the real SharpMUSH parser and the live function/command
  libraries.
- The MCP is a first-class part of the server: it starts with the server, is
  gated by configuration, and is reached by URL.
- Every request is authenticated as a game character.
- No duplication of MUSH-analysis logic: the existing standalone stdio Language
  Server (for editors) and the new MCP tools share one source of truth.

## Non-Goals (v1)

- Out-of-process / "pointable" MCP that connects to a remote LSP endpoint. The
  MCP is **in-server only**. (The standalone stdio LSP in
  `SharpMUSH.LanguageServer` is unchanged and remains available for editors.)
- A TCP/socket transport for the Language Server. Not needed — the MCP calls
  analysis in-process.
- Auth schemes beyond character+password (JWT bearer, OTT, OAuth 2.1). These are
  a documented upgrade path, not v1.
- Per-character permission scoping of analysis results. v1 analysis is read-only
  syntax/semantic work; the authenticated character is captured as session
  identity and used only as an access gate. Per-enactor scoping is a future hook.
- MCP tools for LSP capabilities not listed below (rename, references, code
  actions, inlay hints, semantic tokens, workspace symbols). Easy to add later.

## Architecture

```
 MCP client (Claude Code, editor, tooling)
        │  MCP over HTTP (Streamable HTTP), URL-based
        │  Authorization: Basic base64(character:password)
        ▼
 ┌──────────────────────────────────────────────────────────┐
 │  SharpMUSH.Server  (existing ASP.NET host)                │
 │                                                            │
 │   [MushBasic auth scheme]  ── character+password ──▶       │
 │        │  verified via IPasswordService.PasswordIsValid    │
 │        ▼                                                    │
 │   app.MapMcp("/mcp").RequireAuthorization(...)             │
 │        │  ModelContextProtocol.AspNetCore                  │
 │        ▼                                                    │
 │   MushTools  ([McpServerToolType])                         │
 │        │  in-process DI call (no socket)                   │
 │        ▼                                                    │
 │   Analysis services  ◀── shared ──▶  LSP handlers          │
 │   (diagnose/hover/complete/signature/symbols/format)       │
 │        │  live DI                                          │
 │        ▼                                                    │
 │   real MUSHCodeParser + live function/command libraries    │
 └──────────────────────────────────────────────────────────┘
```

## Components

### 1. Shared analysis services (`SharpMUSH.LanguageServer` refactor)

Extract the per-capability analysis currently embedded in the LSP handlers into
stateless services that accept plain text (plus a position where relevant) and
return **plain domain results, not LSP protocol types**.

- `validate(text)` → diagnostics. **Already centralized** in
  `LSPMUSHCodeParser.GetDiagnostics` — reuse as-is; no extraction needed. This is
  what lets the MCP ship a working first tool immediately.
- `hover(text, position)` → symbol info (function/command signature + docs).
  Extracted from `HoverHandler`.
- `complete(text, position)` → completion items. Extracted from
  `CompletionHandler`.
- `signature_help(text, position)` → active signature + parameters. Extracted
  from `SignatureHelpHandler`.
- `document_symbols(text)` → outline. Extracted from `DocumentSymbolHandler`.
- `format(text)` → formatted text. Extracted from `DocumentFormattingHandler`.

The existing LSP handlers are refactored into **thin adapters** that translate
between LSP protocol types (`HoverParams`, `Position`, `Hover`, …) and the shared
services. The standalone stdio Language Server continues to pass its existing
tests after this refactor — this is an explicit acceptance criterion.

Handlers **not** exposed via MCP in v1 (rename, references, code actions, inlay
hints, semantic tokens, workspace symbols) are left untouched.

**Result-type note:** the shared services return small, plain result records
(e.g. a diagnostic record with severity, message, and a 0-based line/character
range; a hover record with markdown + range). Both the LSP adapter and the MCP
tool map from these. The MCP tool serializes them to JSON.

### 2. MCP hosting in `SharpMUSH.Server`

- Add the `ModelContextProtocol.AspNetCore` package.
- In `Startup`:
  `builder.Services.AddMcpServer().WithHttpTransport().WithTools<MushTools>();`
- In the app pipeline: `app.MapMcp(mcpOptions.Path).RequireAuthorization(policy);`
- Configuration section (`appsettings.json`):

  ```json
  "Mcp": {
    "Enabled": true,
    "Path": "/mcp"
  }
  ```

  Disabled by default in production configs; enabled for development. When
  `Enabled` is false, `MapMcp` is not registered.

### 3. `MushTools` — the MCP tool type

An `[McpServerToolType]` class whose `[McpServerTool]` methods inject the shared
analysis services from DI and map JSON ⇄ domain results. Each method has a
`[Description]`; each parameter is described.

| Tool | Backing service | Signature |
|---|---|---|
| `validate` | diagnostics | `validate(code)` → list of `{severity, message, range}` |
| `complete` | completion | `complete(code, line, character)` → items |
| `hover` | hover | `hover(code, line, character)` → `{markdown, range}` or null |
| `signature_help` | signature help | `signature_help(code, line, character)` → `{signature, parameters, activeParameter}` |
| `document_symbols` | document symbols | `document_symbols(code)` → outline |
| `format` | formatting | `format(code)` → formatted string |
| `open_document` / `close_document` | (session) | optional handles: open a URI+text once, then pass that URI to the query tools to avoid resending text |

- **Positions are 0-based line/character** (LSP convention), documented on each
  tool's parameters.
- **Snippet tools** (the primary surface) are self-contained: each call passes
  the full `code`, the tool opens a synthetic document, runs the query, and
  returns — no client-visible state.
- **Session handles** (`open_document`/`close_document`) are optional: a caller
  that runs many queries over one document can open it once and reference its
  URI, avoiding resend. State lives in a small in-memory per-session document
  cache.

### 4. Authentication — `MushBasic` scheme

A custom `AuthenticationHandler<MushBasicOptions>` (modeled on the existing
`DebugAuthenticationHandler` and reusing the same services as the `connect`
command):

1. Read `Authorization: Basic base64(character:password)`. Absent/malformed →
   `401` with `WWW-Authenticate: Basic`.
2. Resolve the character by name via `GetPlayerQuery(name)` (mediator), matching
   the `connect` flow. Not found → `401`.
3. Verify with
   `IPasswordService.PasswordIsValid($"#{Key}:{CreationTime}", password, player.PasswordHash)`.
   Invalid → `401`.
4. On success, build a `ClaimsPrincipal` carrying the character's DBRef and name;
   this is the session identity.
5. `MapMcp(...).RequireAuthorization(policy)` where `policy` requires the
   `MushBasic` scheme and an authenticated character.

**Transport security:** HTTP Basic sends the password on every request, so the
endpoint must be reached over localhost or TLS. The MCP config binds to localhost
in development; remote exposure requires TLS. This is stated in the docs and the
default config.

**Client ergonomics:** the credential is a single static header set once in the
MCP client config; no login round-trip and nothing to refresh — the direct fit
for "character & password to start off."

## Data flow

**`validate` example:**

1. MCP client calls `validate(code)` with `Authorization: Basic …`.
2. `MushBasic` authenticates the character (401 on failure).
3. `MushTools.Validate` resolves the diagnostics service and calls
   `GetDiagnostics(code)` — in-process, against the live parser/libraries.
4. Diagnostics are mapped to a compact JSON list and returned as the tool result.

Hover/completion/signature/symbols/format follow the same shape: authenticate →
resolve shared service → call with `code` (+ position) → map to JSON.

## Error handling

- **Auth failures** → `401 Unauthorized` with `WWW-Authenticate: Basic`; no tool
  is invoked.
- **Analysis exceptions** → caught inside the tool and returned as a structured
  MCP tool error. The analysis never crashes the server (mirrors the LSP's
  error-resilience design).
- **MCP disabled** (`Mcp.Enabled=false`) → endpoint not mapped; requests `404`.

## Testing

- **Shared services (unit):** one test per extracted capability against a
  parser wired to a known function/command library — e.g. a known-bad snippet
  yields a diagnostic; `hover` on a known function returns its signature;
  `format` normalizes a messy snippet.
- **LSP regression:** the existing `SharpMUSH.LanguageServer` handler tests still
  pass after the handler→adapter refactor. Explicit acceptance criterion.
- **MCP integration** (`SharpMUSH.Tests.Integration`): with `Mcp.Enabled=true`,
  `tools/list` lists the tools; an authenticated `validate` call against the live
  parser returns diagnostics for a bad snippet.
- **Auth:** missing/garbage header → 401; wrong password → 401; valid
  character+password → 200 and a tool result. Reuse a seeded character
  (e.g. God `#1` or a seeded WIZARD) with a known password from the test
  infrastructure.

## Examples (deliverable)

- **Claude Code registration:**

  ```bash
  claude mcp add --transport http sharpmush http://localhost:<port>/mcp \
    --header "Authorization: Basic $(printf '%s' 'One:mypassword' | base64)"
  ```

- **`.mcp.json` snippet** with the URL and the static `Authorization` header.
- **docker-compose note** exposing the server's MCP/HTTP port.
- **A `docs/` page** with worked tool calls: sample MUSH code in → real
  diagnostic / hover / signature JSON out.
- **Editor note:** the standalone stdio Language Server still serves editors
  (VS Code / Neovim / Emacs) unchanged; the MCP is the agent/tooling surface.

## Phasing (de-risks delivery)

1. **Vertical slice:** MCP hosting + `MushBasic` auth + the `validate` tool
   (no service extraction needed). Proves the in-server, authenticated MCP path
   end-to-end.
2. **Extract and expose** `hover`, `complete`, `signature_help`,
   `document_symbols`, `format` — one shared service + one MCP tool at a time,
   refactoring each corresponding LSP handler into a thin adapter and keeping its
   tests green.
3. **Optional session handles** (`open_document`/`close_document`).
4. **Examples & docs.**

## Key packages / APIs

- `ModelContextProtocol` / `ModelContextProtocol.AspNetCore` —
  `AddMcpServer().WithHttpTransport().WithTools<T>()`, `app.MapMcp(path)`,
  `[McpServerToolType]` / `[McpServerTool]` / `[Description]`.
- `IPasswordService.PasswordIsValid`, `GetPlayerQuery`, mediator — existing,
  reused for auth.
- `LSPMUSHCodeParser.GetDiagnostics` — existing, reused for `validate`.
- Custom `AuthenticationHandler<>` modeled on `DebugAuthenticationHandler`.
