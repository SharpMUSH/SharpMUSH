# SharpMUSH Language Server

This directory contains the Language Server Protocol (LSP) implementation for SharpMUSH's MUSH code syntax.

## Overview

The SharpMUSH Language Server provides syntax validation, error diagnostics, and semantic highlighting for MUSH code in text editors that support LSP (VS Code, Neovim, Emacs, etc.).

## Features

- **Real-time Syntax Validation**: Immediate feedback on syntax errors as you type
- **Error Diagnostics**: Detailed error messages with precise locations
- **Code Completion**: Auto-complete for functions, commands, and MUSH patterns
  - Function names with parameter signatures
  - Command names with available switches
  - Special variables (%#, %!, %@, %N, etc.)
  - Q-registers and V-registers
- **Hover Information**: Show documentation and signatures
  - Function signatures with min/max arguments
  - Command details with switches and locks
  - Explanations for MUSH special patterns
- **Go to Definition**: Navigate to attribute definitions
  - Jump to &attribute definitions
  - Jump to @set attribute declarations
- **Semantic Highlighting**: Context-aware syntax highlighting that understands MUSH semantics
  - Built-in functions vs user-defined functions
  - Object references (#123, %#, etc.)
  - Substitutions (%0-%9, %N, etc.)
  - Registers and variables
  - Commands and keywords

## Architecture

The LSP server uses a **stateless, read-only** parser wrapper (`LSPMUSHCodeParser`) that:
- Does not alter any state
- Only performs syntax analysis and semantic token generation
- Requires minimal runtime dependencies
- Is safe for concurrent use by multiple documents

This design ensures that the LSP server:
- Is fast and responsive
- Doesn't interfere with the SharpMUSH runtime
- Can analyze code without needing database connections or player state

## Building

```bash
dotnet build SharpMUSH.LanguageServer/SharpMUSH.LanguageServer.csproj
```

## Running

The language server communicates via stdin/stdout using the LSP protocol:

```bash
dotnet run --project SharpMUSH.LanguageServer/SharpMUSH.LanguageServer.csproj
```

## Logging

Logs are written to:
- **Linux/macOS**: `~/.local/share/SharpMUSH/lsp-server.log`
- **Windows**: `%LOCALAPPDATA%\SharpMUSH\lsp-server.log`

## Supported File Extensions

- `*.mush` - MUSH code files
- `*.mu` - MUSH code files (alternate extension)

## VS Code Integration

Create a VS Code extension with this configuration:

### 1. Create extension structure

```
sharpmush-vscode/
├── package.json
├── client/
│   └── src/
│       └── extension.ts
└── server/ (symlink to SharpMUSH.LanguageServer build output)
```

### 2. package.json example

```json
{
  "name": "sharpmush",
  "displayName": "SharpMUSH Language Support",
  "description": "Language support for SharpMUSH MUSH code",
  "version": "0.1.0",
  "engines": {
    "vscode": "^1.75.0"
  },
  "categories": ["Programming Languages"],
  "activationEvents": ["onLanguage:mush"],
  "main": "./client/out/extension",
  "contributes": {
    "languages": [{
      "id": "mush",
      "extensions": [".mush", ".mu"],
      "aliases": ["MUSH", "mush"],
      "configuration": "./language-configuration.json"
    }],
    "grammars": [{
      "language": "mush",
      "scopeName": "source.mush",
      "path": "./syntaxes/mush.tmLanguage.json"
    }]
  },
  "scripts": {
    "vscode:prepublish": "npm run compile",
    "compile": "tsc -b",
    "watch": "tsc -b -w"
  },
  "dependencies": {
    "vscode-languageclient": "^8.1.0"
  },
  "devDependencies": {
    "@types/node": "^18.x",
    "@types/vscode": "^1.75.0",
    "typescript": "^5.0.0"
  }
}
```

### 3. client/src/extension.ts example

```typescript
import * as path from 'path';
import { workspace, ExtensionContext } from 'vscode';
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
} from 'vscode-languageclient/node';

let client: LanguageClient;

export function activate(context: ExtensionContext) {
  // Path to the language server
  const serverExecutable = 'dotnet';
  const serverPath = context.asAbsolutePath(
    path.join('server', 'SharpMUSH.LanguageServer.dll')
  );

  const serverOptions: ServerOptions = {
    run: { command: serverExecutable, args: [serverPath] },
    debug: { command: serverExecutable, args: [serverPath] }
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: 'file', language: 'mush' }],
    synchronize: {
      fileEvents: workspace.createFileSystemWatcher('**/*.{mush,mu}')
    }
  };

  client = new LanguageClient(
    'sharpmush',
    'SharpMUSH Language Server',
    serverOptions,
    clientOptions
  );

  client.start();
}

export function deactivate(): Thenable<void> | undefined {
  if (!client) {
    return undefined;
  }
  return client.stop();
}
```

## Neovim Integration

Using `nvim-lspconfig`:

```lua
local lspconfig = require('lspconfig')
local configs = require('lspconfig.configs')

-- Define sharpmush LSP
if not configs.sharpmush then
  configs.sharpmush = {
    default_config = {
      cmd = { 'dotnet', 'run', '--project', '/path/to/SharpMUSH.LanguageServer/SharpMUSH.LanguageServer.csproj' },
      filetypes = { 'mush' },
      root_dir = function(fname)
        return lspconfig.util.find_git_ancestor(fname) or vim.fn.getcwd()
      end,
      settings = {},
    },
  }
end

-- Set up the LSP
lspconfig.sharpmush.setup{}

-- Auto-detect .mush and .mu files
vim.filetype.add({
  extension = {
    mush = 'mush',
    mu = 'mush',
  },
})
```

## Emacs Integration

Using `lsp-mode`:

```elisp
(require 'lsp-mode)

(add-to-list 'lsp-language-id-configuration '(mush-mode . "mush"))

(lsp-register-client
 (make-lsp-client :new-connection (lsp-stdio-connection
                                   '("dotnet" "run" "--project" 
                                     "/path/to/SharpMUSH.LanguageServer/SharpMUSH.LanguageServer.csproj"))
                  :major-modes '(mush-mode)
                  :server-id 'sharpmush-ls))

(add-to-list 'auto-mode-alist '("\\.mush\\'" . mush-mode))
(add-to-list 'auto-mode-alist '("\\.mu\\'" . mush-mode))
```

## Protocol Support

The SharpMUSH Language Server currently implements:

- ✅ `initialize` / `shutdown`
- ✅ `textDocument/didOpen`
- ✅ `textDocument/didChange`
- ✅ `textDocument/didClose`
- ✅ `textDocument/didSave`
- ✅ `textDocument/publishDiagnostics`
- ✅ `textDocument/semanticTokens/full`
- ✅ `textDocument/completion` - Function, command, and pattern completion
- ✅ `textDocument/hover` - Show signatures and documentation
- ✅ `textDocument/definition` - Navigate to attribute definitions
- ❌ `textDocument/semanticTokens/range` (planned)
- ❌ `textDocument/references` (planned)
- ❌ `textDocument/codeAction` (planned)
- ❌ `textDocument/signatureHelp` (planned)

## Development

### Project Structure

```
SharpMUSH.LanguageServer/
├── Handlers/
│   ├── TextDocumentSyncHandler.cs   # Document sync and diagnostics
│   ├── SemanticTokensHandler.cs     # Semantic highlighting
│   ├── CompletionHandler.cs         # Code completion
│   ├── HoverHandler.cs              # Hover information
│   └── DefinitionHandler.cs         # Go to definition
├── Services/
│   ├── DocumentManager.cs           # Document state management
│   └── LSPMUSHCodeParser.cs         # Stateless parser wrapper
└── Program.cs                       # LSP server entry point
```

### Key Design Decisions

1. **Stateless Parser**: The `LSPMUSHCodeParser` wraps the main parser but ensures all operations are read-only and don't modify state.

2. **Minimal Dependencies**: The LSP server doesn't require the full SharpMUSH runtime (no database, no player state, no configuration).

3. **Error Resilience**: Parser errors are caught and converted to diagnostics rather than crashing the server.

4. **Full Document Sync**: Currently uses full document sync for simplicity. Incremental sync could be added later for performance.

## Testing

Test the server manually with any LSP client, or use the LSP test tools:

```bash
# Start the server
dotnet run --project SharpMUSH.LanguageServer/SharpMUSH.LanguageServer.csproj

# In another terminal, send LSP messages via stdin
# Example initialize message:
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}' | dotnet run --project SharpMUSH.LanguageServer/SharpMUSH.LanguageServer.csproj
```

## Performance

The language server is designed to be fast and responsive:
- Parse operations are cached per document version
- Semantic analysis reuses the parse tree
- No network or database operations
- Minimal memory footprint

## Future Enhancements

- **Code Completion**: Auto-complete for functions, commands, and object references
- **Hover Information**: Show function signatures and descriptions
- **Go to Definition**: Navigate to function/attribute definitions
- **Find All References**: Find all uses of a function or attribute
- **Signature Help**: Parameter hints while typing function calls
- **Code Actions**: Quick fixes for common errors
- **Rename**: Rename symbols across files
- **Document Symbols**: Outline view of functions and structures

## Contributing

When adding new LSP features:
1. Ensure all operations are stateless and read-only
2. Handle errors gracefully without crashing the server
3. Add appropriate logging for debugging
4. Update this README with new capabilities

## License

Same as SharpMUSH - see LICENSE file in repository root.
