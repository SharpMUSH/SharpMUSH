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
| `parseType` | string | `"function"` (default) or `"command"`. |

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

> More tools mapping the rest of the shared analysis engine — `hover`, `complete`,
> `signature_help`, `document_symbols`, and optional document-session handles —
> follow the same pattern and are being added incrementally. See
> `docs/superpowers/specs/2026-07-08-mcp-lsp-server-design.md`.

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
