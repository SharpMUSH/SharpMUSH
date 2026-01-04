import * as path from 'path';
import * as fs from 'fs';
import { workspace, ExtensionContext, window } from 'vscode';
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;

export function activate(context: ExtensionContext) {
  // Get the language server path from settings or find it
  const config = workspace.getConfiguration('sharpmush');
  let serverPath = config.get<string>('languageServer.path', '');

  if (!serverPath) {
    // Try to find the server in common locations
    const possiblePaths = [
      // Development build
      path.join(context.extensionPath, '..', '..', 'bin', 'Debug', 'net10.0', 'SharpMUSH.LanguageServer.dll'),
      // Release build
      path.join(context.extensionPath, '..', '..', 'bin', 'Release', 'net10.0', 'SharpMUSH.LanguageServer.dll'),
      // Bundled with extension
      path.join(context.extensionPath, 'server', 'SharpMUSH.LanguageServer.dll'),
    ];

    for (const p of possiblePaths) {
      if (fs.existsSync(p)) {
        serverPath = p;
        break;
      }
    }
  }

  if (!serverPath || !fs.existsSync(serverPath)) {
    window.showErrorMessage(
      'SharpMUSH Language Server not found. Please build the SharpMUSH.LanguageServer project or configure the path in settings.'
    );
    return;
  }

  // Command to start the language server
  const serverOptions: ServerOptions = {
    run: {
      command: 'dotnet',
      args: [serverPath],
      transport: TransportKind.stdio
    },
    debug: {
      command: 'dotnet',
      args: [serverPath],
      transport: TransportKind.stdio
    }
  };

  // Options to control the language client
  const clientOptions: LanguageClientOptions = {
    // Register the server for MUSH documents
    documentSelector: [
      { scheme: 'file', language: 'mush' }
    ],
    synchronize: {
      // Notify the server about file changes to .mush and .mu files
      fileEvents: workspace.createFileSystemWatcher('**/*.{mush,mu}')
    }
  };

  // Create the language client and start it
  client = new LanguageClient(
    'sharpmush',
    'SharpMUSH Language Server',
    serverOptions,
    clientOptions
  );

  // Start the client (this will also launch the server)
  client.start();

  window.showInformationMessage('SharpMUSH Language Server started');
}

export function deactivate(): Thenable<void> | undefined {
  if (!client) {
    return undefined;
  }
  return client.stop();
}
