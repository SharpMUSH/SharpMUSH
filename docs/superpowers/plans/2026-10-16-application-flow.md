# Shared application flow implementation

Approved source: [consolidation design](../specs/2026-10-16-open-issue-consolidation-design.md), applications lane (#1794–1796).

1. Pin JSON number/null/false/empty presence, active-field payload projection and unbound error summary in SchemaFormModel tests.
2. Introduce SchemaApplicationState as the common document/data/draft owner. Test reset then merge, replacement data authority on success and failure, defaults only when absent, schema kind transitions, serialized admissions and retirement tokens.
3. Add SchemaApplicationHost for dynamic pages and widgets. Render form/view children from resulting kind, centralize transport/action effects, disable edits while busy and retire responses on disposal or context replacement.
4. Prove optional Jobs edit/review/file with shared declaration and authoritative validation. Transient draft ownership must derive from trusted actor and connection/form incarnation; receipt keys include payload digest and operations serialize per instance. No record store or capture passthrough.
5. Validate focused state, bUnit renderer transitions and Jobs integration behavior with pinned .NET SDK; run formatting twice and dependent suites. Document compatibility and remaining runtime limits honestly.

## Transient seam decision

The runtime exposes trusted connection incarnation to `@input`, but softcode has no safe per-instance atomic draft/receipt ownership across terminal and HTTP requests. The optional adapter uses `@appdraft <declaration attribute>=<action>,<JSON fields>` on its executor, with fixed new/set/review/back/file/cancel actions. Sibling `<attribute>\`VALIDATE` and `<attribute>\`FILE` callbacks receive trusted `draft_actor`, literal `draft_fields`, and `draft_action` arguments; they set `appdraft_result` JSON. Fields are a shared JSON array, supporting the established scalar/list field vocabulary and required/max_length constraints. The callback rechecks world permission/state; the declaration is advisory presentation plus structural validation, never authorization.

Core state is transient only, bounded to 4096 instances, 64 KiB fields, 30 idle minutes. It keys application executor objid plus runtime actor plus terminal connection incarnation or HTTP form nonce. The HTTP nonce selects a tab within the authenticated actor; it does not authorize an actor. File serializes with edits/cancel in that instance, validates again, and binds its receipt to an operation ID and digest. Restart loses drafts/receipts; it offers no crash-safe exactly-once guarantee. Disconnect/logout/rebind retires terminal state. Web authentication and character changes prevent old actor access; client retirement and expiry bound abandoned web state. No capture passthrough changes.

Terminal `@appdraft/show` formats the sibling DISPLAY function and publishes only to the originating handle and captured SessionId after an incarnation check. HTTP emits no core draft broadcast. Each host owns its own nonce, rotated for external document/context replacement; internal transitions keep draft continuity. Application keys include the declaration attribute, so multiple declarations on an object remain independent.
