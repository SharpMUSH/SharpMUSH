# Set up editor and AI analysis

This path assumes an already configured HTTPS game and an **existing character with a game password**. Analysis uses that character name/password, not the portal account email, a portal cookie, an API key or a GitHub login. Analysis does not save attributes or run commands. The remote work in PR #1800 is merged; use a server build containing it, and build the editor/bridge from the matching checkout.

The commands below are instructions for an operator, not stages run during this documentation update. No game, character credentials, TLS keys, exposed listener or IDE session was created to validate this recipe.

## 1. Forward the settings through Compose

In the repository's `deploy/` directory, add these non-secret switches to `.env`:

```dotenv
SHARPMUSH_MCP_ENABLED=true
SHARPMUSH_LSP_ENABLED=true
```

Use only the switches for transports you need. `.env` supplies Compose interpolation; it does **not** automatically inject arbitrary entries into a container. Save the YAML below as `deploy/docker-compose.analysis.yml` if your checkout does not yet include it. The optional overlay maps these switches to `Mcp__Enabled` and `Lsp__Enabled` in `sharpmush-server.environment`. ASP.NET Core maps double underscores to configuration section separators. Both switches default to false.

```yaml
# Optional remote analysis; does not publish ports or configure credentials/TLS.
# Use after docker-compose.prod.yml or docker-compose.cloudflare.yml.
services:
  sharpmush-server:
    environment:
      Mcp__Enabled: "${SHARPMUSH_MCP_ENABLED:-false}"
      Lsp__Enabled: "${SHARPMUSH_LSP_ENABLED:-false}"
      Mcp__Path: /mcp
      Lsp__Path: /lsp
      Analysis__MaxConcurrentAnalyses: "2"
      Analysis__MaxQueuedAnalyses: "8"
      Analysis__AnalysisTimeout: "00:00:05"
```

Validate the merged configuration without starting services (rendered configuration may contain other deployment secrets; do not post it):

```bash
cd SharpMUSH/deploy
# If already in deploy/, do not cd again.
docker compose -f docker-compose.prod.yml -f docker-compose.analysis.yml config --quiet
```

For a reviewed source-built Caddy deployment, the operator applies it with:

```bash
docker compose -f docker-compose.prod.yml -f docker-compose.analysis.yml up -d --no-deps --build sharpmush-server
```

This restarts only the already deployed engine; `--no-deps` avoids recreating its socket-owner dependencies. The Cloudflare variant uses pulled `:dev` images instead of a local build. For that already deployed stack, use:

```bash
docker compose -f docker-compose.cloudflare.yml -f docker-compose.analysis.yml config --quiet
docker compose -f docker-compose.cloudflare.yml -f docker-compose.analysis.yml pull sharpmush-server
docker compose -f docker-compose.cloudflare.yml -f docker-compose.analysis.yml up -d --no-deps sharpmush-server
```

Ensure the selected image contains the merged implementation. Keep the overlay in **every** subsequent configuration/recreation command, including any configured systemd unit's Compose invocations. To disable a transport set its switch to false and recreate the server with the same files. Disabled or missing plugins return 404.

The complete optional plugin payloads must remain in `plugins/mcp/` and `plugins/lsp/`; copying only an entry DLL is insufficient. The supplied Caddy route sends `/ws` to the terminal connection server, while `/mcp` and `/lsp` reach the main server through its catch-all route. No extra public port is needed. Use HTTPS for MCP and WSS for LSP, with normally validated certificates. Native clients may omit Origin; if a client sends Origin, its exact HTTP(S) origin needs an `AnalysisTransports:AllowedOrigins` entry. Do not substitute `Cors:AllowedOrigins` or wildcard trust.

## 2. Install the desktop editor example

On the editor machine, install the SDK pinned by the checkout's `global.json`, plus Node 22 or newer. From the repository root:

```bash
dotnet publish SharpMUSH.LspBridge/SharpMUSH.LspBridge.csproj -c Release -o ./artifacts/lsp-bridge
cd SharpMUSH.LanguageServer/vscode-extension-example
npm ci
npm test
npm run package
```

Keep the bridge's entire publish directory. In VS Code's Extensions menu choose **Install from VSIX…** and select the generated `.vsix`. This is a source-built example, not a claim that a marketplace extension is installed.

Open a trusted workspace and set these values in **user settings JSON**, replacing the path/domain:

```json
{
  "sharpmush.languageServer.mode": "remote",
  "sharpmush.languageServer.bridgePath": "/absolute/path/SharpMUSH/artifacts/lsp-bridge/SharpMUSH.LspBridge.dll",
  "sharpmush.languageServer.endpoint": "wss://mush.example.com/lsp",
  "sharpmush.trace.server": "off"
}
```

Run **SharpMUSH: Set Remote Credentials** from the Command Palette. Enter the existing **character name**, then its password in the hidden prompt. The extension stores both in endpoint-bound SecretStorage and injects them only into the bridge child environment. Changing endpoint requires credentials for that endpoint. Use **Developer: Reload Window** after settings/credential changes, then open or reopen a softcode file. **SharpMUSH: Clear Remote Credentials** deletes the current endpoint's entries and stops the client. Never put the password in settings, arguments, URLs, tracing logs or version control.

Create `analysis-smoke.mushfn` containing `add(`. Expect a syntax diagnostic in Problems. Replace it with `add(1,2)`; expect that syntax error to clear. Hover `add` for function help, request completion with `ad`, and signature help within `add(`. These are expected checks, not a live IDE run recorded here. `.mush`/`.mu` use commands per line, `.mushfn`/`.fun` use function mode, and `.mushcmd` uses command-list mode.

