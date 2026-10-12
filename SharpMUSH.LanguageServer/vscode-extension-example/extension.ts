import * as path from 'path';
import * as fs from 'fs';
import { workspace, commands, ExtensionContext, window } from 'vscode';
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind } from 'vscode-languageclient/node';

let client: LanguageClient | undefined;
function endpoint(value: string): string {
  const uri = new URL(value);
  if (uri.protocol !== 'wss:' || uri.username || uri.password || uri.search || uri.hash) {
    throw new Error('Configure a WSS endpoint without credentials, query or fragment.');
  }
  return uri.toString();
}
function credentialKey(uri: string): string { return 'sharpmush.remote.' + uri; }
function findDll(context: ExtensionContext, configured: string, name: string): string | undefined {
  const locations = configured ? [configured] : [
    path.join(context.extensionPath, 'server', name + '.dll'),
    path.join(context.extensionPath, '..', 'bin', 'Debug', 'net11.0', name + '.dll'),
    path.join(context.extensionPath, '..', 'bin', 'Release', 'net11.0', name + '.dll'),
    path.join(context.extensionPath, '..', '..', name, 'bin', 'Debug', 'net11.0', name + '.dll'),
    path.join(context.extensionPath, '..', '..', name, 'bin', 'Release', 'net11.0', name + '.dll')
  ];
  return locations.find(file => fs.existsSync(file));
}
export async function activate(context: ExtensionContext): Promise<void> {
  context.subscriptions.push(commands.registerCommand('sharpmush.setRemoteCredentials', async () => {
    if (!workspace.isTrusted) { window.showErrorMessage('Trust this workspace before configuring remote analysis.'); return; }
    try {
      const uri = endpoint(workspace.getConfiguration('sharpmush').get<string>('languageServer.endpoint', ''));
      const character = await window.showInputBox({ prompt: 'Existing game character name', ignoreFocusOut: true });
      if (!character || character.includes(':')) { return; }
      const password = await window.showInputBox({ prompt: 'Character password (stored in SecretStorage)', password: true, ignoreFocusOut: true });
      if (!password) { return; }
      await context.secrets.store(credentialKey(uri) + '.character', character);
      await context.secrets.store(credentialKey(uri) + '.password', password);
      window.showInformationMessage('Remote analysis credentials stored. Reload the extension to reconnect.');
    } catch { window.showErrorMessage('Configure a valid remote WSS endpoint before storing credentials.'); }
  }));
  context.subscriptions.push(commands.registerCommand('sharpmush.clearRemoteCredentials', async () => {
    try {
      const uri = endpoint(workspace.getConfiguration('sharpmush').get<string>('languageServer.endpoint', ''));
      await context.secrets.delete(credentialKey(uri) + '.character');
      await context.secrets.delete(credentialKey(uri) + '.password');
      if (client) { await client.stop(); client = undefined; }
      window.showInformationMessage('Remote analysis credentials cleared and client stopped.');
    } catch { window.showErrorMessage('Configure a valid remote WSS endpoint to clear its credentials.'); }
  }));
  if (!workspace.isTrusted) { window.showInformationMessage('SharpMUSH analysis requires a trusted workspace.'); return; }
  const config = workspace.getConfiguration('sharpmush');
  const remote = config.get<string>('languageServer.mode', 'offline') === 'remote';
  const dll = findDll(context, config.get<string>(remote ? 'languageServer.bridgePath' : 'languageServer.path', ''), remote ? 'SharpMUSH.LspBridge' : 'SharpMUSH.LanguageServer');
  if (!dll) { window.showErrorMessage('Build or configure the selected SharpMUSH language server/bridge DLL.'); return; }
  let args = [dll];
  let env: NodeJS.ProcessEnv | undefined;
  if (remote) {
    let uri: string;
    try { uri = endpoint(config.get<string>('languageServer.endpoint', '')); }
    catch { window.showErrorMessage('Configure a valid remote WSS endpoint.'); return; }
    const character = await context.secrets.get(credentialKey(uri) + '.character');
    const password = await context.secrets.get(credentialKey(uri) + '.password');
    if (!character || !password) { window.showErrorMessage('Run SharpMUSH: Set Remote Credentials before connecting.'); return; }
    args = [dll, '--endpoint', uri];
    env = { ...process.env, SHARPMUSH_LSP_CHARACTER: character, SHARPMUSH_LSP_PASSWORD: password };
  }
  const launch = { command: 'dotnet', args, transport: TransportKind.stdio, options: { env } };
  const serverOptions: ServerOptions = { run: launch, debug: launch };
  const watcher = workspace.createFileSystemWatcher('**/*.{mush,mu,mushfn,fun,mushcmd}');
  context.subscriptions.push(watcher);
  const clientOptions: LanguageClientOptions = { documentSelector: [{ scheme: 'file', language: 'mush' }], synchronize: { fileEvents: watcher } };
  client = new LanguageClient('sharpmush', 'SharpMUSH Language Server', serverOptions, clientOptions);
  try { await client.start(); window.showInformationMessage('SharpMUSH analysis started.'); }
  catch { window.showErrorMessage('SharpMUSH analysis could not start. Check the selected runtime and endpoint.'); }
}
export async function deactivate(): Promise<void> { if (client) { await client.stop(); client = undefined; } }
