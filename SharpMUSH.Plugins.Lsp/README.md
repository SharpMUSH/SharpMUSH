# Remote LSP plugin

Default-off boot-time plugin mapped at `/lsp`, separately optional from MCP. It uses the host's
MushBasic/MushAnalysis/Origin policy and shared analysis pool, not a new authentication system.
See [the operator/client contract](../docs/design/remote-analysis.md) for all configuration and limits.

LSP 3.17 subset over `sharpmush-lsp-v1`: one UTF-8 JSON-RPC object per WS text message.
Capabilities are full-document sync, diagnostics, completion, hover, signature help and cancellation.
Each connection has independent URI buffers and generation-tagged state. Requests snapshot text in
receive order and remain valid across later changes; stale push diagnostics are independently suppressed.
Close/reopen with the same version cannot receive the old generation's diagnostics. Full replacement
entries apply sequentially; range edits are not advertised and are rejected before document mutation.

Generic stdio clients use SharpMUSH.LspBridge over WSS. Only desktop/native clients supply Basic headers;
no query token, browser ticket, world write or server-file operation is provided. Connections/frames/
queues/documents/results have limits and idle/absolute deadlines. Cooperative analysis cancellation
retains execution capacity until actual work terminates. This is not hard preemption or a heap ceiling.

Current tests use actual private plugin loading and virtual WebSockets with synthetic identities,
not production credentials or listeners. Deployment, real TLS/proxy, actual-world identity and editor
UI integration require separately approved fixtures. Removal/disable requires restart; no hot unload.

Idle clients do not acquire fragmented-message reader admission. Small single-frame control traffic
remains independent of that pool. Request capacity errors are awaited; synchronization admission
failure closes explicitly, requiring reconnect/initialize/reopen instead of losing latest diagnostics.
Generation checks run again inside outbound sequencing. Bridge shutdown observes remaining pumps
without awaiting an uncancellable console read. See `docs/design/analysis-validation.md` for evidence.
