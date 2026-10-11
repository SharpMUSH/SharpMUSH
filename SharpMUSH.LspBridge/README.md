# SharpMUSH stdio bridge

```sh
dotnet publish SharpMUSH.LspBridge -c Release -o /path/to/bridge
dotnet /path/to/bridge/SharpMUSH.LspBridge.dll --endpoint wss://game.example/lsp
```

Provide an existing character name/password in the private child environment variables
`SHARPMUSH_LSP_CHARACTER` and `SHARPMUSH_LSP_PASSWORD`; the VS Code example injects endpoint-bound
SecretStorage values. Do not put secrets in arguments, URLs, logs or committed configuration.
Only WSS without userinfo/query/fragment is accepted. Standard TLS validation and no redirects are
used; there is no insecure certificate bypass. Native clients omit Origin; browser clients require
an explicitly configured host allowlist and a separately scoped authentication strategy.

Normal ASCII Content-Length/UTF-8 LSP stdio becomes one JSON-RPC WS text message under
`sharpmush-lsp-v1`. Header cap is 1 KiB and message cap 32 MiB. Logs/errors go to stderr only.
EOF/disconnect ends the bridge. Shutdown returns its null reply before a separate exit notification;
reconnect starts a new session and requires initialize/reopen. .NET 11 is required. The core and duplex
pump are tested with virtual WS/in-memory pipes; real TLS/socket subprocess/editor UI has not been run.
