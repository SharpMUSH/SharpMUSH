# SharpMUSH VS Code desktop example

This example supports offline stdio and optional authenticated remote LSP. It is built from source; no marketplace installation is assumed. For operator enablement and MCP, see [Set up editor and AI analysis](../../deploy/analysis-tools.md).

## Build, install and configure

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

An `analysis-timeout` warning preserves the document; edit again to retry. If pending document work is rejected within an established session, the session closes: reload/reconnect and reopen files. If initial session admission returns HTTP 429 before WebSocket upgrade, no session exists; retry the connection after capacity becomes available. The client does not automatically resume documents.


## Offline mode

Build `SharpMUSH.LanguageServer`, set `sharpmush.languageServer.mode` to `offline` and `sharpmush.languageServer.path` to the built DLL. Remote credential commands are not needed.

`npm test` uses a mocked IDE API. These instructions have not been exercised in a real VS Code UI/TLS fixture.