An `analysis-timeout` warning preserves the document; edit again to retry. Admission rejection instead closes the session: reload/reconnect and reopen files. The client does not automatically resume documents.

## 3. Try MCP with curl before configuring an AI client

These Bash requests use the named client **curl**, not an LSP client. Replace the non-secret URL/character. `--user` has no password argument, so curl prompts on the terminal for the existing character's password on each request. Do not append `:password`, use `-v`/tracing or follow redirects with credentials.

```bash
analysis_url=https://mush.example.com/mcp
analysis_character=YourExistingCharacter
curl --silent --show-error --include --user "$analysis_character" \
  --header 'Content-Type: application/json' \
  --header 'Accept: application/json, text/event-stream' \
  --data '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"curl-sharpmush-smoke","version":"1.0"}}}' \
  "$analysis_url"
```

Expect HTTP 200 with JSON-RPC result containing negotiated `protocolVersion: "2025-11-25"` and tool capabilities. Responses can be JSON or `data:` records in an SSE response; don't require one particular envelope rendering. This recipe deliberately pins the tested older initialize lifecycle, not the newer discovery lifecycle.

```bash
curl --silent --show-error --include --user "$analysis_character" \
  --header 'Content-Type: application/json' \
  --header 'Accept: application/json, text/event-stream' \
  --header 'MCP-Protocol-Version: 2025-11-25' \
  --data '{"jsonrpc":"2.0","method":"notifications/initialized"}' "$analysis_url"

curl --silent --show-error --include --user "$analysis_character" \
  --header 'Content-Type: application/json' \
  --header 'Accept: application/json, text/event-stream' \
  --header 'MCP-Protocol-Version: 2025-11-25' \
  --data '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' "$analysis_url"
```

Expect HTTP 202 without a response body for the notification, then a tool list containing `validate`, `format`, `hover`, `complete`, `signature_help`, `document_symbols`, `open_document` and `close_document`. The endpoint is stateless; this recipe does not invent a session-id header.

```bash
curl --silent --show-error --include --user "$analysis_character" \
  --header 'Content-Type: application/json' \
  --header 'Accept: application/json, text/event-stream' \
  --header 'MCP-Protocol-Version: 2025-11-25' \
  --data '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"validate","arguments":{"code":"add(","parseType":"function"}}}' \
  "$analysis_url"
```

Expect a successful tool result with at least one diagnostic (`severity`, `message`, and zero-based start/end line/character fields). Repeat with `add(1,2)` and expect no syntax diagnostics. Tool results are analysis, not evidence that gameplay permissions or live commands work. These expected outputs are based on the implementation's tests; no credentialed live request was run for this guide.

## 4. Optional VS Code MCP client

For VS Code's desktop extension-host MCP client, run **MCP: Open User Configuration**. Use its VS Code `servers`/`inputs` format:

```json
{
  "inputs": [{
    "id": "sharpmush-basic",
    "type": "promptString",
    "description": "Base64 of existing character:password (treat as a password)",
    "password": true
  }],
  "servers": {
    "sharpmush": {
      "type": "http",
      "url": "https://mush.example.com/mcp",
      "headers": { "Authorization": "Basic ${input:sharpmush-basic}" }
    }
  }
}
```

Supply base64 of UTF-8 `character:password` using trusted local tooling/password management; base64 is reversible and remains a secret. Do not put its value into this file or use an online encoder. This MCP input is separate from the LSP extension's SecretStorage command. Review server trust, start it using **MCP: List Servers**, then ask the agent to call `validate` with `code: "add("`, `parseType: "function"`; confirm the tool invocation and inspect the diagnostic. This needs an available VS Code chat/agent provider. Interactive-input configurations are not forwarded to Agent Host sessions; this example is scoped to the desktop extension host. No OAuth/browser-ticket login is offered by SharpMUSH.

Client configuration fields follow the [VS Code MCP reference](https://code.visualstudio.com/docs/agents/reference/mcp-configuration); [VS Code setup guidance](https://code.visualstudio.com/docs/agent-customization/mcp-servers) describes user configuration and trust. The concrete client UI/authentication path remains untested here.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| `/mcp` or `/lsp` returns 404 | Enabled flag actually appears in the server environment; matching image/plugin payload; correct route and overlay on recreation |
| 401 | Existing character name/password, not portal account email; account/character active state; changed password; proxy preserves Authorization |
| 403 with Origin | Exact `AnalysisTransports:AllowedOrigins` value; native clients may omit Origin |
| MCP GET returns 405 | Expected when no SSE stream is available; use POST initialize/tool requests, not a browser GET health test |
| 400 or unsupported MCP method | Protocol revision/lifecycle, Content-Type and Accept headers; do not combine discovery with older initialization |
| 408 / 413 / 429 | Body-read deadline, size cap or admission/IP limit; reduce input/retry later; proxy may impose a smaller cap |
| LSP fails before diagnostics | WSS URL, valid TLS chain, `/lsp` main-server routing and `sharpmush-lsp-v1` transport via bridge; `/ws` is terminal only |
| VS Code does not start bridge | Trusted workspace, installed VSIX, existing bridge DLL/dependencies, dotnet on PATH, mode/endpoint and stored credentials; reload window |
| Diagnostic warning `analysis-timeout` | Next edit retries; no document reopening needed just for this warning |
| Explicit overload close | Reconnect, initialize and reopen current text; no transparent resume |

Keep the analysis quotas, authentication and exact Origin policy described in the remote-analysis contract. Do not disable certificate validation to make a failing client connect.
