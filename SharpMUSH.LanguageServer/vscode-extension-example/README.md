# VS Code Extension Example for SharpMUSH

This directory contains example files for creating a VS Code extension that uses the SharpMUSH Language Server.

## Setup

1. **Prerequisites**:
   - Node.js22 or newer and npm installed
   - Build the offline language server or remote bridge with the repository's .NET11 SDK.
   - TypeScript and VSCE are project dependencies; no global installation is needed.

2. **Install dependencies**:
   ```bash
   npm install
   ```

3. **Compile TypeScript**:
   ```bash
   npm run compile
   ```

4. **Package the extension**:
   ```bash
   npm run package
   ```

This will create a `.vsix` file that can be installed in VS Code.

## Development

1. Copy this directory to a new location outside the SharpMUSH repository
2. Build the SharpMUSH.LanguageServer project
3. Either:
   - Copy the built `SharpMUSH.LanguageServer.dll` and its dependencies to `server/` directory
   - Or configure the path in VS Code settings: `sharpmush.languageServer.path`

## Testing

1. Open this directory in VS Code
2. Press F5 to start debugging
3. This will open a new VS Code window with the extension loaded
4. Create a `.mush` or `.mu` file and start editing

## Files

- `package.json` - Extension manifest
- `extension.ts` - Extension entry point and LSP client
- `language-configuration.json` - Language configuration (brackets, comments, etc.)
- `syntaxes/mush.tmLanguage.json` - TextMate grammar for basic syntax highlighting

## Notes

The TextMate grammar provides basic syntax highlighting, but the LSP server provides semantic highlighting which is more accurate and context-aware.

## Bounded remote mode

Build/publish `SharpMUSH.LspBridge`, then set `sharpmush.languageServer.mode` to `remote`,
`sharpmush.languageServer.bridgePath` to its DLL, and `sharpmush.languageServer.endpoint` to a WSS
`/lsp` endpoint without userinfo/query/fragment. Run **SharpMUSH: Set Remote Credentials** for an
existing character. Values are endpoint-bound SecretStorage entries injected into the bridge's
private child environment, never settings/arguments/logs. **Clear Remote Credentials** removes both
and stops the current client. Reload to reconnect after changing settings/credentials.

Offline mode remains the default and uses `sharpmush.languageServer.path`. .NET 11 is required for
both. Remote capabilities are full sync, diagnostics, completion, hover and signatures across
`.mush`, `.mu`, `.mushfn`, `.fun`, `.mushcmd`; no remote formatting/rename/attribute save is advertised.
Only trusted workspaces may launch/configure the client. `npm test` compiles TypeScript and verifies
remote launch/SecretStorage behavior with a mocked IDE API, not a real VS Code UI/TLS E2E test.

Remote overload or connection loss requires a fresh initialize and reopening current documents.
The server closes explicitly if synchronized text cannot obtain analysis admission. Real VS Code UI
and TLS/proxy integration still require a separately approved disposable fixture.
