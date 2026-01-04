# VS Code Extension Example for SharpMUSH

This directory contains example files for creating a VS Code extension that uses the SharpMUSH Language Server.

## Setup

1. **Prerequisites**:
   - Node.js and npm installed
   - TypeScript installed globally: `npm install -g typescript`
   - VS Code Extension Manager: `npm install -g @vscode/vsce`

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
