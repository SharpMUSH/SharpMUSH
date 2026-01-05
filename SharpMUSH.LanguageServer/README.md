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
- **Find All References**: Locate all usages of symbols
  - Find attribute references across the document
  - Locate function calls
  - Track object references
- **Code Actions/Quick Fixes**: Intelligent error correction
  - Auto-fix unclosed parentheses
  - Suggest corrections for function name typos
  - Quick fixes for common errors
- **Signature Help**: Parameter hints while typing
  - Show function parameter lists
  - Highlight current parameter
  - Display parameter documentation
  - Mark optional vs required parameters
- **Document Symbols**: Outline view of code structure
  - List all attribute definitions
  - Show function calls
  - Display MUSH commands
  - Navigate document structure
- **Rename Symbol**: Safe refactoring across files
  - Rename attributes and symbols
  - Whole-word matching
  - Atomic workspace edits
- **Code Formatting**: Auto-format with consistent style
  - Trim trailing whitespace
  - Normalize spacing around operators
  - Add space after commands
  - Consistent indentation
- **Workspace Symbols**: Search across all files
  - Find attributes across workspace
  - Locate functions and commands
  - Fuzzy search support
  - Organized by symbol type
- **Inlay Hints**: Show parameter names inline
  - Display parameter names in function calls
  - Context-aware names for well-known functions (GET, SET, ADD, etc.)
  - Generic `arg1`, `arg2` names for other functions
  - Tooltips with parameter descriptions
- **Semantic Highlighting**: Context-aware syntax highlighting that understands MUSH semantics
  - Built-in functions vs user-defined functions
  - Object references (#123, %#, etc.)
  - Substitutions (%0-%9, %N, etc.)
  - Registers and variables
  - Commands and keywords
  - Range support for efficient large file handling

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
- ✅ `textDocument/semanticTokens/range` - Efficient highlighting for large files
- ✅ `textDocument/completion` - Function, command, and pattern completion
- ✅ `textDocument/hover` - Show signatures and documentation
- ✅ `textDocument/definition` - Navigate to attribute definitions
- ✅ `textDocument/references` - Find all usages of symbols
- ✅ `textDocument/codeAction` - Quick fixes and suggestions
- ✅ `textDocument/signatureHelp` - Parameter hints while typing
- ✅ `textDocument/documentSymbol` - Document outline view
- ✅ `textDocument/rename` - Safe symbol renaming
- ✅ `textDocument/formatting` - Auto-format MUSH code
- ✅ `workspace/symbol` - Search symbols across all files
- ✅ `textDocument/inlayHint` - Show parameter names inline in function calls

**Total: 19 LSP protocol methods implemented**

## Development

### Project Structure

```
SharpMUSH.LanguageServer/
├── Handlers/
│   ├── TextDocumentSyncHandler.cs   # Document sync and diagnostics
│   ├── SemanticTokensHandler.cs     # Semantic highlighting (full & range)
│   ├── CompletionHandler.cs         # Code completion
│   ├── HoverHandler.cs              # Hover information
│   ├── DefinitionHandler.cs         # Go to definition
│   ├── ReferencesHandler.cs         # Find all references
│   ├── CodeActionHandler.cs         # Quick fixes and code actions
│   ├── SignatureHelpHandler.cs      # Parameter hints
│   ├── DocumentSymbolHandler.cs     # Document outline
│   ├── RenameHandler.cs             # Symbol renaming
│   ├── DocumentFormattingHandler.cs # Code formatting
│   ├── WorkspaceSymbolsHandler.cs   # Workspace-wide symbol search
│   └── InlayHintHandler.cs          # Inline parameter name hints
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
