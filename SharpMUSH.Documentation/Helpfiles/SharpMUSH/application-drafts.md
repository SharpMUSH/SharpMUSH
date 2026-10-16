<!-- help-article
{"corpus":"help","id":"application-drafts","lookup":"@appdraft","aliases":["appfields()","appfields","application drafts","@appdraft/show"],"sections":[{"id":"callbacks","heading":"Callbacks and ownership","lookup":"@appdraft callbacks"},{"id":"lifetime","heading":"Lifetime and retries","lookup":"@appdraft lifetime"}]}
-->
# @appdraft

`@appdraft[/show] <object>/<declaration>=<action>[,<JSON fields>]`

An optional application adapter for ordinary commands. Actions are `new`, `set`, `review`, `back`, `file`, and `cancel`. It does not capture input or alter `@input`.

The declaration attribute holds a JSON field array with unique keys, supported field types and optional required/max_length validation. Values remain typed JSON. Unknown fields and unsupported shapes fail. Defaults apply only when absent. The pure `appfields(<declaration>,<JSON fields>)` function returns `{"ok":true}` or `{"ok":false,"errors":{...}}` using those same structural checks.

See [@appdraft callbacks] for integration and [@appdraft lifetime] for expiry and retries.

## Callbacks and ownership

The adapter derives the actor and instance from the connected runtime or verified HTTP context, never posted actor fields. The declaration's `VALIDATE` and `FILE` children are command callbacks, receiving named draft_actor, draft_fields and draft_action arguments. Each callback owns an attribute argument frame: hasarg(draft_fields,supplied), and the same query for draft_actor or draft_action, returns 1. Inherited caller environment values stay visible but are not supplied inputs; no numbered aliases are created. The current attribute identity is the callback; the caller's frame resumes after return. They set the appdraft_result q-register to action-response JSON. VALIDATE must recheck world permissions/state. FILE invokes the application's existing mutation routine. The adapter also leaves its response in appdraft_result; the caller chooses terminal or HTTP presentation. `/show` formats the DISPLAY child with the response as %0 and publishes only to the original connection incarnation after checking it again; it never broadcasts the draft to sibling connections.

## Lifetime and retries

Drafts expire after 30 idle minutes; disconnect/logout/rebind retires terminal drafts. File requires review, serializes the instance and returns a matching operation's saved receipt on retry. Conflicting retries fail. Drafts and receipts are in memory; restart loses them and does not guarantee exactly-once world effects.

Jobs exposes this optionally as `+request/draft new`, `+request/draft set title=<literal text>`, `review`, `back`, `file`, and `cancel`. Legacy +request remains available.
