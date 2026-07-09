# SharpMUSH MCP Server

SharpMUSH hosts a [Model Context Protocol](https://modelcontextprotocol.io) (MCP)
endpoint **inside the running game server**. It exposes SharpMUSH's live code
intelligence as MCP tools so that AI agents (Claude Code and others) and MCP-aware
tooling can validate and introspect MUSH softcode against the **real parser and the
running world's registered functions and commands** — no separate process, no LSP
socket.

The endpoint is authenticated per request with a **game character's name and
password**.

> Editors (VS Code, Neovim, Emacs) are served separately by the standalone
> `SharpMUSH.LanguageServer` (stdio LSP). The MCP endpoint is the agent/tooling
> surface; both share one analysis engine (`SharpMUSH.CodeAnalysis`).

## Enabling the endpoint

The endpoint is **disabled by default** and mapped only when enabled in
configuration. In `appsettings.json` (or an environment-specific override):

```json
{
  "Mcp": {
    "Enabled": true,
    "Path": "/mcp"
  }
}
```

- `Enabled` — when `false`, the route is not mapped and requests return 404.
- `Path` — the route the Streamable-HTTP endpoint is served at (default `/mcp`).

It is enabled by default in the **Development** environment
(`appsettings.Development.json`).

## Authentication

Every request must carry HTTP Basic credentials of a **game character**:

```
Authorization: Basic base64(character:password)
```

The character is resolved and the password verified through the exact same path the
in-game `connect` command uses. A missing, malformed, unknown-character, or
wrong-password credential returns **401 Unauthorized** with
`WWW-Authenticate: Basic`. A character with no password set cannot authenticate.

> **Transport security:** Basic sends the password on every request, so reach the
> endpoint over **localhost or TLS** only. The credential is a single static header
> you set once in your MCP client config — there is no login round-trip and nothing
> to refresh.

## Registering with Claude Code

```bash
# character "Wizard" with password "s3cret" against a dev server on :8081 (HTTPS)
claude mcp add --transport http sharpmush https://localhost:8081/mcp \
  --header "Authorization: Basic $(printf '%s' 'Wizard:s3cret' | base64)"
```

Or as an `.mcp.json` entry:

```json
{
  "mcpServers": {
    "sharpmush": {
      "type": "http",
      "url": "https://localhost:8081/mcp",
      "headers": {
        "Authorization": "Basic V2l6YXJkOnMzY3JldA=="
      }
    }
  }
}
```

(`V2l6YXJkOnMzY3JldA==` is `base64("Wizard:s3cret")` — replace with your own
character and password.)

## Tools

Positions, where a tool takes one, are **0-based** `line` / `character` (LSP
convention).

### `validate`

Validate SharpMUSH softcode against the live parser and return any syntax errors,
warnings, or hints.

| Parameter | Type | Description |
|---|---|---|
| `code` | string | The MUSH softcode to validate. |
| `parseType` | string | `"function"` (default), `"commandsperline"` (each line is its own command — a real-world `.mush` upload file), `"commandlist"` (one `;`-separated command list), or `"command"` (a single command). |

**Parse mode.** Chosen differently per surface:

- **MCP** — the caller passes `parseType` (above). In `commandsperline` mode each non-blank
  line is validated independently as a single command and diagnostics are reported on the
  line they occur.
- **Language Server** — the mode is chosen by **file extension**:
  - `.mush` / `.mu` → **one command per line** (a real-world `.mush` upload / quote file).
  - `.mushfn` / `.fun` → **function** (a single expression).
  - `.mushcmd` → **command list** (a single `;`-separated command list).
  - anything else → function.

  Save an editor buffer with the matching extension to control how it's analyzed.

Both channels resolve through the same `MushParseMode` / `MushAnalysisMode` rule in
`SharpMUSH.CodeAnalysis`.

**Example call**

```json
{
  "name": "validate",
  "arguments": { "code": "add(1,2" }
}
```

**Example result** (a diagnostic per problem; empty when the code is clean):

```json
[
  {
    "Severity": "Error",
    "Message": "…missing ')'…",
    "StartLine": 0,
    "StartCharacter": 0,
    "EndLine": 0,
    "EndCharacter": 7,
    "Code": null,
    "Source": "SharpMUSH Parser"
  }
]
```

Calling `validate` with `add(1,2)` returns `[]` — no diagnostics.

### `format`

Format SharpMUSH softcode with a consistent style: trims leading/trailing whitespace
per line, inserts a space after commas, and a space between an `@command` and its
first argument. Line count is preserved.

| Parameter | Type | Description |
|---|---|---|
| `code` | string | The MUSH softcode to format. |

**Example**

```json
{ "name": "format", "arguments": { "code": "  add(1,2,3)  " } }
```

returns `"add(1, 2, 3)"`.

The same formatting backs the Language Server's `textDocument/formatting`, so editors
and agents format identically.

### `hover`

Return hover documentation for the word at a position: a function/command signature,
or an explanation of a built-in substitution (`%#`, `%0`, `%qa`, …). Returns `null`
when there is nothing to show.

| Parameter | Type | Description |
|---|---|---|
| `line` | int | 0-based line. |
| `character` | int | 0-based character. |
| `code` | string? | The softcode (omit if `documentId` is given). |
| `documentId` | string? | An open document's id (see session handles). |

```json
{ "name": "hover", "arguments": { "code": "add(1,2)", "line": 0, "character": 1 } }
```

→ `{ "Markdown": "### Function: `add`…", "StartLine": 0, "StartCharacter": 0, "EndLine": 0, "EndCharacter": 3 }`

### `complete`

Return completion suggestions (functions, commands, and common substitutions) for the
word prefix at a position. Same `line` / `character` / `code` / `documentId` parameters
as `hover`. Each item has `Label`, `Kind` (`Function` / `Keyword` / `Variable`),
`Detail`, `Documentation`, `InsertText`, and `IsSnippet`.

### `signature_help`

Return parameter hints for the function call surrounding a position — `Label`,
`Documentation`, the `Parameters` list, and the `ActiveParameter` index — or `null`
when the position is not inside a known function call. Same parameters as `hover`.

### `document_symbols`

Return an outline of the softcode: attribute definitions (`&attr …`), `@set`
attributes, function calls, and commands. Each symbol has `Name`, `Kind`
(`Property` / `Function` / `Method`), `Detail`, and 0-based ranges.

| Parameter | Type | Description |
|---|---|---|
| `code` | string? | The softcode (omit if `documentId` is given). |
| `documentId` | string? | An open document's id. |

### Session handles: `open_document` / `close_document`

To run many queries over the same softcode without resending it, store it once and pass
the returned id as `documentId` to any tool above:

```json
{ "name": "open_document", "arguments": { "code": "&cmd $foo: think bar" } }
```

→ returns a `documentId` string. Then e.g.
`{ "name": "hover", "arguments": { "documentId": "…", "line": 0, "character": 2 } }`.
Release it with `{ "name": "close_document", "arguments": { "documentId": "…" } }`.

Every query tool accepts **either** `code` **or** `documentId`; supplying neither is an
error.

> All tools map the shared analysis engine (`SharpMUSH.CodeAnalysis`) that also backs the
> Language Server, so results match across editors and agents. See
> `docs/superpowers/specs/2026-07-08-mcp-lsp-server-design.md` for the design.

## Docker / remote access

The game server already exposes its HTTP(S) port (`8081` HTTPS / `8080` HTTP in
dev). To reach the MCP endpoint from outside a container, publish that port in your
compose/k8s config, and terminate TLS in front of it (Basic auth over plaintext is
unsafe). Point your MCP client's `url` at the published `https://host:port/mcp`.

## How it fits together

```
 MCP client ──HTTP (Basic auth)──► SharpMUSH.Server  ──in-process──►  IMushCodeAnalyzer
 (Claude Code, …)                    app.MapMcp("/mcp")               (SharpMUSH.CodeAnalysis)
                                     MushBasic auth scheme                    │
                                                                     live MUSHCodeParser +
                                                                     function/command libraries
```

The same `IMushCodeAnalyzer` backs the standalone `SharpMUSH.LanguageServer` used by
editors, so diagnostics are identical across both surfaces.
