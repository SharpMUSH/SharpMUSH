const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');
const root = path.resolve(__dirname, '..');
const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json')));
assert.deepEqual([...pkg.contributes.languages[0].extensions].sort(), ['.fun', '.mu', '.mush', '.mushcmd', '.mushfn']);
assert.equal(pkg.contributes.configuration.properties['sharpmush.languageServer.mode'].default, 'offline');
(async () => {
  const captured = { commands: new Map(), watchers: [], logs: [] };
  const endpoint = 'wss://fixture.invalid/lsp';
  const secretKey = 'sharpmush.remote.' + endpoint;
  const secrets = new Map([[secretKey + '.character', 'fixture-user'], [secretKey + '.password', 'sentinel-never-in-args']]);
  const context = { extensionPath: root, subscriptions: [], secrets: { get: async key => secrets.get(key), store: async (key, value) => secrets.set(key, value), delete: async key => secrets.delete(key) } };
  const vscode = {
    workspace: { isTrusted: true, getConfiguration: () => ({ get: (key, fallback) => ({ 'languageServer.mode': 'remote', 'languageServer.endpoint': endpoint, 'languageServer.bridgePath': '/fixture/SharpMUSH.LspBridge.dll' })[key] ?? fallback }), createFileSystemWatcher: pattern => { captured.watchers.push(pattern); return { dispose() {} }; } },
    commands: { registerCommand: (id, fn) => { captured.commands.set(id, fn); return { dispose() {} }; } },
    window: { showErrorMessage: value => captured.logs.push(value), showInformationMessage: value => captured.logs.push(value), showInputBox: async () => undefined }
  };
  const load = Module._load;
  Module._load = function(id, ...args) {
    if (id === 'vscode') return vscode;
    if (id === 'fs') return { ...fs, existsSync: () => true };
    if (id === 'vscode-languageclient/node') return { TransportKind: { stdio: 0 }, LanguageClient: class {
      constructor(id, name, server, client) { captured.server = server; captured.client = client; }
      async start() { captured.started = true; }
      async stop() {}
    } };
    return load.call(this, id, ...args);
  };
  try {
    const extension = require(path.join(root, 'out/extension.js'));
    await extension.activate(context);
    assert.equal(captured.started, true);
    assert.deepEqual(captured.server.run.args, ['/fixture/SharpMUSH.LspBridge.dll', '--endpoint', endpoint]);
    assert.equal(captured.server.run.options.env.SHARPMUSH_LSP_PASSWORD, 'sentinel-never-in-args');
    assert(!JSON.stringify(captured.server.run.args).includes('sentinel'));
    assert(!captured.logs.join('').includes('sentinel'));
    assert(captured.watchers.some(pattern => pattern.includes('mushfn') && pattern.includes('mushcmd') && pattern.includes('fun')));
    assert(captured.commands.has('sharpmush.setRemoteCredentials'));
    assert(captured.commands.has('sharpmush.clearRemoteCredentials'));
    await captured.commands.get('sharpmush.clearRemoteCredentials')();
    assert.equal(secrets.has(secretKey + '.password'), false);
    await extension.deactivate();
    console.log('PASS: five extensions, remote stdio launch, endpoint-bound secrets, safe args/logs, clear credentials');
  } finally { Module._load = load; }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
