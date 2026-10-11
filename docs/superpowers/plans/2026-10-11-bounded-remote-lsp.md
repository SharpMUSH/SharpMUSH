# Bounded remote LSP and filtered MCP execution

Spec: docs/superpowers/specs/2026-10-11-remote-code-tools.md; current approved bounded scope and
review evidence: /home/grave/Documents/Codex/2026-10-11/task-3/REVIEW-AND-REMOTE-LSP.md.
Owner approved local implementation, commits/push and draft SharpMUSH PR. No merge/deploy or fixture
world/credentials/TLS/network creation. Separate documentation PRs are coordinated by parent.

1. Host-neutral cooperative execution and syntax-only catalog analyzer/session contracts; TDD
   async cancellation, quota/work termination and principal metadata projection.
2. Host session factory revalidates full player incarnation/account/password version, and MCP
   uses that factory without changing eight tool wire shapes. Actual in-memory two-principal,
   restriction-refresh and revocation tests.
3. Default-off LSP plugin: bounded WS JSON-RPC framing, per-principal/global sessions and document
   budgets, host auth/Origin policy, per-request filtered metadata, full sync/version checks,
   diagnostics/completion/hover/signature, cancellation/disconnect/shutdown. Omit owner-local
   presentation, formatting/rename/world writes. Reuse established LSP SDK where possible.
4. Stdio WSS bridge with bounded framing and secret-safe setup; existing offline process remains.
   Test framing, EOF/cancellation and editor five-extension launch/compile fixes as dependencies allow.
5. Complete optional-plugin build/publish/type-identity tests, safe local virtual HTTP/WS regression
   checks, contract/setup handoff for Guide/Docs and repository documentation. No production fixture.
6. Fresh whole-branch review, fix important findings, verify, commit/push own branch and create draft
   PR. Attach the resulting PR. Report exact limitations; no production readiness claim.

Pre-flight: MCP work executor is currently plugin-owned; LSP cannot depend on optional MCP.
Ruling: move cooperative work implementation into CodeAnalysis, retain MCP options adapter and honor
legacy limits through host defaults. Principal session contracts live in CodeAnalysis, not transport SDKs.
Pre-flight: current local handlers use synchronous analyzer, unbounded documents and ignore request tokens.
Ruling: remote adapters need explicit async execution/session ownership instead of blindly registering
all local handlers. First advertised feature set stays diagnostics/completion/hover/signature/full sync.
