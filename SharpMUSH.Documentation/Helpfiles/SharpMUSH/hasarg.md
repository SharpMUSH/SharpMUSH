<!-- help-article
{"corpus":"help","id":"hasarg-function","lookup":"hasarg()","aliases":["hasarg"],"sections":[]}
-->
# hasarg()

`hasarg(<name>[,<scope>])`

Returns 1 if a key exists in the current attribute invocation, even when its value is empty or zero; otherwise 0. Names and scopes are case-insensitive. The default scope is `visible`; `supplied` checks only inputs supplied to this invocation. Other scopes return `#-1 INVALID ARGUMENT`. Outside an attribute invocation, both scopes return 0.

Includes inherit visible keys but only their overrides are supplied, even for an equal or empty override. Named and numbered aliases agree. Signature-generated counts are visible but not supplied. Nested attributes own their presence metadata; ordinary built-in wrappers preserve it, as do queued snapshots. The query never evaluates the target value. Nonempty-only register listings and %+ retain their existing meanings.

```sharp
&SHOW me=[hasarg(who)]|[hasarg(who,supplied)]|[hasarg(0,supplied)]|[hasarg(missing)]
think uargs(me/SHOW,who,)
```
Output: `1|1|1|0`.
