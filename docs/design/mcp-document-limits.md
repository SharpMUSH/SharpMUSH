# MCP temporary document ownership and limits

The optional `SharpMUSH.Plugins.Mcp` plugin owns this state and transport. The host supplies
authentication/authorization and the analyzer. The eight tool names and arguments are unchanged. `open_document` still returns an opaque id,
`close_document` returns a boolean, and analysis accepts inline code or a document id.

Handles now belong to the authenticated `MushBasic` identity's character key, creation time and
optional account id. Identity comes from the current HTTP request, not a tool parameter. A different
character/incarnation/account cannot read or close a handle. Foreign handles behave like unknown
handles. Missing or different authentication schemes cannot access the store. This does not change
which credentials the endpoint accepts or grant game writes.

Configure these keys under the existing `Mcp` section:

| Key | Default | Meaning |
| --- | --- | --- |
| `MaxDocumentBytes` | 5,242,880 | UTF-8 bytes of stored or inline code, before analysis |
| `MaxDocumentsPerPrincipal` | 64 | Open handles for one authenticated identity |
| `MaxBytesPerPrincipal` | 33,554,432 | Total UTF-8 text bytes for that identity |
| `MaxStoredDocuments` | 1,024 | Global open handle cap |
| `MaxStoredBytes` | 134,217,728 | Global UTF-8 text budget |
| `DocumentIdleTimeout` | `00:15:00` | Idle timeout; successful owner reads renew it |
| `MaxRequestBodyBytes` | 33,554,432 | Raw HTTP envelope before JSON decoding |
| `MaxConcurrentRequests` | 4 | Global admitted HTTP requests; no waiting queue |
| `BodyReadTimeout` | `00:00:10` | Elapsed deadline for buffering a POST body |
| `MaxConcurrentAnalyses` | 2 | Global analysis workers |
| `MaxQueuedAnalyses` | 8 | Additional admitted analyses waiting for a worker |
| `AnalysisTimeout` | `00:00:05` | Elapsed analysis budget including queue time |

All budgets and the timeout must be positive. The aggregate defaults allow multiple full 5 MiB
documents. Counts and byte accounting are atomic under concurrent opens/closes. Admission rejects
new work when any budget is full instead of evicting another user. Close or idle expiry releases
budget. Foreign reads do not refresh idle time. Expiry is checked on store operations; idle entries
may remain allocated until a later operation, but remain subject to the global caps.

These are UTF-8 text accounting limits, not measured managed-heap limits. Strings, parser structures,
JSON bodies and protocol overhead consume more memory. An endpoint wrapper bounds declared and
unknown-length POST bodies before SDK JSON decoding. Authorized requests spool beyond a 64 KiB
prefix into request-owned temporary storage. Oversize bodies return 413, full HTTP admission 429,
and slow body reads 408. Client/host-stop/disposal cancellation interrupts body reads, and admission resources remain until
actual handlers finish. Authentication runs before body admission; the IP limiter runs before named
Basic authorization so failed authentication is also limited. Host Origin policy requires an exact
`AnalysisTransports:AllowedOrigins` entry when a header is present; native clients may omit Origin.
The endpoint sets Kestrel's mutable body-size feature to the configured envelope limit, preserving
other routes' limits. A proxy or a body-size feature already made read-only can impose a lower cap.
The 32 MiB default accepts a 5 MiB ASCII document even when every character is escaped as six JSON
bytes, with protocol overhead. Temporary storage and parser memory remain distinct from text quotas.

The six analysis tools acquire a global worker with an elapsed deadline and request cancellation.
Ambient shorter execution budgets remain effective. Saturated admission returns a tool error.
Cancellation now reaches syntax lexing, token buffering, tree/line traversal and intelligence loops;
analysis propagates cancellation instead of disguising it as diagnostics or unchanged output.
The executor awaits actual worker termination before releasing capacity, including after timeout or
host shutdown. This is cooperative cancellation, not hard preemption: an uncooperative library call
can overrun its deadline, while retaining its worker and admission slots until it ends. Open/close
remain synchronous and document-only. Cancellation tokens are injected by the SDK and do not appear
in the eight unchanged wire schemas.

Compatibility changes: over-budget input now returns a tool error; capacity pressure no longer
silently evicts old handles; idle handles expire; handles cannot be shared across characters.
Unknown/foreign handles use the same existing error path. Handles remain process-lifetime only and
are lost on restart. The internal store now requires DI request context/options/clock rather than
an unauthenticated parameterless constructor. No tool schema gains a principal or owner argument.

Validated with synthetic host identities and real in-memory HTTP MCP dispatch, without creating
a game or credentials. This is not a published-production-host or proxy-fixture validation.
